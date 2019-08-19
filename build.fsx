#r "paket: groupref build //"
#load "./.fake/build.fsx/intellisense.fsx"

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

let (+/+) = Path.combine

module DemoSettings =

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



    let pkg = "TestPkg"

    let root = @"C:\Users\Orlando\Desktop\Projects2019\Fake.TurboFabric"
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
open DemoSettings
module TurboFabric =
    open Microsoft.ServiceFabric.Client
    open Microsoft.ServiceFabric.Common.Security
    open Microsoft.ServiceFabric.Client.Http
    open FSharp.Data
    open System.Xml.Linq

    type Schema = XmlProvider<Schema = "C:\\Users\\Orlando\\Desktop\\Projects2019\\Fake.TurboFabric\\ServiceFabricServiceModel.xsd">

    type AppManifest = Schema.ApplicationManifest
    type ServiceManifest = Schema.ServiceManifest
    type Settings = Schema.Settings

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

    let serviceManifest = serviceMani ExpectoService :?> XElement |> ServiceManifest
    let applicationManifest = appMani Application :?> XElement |> AppManifest
    let settings = configMani :?> XElement |> Settings



    let makeFolders (application : ServiceFabricApplication) =
        Directory.ensure packagePrep
        Directory.ensure (packagePrep +/+ pkg)
        Directory.ensure (packagePrep +/+ pkg +/+ "Config")
        
        settings.XElement.Save(packagePrep +/+ pkg +/+ "Config" +/+ "Settings.xml")
        serviceManifest.XElement.Save(packagePrep  +/+ pkg +/+ "ServiceManifest.xml")
        applicationManifest.XElement.Save(packagePrep +/+ "ApplicationManifest.xml")

        Directory.ensure (packagePrep +/+ pkg +/+ "Code")
        //copy build

    let build (application : ServiceFabricApplication) =
        let result =
            DotNet.exec (DotNet.Options.withWorkingDirectory root) "build" application.Services.[0].Proj
        if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" "build" root

        let result =
            DotNet.exec (DotNet.Options.withWorkingDirectory root) "publish" (application.Services.[0].Proj + " -o " + packagePrep +/+ pkg +/+ "Code")
        if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" "publish" root
    // let zip () =
    //     [   !! "ci/build/project1/**/*"
    //         |> Zip.filesAsSpecs "ci/build/project1"
    //         |> Zip.moveToFolder "project1" ]
    //     |> Seq.concat
    //     |> Zip.zipSpec (sprintf @"ci/deploy/project.%s.zip" buildVersion)    

    let getCreds (pfxPath : string) name thumbprint (pass : string) (ct : CancellationToken) =
            let clientCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(pfxPath, pass)
            let name = X509Name(name,thumbprint)
            let remoteSecuritySettings = RemoteX509SecuritySettings(List<X509Name>([name]))
            Task.FromResult<SecuritySettings>(X509SecuritySettings(clientCert, remoteSecuritySettings))

    let connect cluster getcreds =
        let sfClient = ServiceFabricClientBuilder()
                        .UseEndpoints(Uri(@"https://"+cluster+":19080"))
                        .UseX509Security(Func<CancellationToken,Task<SecuritySettings>>(getcreds))
                        .BuildAsync().GetAwaiter().GetResult();
        sfClient

    let push (client : IServiceFabricClient) path storePath =
        let ct = CancellationToken()
        client.ImageStore.UploadApplicationPackageAsync(path,true,storePath,ct)

    let start (client : IServiceFabricClient) appType =
        let ct = CancellationToken()
        let appParams = Dictionary<string, string>()
        let appDesc = Microsoft.ServiceFabric.Common.ApplicationDescription(Microsoft.ServiceFabric.Common.ApplicationName("fabric:/" + ""), appType, "1.0.0", appParams);
        client.Applications.CreateApplicationAsync(appDesc,Nullable<int64>(),ct)

    let provision (client : IServiceFabricClient) appType =
        client.ApplicationTypes.ProvisionApplicationTypeAsync(Microsoft.ServiceFabric.Common.ProvisionApplicationTypeDescription(appType))


let runDotNet cmd workingDir =
    let result =
        DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd ""
    if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir


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


Target.create "Clean" (fun _ ->
    !! "src/**/bin"
    ++ "src/**/obj"
    ++ "prep"
    |> Shell.cleanDirs
)

Target.create "Build" (fun _ ->
    // !! "src/**/*.*proj"
    // |> Seq.iter (DotNet.build id)
    let app = Application
    TurboFabric.makeFolders app
    TurboFabric.build app
)

Target.create "All" ignore

"Clean"
  ==> "Build"
  ==> "All"

Target.runOrDefault "All"
