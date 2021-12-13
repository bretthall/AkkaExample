I've been using Akka.Net quite a bit at work lately, so for this years [F# advent calendar](https://sergeytihon.com/2021/10/18/f-advent-calendar-2021/) you're getting a blog post about using [Akka.Net](https://getakka.net) and [Akkling](https://github.com/Horusiath/Akkling) with F#.

Before people start looking at me funny for speaking gibberish: Akka is an oddly named *actor system* library. What's an *actor* though? Is it some code that *pretends* to do something? Much as an actor in a movie or a play pretends to be someone else? Outside of mocks for testing, that doesn't sound terribly useful. Instead, think of an *actor* as something that *acts*. But, acts on what? In this case: messages. Messages sent by other entities in your system in order to induce the actor to do something. That something could be sending messages to other actors, updating some state internal to the actor, or some other side-effect. The main thing with actors is the processing of messages. There could be some piece of state being managed as well, but that state is only changed in response to messages. Why would someone want to structure a system in this way? I'll leave it to the [Akka.Net website](https://getakka.net) to explain the advantages.

So to start, let's create the simplest actor possible. This actor will accept any message you care to send it, and then dutifully ignore it. This can be done like so:
```
    let actorProps = Akkling.Props.props Akkling.Spawn.ignored
    let actor = Akkling.Spawn.spawn parent name actorProps
```
This code uses the Akkling library, and I'll get to what that adds on top of regular Akka.Net in a moment. Note that I've kept namespaces and modules fully qualified to make it clear where things are coming from. To create an actor we call `Akkling.Spawn.spawn`. This function needs three things: a parent for the actor, a name for the actor, and properties for the actor. Actors are arranged in a hierarchy, and every actor has a parent that has to be given to `spawn` as a `Akka.Actor.IActorRefFactory`. The actor system itself provides a root node for the hierarchy, and that can be used as a parent for top-level actors (technically, regular actors don't have access to the actual root node, but to one of its children that forms the root of the *user* part of the tree). An actor can also use its *actor context* as the parent for creating other actors. In order to get a hold of the context for our actor, we'll need to make it a bit more complicated, which we'll get to in a bit. Every actor also has a path given by a URL of the form `akka://p1/p2/p3/.../pN/name` where name is the name given to `spawn` and the p's are the names of the actor's parent, it's parent's parent, etc. back to the root of the tree. This path must be unique among all actors in the system. There is also a variant of `spawn`, `spawnAnonymous`, that makes up a unique name for you. Finally, there are the properties. There's a lot in there, but for now we're just using `Akkling.Props.props` to create a set of default properties. The one property that can't be defaulted is how you want to process messages. The argument to props is a function of the type `Akkling.Actors.Actor<'msg> -> Akkling.Actors.Effect<'msg>`. `Actor` is the actor context for our actor, `Effect` is a *message handling effect*. In our case we pass `ignored`, which generates an effect that ignores the message passed to it. 

The resturn value of `spawn` has type `Akkling.ActorRefs.IActorRef<'msg>`. This is an *actor reference*, and can be used to send messages to the actor using the *tell* operator `<!`, e.g. `actor <! msg`. The parameter in the type, `'msg`, is the *message type* of the actor reference. Only messages of that type can be sent through the actor reference. Normally, this type is fixed by the function passed to `props` when creating the properties (the above code doesn't do this since `ignored` is generic). When using regular Akka.Net without Akkling, the actor references do not have a message type. They essentially behave like a `IActorRef<obj>`. Akkling adds type safety on top of Akka by giving the actor references message types. I find this tremendously helpful. Without that typing, you have to go look at an actor's implementation or documentation to figure out what messages it will actually do something with. Send them the wrong type of message and it will probably be silently ignored. But with Akkling, the acceptable message type is advertised in the actor reference that you use to send messages to the actor.

This typing does have some subtlety though. It turns out an actor can have lots of message types. The first type is the type that it processes. This is fixed by the function passed to `props` when creating the actor. We can also control the message type on actor references using the `Akkling.ActorRefs.retype` function. Let's create our actor in a more verbose fashion. It will still ignore everything, but now we'll take control of our message types:
```
let makeActor parent name =
    let start (_ctx: Akkling.Actors.Actor<obj>) = Akkling.Spawn.become Akkling.Spawn.ignored
    let actorProps = Akkling.Props.props start
    Akkling.Spawn.spawn parent name actorProps
```
We've now encapsulated our actor creation inside of a function. We've also changed the function that is passed to `props` so that it now specifies that out actor's context is of type `Actor<obj>`. This means that the message that will be processed are of type `obj`. If we had specified the actor context to be of type `Actor<string>`, then the actor would only process strings. If anything else was sent to it, Akkling would filter out the messages and we would never see them. Using type `obj` for the parameter of the context means that we will get all messages sent to the actor; none of them will be filtered out. The body of `start` is `become ignored`. The result of the function that is passed to `props` must be an `Effect`, but the type of `ignored` is `'msg -> Effect<'msg>` which won't work. So we need to wrap it with `become` which takes a function with signature like `ignored` and turns it into an `Effect`. Note that `spawn` will now return a `IActorRef<obj>` instead of `IActorRef<'a>`, this is due to the type annotation in `start`.

Now, say that we only want the caller of `makeActor` to send strings to the actor, but we still want to be able to process other message types as well. This comes up more often than you might think at first glance, usually due to the need to process messages from the actor system which have their own types. To do this we need to add a few things to the example:
```
let makeActor parent name : IActorRef<string> =
    let start (_ctx: Akkling.Actors.Actor<obj>) = Akkling.Spawn.become Akkling.Spawn.ignored
    let actorProps = Akkling.Props.props start
    let actor = Akkling.Spawn.spawn parent name actorProps
    Akkling.ActorRefs.retype actor
```
First, note that there's now a type annotation for the return type of `makeActor`, `IActorRef<string>`. Second, note that we now apply `retype` to the actor reference returned by `spawn` before returning it. This will convert the `IActorRef<obj>` that we get from `spawn` to a `IActorRef<string>` that we return. I refer to the message type that is actually processed, e.g. the `'msg` in `Actor<'msg>`, as the *internal type*, and the message type in the actor reference as the *external type*. For simple actors, these two types would normally be the same. There are a couple of reasons for them to be different though: the actor needs to be able to process special messages from the actor system and/or the actor (or other closely coupled actors) need to send it messages that you don't want other actors in general to be able to send it. There can also be cases where you give out multiple references to the same actor, with each reference having a different message type.

A quick disclaimer about `retype`: it's dangerous. There are only a few places where I consider it valid to use. The first is within an actor creation function, like `makeActor` above, in order to convert from the internal to an external message type. It can also useful in unit tests. Using it anywhere else and you're asking for trouble. If you have an actor whose internal type is `Foo` and you retype a reference to it to `Bar`, where `Bar` is not derived from `Foo`, then every message you send through the retyped reference will be lost. What external message types an actor can support is an implementation detail that you don't want percolating out through the rest of your codebase, else it becomes very easy for a supposedly private change to an actor to cause breakage all over the place. 

The above actor still doesn't do anything useful, it just does so in a fancier way. So let's add some actual functionality, and also add some state to the actor. Let's make actor that implements a simple key/value store, and to keep it really simple we'll make the keys and values strings. Since actors are all about messages, let's define the messages for our actor first:
```
type Response = {
    key: string
    value: Option<string>
}

type StorageMsg =
    | Query of
        {|
          key:string
          receiver: Akkling.ActorRefs.IActorRef<Response>
        |}
    | Update of
        {|
          key:string
          value: string
        |}
    | Stop
```
Here I've defined two messages. `StorageMsg` is the type of message that can be sent to our storage actor with cases that allow querying values, setting values, and stopping the actor. The `Query` case has a `receiver` member that is a reference to an actor that the response will be sent to as a `Response` object. The same thing could have been accomplished by requiring the sender of the `Query` message to use the *ask* operator instead of the *tell* operator (`<?` versus `<!`) and then sending the response to the sender of the `Query` message (accessed using the `Sender` method of the actor context). I generally prefer to avoid the *ask* operator though, instead making things explicit by putting the receiver of a response in the request message. The *ask* operator returns an `async` operation that will finish when the response is sent. The way this is normally handled is by binding the ask operation into an async workflow that sends the response to the original actor using the tell operator. This is just a long winded way of doing what I've done above. Generally, you want to avoid *ask* as much as possible.

So now, how do the messages get processed? Like so:
```
let startStorageActor parent =
    
    Akkling.Spawn.spawn parent "storage" (Akkling.Props.props <| fun (ctx: Akkling.Actors.Actor<StorageMsg>) ->
        
        let rec handleMsgs values msg =
            match msg with
            | Query query ->
                Akkling.Logging.logDebug ctx $"Got query for {query.key}"
                query.receiver <! {
                    key = query.key
                    value = values |> Map.tryFind query.key
                }
                Akkling.Spawn.ignored msg
            | Update update ->
                Akkling.Logging.logDebug ctx $"Got update for {update.key}: {update.value}"
                let newValues = Map.add update.key update.value values
                Akkling.Spawn.become (handleMsgs newValues)    
            | Stop ->
                Akkling.Logging.logDebug ctx "Got stop message"
                Akkling.Spawn.stop ()
                
        Akkling.Spawn.become (handleMsgs Map.empty)
    )
```
As before, we call `spawn`, pass it the parent, a name, and properties. In this case, the properties have the message handling lambda function bound into them. That lambda defines an inner function, `handleMessages`, that, well, handles messages. We pass it to `become` in order to make it the function that handles the messages. Note that `handleMessages` takes two arguments: the currently set values and the message to process. Akkling supplies the message, but where do the values come from? Note that when we call `become` we partially apply `handleMessages` to `Map.empty` turning it into a `StorageMsg -> Effect<StorageMsg>` function. If you look at the match case for `Update` you'll also see `become (handleMsgs newValues)`. So to update the *state* of the actor we use partial application with the message handling function. 

When we get a `Query` message, we just send a `Response` to the actor named in the `Query` message. then we call `ignored`. We haven't ignored the message, there was an action taken in response, i.e. the sending of the response. But the message did not change the state of the actor. When you see `ignored`, think "the actor state is not changing", which it isn't. Contrast this with the `Update` case: all it does is call `become` to change the message handling function to one that hs the updated set of values bound into it via partial application. In this case we used `handleMsgs` again, but we could put a different function in there if we wanted. If your actor behaves more like a finite state machine, then passing different functions to `become` is the way to implement it. Finally, when handling the `Stop` case of `Respose`, we call `stop` which creates an effect that stops the actor.

So, how do we use this actor. At this point I could launch into how one bootstraps the actor system and so on. But this already getting long, so I'll point you to the [Akkling documentation](https://github.com/Horusiath/Akkling/wiki/Bootstrapping-an-actor-system) for that. Instead, I'll show how to set up some unit tests for the actor using the Akkling test kit and Nunit with FsUnit. The test kit will work with any test framework, I'm just most familiar with NUnit so I used that. Anyway, let's make sure that if we query a value from the actor when nothing has been set that we get an empty response:
```
[<Test>]
let ``query for non-existing value gives correct result`` () =
    TestKit.testDefault <| fun tk ->
        let storage = startStorageActor tk.Sys
        
        let key = "key"
        let receiver = tk.CreateTestProbe "receiver"
        storage <! Query {|key = key; receiver = typed receiver|}
        let response = receiver.ExpectMsg<Response> ()
        response.key |> shouldEqual key
        response.value |> shouldEqual None
```
The whole test is run under a call to `TestKit.testDefault`. This gives a `TestKit.Tck` instance, which gives us access to the test kit functionality. The first bit of this functionality that we use is `tk.Sys`. This is a root actor for our actor system and we use it as the parent for our storage actor. 

The next bit of test kit functionality we use is a *test probe*. This is an actor that, by default, just captures the messages sent to it in a buffer. Above, we create a test probe called `receiver` and then use it in a `Query` message that we send to the storage actor using the *tell* operator  `<!`. Now, we expect that a `Response` message will be sent to the receiver. To test this we use the `ExpectMsg` member of the `receiver` test probe. This function looks at the front of the probe's message buffer, blocking if the buffer is empty. If no message is received within a timeout then the test will fail. If the first message in the queue is not of `Repsonse` type, then the test will also fail. If the actor gets a `Response`, then the message is returned and we do further checks on it to make sure that the value is `None`.

We can also check that querying an existing value from the actor gives the correct result:
```
[<Test>]
let ``query for existing value gives correct result`` () =
    TestKit.testDefault <| fun tk ->
        let receiver = tk.CreateTestProbe "receiver"
        let storage = startStorageActor tk.Sys
        let key = "key"
        let value = "value"
        storage <! Update {|key = key; value = value|}
        
        storage <! Query {|key = key; receiver = typed receiver|}
        let response = receiver.ExpectMsg<Response> ()
        response.key |> shouldEqual key
        response.value |> shouldEqual (Some value)
```
We proceed as before, but first send an `Update` message to set a value, followed by a query to get the same value. 

The test kit has much more functionality than I've shown here. Just `TestProbe.ExpectMsg` alone has a bunch more variants. There's also the ability to control the flow of time for the message scheduler which makes testing timeouts and the like very easy.

There's a lot more to Akka and Akkling that I haven't touched on here: error handling using supervision hierarchies, actor persistence, the Akka IO system for dealing with socket communication, and Akka remoting for building distributed actor system, to name a few. I had originally hoped to touch on many of those topics, but this post is already getting long and I'm running out of time. Go figure, programmer underestimates how long it will take to do something. Really, there's a whole stand-alone advent calendar's worth of posts here. If I can keep my blogging act together, then I hope to do more posts expanding on what we've done here. But for now, you can view the code for the storage all pulled together on [github](https://github.com/bretthall/AkkaExample).