#r "paket: groupref build //"
#load "./.fake/build.fsx/intellisense.fsx"
//#load "./.paket/load/netcoreapp2.1/Build/build.group.fsx"

#if !FAKE
#r "netstandard"
#r "Facades/netstandard" // https://github.com/ionide/ionide-vscode-fsharp/issues/839#issuecomment-396296095
#endif

open System

open Fake.Core
open Fake.DotNet
open Fake.IO

open Fake.Core
open Fake.IO
open Fake.DotNet
open Fake.Core.Xml
open Fake.Core.TargetOperators
open Fake.IO.Globbing.Operators


open System.Threading.Tasks
open System.Collections.Generic
open System
open System.Threading
//open System.Management.Automation

let (+/+) = Path.combine

let root = @"C:\Users\danderegg\Desktop\Projects\Fake.TurboFabric"


module TurboFabric =
    open Microsoft.ServiceFabric.Client
    open Microsoft.ServiceFabric.Common.Security
    open Microsoft.ServiceFabric.Client.Http
    open FSharp.Data
    open System.Xml.Linq

    type Schema = XmlProvider<Schema = @"C:\Users\danderegg\Desktop\Projects\Fake.TurboFabric\ServiceFabricServiceModel.xsd">

    type AppManifest = Schema.ApplicationManifest
    type ServiceManifest = Schema.ServiceManifest
    type Settings = Schema.Settings

    type ServiceFabricService =
        {
            Name : string
            Proj : string
            Version : string
            Dll : string
            Type : string
        }

    type ServiceFabricApplication =
        {
            Name : string
            Version : string
            Type : string
            Services : ServiceFabricService []
        }

    type Creds =
        {
            Endpoint : string
            PfxPath : string
            Pass : string
            Name : string
            Thumbprint : string
        }


    let xName expandedName = XName.Get(expandedName)
    let xElement expandedName content = XElement(xName expandedName, content |> Seq.map (fun v -> v :> obj) |> Seq.toArray) :> obj
    let xAttribute expandedName value = XAttribute(xName expandedName, value) :> obj

    let serviceMani (service : ServiceFabricService) =
        xElement "ServiceManifest" [
            xAttribute "Name" service.Name
            xAttribute "Version" service.Version
            //xAttribute "xmlns" "http://schemas.microsoft.com/2011/01/fabric"
            //xAttribute "xmlns:xsd" "http://www.w3.org/2001/XMLSchema"
            //xAttribute "xmlns:xsi" "http://www.w3.org/2001/XMLSchema-instance"
            xElement "ServiceTypes" [
                xElement "StatelessServiceType" [
                    xAttribute "ServiceTypeName" service.Type
                ]
            ]
            xElement "CodePackage" [
                xAttribute "Name" "Code"
                xAttribute "Version" service.Version
                xElement "EntryPoint" [
                    xElement "ExeHost" [
                        xAttribute "IsExternalExecutable" true
                        xElement "Program" ["dotnet"]
                        xElement "Arguments" [service.Dll]
                    ]
                ]
            ]
            xElement "ConfigPackage" [
                xAttribute "Name" "Config"
                xAttribute "Version" service.Version
            ]
        ]

    let appMani (application : ServiceFabricApplication) =
        xElement "ApplicationManifest" [
            xAttribute "ApplicationManifestName" application.Name
            xAttribute "ApplicationManifestVersion" application.Version
            //xAttribute "xmlns" "http://schemas.microsoft.com/2011/01/fabric"
            //xAttribute "xmlns:xsd" "http://www.w3.org/2001/XMLSchema"
            //xAttribute "xmlns:xsi" "http://www.w3.org/2001/XMLSchema-instance"
            xElement "ServiceManifestImport" [
                xElement "ServiceManifestRef" [
                    xAttribute "ServiceManifestName" application.Services.[0].Name
                    xAttribute "ServiceManifestVersion" application.Services.[0].Version
                ]
            ]
        ]

    let configMani =
        xElement "Settings" [
            //xAttribute "xmlns" "http://schemas.microsoft.com/2011/01/fabric"
            //xAttribute "xmlns:xsd" "http://www.w3.org/2001/XMLSchema"
            //xAttribute "xmlns:xsi" "http://www.w3.org/2001/XMLSchema-instance"
        ]

    let serviceManifest expectoService = serviceMani expectoService :?> XElement |> ServiceManifest
    let applicationManifest application = appMani application :?> XElement |> AppManifest
    let settings = configMani :?> XElement |> Settings



    let makeFolders (application : ServiceFabricApplication) packagePrep pkg=
        Directory.ensure packagePrep
        Directory.ensure (packagePrep +/+ pkg)
        Directory.ensure (packagePrep +/+ pkg +/+ "Config")

        settings.XElement.Save(packagePrep +/+ pkg +/+ "Config" +/+ "Settings.xml")
        (serviceManifest application.Services.[0]).XElement.Save(packagePrep  +/+ pkg +/+ "ServiceManifest.xml")
        (applicationManifest application).XElement.Save(packagePrep +/+ "ApplicationManifest.xml")

        Directory.ensure (packagePrep +/+ pkg +/+ "Code")


    let build (application : ServiceFabricApplication) packagePrep pkg =
        let result =
            DotNet.exec (DotNet.Options.withWorkingDirectory root) "build" application.Services.[0].Proj
        if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" "build" root

        let result =
            DotNet.exec (DotNet.Options.withWorkingDirectory root) "publish" (application.Services.[0].Proj + " -o " + packagePrep +/+ pkg +/+ "Code")
        if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" "publish" root

    let zip packagePrep =
        [   "", !! (packagePrep + "/**/*") ]
        |> Zip.zipOfIncludes "deploy.sfpkg"

    let getCreds (creds : Creds) ct =
            let clientCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(creds.PfxPath, creds.Pass)
            let name = X509Name(creds.Name,creds.Thumbprint)
            let remoteSecuritySettings = RemoteX509SecuritySettings(List<X509Name>([name]))
            Task.FromResult<SecuritySettings>(X509SecuritySettings(clientCert, remoteSecuritySettings))

    let connect creds =
        let sfClient = ServiceFabricClientBuilder()
                        .UseEndpoints(Uri(creds.Endpoint))
                        .UseX509Security(Func<CancellationToken,Task<SecuritySettings>>(getCreds creds))
                        .BuildAsync().GetAwaiter().GetResult();
        sfClient

    let push (client : IServiceFabricClient) path storePath =
        let ct = CancellationToken()
        client.ImageStore.UploadApplicationPackageAsync(path,true,storePath,ct).GetAwaiter().GetResult()

    let start (client : IServiceFabricClient) appType appName =
        let ct = CancellationToken()
        let appParams = Dictionary<string, string>()
        let appDesc = Microsoft.ServiceFabric.Common.ApplicationDescription(Microsoft.ServiceFabric.Common.ApplicationName("fabric:/" + ""), appType, "1.0.0", appParams);
        client.Applications.CreateApplicationAsync(appDesc,Nullable<int64>(),ct)

    let provision (client : IServiceFabricClient) appType =
        client.ApplicationTypes.ProvisionApplicationTypeAsync(Microsoft.ServiceFabric.Common.ProvisionApplicationTypeDescription(appType))


module DemoSettings =
    open TurboFabric
    let pkg = "TestPkg"
    let packagePrep = root +/+ "prep"

    let ExpectoService = {
        Dll = "ExpectoService.dll"
        Name = "Expecto"
        Proj = @"src\ExpectoService\ExpectoService.fsproj"
        Version = "1.0.0"
        Type = "ExpectoServiceType"
    }

    let Application = {
        Services = [|ExpectoService|]
        Name = "Test"
        Version = "1.0.0"
        Type = "TestApplicationType"
    }

    let Creds = {
        Endpoint = @"https://mushed.eastus2.cloudapp.azure.com:19000"
        PfxPath = @"C:\Users\Orlando\Downloads\mushed-mushed-client-20190820.pfx"
        Pass = ""
        Name = "mushedclient"
        Thumbprint = "BA256D31BC18BA647CEFF1498CE7E6FE63400021"
        //cluster id a6611203-9586-4e9a-996a-447f5dea257c
    }

// let runDotNet cmd workingDir =
//     let result =
//         DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd ""
//     if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir


// Target.create "Run" (fun _ ->
//     let server = async {
//         runDotNet "watch run" serverPath
//     }

//     let vsCodeSession = Environment.hasEnvironVar "vsCodeSession"
//     let safeClientOnly = Environment.hasEnvironVar "safeClientOnly"

//     let tasks =
//         [ if not safeClientOnly then yield server]

//     tasks
//     |> Async.Parallel
//     |> Async.RunSynchronously
//     |> ignore
// )
// type System.String with
//     static member Delimit delimiter (items:string seq) =
//         String.Join(delimiter,items |> Array.ofSeq)

// module Proc =
//     // ExecProcess is a thin wrapper around Process.Start
//     // docs at
//     //   https://github.com/fsharp/FAKE/blob/master/src/app/FakeLib/ProcessHelper.fs#L184-L185
//     //   https://github.com/fsharp/FAKE/blob/master/src/app/FakeLib/ProcessHelper.fs#L68-L107
//     let ExecProcessElevated cmd args timeOut =
//         ExecProcess (fun si ->
//             si.Verb <- "runas"
//             si.Arguments <- args
//             si.FileName <- cmd
//             si.UseShellExecute <- true) timeOut
//   // wrapper for fake built-in in case we want the entire process results, not just the exitcode
//   let runElevated cmd args timeOut =
//       let tempFilePath = System.IO.Path.GetTempFileName()
//       // hacky work around for not being able to capture output of an elevated process
//       let resultCode = ExecProcessElevated "cmd" (sprintf "/c %s %s > %s" cmd args tempFilePath) timeOut
//       trace "reading output results of runElevated"
//       let outputResults = File.ReadAllLines tempFilePath
//       File.Delete tempFilePath
//       // idiomaticisizing the result, not needed, especially for uses outside of FAKE
//       let processResult = ProcessResult.New resultCode (ResizeArray<_> outputResults) (ResizeArray<_>())
//       (String.Delimit "\r\n" outputResults)
//       |> trace
//       processResult

// module Sec =
//         open System.Security.Principal
//         let getIsAdmin() =
//             WindowsIdentity.GetCurrent()
//             |> WindowsPrincipal
//             |> fun wp -> wp.IsInRole(WindowsBuiltInRole.Administrator)
//         let requireAdmin () =
//             let runningAsAdmin = getIsAdmin()
//             if not runningAsAdmin then
//                 failwithf "Can't alter hosts file without admin permissions"

// Sec.requireAdmin()

// let retCode =
//     ExecProcess
//       (fun info ->
//         info.Name <- "powershell.exe"  // Don't know if you need full path here
//         info.Verb <- "runas"
//         info.WorkingDirectory <- getBuildParam "BuildRoot"
//         info.UseShellExecute
//         info.Arguments <-
//           [ "-File"; getBuildParam "BuildRoot" + "\DeployScripts\scripts\AdminScripts\VersionUpdateFile.ps1" |> inQuotes;
//             "-path"; getBuildParam "BuildSolutionVersioningConfig" |> inQuotes;
//             "-majorVersion"; getBuildParam "BuildNumberMajor" |> inQuotes;
//             "-minor"; getBuildParam "BuildNumberMinor" |> inQuotes;
//             "-build"; getBuildParam "BuildNumber" |> inQuotes;
//             "-revision"; getBuildParam "BuildNumberRevision" |> inQuotes
//           ] |> separated " "
//       )
//       (TimeSpan.FromMinutes 5.0)
//   if retCode <> 0 then
//     failwith (sprintf "PowerShell exited with non-zero exit code %d" retCode)

// let pshell =
//         let pipe=PowerShell.Create()
//             .AddCommand("invoke-command")
//         pipe.AddParameter("ComputerName", server)
//             .Invoke()
//             |> Seq.map (sprintf  "%O")
//             |> Seq.iter (fun line -> if line.Contains("not installed") then traceError line else trace line)
//         pipe.Streams.Error |> Seq.iter (traceError << sprintf "%O" )


//let connect creds =


Target.create "Clean" (fun _ ->
    !! "src/**/bin"
    ++ "src/**/obj"
    ++ "prep"
    |> Shell.cleanDirs
)

Target.create "Build" (fun _ ->
    // !! "src/**/*.*proj"
    // |> Seq.iter (DotNet.build id)
    let app = DemoSettings.Application
    TurboFabric.makeFolders app DemoSettings.packagePrep DemoSettings.pkg
    TurboFabric.build app DemoSettings.packagePrep DemoSettings.pkg
    TurboFabric.zip DemoSettings.packagePrep

    let client = TurboFabric.connect DemoSettings.Creds
    TurboFabric.push client DemoSettings.packagePrep  DemoSettings.pkg

    ()
)

Target.create "All" ignore

"Clean"
  ==> "Build"
  ==> "All"

Target.runOrDefault "All"
