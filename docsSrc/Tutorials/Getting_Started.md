---
title: Getting Started
category: Tutorials
categoryindex: 1
index: 1
---

# Getting Started

To use the raw R3 from F# you would add first the NuGet package to R3:

    [lang=bash]
    paket install R3

Then, this is a working sample:

```fsharp
open System

// Create a bus
use r3Bus = new R3.Subject<int> ()

// Filter events
let interesting = R3.ObservableExtensions.Where (r3Bus, fun x -> x % 2 = 0)

// Subscribe to events
let subscription =
    R3.ObservableExtensions.SubscribeAwait (
        interesting,
        fun i cancellationToken ->
            task {
                // Listen events
                printfn "%i" i
                return ()
            }
            |> System.Threading.Tasks.ValueTask
    )

// Publish some events
[ 1..10 ] |> List.iter r3Bus.OnNext
```

As you can see, this is nice, but a little bit noisy.
This package will come top help.

Then you do:

    [lang=bash]
    paket install FSharp.Azure.Cosmos

Now you can use functions closer to the traditional F#-style, like:

```fsharp
// Filter events
let interesting = r3Bus |> FSharp.Azure.Cosmos.Observable.filter (fun x -> x % 2 = 0)
```
