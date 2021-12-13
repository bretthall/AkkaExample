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