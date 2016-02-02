﻿namespace Nata.IO.Tests

open System
open System.Text
open FSharp.Data
open NUnit.Framework
open Nata.IO
open Nata.IO.Capability
open Nata.IO.Memory

[<TestFixture>]
type ReaderFromTests() =

    let connect(fn) =
        let stream = Stream.connect() (Guid.NewGuid().ToString())
        stream |> readerFrom |> fn,
        stream |> writer

    let event =
        { Type = "event_type"
          Stream = "event_stream"
          Date = DateTime.Now
          Data = ()
          Metadata = () }

    [<Test>]
    member x.MapDataValueTest() =
        let readFrom, write = connect(ReaderFrom.mapData ((+) 1))

        let input = [1;2;3]
        let output = [2;3;4]

        let run() =
            for i in input do
                event |> Event.mapData (fun _ -> i) |> write
            readFrom 0 |> Seq.map (fst >> Event.data) |> Seq.toList

        Assert.AreEqual(output, run())

    [<Test>]
    member x.MapDataTypeTest() =
        let readFrom, write = connect(ReaderFrom.mapData (fun x -> x.ToString()))

        let input = [1;2;3]
        let output = ["1";"2";"3"]

        let run() =
            for i in input do
                event |> Event.mapData (fun _ -> i) |> write
            readFrom 0 |> Seq.map (fst >> Event.data) |> Seq.toList

        Assert.AreEqual(output, run())

    [<Test>]
    member x.MapMetadataValueTest() =
        let readFrom, write = connect(ReaderFrom.mapMetadata (fun i -> i*i))

        let input = [1;2;3]
        let output = [1;4;9]

        let run() =
            for i in input do
                event |> Event.mapMetadata (fun _ -> i) |> write
            readFrom 0 |> Seq.map (fst >> Event.metadata) |> Seq.toList

        Assert.AreEqual(output, run())

    [<Test>]
    member x.MapMetadataTypeTest() =
        let readFrom, write = connect(ReaderFrom.mapMetadata int64)

        let input = [1;2;3]
        let output = [1L;2L;3L]

        let run() =
            for i in [1;2;3] do
                event |> Event.mapMetadata (fun _ -> i) |> write
            readFrom 0 |> Seq.map (fst >> Event.metadata) |> Seq.toList

        Assert.AreEqual(output, run())

    [<Test>]
    member x.MapTest() =
        let mapping = ReaderFrom.map (fun (x:int) -> int64 (x*x)) (fun (x:int) -> (1+x).ToString()) (Codec.Identity)
        let readFrom, write = connect(mapping)

        let input = [1;2;3;4]

        let outputData = [1L;4L;9L;16L]
        let outputMetadata = ["2";"3";"4";"5"]

        let results =
            for i in input do
                event |> Event.map (fun _ -> i) (fun _ -> i) |> write
            readFrom 0 |> Seq.map fst |> Seq.toList


        Assert.AreEqual(outputData, results |> List.map Event.data)
        Assert.AreEqual(outputMetadata, results |> List.map Event.metadata)

