﻿namespace Nata.IO.AzureStorage.Tests

open System
open System.IO
open System.Collections.Concurrent
open System.Reflection
open System.Threading
open System.Threading.Tasks

open FSharp.Data
open NUnit.Framework
open Nata.IO
open Nata.IO.Capability
open Nata.IO.AzureStorage

[<TestFixture>]
type ContainerTests() =

    let account() = Account.create Emulator.Account.connectionString
    let name() = Guid.NewGuid().ToString("n")

    [<Test>]
    member x.TestCreateDevelopmentContainer() =
        let container = Blob.container (name()) (account())
        Assert.True(container.Exists())