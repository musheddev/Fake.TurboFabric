namespace TurboFabric.ParameterServer

open System
open System.Threading
open Microsoft.ServiceFabric.Services.Runtime

open System.Threading.Tasks
open System.Diagnostics.Tracing
open System.Diagnostics

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

module Tests =

    open Expecto

    [<Tests>]
    let tests =
      testList "samples" [
        testCase "universe exists (╭ರᴥ•́)" <| fun _ ->
          let subject = true
          Expect.isTrue subject "I compute, therefore I am."

        testCase "when true is not (should fail)" <| fun _ ->
          let subject = false
          Expect.isTrue subject "I should fail because the subject is false"

        testCase "I'm skipped (should skip)" <| fun _ ->
          Tests.skiptest "Yup, waiting for a sunny day..."

        testCase "I'm always fail (should fail)" <| fun _ ->
          Tests.failtest "This was expected..."

        testCase "contains things" <| fun _ ->
          Expect.containsAll [| 2; 3; 4 |] [| 2; 4 |]
                             "This is the case; {2,3,4} contains {2,4}"

        testCase "contains things (should fail)" <| fun _ ->
          Expect.containsAll [| 2; 3; 4 |] [| 2; 4; 1 |]
                             "Expecting we have one (1) in there"

        testCase "Sometimes I want to ༼ノಠل͟ಠ༽ノ ︵ ┻━┻" <| fun _ ->
          Expect.equal "abcdëf" "abcdef" "These should equal"

        test "I am (should fail)" {
          "╰〳 ಠ 益 ಠೃ 〵╯" |> Expect.equal true false
        }
      ]


module Main =

    type ExpectoService(ctx) =
        inherit StatelessService(ctx)

        override this.RunAsync(cancel) = Task.CompletedTask

        // override this.CreateServiceInstanceListeners() =
        //      [| ServiceInstanceListener(fun serviceContext ->
        //                 KestrelCommunicationListener(serviceContext, "ServiceEndpoint", fun (url, listener) -> WebHostBuilder().Build() ))
        //                  |] |> seq

        
    [<Literal>]
    let Name = "TestIntegrations.Expecto"

    [<EntryPoint>]
    let main args =
        try
            ServiceEventSource.Init(Name)
            let t = ServiceRuntime.RegisterServiceAsync(Name,Func<Fabric.StatelessServiceContext,StatelessService>(fun ctx -> ExpectoService(ctx) :> StatelessService))
            t.GetAwaiter().GetResult()

            ServiceEventSource.ServiceTypeRegistered(typeof<ExpectoService>.Name)

            Thread.Sleep Timeout.Infinite
            0
        with
        | ex -> ServiceEventSource.ServiceHostInitializationFailed(ex); -1