namespace TurboFabric.ParameterServer

open System
open System.Threading
open Microsoft.ServiceFabric.Services.Runtime
open Microsoft.ServiceFabric.Services.Communication.Runtime
open System
open System.Threading.Tasks
open System.Diagnostics.Tracing
open System.Diagnostics.Tracing

type ParameterService(ctx) =
    inherit StatelessService(ctx)

    override this.CreateServiceInstanceListeners() =
        Array.empty |> seq

type ServiceEventSource() =
    inherit EventSource()

    [<Event(0,Level = EventLevel.Informational)>]
    member this.Message(args) = this.WriteEvent(0,sprintf "%s" args)

module Main =

    [<EntryPoint>]
    let main args =
        try
            async {
                let t = ServiceRuntime.RegisterServiceAsync("",Func<Fabric.StatelessServiceContext,StatelessService>(fun ctx -> ParameterService(ctx) :> StatelessService))
                do! Async.AwaitTask t
            } |> Async.RunSynchronously
            Thread.Sleep Timeout.Infinite
            0
        with
        | ex -> failwith "fail"