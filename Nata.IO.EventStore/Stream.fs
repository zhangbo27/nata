﻿namespace Nata.IO.EventStore

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Threading
open NLog.FSharp
open Nata.IO
open EventStore.ClientAPI
open EventStore.ClientAPI.Exceptions
open EventStore.ClientAPI.SystemData


module Stream =

    type Data = byte[]
    type Event = Event<Data>
    type Name = string
    type Index = int
    type Position = Position<Index>
    type Direction = Forward | Reverse

    let private log = new Logger()

    let private decode (resolvedEvent:ResolvedEvent) =
        Event.create           resolvedEvent.Event.Data
        |> Event.withCreatedAt resolvedEvent.Event.Created
        |> Event.withStream    resolvedEvent.Event.EventStreamId
        |> Event.withEventType resolvedEvent.Event.EventType
        |> Event.withBytes     resolvedEvent.Event.Metadata
        |> Event.withIndex    (resolvedEvent.Event.EventNumber |> int64),
        resolvedEvent.Event.EventNumber

    let rec private indexOf = function
        | Position.Start -> StreamPosition.Start
        | Position.Before x -> -1 + indexOf x
        | Position.At x -> x
        | Position.After x -> 1 + indexOf x
        | Position.End -> StreamPosition.End

    let rec private read (connection : IEventStoreConnection)
                         (stream : string)
                         (direction : Direction)
                         (position : Position<Index>) : seq<Event*Index> =
        seq {
            let from = indexOf position
            let sliceTask = 
                match direction with
                | Direction.Forward -> connection.ReadStreamEventsForwardAsync(stream, from, 1000, true)
                | Direction.Reverse -> connection.ReadStreamEventsBackwardAsync(stream, from, 1000, true)
            let slice = sliceTask |> Async.AwaitTask |> Async.RunSynchronously
            match slice.Status with
            | SliceReadStatus.StreamDeleted -> 
                log.Warn "Stream %s at (%d) was deleted." stream from
            | SliceReadStatus.StreamNotFound -> 
                log.Warn "Stream %s at (%d) was not found." stream from
            | SliceReadStatus.Success ->
                if slice.Events.Length > 0 then
                    for resolvedEvent in slice.Events ->
                        decode resolvedEvent
                    yield! read connection stream direction (Position.At slice.NextEventNumber)
            | x -> failwith (sprintf "Stream %s at (%d) produced undocumented response: %A" stream from x)
        }


    let rec private listen (connection : IEventStoreConnection)
                           (stream : string)
                           (position : Position<Index>) : seq<Event*Index> =                         
        let from = indexOf position
        let queue = new BlockingCollection<Option<Event*Index>>(new ConcurrentQueue<Option<Event*Index>>())
        let subscription =
            let settings = CatchUpSubscriptionSettings.Default
            let onDropped,onEvent,onLive =
                Action<_,_,_>(fun _ _ _ -> queue.Add None),
                Action<_,_>(fun _ -> decode >> Some >> queue.Add),
                Action<_>(ignore)
            match from with
            | -1 -> connection.SubscribeToStreamAsync(stream, true, onEvent, onDropped)
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                    |> Client.Live
            |  0 -> connection.SubscribeToStreamFrom(stream, Nullable(), settings, onEvent, onLive, onDropped)
                    |> Client.Catchup
            |  x -> connection.SubscribeToStreamFrom(stream, Nullable(x-1), settings, onEvent, onLive, onDropped)
                    |> Client.Catchup

        let rec traverse last =
            seq {
                match queue.Take() with
                | Some (event, index) ->
                    yield event, index
                    yield! traverse (index+1)
                | None ->
                    Thread.Sleep(10000)
                    yield! listen connection stream (Position.At last)
            }
        traverse from


    let private write (connection : IEventStoreConnection)
                      (targetStream : string)
                      (position : Position<int>)
                      (event : Event) : Index =
        let rec targetVersionOf = function
            | Position.Start -> ExpectedVersion.EmptyStream
            | Position.At x -> x-1
            | Position.End -> ExpectedVersion.Any  
            | Position.Before x ->
                match targetVersionOf x with
                | index when index < 0 -> raise (Position.Invalid(position))
                | index -> index - 1
            | Position.After x ->
                match targetVersionOf x with
                | index when index < -1 -> raise (Position.Invalid(position))
                | index -> index + 1
        let eventId = Guid.NewGuid()
        let eventPosition = targetVersionOf position
        let eventMetadata = Event.bytes event |> Option.getValueOr [||]
        let eventType = Event.eventType event |> Option.getValueOrYield guid
        let eventData = new EventData(eventId, eventType, true, event.Data, eventMetadata)
        let result =
            connection.AppendToStreamAsync(targetStream, eventPosition, eventData)
            |> Async.AwaitTask
            |> Async.Catch
            |> Async.RunSynchronously
        match result with
        | Choice1Of2 result -> result.NextExpectedVersion
        | Choice2Of2 exn ->
            match exn with
            | :? AggregateException as e ->
                match e.InnerException with
                | :? WrongExpectedVersionException as v ->
                    raise (Position.Invalid(position))
                | _ ->
                    raise exn
            | _ -> raise exn

    let rec index (connection : IEventStoreConnection)
                  (stream : string)
                  (position : Position<Index>) : Index =
        match position with
        | Position.At x -> x
        | Position.Before x -> (index connection stream x) - 1
        | Position.After x -> (index connection stream x) + 1
        | Position.Start ->
            read connection stream Direction.Forward position
            |> Seq.tryPick Some
            |> Option.map snd
            |> Option.getValueOr StreamPosition.Start
        | Position.End ->
            read connection stream Direction.Reverse position
            |> Seq.tryPick Some
            |> Option.map (snd >> (+) 1)
            |> Option.getValueOr StreamPosition.Start


    let connect : Connector<Settings,Name,Data,Index> =
        Client.connect >> (fun connection stream ->
            [   
                Capability.Indexer <|
                    index connection stream

                Capability.Reader <| fun () ->
                    read connection stream Direction.Forward Position.Start |> Seq.map fst

                Capability.ReaderFrom <|
                    read connection stream Direction.Forward

                Capability.Writer <| fun event ->
                    write connection stream Position.End event |> ignore

                Capability.WriterTo <|
                    write connection stream

                Capability.Subscriber <| fun () ->
                    listen connection stream Position.Start |> Seq.map fst

                Capability.SubscriberFrom <|
                    listen connection stream
            ])