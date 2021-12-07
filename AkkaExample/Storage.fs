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
          receiver: IActorRef<Response>
        |}
    | Update of
        {|
          key:string
          value: string
          receiver: IActorRef<Response>
        |}
    | Stop

let startStorageActor parent =
    
    Spawn.spawn parent "storage" (props <| fun (ctx: Actor<StorageMsg>) ->
        
        let rec handleMsgs values msg =
            match msg with
            | Query query ->
                logDebug ctx $"Got query for {query.key}"
                query.receiver <! {
                    key = query.key
                    value = values |> Map.tryFind query.key
                }
                ignored msg
            | Update update ->
                logDebug ctx $"Got update for {update.key}: {update.value}"
                update.receiver <! {
                    key = update.key
                    value = Some update.value
                }
                let newValues = Map.add update.key update.value values
                become (handleMsgs newValues)    
            | Stop ->
                logDebug ctx "Got stop message"
                stop ()
                
        become (handleMsgs Map.empty)
    )