namespace Fake
open Fake.Core
open Fake.IO

module TurboFabric =
    let hello name =
        printfn "Hello %s" name
