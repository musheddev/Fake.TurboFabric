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



module Fake =
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

