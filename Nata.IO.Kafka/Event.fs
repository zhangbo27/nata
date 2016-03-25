﻿namespace Nata.IO.Kafka

open System
open System.Text
open Nata.IO

type Data = byte[]
type Event = Event<Data> 

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Event =

    let ofMessage (topic:TopicName) (message:Message) =
        Event.create message.Value
        |> Event.withKey (message.Key |> Encoding.Default.GetString)
        |> Event.withStream topic
        |> Event.withPartition message.PartitionId
        |> Event.withIndex message.Offset

    let toMessage (event:Event) =
        { Message.Value =
            event
            |> Event.data
          Message.PartitionId =
            event
            |> Event.partition 
            |> Option.getValueOr 0
          Message.Offset =
            event
            |> Event.index
            |> Option.getValueOr 0L
          Message.Key =
            event
            |> Event.key
            |> Option.filter (String.IsNullOrWhiteSpace >> not)
            |> Option.map (Encoding.Default.GetBytes)
            |> Option.bindNone (Guid.NewGuid().ToByteArray) }