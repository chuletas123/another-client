using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AnotherClient.Installer
{
    internal static class Tests
    {
        private static string directory,key;private static RSACryptoServiceProvider rsa;private static int checks;
        private static void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);checks++;}
        private static void Reject(Action action,string label){try{action();}catch(Exception e){if(e is IOException||e is InvalidDataException||e is ArgumentException||e is CryptographicException||e is FormatException){checks++;return;}throw;}throw new Exception("NOT REJECTED: "+label);}
        private sealed class Package
        {
            internal string Zip;internal ReleaseSpec Spec;internal byte[] Envelope;
        }
        private static byte[] Sign(ReleaseSpec spec)
        {
            byte[] payload=Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(spec));
            return Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(new SignedEnvelope{schemaVersion=1,payload=Convert.ToBase64String(payload),signature=Convert.ToBase64String(rsa.SignData(payload,CryptoConfig.MapNameToOID("SHA256")))}));
        }
        private static Package Make(int sequence,Action<ZipArchive> extra=null)
        {
            string zip=Path.Combine(directory,"payload-"+Guid.NewGuid().ToString("N")+".zip");
            var contents=new Dictionary<string,byte[]>{{"Another Client.exe",Encoding.UTF8.GetBytes("fixture-exe-"+sequence)},{"ac-player.json",Encoding.UTF8.GetBytes("{}")},{"2026E/content/hello.txt",Encoding.UTF8.GetBytes("fixture asset "+sequence)}};
            var hashes=contents.ToDictionary(p=>p.Key,p=>ReleaseTrust.Hash(p.Value));
            byte[] manifest=Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(new {accountsCopied=false,privateKeysCopied=false,files=hashes}));
            contents.Add("client-manifest.json",manifest);
            using(var stream=File.Create(zip))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create)){
                archive.CreateEntry("fixture/");
                foreach(var pair in contents)using(var output=archive.CreateEntry("fixture/"+pair.Key).Open())output.Write(pair.Value,0,pair.Value.Length);
                if(extra!=null)extra(archive);
            }
            var spec=new ReleaseSpec{schemaVersion=1,product="AnotherClient",channel="alpha",platform="windows-x64",version="fixture-"+sequence,sequence=sequence,archiveUrl="https://github.com/chuletas123/another-client/releases/download/fixture/test.zip",topFolder="fixture",archiveBytes=new FileInfo(zip).Length,archiveSha256=ReleaseTrust.HashFile(zip,CancellationToken.None),manifestSha256=ReleaseTrust.Hash(manifest),exeSha256=hashes["Another Client.exe"],fileCount=contents.Count,uncompressedBytes=contents.Values.Sum(v=>(long)v.Length)};
            return new Package{Zip=zip,Spec=spec,Envelope=Sign(spec)};
        }
        private sealed class Transport:IReleaseTransport
        {
            internal Package Package;internal bool Offline,Corrupt;internal int Downloads,Feeds;internal Action DownloadStarted;
            public Task<byte[]> Feed(CancellationToken token){Feeds++;token.ThrowIfCancellationRequested();if(Offline)throw new IOException("Fixture offline");return Task.FromResult(Package.Envelope);}
            public Task Download(ReleaseSpec spec,string target,Action<long> progress,CancellationToken token){Downloads++;if(DownloadStarted!=null)DownloadStarted();token.ThrowIfCancellationRequested();File.Copy(Package.Zip,target);if(Corrupt){using(var stream=new FileStream(target,FileMode.Open,FileAccess.Write))stream.WriteByte(0x13);}progress(spec.archiveBytes);return Task.FromResult(0);}
        }
        private sealed class Handler:HttpMessageHandler
        {
            internal Func<HttpRequestMessage,HttpResponseMessage> Respond;internal int Calls;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Calls++;return Task.FromResult(Respond(request));}
        }
        private static void Network(Package package)
        {
            var handler=new Handler{Respond=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(package.Envelope)}};
            using(var transport=new GitHubTransport(handler))Check(transport.Feed(CancellationToken.None).GetAwaiter().GetResult().SequenceEqual(package.Envelope),"feed bounded streaming response");
            handler=new Handler{Respond=request=>{var r=new HttpResponseMessage(HttpStatusCode.Found);r.Headers.Location=new Uri("http://example.com/unsafe");return r;}};
            using(var transport=new GitHubTransport(handler))Reject(()=>transport.Feed(CancellationToken.None).GetAwaiter().GetResult(),"feed unsafe redirect");
            handler=new Handler{Respond=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[65537])}};
            using(var transport=new GitHubTransport(handler))Reject(()=>transport.Feed(CancellationToken.None).GetAwaiter().GetResult(),"oversized network feed");
            handler=new Handler{Respond=request=>new HttpResponseMessage(HttpStatusCode.NotFound)};
            using(var transport=new GitHubTransport(handler))Reject(()=>transport.Feed(CancellationToken.None).GetAwaiter().GetResult(),"HTTP failure");
            byte[] archive=File.ReadAllBytes(package.Zip);handler=new Handler{Respond=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(archive)}};
            using(var transport=new GitHubTransport(handler)){
                string target=Path.Combine(directory,"network.zip");long read=0;transport.Download(package.Spec,target,n=>read=n,CancellationToken.None).GetAwaiter().GetResult();
                Check(read==package.Spec.archiveBytes&&ReleaseTrust.HashFile(target,CancellationToken.None)==package.Spec.archiveSha256,"download streamed with exact size");
            }
            handler=new Handler{Respond=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[1])}};
            using(var transport=new GitHubTransport(handler))Reject(()=>transport.Download(package.Spec,Path.Combine(directory,"short.zip"),n=>{},CancellationToken.None).GetAwaiter().GetResult(),"content length mismatch");
            handler=new Handler{Respond=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(archive)}};
            using(var transport=new GitHubTransport(handler))using(var cancel=new CancellationTokenSource()){
                cancel.Cancel();bool cancelled=false;try{transport.Download(package.Spec,Path.Combine(directory,"cancelled.zip"),n=>{},cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
                Check(cancelled&&handler.Calls==0,"cancel before network dispatch");
            }
        }
        private static Installation Prepare(InstallerEngine engine,CancellationToken token=default(CancellationToken)){return engine.Prepare(token).GetAwaiter().GetResult();}
        private static string Fresh(){return Path.Combine(directory,Guid.NewGuid().ToString("N"));}
        internal static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
            directory=Path.GetFullPath(args[0]);Directory.CreateDirectory(directory);
            try{
                using(rsa=new RSACryptoServiceProvider(3072)){
                    rsa.PersistKeyInCsp=false;key=rsa.ToXmlString(false);
                    foreach(string bad in new[]{"../x","/x","C:/x","a\\x","a//x","a/./x","a/../x","a/x.","a/x ","nul","a/CON.txt","a/x:ads","a/?","a/COM1.log"})Reject(()=>SafeFiles.Relative(bad),"path "+bad);
                    Check(SafeFiles.Child(directory,"a/b").StartsWith(directory,StringComparison.OrdinalIgnoreCase),"child contained");
                    Reject(()=>SafeFiles.DeleteOwnedTree(directory,Path.GetDirectoryName(directory)),"delete outside fixture");
                    Reject(()=>SafeFiles.DeleteOwnedTree(directory,directory),"delete parent fixture");
                    var first=Make(1);Check(ReleaseTrust.Verify(first.Envelope,key).Spec.sequence==1,"trusted signature");
                    Network(first);
                    string deferredRoot=Fresh();var deferredSource=new Transport{Package=first};var deferredEngine=new InstallerEngine(deferredRoot,key,deferredSource,p=>{},true);
                    var prepared=Prepare(deferredEngine);Check(deferredEngine.Installed()==null,"new client waits for component validation before activation");
                    deferredEngine.Commit(prepared,CancellationToken.None);Check(deferredEngine.Installed().Release.Spec.sequence==1,"component-ready installation can activate");
                    deferredSource.Package=Make(2);var update=Prepare(deferredEngine);Check(deferredEngine.Installed().Release.Spec.sequence==1,"update preparation preserves active version");
                    using(var cancelledCommit=new CancellationTokenSource()){
                        cancelledCommit.Cancel();bool stopped=false;try{deferredEngine.Commit(update,cancelledCommit.Token);}catch(OperationCanceledException){stopped=true;}
                        Check(stopped&&deferredEngine.Installed().Release.Spec.sequence==1,"cancelled component phase cannot activate update");
                    }
                    deferredEngine.Commit(update,CancellationToken.None);Check(deferredEngine.Installed().Release.Spec.sequence==2,"validated update activated after components");
                    byte[] altered=(byte[])first.Envelope.Clone();var env=ReleaseTrust.Json.Deserialize<SignedEnvelope>(Encoding.UTF8.GetString(altered));env.payload=Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
                    Reject(()=>ReleaseTrust.Verify(Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(env)),key),"tampered feed");
                    using(var stranger=new RSACryptoServiceProvider(3072)){stranger.PersistKeyInCsp=false;Reject(()=>ReleaseTrust.Verify(first.Envelope,stranger.ToXmlString(false)),"unknown signer");}
                    string original=first.Spec.archiveUrl;foreach(string bad in new[]{"http://github.com/chuletas123/another-client/releases/download/a/b.zip","https://example.com/a.zip","https://github.com/someone/else/releases/download/a/b.zip","https://github.com/chuletas123/another-client/releases/download/a/b.zip?token=secret"}){
                        first.Spec.archiveUrl=bad;Reject(()=>ReleaseTrust.Verify(Sign(first.Spec),key),"release origin");
                    }first.Spec.archiveUrl=original;
                    string root=Fresh();var progress=new List<InstallProgress>();var source=new Transport{Package=first};
                    var engine=new InstallerEngine(root,key,source,progress.Add);
                    string privateState=Path.Combine(root,"Ac Player","registration-policy","fixture-private");Directory.CreateDirectory(Path.GetDirectoryName(privateState));File.WriteAllText(privateState,"preserved");
                    var installed=Prepare(engine);Check(File.Exists(installed.Executable),"fresh install");Check(engine.Installed()!=null,"current pointer valid");
                    Check(File.ReadAllText(privateState)=="preserved","private state preserved");Check(source.Downloads==1,"download once");
                    Check(progress.Any(p=>p.Message=="Downloading Another Client..."),"download phase");Check(progress.Any(p=>p.Message=="Installing Another Client..."),"install phase");
                    Check(progress.All(p=>p.Percent>=0&&p.Percent<=100),"progress bounded");Check(progress.Zip(progress.Skip(1),(a,b)=>b.Percent>=a.Percent).All(x=>x),"progress monotonic");
                    Prepare(engine);Check(source.Downloads==1,"up-to-date skips download");Check(source.Feeds==2,"launch checks feed");
                    source.Offline=true;Check(Prepare(engine).Release.Spec.sequence==1,"offline starts installed");source.Offline=false;
                    progress.Clear();var second=Make(2);source.Package=second;Prepare(engine);
                    Check(engine.IsUpdating&&progress.Any(p=>p.Message=="Updating Another Client..."),"existing installation uses update text");
                    Check(!progress.Any(p=>p.Message=="Downloading Another Client..."||p.Message=="Installing Another Client..."||p.Message=="Verifying Another Client..."),"update phases never use first-install wording");
                    Check(progress.All(p=>p.Percent>=0&&p.Percent<=100)&&progress.Zip(progress.Skip(1),(a,b)=>b.Percent>=a.Percent).All(v=>v),"update wording preserves bounded monotonic byte progress");
                    var portableProgress=new List<InstallProgress>();var portable=new InstallerEngine(Fresh(),key,new Transport{Package=first},portableProgress.Add,true,true);Prepare(portable);
                    Check(portable.IsUpdating&&portableProgress.Any(p=>p.Message=="Updating Another Client...")&&!portableProgress.Any(p=>p.Message=="Downloading Another Client..."),"portable direct-client update also uses update wording");
                    Check(engine.Installed().Release.Spec.sequence==2,"new version active");Check(engine.Installed("previous.json").Release.Spec.sequence==1,"previous version retained");Check(File.Exists(installed.Executable),"old version untouched");
                    source.Package=first;Check(Prepare(engine).Release.Spec.sequence==2,"signed older feed cannot downgrade");
                    var third=Make(3);source.Package=third;source.Corrupt=true;Check(Prepare(engine).Release.Spec.sequence==2,"bad update starts previous valid");Check(engine.Installed().Release.Spec.sequence==2,"bad update not activated");source.Corrupt=false;
                    using(var cancellation=new CancellationTokenSource()){
                        source.DownloadStarted=()=>cancellation.Cancel();bool cancelled=false;
                        try{Prepare(engine,cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
                        Check(cancelled,"cancellation propagated");Check(engine.Installed().Release.Spec.sequence==2,"cancellation keeps current");
                        source.DownloadStarted=null;
                    }
                    Check(Directory.GetFiles(Path.Combine(root,"Installer","Downloads"),"*.partial").Length==0,"no partial downloads left");
                    File.WriteAllText(Path.Combine(root,"Installer","current.json"),"broken");source.Offline=true;
                    Check(Prepare(engine).Release.Spec.sequence==1,"corrupt pointer recovers previous");source.Offline=false;
                    source.Package=second;string damaged=Path.Combine(root,"Versions",second.Spec.DirectoryName,"Another Client.exe");File.WriteAllText(damaged,"changed");
                    Check(Prepare(engine).Release.Spec.sequence==2,"corrupt version repaired");Check(engine.Installed()!=null,"repaired version verifies");Check(Directory.GetDirectories(Path.Combine(root,"Versions"),"*.invalid-*").Length==1,"damaged directory retained separately");
                    Check(engine.Installed("previous.json").Release.Spec.sequence==1,"repair preserves valid rollback pointer");
                    Check(File.ReadAllText(privateState)=="preserved","private state survives update and repair");
                    source.Package=third;
                    using(var cancel=new CancellationTokenSource()){
                        var cancelling=new InstallerEngine(root,key,source,p=>{if(p.Percent>=79&&p.Percent<98)cancel.Cancel();});
                        bool cancelled=false;try{Prepare(cancelling,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
                        Check(cancelled,"cancel during extraction");Check(engine.Installed().Release.Spec.sequence==2,"extraction cancellation preserves current");
                        Check(Directory.GetDirectories(Path.Combine(root,"Installer","Staging")).Length==0,"cancelled extraction removes only its staging");
                    }
                    // Each malicious archive is rejected before any extracted files can escape staging.
                    foreach(string path in new[]{"fixture/../escape","fixture/C:/escape","fixture/a\\b","fixture/Another Client.exe"}){
                        var malicious=Make(4,z=>{using(var w=z.CreateEntry(path).Open())w.WriteByte(1);});string target=Fresh();Directory.CreateDirectory(target);
                        Reject(()=>InstallerEngine.Extract(malicious.Zip,target,malicious.Spec,CancellationToken.None,n=>{}),"malicious zip "+path);
                        Check(Directory.GetFiles(target,"*",SearchOption.AllDirectories).Length==0,"preflight leaves no output");
                    }
                    var link=Make(4,z=>{var entry=z.CreateEntry("fixture/link");entry.ExternalAttributes=unchecked((int)0xa1ff0000);});string linkTarget=Fresh();Directory.CreateDirectory(linkTarget);
                    Reject(()=>InstallerEngine.Extract(link.Zip,linkTarget,link.Spec,CancellationToken.None,n=>{}),"zip symlink");
                    // Real release verification is optional; it never launches the engine or writes AppData.
                    if(args.Length==4){
                        string publicKey=File.ReadAllText(args[2]);var actual=ReleaseTrust.Verify(File.ReadAllBytes(args[3]),publicKey);
                        Check(ReleaseTrust.HashFile(args[1],CancellationToken.None)==actual.Spec.archiveSha256,"actual archive digest");
                        string target=Fresh();Directory.CreateDirectory(target);InstallerEngine.Extract(args[1],target,actual.Spec,CancellationToken.None,n=>{});InstallerEngine.VerifyFolder(target,actual.Spec,CancellationToken.None,true);
                        Check(Directory.GetFiles(target,"*",SearchOption.AllDirectories).Length==actual.Spec.fileCount,"all real client files verified");
                        Check(!Directory.GetFiles(target,"*",SearchOption.AllDirectories).Any(p=>p.EndsWith(".pfx",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".dpapi",StringComparison.OrdinalIgnoreCase)),"no private key artifacts");
                    }
                }
                Console.WriteLine("PASS: "+checks+" checks. No windows opened, no Player started, no account data changed.");return 0;
            }catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
        }
    }
}
