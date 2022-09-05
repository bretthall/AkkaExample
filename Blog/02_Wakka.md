In my [previous post](https://backwardsincompatibilities.wordpress.com/2021/12/13/akka-for-advent/) I talked about building actors using [Akka.NET](https://getakka.net) and [Akkling](https://github.com/Horusiath/Akkling) with F#. In that post I used *effect* functions like `Akkling.Spawn.ignored` and `Akkling.Spawn.become` that generate `Akkling.Actors.Effect<'Message>` which, well, effect how the actor behaves (or don't effect it in the case of `ignored`). `Akkling.Props.props` needs a function of type `Akkling.Actors.Actor<'Msg> -> Akkling.Actors.Effect<'Msg>` that says how messages will be processed, and `ignored` and `become` provide that. I left out that there is another way to generate effects besides these functions: a computation expression. If we take the storage actor example from the previous post, and implement it using hte computation expression instead, then we get:

```f#
let startStorageActorCE parent =
    
    Akkling.Spawn.spawn parent "storage"
        (Akkling.Props.props <| fun (ctx: Akkling.Actors.Actor<StorageMsg>) ->
            
            let rec handleMsgs values = actor {
                match! ctx.Receive () with
                | Query query ->
                    query.receiver <! {
                        key = query.key
                        value = values |> Map.tryFind query.key
                    }
                    return! handleMsgs values
                | Update update ->
                    let newValues = Map.add update.key update.value values
                    return! handleMsgs newValues
                | Stop ->
                    return! Akkling.Spawn.stop ()                
            }
                    
            handleMsgs Map.empty
        )
```

Instead of using `become` to set the message handler, we just use the `actor {...}` computation expression and make recursive calls to `handleMsgs`. Really, there's not much difference between the two, and it seems like it should be a *six of one, half dozen of another* situation. But the thing to remember about computation expressions is that they make debugging more difficult. If you don't know how computation expressions work, they take expressions like `let! x = foo ()` and turn them into `actor.Bind(foo (), fun x -> ...)` when the code is compiled. The `Bind` method of the *actor computation expression builder* performs the magic that the computation expression does. This makes the code easier to read and write, but when you try to step through the code in the debugger, you don't see the nice computation expressions code, you see the calls to `Bind` instead. And to make matters worse, the names that you use with `let!` will not be present when you stop on other lines of the computation expression. The de-sugaring process renames them, so you have to guess which value is the one that you want in the watch window. If you work with computation expressions often, you get used to working around these problems, but they're still there.

Does this mean that computation expressions are *bad*? Not in the least. The gain in readability normally compensates for the debugger problems. But in the case of Akka, my feeling is that we don't really gain anything over `become` if we use the computation expression instead. The code looks almost the same, but now you're going to have trouble if you need to step through in the debugger. 

Now, if you're experienced with computation expressions, you might be thinking that there should be another advantage to the computation expressions approach: composabililty. We should be able to compose smaller computation expressions into bigger computation expressions to build up functionality. But, this won't work with the `actor` computation expression. To illustrate what I mean, say that we want to write a computation expression that receives messages until it gets either a `string` or a `System.Double`, at which point we print out what we got. One might try to write this so that we get a reusable function for filtering messages:

```f#
type Msg = 
    | S of string
    | D of System.Double

let rec filterMsgs (ctx: Akkling.Actors.Actor<obj>) = actor {
    match! ctx.Receive() with 
    | :? string as s -> return S s
    | :? System.Double as d -> return D d
    | _ -> return! filterMsgs ()
}
let filterActor parent =
    
    Akkling.Spawn.spawn parent "filter"
        (Akkling.Props.props <| fun (ctx: Akkling.Actors.Actor<obj>) ->

            let printMsg = 
                actor {
                    match! filterMsgs () with 
                    | S s -> printfn $"Got string: {s}"
                    | D d -> printfn $"Got double: {d}"
                }
            
            printMsg
        )
```

The problem is that this is not valid code. The `return` expressions in `filterMsgs` will not compile since the `actor` expression must evaluate to an `Effect`. But, there is no effect to *return a value*, and just using `return` on a regular value in an `actor` expression doesn't generate an `Effect`. To accomplish what we're trying to do here we need to have multiple message handling functions and switch between them. We can't create a stand-alone `filterMsgs` function like this that is re-usable. Now there is machinery in Akka.NET and Akkling for creating a *call-stack* of message handling functions, but it doesn't result in code that is as easy to read as the above. And, it leads us right back to where we were before, the code that uses computation expressions isn't any easier to read or write than that which uses `become`.

Now, it would be nice to be able to write code like the above for actors. And with a new library, it is possible:

```f#
open WAkka.Simple

type Msg = 
    | S of string
    | D of System.Double
    
let rec filterMsgs () = actor {
    match! Receive.Any() with 
    | :? string as s -> return S s
    | :? System.Double as d -> return D d
    | _ -> return! filterMsgs ()
}

let printMsg = 
    actor {
        match! filterMsgs () with 
        | S s -> printfn $"Got string: {s}"
        | D d -> printfn $"Got double: {d}"
    }
```

This will compile and run, but note the `open WAkka.Simple` line at the start. That's to open the new library that we created at Wyatt Technology and that I recently convinced the higher-ups to let me open source. The resulting library, [WAkka](https://github.com/WyattTechnology/WAkka), is available now on Nuget. The name is not *great*, I admit that. It's sort of short for *Wyatt Technology Akka*, and stuck once we started using it internally, and I haven't been able to come up with anything better. So, check it out, the [readme](https://github.com/WyattTechnology/WAkka#readme) has a lot more examples of usage, goes into more detail on what functionality is available, and talks about some drawbacks (there's always trade-offs).


