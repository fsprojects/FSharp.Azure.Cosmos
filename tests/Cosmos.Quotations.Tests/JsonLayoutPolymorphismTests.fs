namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Quotations

/// The base of a hierarchy that is written with a discriminator, named by the type.
[<AbstractClass; JsonDerivedType(typeof<StoredDog>, "dog+v1"); JsonDerivedType(typeof<StoredCat>, "cat+v1")>]
type StoredAnimal () =

    /// A property of every animal.
    member val Name = "" with get, set

/// A derived class.
and StoredDog () =
    inherit StoredAnimal ()

/// Another derived class.
and StoredCat () =
    inherit StoredAnimal ()

/// A base of a hierarchy that is written with a discriminator under a name of its own, and one derived class without.
[<AbstractClass;
  JsonPolymorphic(TypeDiscriminatorPropertyName = "kind");
  JsonDerivedType(typeof<StoredCar>, "car");
  JsonDerivedType(typeof<StoredTruck>)>]
type StoredVehicle () =

    /// A property of every vehicle.
    member val Plate = "" with get, set

/// A derived class with a discriminator.
and StoredCar () =
    inherit StoredVehicle ()

/// A derived class without one.
and StoredTruck () =
    inherit StoredVehicle ()

/// A base of a hierarchy whose discriminators are numbers.
[<AbstractClass; JsonDerivedType(typeof<StoredBoat>, 1)>]
type StoredShip () =

    /// A property of every ship.
    member val Name = "" with get, set

/// A derived class with a number as its discriminator.
and StoredBoat () =
    inherit StoredShip ()

/// <summary>
/// What <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> says about types with derived types, compared with
/// what the serializer writes for a derived type that it writes as its base.
/// </summary>
[<TestClass; JsonLayoutUnitTestCategory>]
type JsonLayoutPolymorphismTests () =

    /// The layout of FSharp.SystemTextJson as it is made.
    static let layout () : IJsonLayout = LayoutAssert.WantLayout (SerializerProfiles.fsharpDefault.Create ())

    /// Every way to set the serializer up that the layout supports.
    static member ProfileRows : IEnumerable<TestDataRow<SerializerProfile>> =
        SerializerProfiles.supported
        |> Seq.map (fun profile ->
            TestDataRow<SerializerProfile>(
                profile,
                DisplayName = $"a type with derived types must be laid out as it is written by %s{profile.Name}"
            )
        )

    [<TestMethod; DynamicData(nameof JsonLayoutPolymorphismTests.ProfileRows)>]
    member _.``A derived type must be written with the discriminator that the layout says`` (profile : SerializerProfile) =
        let options = profile.Create ()
        let layout = LayoutAssert.WantLayout options

        let check (baseType : Type) (animals : (Type * objnull) seq) =
            match layout.Polymorphism baseType with
            | PolymorphismEncoding.Polymorphic (property, derivedTypes) ->
                for derived in derivedTypes do
                    let value =
                        animals
                        |> Seq.pick (fun (valueType, value) ->
                            if Type.(=) (valueType, derived.Type) then
                                Some value
                            else
                                None
                        )

                    let written = JsonSerializer.SerializeToElement (value, baseType, options)

                    match derived.Discriminator, written.TryGetProperty property with
                    | ValueSome discriminator, (true, found) ->
                        Assert.AreEqual<string | null>(
                            discriminator,
                            found.GetString (),
                            $"The document of %s{derived.Type.Name} should hold the discriminator under %s{property} (%s{profile.Name})."
                        )
                    | ValueNone, (false, _) -> ()
                    | said, (found, _) ->
                        Assert.Fail
                            $"%s{derived.Type.Name}: the layout says %A{said}, and the document holds the property: %b{found}"
            | other -> Assert.Fail $"%s{baseType.Name} should be polymorphic, but the layout says %A{other}"

        check
            typeof<StoredAnimal>
            (seq {
                (typeof<StoredDog>, box (StoredDog ()))
                (typeof<StoredCat>, box (StoredCat ()))
            })

        check
            typeof<StoredVehicle>
            (seq {
                (typeof<StoredCar>, box (StoredCar ()))
                (typeof<StoredTruck>, box (StoredTruck ()))
            })

    [<TestMethod>]
    member _.``The derived types of a type must be the ones that it declares, with their discriminators`` () =
        match (layout ()).Polymorphism typeof<StoredAnimal> with
        | PolymorphismEncoding.Polymorphic (property, derivedTypes) ->
            Assert.AreEqual<string>("$type", property, "The discriminator has the name that System.Text.Json gives it.")

            CollectionAssert.AreEquivalent (
                [| struct (typeof<StoredDog>, ValueSome "dog+v1"); struct (typeof<StoredCat>, ValueSome "cat+v1") |],
                derivedTypes
                |> Seq.map (fun derived -> struct (derived.Type, derived.Discriminator))
                |> Seq.toArray,
                "Every derived type that the type declares has the discriminator that it declares."
            )
        | other -> Assert.Fail $"The type should be polymorphic, but the layout says %A{other}"

    [<TestMethod>]
    member _.``A discriminator that has a name of its own must have it`` () =
        match (layout ()).Polymorphism typeof<StoredVehicle> with
        | PolymorphismEncoding.Polymorphic (property, derivedTypes) ->
            Assert.AreEqual<string>("kind", property, "The attribute of the type names the property.")

            Assert.IsTrue (
                derivedTypes
                |> Seq.exists (fun derived ->
                    Type.(=) (derived.Type, typeof<StoredTruck>)
                    && derived.Discriminator.IsNone
                ),
                "A derived type that declares no discriminator has none."
            )
        | other -> Assert.Fail $"The type should be polymorphic, but the layout says %A{other}"

    [<TestMethod>]
    member _.``A type without derived types must not be polymorphic`` () =
        let layout = layout ()

        for valueType in
            seq {
                typeof<StoredPerson>
                typeof<StoredEntry>
                typeof<StoredDog>
                typeof<string>
                typeof<int option>
            } do
            Assert.AreEqual<PolymorphismEncoding>(
                PolymorphismEncoding.NotPolymorphic,
                layout.Polymorphism valueType,
                $"%s{valueType.Name} declares no derived types."
            )

    [<TestMethod>]
    member _.``A type whose discriminators are numbers must not be described`` () =
        Assert.AreEqual<string>(
            "{\"$type\":1,\"Name\":\"\"}",
            JsonSerializer.Serialize<StoredShip>(StoredBoat (), SerializerProfiles.fsharpDefault.Create ()),
            "The serializer should write the number, or the test checks nothing."
        )

        Assert.AreEqual<PolymorphismEncoding>(
            PolymorphismEncoding.NotDescribed,
            (layout ()).Polymorphism typeof<StoredShip>,
            "A discriminator that is a number is no text to compare with."
        )

    [<TestMethod>]
    member _.``A type with derived types that a converter of its own writes must not be described`` () =
        // System.Text.Json refuses the contract of such a type itself: a converter of an application cannot carry a
        // discriminator, so there is no contract whose polymorphism could be read.
        let options = SerializerProfiles.fsharpDefault.Create ()

        Assert.Throws<NotSupportedException>(
            Action (fun () -> options.GetTypeInfo typeof<StoredGuarded> |> ignore),
            "The serializer should refuse the type, or the test checks nothing."
        )
        |> ignore

        Assert.AreEqual<PolymorphismEncoding>(
            PolymorphismEncoding.NotDescribed,
            (layout ()).Polymorphism typeof<StoredGuarded>,
            "The serializer has no contract for the type, so the layout cannot say how it is written."
        )

    [<TestMethod>]
    member _.``A type that the serializer refuses must not be described`` () =
        Assert.AreEqual<PolymorphismEncoding>(
            PolymorphismEncoding.NotDescribed,
            (layout ()).Polymorphism typeof<StoredRefused>,
            "The serializer has no contract for the type."
        )
