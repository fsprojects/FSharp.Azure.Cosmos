// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/InternalLogic.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/InternalLogic.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes:
// - the opened namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest;
// - MSTest invokes the test method: every case is one call of an invoker such as ITestMethod.InvokeAsync, memoised
//   per node of the shrink tree, with the values of an MSTest data row as the leading arguments, and an inconclusive
//   result discards the case; this replaces the invocation through reflection and the handling of return values
//   (bool, Property, Result, Async, Task<'T>), which MSTest 4 cannot discover;
// - generic methods and return types other than unit, Task and ValueTask are rejected before any generator is built;
// - a GenAttribute is found through its non-generic base, wherever it sits in the inheritance chain;
// - the Seed setting is applied, the run stops without shrinking once the TestContext token is cancelled, which is
//   checked before every invocation and after it, whatever its outcome, and a recheck
//   runs on the thread pool; the arguments generated for a case that the cancelled token keeps from running are
//   disposed as well;
// - an invocation whose outcome is none of Passed, Failed, Inconclusive and Timeout, such as Error, did not run the test
//   method as a test: the run stops at it without shrinking, and its result is the result of the property;
// - the run is folded into one MSTest TestResult, whose failure message ends with a Recheck attribute to add next to
//   the Property attribute, and any exception becomes an error result;
// - the conventions of this repository are applied: a setting that is not given is a voption, the generated arguments
//   are an array instead of a list, collections are joined in array and sequence expressions, the journal pairs the
//   parameters with their values in struct tuples, the configuration helpers are functions instead of function values,
//   and the return type is compared with the types of KnownTypes through the operators of System.Type;
// - nullness checking is on: a data row and the arguments of an invocation may hold null, a null data row is checked for
//   where MSTest hands it in, nested exceptions are matched instead of tested with isNull, and the one call whose
//   annotation contradicts MSTest's own use of it, ITestMethod.InvokeAsync, is in the helper invokerOf.
// The file is formatted with Fantomas, under the settings of this repository.

module internal InternalLogic

open Hedgehog
open Hedgehog.FSharp
open Hedgehog.MSTest
open Microsoft.VisualStudio.TestTools.UnitTesting
open System
open System.Diagnostics
open System.Reflection
open System.Threading
open System.Threading.Tasks
// For the collection functions of this repository that return a voption or struct tuples
open FSharp.Azure.Cosmos

// ========================================
// Type Utilities & Helpers
// ========================================

type private TypedReflectionMarker = class end

[<Literal>]
let private GenxAutoBoxMethodName = "genxAutoBoxWith"

let private genxAutoBoxWith<'T> x = x |> Gen.autoWith<'T> |> Gen.map box

// Neither lookup can fail: the marker type is nested in this module, and the module declares the method above
let private genxAutoBoxWithMethodInfo : MethodInfo =
    (nonNull typeof<TypedReflectionMarker>.DeclaringType).GetTypeInfo().GetDeclaredMethod(GenxAutoBoxMethodName)
    |> nonNull

// ========================================
// Resource Management
// ========================================

let dispose (o : objnull) =
    match o with
    | :? IDisposable as d -> d.Dispose ()
    | _ -> ()

// ========================================
// Configuration Helpers
// ========================================

let withTests (tests : int<tests> voption) (config : IPropertyConfig) : IPropertyConfig =
    match tests with
    | ValueSome tests -> config |> PropertyConfig.withTests tests
    | ValueNone -> config

let withShrinks (shrinks : int<shrinks> voption) (config : IPropertyConfig) : IPropertyConfig =
    match shrinks with
    | ValueSome shrinks -> config |> PropertyConfig.withShrinks shrinks
    | ValueNone -> config

let withSeed (seed : uint64 voption) (config : IPropertyConfig) : IPropertyConfig =
    match seed with
    | ValueSome seed -> config |> PropertyConfig.withSeed (Seed.from seed)
    | ValueNone -> config

// ========================================
// Method Validation
// ========================================

/// Throws when the method cannot run as a property
let validate (testMethod : MethodInfo) =
    // MSTest 4.2.3 discovers a generic test method although TestMethodAttribute documents that it must not be generic;
    // upstream closes the method over obj, which turns every generated value into null
    if testMethod.ContainsGenericParameters then
        invalidOp (
            $"%s{testMethod.Name} is generic, but a property method must not be: Hedgehog cannot generate arguments "
            + "for a type parameter. Give every parameter a concrete type."
        )

    // MSTest 4 discovers only methods that return void, Task or ValueTask; any other return type fails the discovery of
    // the whole assembly (UTA007), so this guards the runner when it is driven directly
    let returnType = testMethod.ReturnType

    if
        Type.(<>) (returnType, KnownTypes.voidType)
        && Type.(<>) (returnType, KnownTypes.task)
        && Type.(<>) (returnType, KnownTypes.valueTask)
    then
        let advice =
            if
                ReflectionHelpers.isGenericTask returnType
                || ReflectionHelpers.isGenericValueTask returnType
            then
                "return Task or ValueTask without a result and fail by throwing, such as through an Assert call"
            elif ReflectionHelpers.isAsync returnType then
                "return a Task from a task { } expression instead of an Async"
            elif ReflectionHelpers.isResult returnType then
                "fail by throwing, such as through an Assert call, instead of returning Error"
            else
                "return unit, Task or ValueTask and fail by throwing, such as through an Assert call"

        invalidOp $"%s{testMethod.Name} returns %s{returnType.Name}, which MSTest cannot run as a test method: %s{advice}."

// ========================================
// Generator Creation
// ========================================

module private GeneratorFactory =
    /// Tries to get a custom generator from a GenAttribute on a parameter, whatever its place in the inheritance chain
    let tryGetAttributeGenerator (parameterInfo : ParameterInfo) : Gen<obj> voption =
        parameterInfo.GetCustomAttributes<GenAttribute>()
        |> Seq.tryHead
        |> ValueOption.map _.Box()

    /// Creates a generator for a parameter based on attribute or type
    let createGenerator (autoGenConfig : obj) (parameter : ParameterInfo) : Gen<obj> =
        match tryGetAttributeGenerator parameter with
        | ValueSome gen -> gen
        | ValueNone ->
            genxAutoBoxWithMethodInfo.MakeGenericMethod(parameter.ParameterType).Invoke(null, [| autoGenConfig |]) :?> Gen<obj>

    /// Creates an array generator for all test method parameters
    let createParameterArrayGenerator (context : PropertyContext) (parameters : ParameterInfo[]) : Gen<obj array> =
        let gens =
            parameters
            |> Array.map (createGenerator context.AutoGenConfig)
            |> Gen.sequenceArray

        match context.Size, context.Recheck with
        | _, ValueSome _ -> gens // Size from recheck data if present
        | ValueSome size, _ -> gens |> Gen.resize size
        | ValueNone, _ -> gens

// ========================================
// Invocation
// ========================================

/// What one run of a property produced, before it becomes an MSTest result
type PropertyRun = {
    /// Hedgehog's report of the run
    Report : Report
    /// The number of invocations of the test method, shrink steps included
    Invocations : int
    /// The result of the last invocation that failed: with every invocation memoised, that of the counterexample
    LastFailure : TestResult voption
    /// Whether the run stopped because the TestContext cancellation token was cancelled
    Cancelled : bool
    /// The result of the invocation that MSTest could not run as a test; the run stopped at it
    RunnerFailure : TestResult voption
}

/// The bookkeeping of one run; Hedgehog evaluates the cases of a run one after another, so it needs no locking
type private RunState () =
    member val Invocations = 0 with get, set
    member val LastFailure : TestResult voption = ValueNone with get, set
    member val Cancelled = false with get, set
    member val RunnerFailure : TestResult voption = ValueNone with get, set

[<Literal>]
let private TestFailedExceptionTypeName =
    "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.ObjectModel.TestFailedException"

/// The exception that an invocation reports, or one that names its outcome when it reports none
let private reportedExceptionOf (result : TestResult) : exn =
    match result.TestFailureException with
    | null -> InvalidOperationException ($"The invocation of the test method ended with the outcome %O{result.Outcome}.")
    | error -> error

/// The exception of a failed invocation. MSTest wraps what the test method threw into its internal
/// TestFailedException; one without an inner exception, such as a timeout, stands for itself.
let exceptionOf (result : TestResult) : exn =
    let error = reportedExceptionOf result

    match error.InnerException with
    | null -> error
    | inner when String.Equals (error.GetType().FullName, TestFailedExceptionTypeName, StringComparison.Ordinal) -> inner
    | _ -> error

module private PropertyBuilder =
    /// Creates a property whose every case invokes the test method once
    let createProperty
        (state : RunState)
        (rechecking : bool)
        (cancellationToken : unit -> CancellationToken)
        (invoke : objnull array -> Task<TestResult>)
        (parameters : ParameterInfo[])
        (dataRow : objnull array)
        (gens : Gen<obj array>)
        : Property<unit> =

        // A case that does not run is discarded, but the arguments generated for it are disposed all the same
        let skip (generated : obj array) (reason : string) : Journal * Outcome<unit> =
            Array.iter dispose generated
            Journal.singletonMessage $"Not run: %s{reason}", Discard

        let invokeOnce (generated : obj array) : Task<Journal * Outcome<unit>> = task {
            // Read before every invocation: MSTest gives the TestContext a new token source before each TestCleanup,
            // so the token in effect during this invocation is the one read now, and the one to check after it
            let token = cancellationToken ()

            if state.RunnerFailure.IsSome then
                return skip generated "an earlier invocation did not run the test method as a test."
            elif state.Cancelled || token.IsCancellationRequested then
                state.Cancelled <- true
                return skip generated "the TestContext cancellation token is cancelled."
            else
                let arguments = [| yield! dataRow; yield! generated |]

                let! result = task {
                    try
                        try
                            return! invoke arguments
                        with e ->
                            // MSTest reports what the test method throws in the result; an exception comes
                            // from MSTest itself, so it is an error of the runner, not a failed case
                            return TestResult (Outcome = UnitTestOutcome.Error, TestFailureException = e)
                    finally
                        Array.iter dispose generated
                }

                state.Invocations <- state.Invocations + 1

                // A [<Timeout>] cancels the token of the invocation it applies to, and a cancelled run cancels it as
                // well. The token is checked after every invocation, whatever its outcome: MSTest reports Passed for
                // a test method that returns without observing its cancelled token, and after the last case nothing
                // else would notice it. So the property stops in the same way wherever the cancellation reached it.
                if token.IsCancellationRequested then
                    state.Cancelled <- true

                match result.Outcome with
                | UnitTestOutcome.Passed -> return Journal.empty, Success ()
                // Assert.Inconclusive marks a case whose precondition does not hold, so Hedgehog discards it. A
                // recheck replays a single case and cannot discard it, so there the case fails instead.
                | UnitTestOutcome.Inconclusive when not rechecking -> return Journal.empty, Discard
                // The test method ran and its case is falsified
                | UnitTestOutcome.Failed
                | UnitTestOutcome.Inconclusive
                | UnitTestOutcome.Timeout ->
                    // After a cancelled token no shrink step runs: every one of them would wait for the same
                    // timeout again, so the property reports this case as it is.
                    state.LastFailure <- ValueSome result
                    return Journal.exn (exceptionOf result), Failure
                // Any other outcome, such as Error or NotFound, says that MSTest could not run the test method as a
                // test. No smaller case would fare better, so the run stops here without shrinking: the cases that
                // Hedgehog still asks for are skipped, and this result becomes the result of the property.
                | _ ->
                    state.RunnerFailure <- ValueSome result
                    return Journal.exn (reportedExceptionOf result), Failure
        }

        let createJournal (generated : obj array) =
            seq {
                yield! dataRow
                yield! generated
            }
            |> Seq.zip parameters
            |> Seq.map (fun struct (param, value) -> fun () -> TestParameter (param.Name, value))
            |> Seq.toArray // not sure if journal will do multiple enumerations
            |> Journal.ofSeq

        gens
        |> Property.bindWith
            createJournal
            (fun generated ->
                // Hedgehog 2.0.4 runs an asynchronous result again whenever it unwraps it, and it unwraps the final
                // counterexample a second time to read its journal. The lazy task makes that one invocation per node of the
                // shrink tree: side effects happen once, and the journal is that of the invocation that failed.
                let invocation = lazy (invokeOnce generated)
                Property.ofAsyncWithJournal (async { return! Async.AwaitTask invocation.Value })
            )

// ========================================
// Report Generation
// ========================================

/// Runs the method as a property: Hedgehog generates the arguments that follow the data row, and invoke runs the test
/// method once for every case and every shrink step
let runAsync
    (context : PropertyContext)
    (testMethod : MethodInfo)
    (dataRow : objnull array)
    (invoke : objnull array -> Task<TestResult>)
    (cancellationToken : unit -> CancellationToken)
    : Task<PropertyRun> = task {
    let parameters = testMethod.GetParameters ()
    let generatedParameters =
        parameters
        |> Array.skip (min dataRow.Length parameters.Length)
    let gens = GeneratorFactory.createParameterArrayGenerator context generatedParameters
    let state = RunState ()

    let property =
        PropertyBuilder.createProperty state context.Recheck.IsSome cancellationToken invoke parameters dataRow gens

    let config =
        PropertyConfig.defaults
        |> withTests context.Tests
        |> withShrinks context.Shrinks
        |> withSeed context.Seed

    let! report =
        match context.Recheck with
        // Hedgehog 2.0.4 rechecks synchronously and blocks on asynchronous results, so the replay runs on the thread
        // pool instead of blocking the test thread
        | ValueSome recheckData -> Task.Run (fun () -> Property.reportRecheckWith recheckData config property)
        | ValueNone -> Property.reportTaskWith config property

    return {
        Report = report
        Invocations = state.Invocations
        LastFailure = state.LastFailure
        Cancelled = state.Cancelled
        RunnerFailure = state.RunnerFailure
    }
}

// ========================================
// MSTest Results
// ========================================

let private recheckHint (report : Report) =
    match report.Status with
    | Failed { RecheckInfo = Some info } ->
        let data = RecheckData.serialize info.Data
        // Recheck on its own, to be added next to the method's Property attribute: replacing that attribute would drop
        // settings such as its AutoGenConfig, and the recheck data would no longer replay this counterexample
        $"%s{Environment.NewLine}Reproduce by adding next to the Property attribute: [<Recheck(\"%s{data}\")>]"
    | _ -> ""

/// The result of the property, with the output of the invocation that it reports
let private resultOf
    (outcome : UnitTestOutcome)
    (error : exn)
    (source : TestResult voption)
    (summary : string)
    (duration : TimeSpan)
    =
    match source with
    | ValueSome source ->
        TestResult (
            Outcome = outcome,
            TestFailureException = error,
            LogOutput = summary + Environment.NewLine + source.LogOutput,
            LogError = source.LogError,
            DebugTrace = source.DebugTrace,
            TestContextMessages = source.TestContextMessages,
            ResultFiles = source.ResultFiles,
            Duration = duration
        )
    | ValueNone -> TestResult (Outcome = outcome, TestFailureException = error, LogOutput = summary, Duration = duration)

/// A failed property; the invocation that failed is the counterexample that the message shows
let private failedResult (message : string) (failure : TestResult voption) (summary : string) (duration : TimeSpan) =
    let error =
        match failure with
        | ValueSome source -> AssertFailedException (message, exceptionOf source)
        | ValueNone -> AssertFailedException (message)

    resultOf UnitTestOutcome.Failed error failure summary duration

/// Folds a run into the one MSTest result of the property
let toTestResult (run : PropertyRun) (duration : TimeSpan) : TestResult =
    let report = run.Report

    let summary =
        $"Hedgehog: %d{int report.Tests} tests, %d{int report.Discards} discards, "
        + $"%d{run.Invocations} invocations of the test method."

    match run.RunnerFailure with
    | ValueSome source ->
        // Not a counterexample, so nothing of Hedgehog's report applies: the property ends with the outcome and the
        // exception of the invocation that MSTest could not run, as a plain test method would
        let stopped =
            $"The property stopped after %d{run.Invocations} invocations without shrinking, because an invocation ended "
            + $"with the outcome %O{source.Outcome}: MSTest did not run the test method as a test."

        resultOf source.Outcome (reportedExceptionOf source) run.RunnerFailure (summary + Environment.NewLine + stopped) duration
    | ValueNone when run.Cancelled ->
        let message =
            $"The property stopped after %d{run.Invocations} invocations without shrinking, because the TestContext "
            + "cancellation token was cancelled: a [<Timeout>] applies to each invocation, or the test run was cancelled."
            + Environment.NewLine
            + Report.render report
            + recheckHint report

        failedResult message run.LastFailure summary duration
    | ValueNone ->
        match report.Status with
        | OK -> TestResult (Outcome = UnitTestOutcome.Passed, LogOutput = summary, Duration = duration)
        | GaveUp ->
            let message =
                Report.render report
                + Environment.NewLine
                + "Hedgehog gives up after 100 discarded cases; an invocation that calls Assert.Inconclusive is discarded."

            failedResult message ValueNone summary duration
        | Failed _ -> failedResult (Report.render report + recheckHint report) run.LastFailure summary duration

/// The result of a property that could not run
let errorResult (error : exn) (duration : TimeSpan) : TestResult =
    let error =
        match error with
        | :? TargetInvocationException as e ->
            match e.InnerException with
            | null -> error
            | inner -> inner
        | e -> e

    TestResult (
        Outcome = UnitTestOutcome.Error,
        TestFailureException = AssertFailedException ($"Hedgehog could not run the property: %s{error.Message}", error),
        Duration = duration
    )

/// Runs the method as a property and folds the run into one MSTest result; no exception escapes. This is the entry
/// point of PropertyAttribute.ExecuteAsync, and the adapter's own tests drive it with an invoker of their own.
let executeAsync
    (testMethod : MethodInfo)
    (dataRow : objnull array | null)
    (invoke : objnull array -> Task<TestResult>)
    (cancellationToken : unit -> CancellationToken)
    : Task<TestResult> = task {
    let stopwatch = Stopwatch.StartNew ()

    // MSTest gives no arguments, null, to a test method without a data row
    let dataRow =
        match dataRow with
        | null -> [||]
        | dataRow -> dataRow

    try
        validate testMethod
        let context = PropertyContext.fromMethod testMethod
        let! run = runAsync context testMethod dataRow invoke cancellationToken
        return toTestResult run stopwatch.Elapsed
    with error ->
        return errorResult error stopwatch.Elapsed
}

/// The cancellation token of the current invocation. TestContext.Current is the only way from an attribute to the
/// TestContext, and MSTest 4.2.3 and 4.3.2 mark it experimental (MSTESTEXP), so FS0057 is suppressed here only.
let currentCancellationToken () : CancellationToken =
    #nowarn "57"
    match TestContext.Current with
    | null -> CancellationToken.None
    | context -> context.CancellationToken
#warnon "57"

/// Invokes the test method for one case. MSTest declares the elements of the arguments of ITestMethod.InvokeAsync as
/// non-null, although it passes ITestMethod.Arguments, whose elements it declares as nullable, to that method itself,
/// and a data row or a generator may well hold a null. So FS3261 is suppressed for this one call only.
let invokerOf (testMethod : ITestMethod) : objnull array -> Task<TestResult> =
    #nowarn "3261"
    fun arguments -> testMethod.InvokeAsync arguments
#warnon "3261"
