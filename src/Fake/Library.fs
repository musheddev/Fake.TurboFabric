namespace TurboFabric
open Fake.Core
open Fake.IO
open Fake.DotNet
open Microsoft.ServiceFabric.Client.Http
open System.Threading.Tasks
open Microsoft.ServiceFabric.Common.Security
open System.Collections.Generic
open Microsoft.ServiceFabric.Client
open System
open System.Threading
open Microsoft.ServiceFabric.Client
open Microsoft.ServiceFabric.Common.Security
open Microsoft.ServiceFabric.Client.Http
open FSharp.Data
open System.Xml.Linq

module Fake =

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


