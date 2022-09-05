module AkkaExample.Storage

open Akkling

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

let startStorageActor parent =
    
    Akkling.Spawn.spawn parent "storage"
        (Akkling.Props.props <| fun (ctx: Akkling.Actors.Actor<StorageMsg>) ->
        
            let rec handleMsgs values msg =
                match msg with
                | Query query ->
                    query.receiver <! {
                        key = query.key
                        value = values |> Map.tryFind query.key
                    }
                    Akkling.Spawn.ignored msg
                | Update update ->
                    let newValues = Map.add update.key update.value values
                    Akkling.Spawn.become (handleMsgs newValues)    
                | Stop ->
                    Akkling.Spawn.stop ()
                    
            Akkling.Spawn.become (handleMsgs Map.empty)
        )
        
let startStorageActorCE parent =
    
    Akkling.Spawn.spawn parent "storage"
        (Akkling.Props.props <| fun (ctx: Akkling.Actors.Actor<StorageMsg>) ->
            
            let rec handleMsgs values = actor {
                match! ctx.Receive () with
                | Query query ->
                    let! nextMsg = ctx.Receive ()
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
