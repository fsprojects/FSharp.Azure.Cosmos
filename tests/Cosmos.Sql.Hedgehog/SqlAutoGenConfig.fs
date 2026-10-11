namespace FSharp.Azure.Cosmos.Tests.Models

open Hedgehog
open Hedgehog.FSharp

/// <summary>
/// The generators of the Cosmos DB SQL syntax tree for the parameters of a property, by parameter type: a test class
/// or a property names this type in <see cref="T:Hedgehog.MSTest.PropertiesAttribute"/> or
/// <see cref="T:Hedgehog.MSTest.PropertyAttribute"/>.
/// <para>
/// Every tree it generates is valid: the generators are those of
/// <see cref="T:FSharp.Azure.Cosmos.Tests.Models.SqlGenerators"/> without <c>arbitrary</c> in their name. A parameter
/// that needs another generator of that module takes it through an attribute that derives from
/// <see cref="T:Hedgehog.MSTest.GenAttribute`1"/>.
/// </para>
/// </summary>
[<AbstractClass; Sealed>]
type SqlAutoGenConfig =

    /// <summary>
    /// The configuration: the default generators of Hedgehog and, on top of them, the generators of
    /// <see cref="T:FSharp.Azure.Cosmos.Sql.SqlQuery"/>, <see cref="T:FSharp.Azure.Cosmos.Sql.ScalarExpression"/>,
    /// <see cref="T:FSharp.Azure.Cosmos.Sql.Literal"/>, <see cref="T:FSharp.Azure.Cosmos.Sql.Identifier"/>,
    /// <see cref="T:FSharp.Azure.Cosmos.Sql.ParameterName"/> and the operators.
    /// </summary>
    static member Config : IAutoGenConfig =
        AutoGenConfig.defaults
        |> AutoGenConfig.addGenerator SqlGenerators.sqlQueryGen
        |> AutoGenConfig.addGenerator SqlGenerators.scalarExpressionGen
        |> AutoGenConfig.addGenerator SqlGenerators.literalGen
        |> AutoGenConfig.addGenerator SqlGenerators.identifierGen
        |> AutoGenConfig.addGenerator SqlGenerators.parameterNameGen
        |> AutoGenConfig.addGenerator SqlGenerators.binaryOperatorGen
        |> AutoGenConfig.addGenerator SqlGenerators.unaryOperatorGen
        |> AutoGenConfig.addGenerator SqlGenerators.sortOrderGen
