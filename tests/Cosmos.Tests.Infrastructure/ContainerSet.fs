namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Collections
open System.Collections.Generic
open System.Collections.Immutable

open Microsoft.Azure.Cosmos

/// <summary>
/// Compares the definitions of containers by their identifiers. An identifier is case-sensitive.
/// </summary>
[<Sealed>]
type private ContainerIdComparer () =

    interface IEqualityComparer<ContainerProperties> with
        /// <inheritdoc />
        member _.Equals (first, second) =
            match first, second with
            | null, null -> true
            | null, _
            | _, null -> false
            | first, second -> String.Equals (first.Id, second.Id, StringComparison.Ordinal)

        /// <inheritdoc />
        member _.GetHashCode container = StringComparer.Ordinal.GetHashCode container.Id

/// <summary>
/// A set of container definitions that cannot hold two definitions of the same container. The identifier of a
/// container is the key.
/// </summary>
/// <remarks>
/// <para>
/// A fixture declares its containers in a set. The set throws at the place of a second declaration of a container.
/// Without it, nothing reports the second declaration: <c>CreateContainerIfNotExistsAsync</c> returns the container
/// that exists already and ignores all of its other settings, except the partition key path.
/// </para>
/// <para>
/// The set throws because an <see cref="ImmutableHashSet{T}"/> does not: it keeps the first of two equal items and
/// drops the second without a message. The sequence of the containers is not specified.
/// </para>
/// </remarks>
[<Sealed>]
type ContainerSet private (containers : ImmutableHashSet<ContainerProperties>) =

    static let empty = ContainerSet (ImmutableHashSet.Create<ContainerProperties>(ContainerIdComparer ()))

    /// <summary>
    /// The set without a container.
    /// </summary>
    static member Empty = empty

    /// <summary>
    /// Creates a set of the specified containers.
    /// </summary>
    /// <exception cref="ArgumentException">Two of the containers have the same identifier.</exception>
    static member Create ([<ParamArray>] containers : ContainerProperties array) : ContainerSet =
        containers
        |> Seq.fold (fun (set : ContainerSet) container -> set.Add container) empty

    /// <summary>
    /// The number of containers in the set.
    /// </summary>
    member _.Count = containers.Count

    /// <summary>
    /// Returns a set that has the specified container in addition to the containers of this set.
    /// </summary>
    /// <exception cref="ArgumentException">This set has a container with the same identifier.</exception>
    member _.Add (container : ContainerProperties) : ContainerSet =
        if containers.Contains container then
            invalidArg (nameof container) $"The container '%s{container.Id}' is declared twice. Declare each container once."

        ContainerSet (containers.Add container)

    interface IReadOnlyCollection<ContainerProperties> with
        /// <inheritdoc />
        member _.Count = containers.Count

        /// <inheritdoc />
        member _.GetEnumerator () : IEnumerator<ContainerProperties> =
            (containers :> IEnumerable<ContainerProperties>).GetEnumerator()

    interface IEnumerable with
        /// <inheritdoc />
        member _.GetEnumerator () : IEnumerator = (containers :> IEnumerable).GetEnumerator()
