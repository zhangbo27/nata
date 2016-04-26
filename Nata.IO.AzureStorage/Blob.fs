﻿namespace Nata.IO.AzureStorage

open System
open System.IO
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Blob
open Nata.IO

module Blob =

    let container name (account:Account) =
        let client = account.CreateCloudBlobClient()
        let container = client.GetContainerReference(name)
        let result = container.CreateIfNotExists()
        container

    module Block =
    
        let write (container:CloudBlobContainer) (blobName:string) =
            let reference = container.GetBlockBlobReference(blobName)
            fun (event:Event<byte[]>) ->
                reference.UploadFromByteArray(event.Data, 0, event.Data.Length)

        let writeTo (container:CloudBlobContainer) (blobName:string) =
            let reference = container.GetBlockBlobReference(blobName)
            fun (position:Position<string>) ->
                let condition = function
                    | Position.Start -> AccessCondition.GenerateIfNotExistsCondition()
                    | Position.Before x -> AccessCondition.GenerateEmptyCondition()
                    | Position.At etag -> AccessCondition.GenerateIfMatchCondition(etag)
                    | Position.After x -> AccessCondition.GenerateEmptyCondition()
                    | Position.End -> AccessCondition.GenerateEmptyCondition()
                fun (event:Event<byte[]>) ->
                    reference.UploadFromByteArray(event.Data, 0, event.Data.Length, condition position)
                    reference.Properties.ETag

        let readFrom (container:CloudBlobContainer) (blobName:string) =
            let reference = container.GetBlockBlobReference(blobName)
            let condition = function
                | Position.Start -> AccessCondition.GenerateIfNotExistsCondition()
                | Position.Before x -> AccessCondition.GenerateEmptyCondition()
                | Position.At etag -> AccessCondition.GenerateIfMatchCondition(etag)
                | Position.After x -> AccessCondition.GenerateEmptyCondition()
                | Position.End -> AccessCondition.GenerateEmptyCondition()
            Seq.unfold <| fun (position:Position<string>) ->
                try 
                    use memory = new MemoryStream()
                    reference.DownloadToStream(memory, condition position)
                    
                    let bytes = memory.ToArray()
                    let created =
                        reference.Properties.LastModified
                        |> Nullable.map DateTime.ofOffset
                    let etag, contentType =
                        reference.Properties.ETag,
                        reference.Properties.ContentType
                    let event =
                        Event.create bytes
                        |> Event.withName blobName
                        |> Event.withTag etag
                        |> Event.withEventType contentType
                        |> Event.withCreatedAtNullable created
                    Some ((event, etag), position)
                with
                | :? StorageException as e when
                    [ 404   // 404 - blob does not yet exist
                      412 ] // 412 - etag for blob has expired or is invalid
                    |> List.exists ((=) e.RequestInformation.HttpStatusCode) -> None

        let read container blobName =
            readFrom container blobName Position.End |> Seq.map fst
            
    module Append =

        open FSharp.Data

        let encode, decode = Event.Codec.EventToString
        let tryDecode line =
            try Some (decode line)
            with _ -> None
    
        let write (container:CloudBlobContainer) (blobName:string) =
            let reference = container.GetAppendBlobReference(blobName)
            reference.CreateOrReplace(AccessCondition.GenerateIfNotExistsCondition())
            encode >> reference.AppendText

        let read (container:CloudBlobContainer) (blobName:string) =
            let reference = container.GetAppendBlobReference(blobName)
            seq {
                use stream = reference.OpenRead()
                use reader = new StreamReader(stream)
                while reader.EndOfStream |> not do
                    yield
                        reader.ReadLine()
                        |> decode
                        |> Event.withName blobName
            }