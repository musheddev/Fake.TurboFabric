namespace TurboFabric.ParameterServer

open System
open System.Threading
open Microsoft.ServiceFabric.Services.Runtime
open Microsoft.ServiceFabric.Services.Communication.Runtime
open Microsoft.ServiceFabric.Services.Communication.AspNetCore

open System
open System.Threading.Tasks
open System.Diagnostics.Tracing
open System.Diagnostics.Tracing
open System.Diagnostics
open System.Threading

[<RequireQualifiedAccess>]
module ServiceEventSource =

    [<Literal>]
    let MessageEventId = 1
    [<Literal>]
    let ServiceTypeRegisteredEventId = 3
    [<Literal>]
    let ServiceHostInitializationFailedEventId = 4
    [<Literal>]
    let ServiceWebHostBuilderFailedEventId = 5

    type ServiceEventSource(name : string) =
        inherit EventSource(name)

        [<Event(MessageEventId,Level = EventLevel.Informational)>]
        member this.Message(args) =
            if this.IsEnabled() then
                this.WriteEvent(MessageEventId,sprintf "%s" args)

        [<Event(ServiceTypeRegisteredEventId,Level = EventLevel.Informational)>]
        member this.ServiceTypeRegistered( hostProcessId, serviceType) =
            this.WriteEvent(ServiceTypeRegisteredEventId,sprintf "Service host process %i registered service type %s" hostProcessId serviceType)

        [<Event(ServiceHostInitializationFailedEventId, Level = EventLevel.Error)>]
        member this.ServiceHostInitializationFailed(e : Exception) =
            this.WriteEvent(ServiceHostInitializationFailedEventId, sprintf "Service host initialization failed: %s" e.Message)

        [<Event( ServiceWebHostBuilderFailedEventId, Level = EventLevel.Error)>]
        member this.ServiceWebHostBuilderFailed(e : Exception) =
            this.WriteEvent( ServiceWebHostBuilderFailedEventId, sprintf "Service Owin Web Host Builder Failed: %s" e.Message)


    type private IDependency =
        abstract Current : ServiceEventSource

    let mutable private serviceEventSourceBox : IDependency =
      { new IDependency with
        member x.Current = failwith "Not initialized: call ServiceEventSource.Init(name) first." }

    let Init name =
        serviceEventSourceBox <-
            { new IDependency with
                member x.Current = new ServiceEventSource(name)}

    let private current = serviceEventSourceBox.Current

    let ServiceWebHostBuilderFailed (e : Exception) = current.ServiceWebHostBuilderFailed(e)

    let ServiceHostInitializationFailed (e : Exception) = current.ServiceHostInitializationFailed(e)

    let ServiceTypeRegistered name =
        current.ServiceTypeRegistered(Process.GetCurrentProcess().Id,name)

    let Message message = current.Message(message)

module Main =

    type ParameterService(ctx) =
        inherit StatelessService(ctx)

        override this.CreateServiceInstanceListeners() =
             [| ServiceInstanceListener(fun serviceContext ->
                        KestrelCommunicationListener(serviceContext, "ServiceEndpoint", fun (url, listener) -> WebHostBuilder().Build() ))
                         |] |> seq


    [<EntryPoint>]
    let main args =
        try
            let name = ""
            ServiceEventSource.Init(name)
            let t = ServiceRuntime.RegisterServiceAsync(name,Func<Fabric.StatelessServiceContext,StatelessService>(fun ctx -> ParameterService(ctx) :> StatelessService))
            t.GetAwaiter().GetResult()

            ServiceEventSource.ServiceTypeRegistered(typeof<ParameterService>.Name)

            Thread.Sleep Timeout.Infinite
            0
        with
        | ex -> ServiceEventSource.ServiceHostInitializationFailed(ex); -1