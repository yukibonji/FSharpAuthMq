﻿namespace Host

open System
open System.Collections.Generic
open System.Text
open ZeroMQ
open ServiceHost.Pipeline
open ServiceHost.Pipeline.PipelineExecution
open Newtonsoft.Json
open Newtonsoft.Json.FSharp
open Serializer.Json

module ServiceHost =

    let handlingPipeline pipelineState =
        async { return Continue pipelineState }
        |> bind Service.Authentication.authenticate
        |> bind Service.Authentication.revoke

    let private startService serialize deserialize (socket:ZmqSocket) =
        async {
            try
                
                while true do
                    let request = socket.Receive Encoding.UTF8
                    match request with 
                    | null -> socket.Send("Empty Packet Received", Encoding.UTF8) |> ignore //something went wrong
                    | _    -> let deserializedRequest = deserialize request
                              let! response = handlingPipeline ({ request=deserializedRequest; environment=Map.empty })
                              let (serializedResponse:string) =
                                  match response with
                                  | Handled s     -> serialize (s.environment.Item "result")
                                  | Continue s    -> "fail - no science"
                                  | Abort a       -> a.errorMessage
                              socket.Send(serializedResponse, Encoding.UTF8) |> ignore
            with
            | e -> printfn "%A" e
        }

    let initializeService (context:ZmqContext) endpoint =
        let converters : JsonConverter[] = [|UnionConverter<Service.Authentication.Command> ()|]
        context
        |> Zmq.RequestResponse.responder endpoint
        |> startService (serialize [||]) (deserialize converters)
        |> Async.Start

    [<EntryPoint>]
    let main argv =
        use context = ZmqContext.Create()
        initializeService context "tcp://*:5556"
        Console.ReadLine() |> ignore
        0