---
title: Background
category: Explanations
categoryindex: 3
index: 1
---

# Background

`IObservable<T>` is .NET way of dealing with lazy event streams with publish/subscribe-pattern called Reactive Programming, "LINQ to Events and async operations".

Where a standard list (IEnumerable) is pull-based, IObservable is a push-based (infinite) list, like "a lazy list of mouse events": when an event happens, the corresponding list gets a new value.
If Nullable is just "a list of 0 or 1", then async-await could be just an IObservable of 0 or 1.

There are many advantages of using reactive programming and Rx:

- Manual thread/lock -handling can be avoided
- No temporary class variables to capture the current or some previous state 
- Testing is easy: generate lists like they would be event-lists. 
- Testing the wrong async event order is easy. 
- Also, testing long-duration workflows is easy as you can "fake" time passing

It's always good to have alternatives, and if R3 is your alternative to Rx, then `FSharp.Azure.Cosmos` is your F# wrapper, like `FSharp.Control.Reactive` for F#.
