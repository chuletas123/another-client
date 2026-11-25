using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnotherClient.Updates;
namespace AnotherClient.Installer
{
    internal static class ClientCheckTests
    {
        private static int checks;private static string key,root;private static RSACryptoServiceProvider rsa;
        private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        private static void Reject(Action action,string message){try{action();}catch(Exception e){if(e is IOException||e is InvalidDataException||e is ArgumentException||e is FormatException){checks++;return;}throw;}throw new Exception("Not rejected: "+message);}
        private static byte[] Sign(ReleaseSpec spec){byte[] data=Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(spec));return Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(new SignedEnvelope{schemaVersion=1,payload=Convert.ToBase64String(data),signature=Convert.ToBase64String(rsa.SignData(data,CryptoConfig.MapNameToOID("SHA256")))}));}
        private sealed class Source:IReleaseTransport
        {
            internal byte[] Value;internal string Archive;internal bool Offline;internal int Calls;
            public Task<byte[]> Feed(CancellationToken token){Calls++;if(Offline)throw new IOException("offline");return Task.FromResult(Value);}
            public Task Download(ReleaseSpec spec,string target,Action<long> progress,CancellationToken token){File.Copy(Archive,target);progress(spec.archiveBytes);return Task.FromResult(0);}
        }
        private static Source Package(int sequence)
        {
            var files=new Dictionary<string,byte[]>{{"Another Client.exe",Encoding.UTF8.GetBytes("fixture-"+sequence)},{"ac-player.json",Encoding.UTF8.GetBytes("{}")}};
            var manifest=Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(new{accountsCopied=false,privateKeysCopied=false,files=files.ToDictionary(p=>p.Key,p=>ReleaseTrust.Hash(p.Value))}));
            files.Add("client-manifest.json",manifest);string archive=Path.Combine(root,Guid.NewGuid().ToString("N")+".zip");
            using(var stream=File.Create(archive))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))foreach(var pair in files)using(var output=zip.CreateEntry("fixture/"+pair.Key).Open())output.Write(pair.Value,0,pair.Value.Length);
            var spec=new ReleaseSpec{schemaVersion=1,product="AnotherClient",channel="alpha",platform="windows-x64",version="fixture-"+sequence,sequence=sequence,archiveUrl="https://github.com/chuletas123/another-client/releases/download/fixture/test.zip",topFolder="fixture",archiveBytes=new FileInfo(archive).Length,archiveSha256=ReleaseTrust.HashFile(archive,CancellationToken.None),manifestSha256=ReleaseTrust.Hash(manifest),exeSha256=ReleaseTrust.Hash(files["Another Client.exe"]),fileCount=files.Count,uncompressedBytes=files.Sum(p=>(long)p.Value.Length)};
            return new Source{Value=Sign(spec),Archive=archive};
        }
        private static string Reply(string installRoot){return Path.Combine(installRoot,"Installer","Requests",Guid.NewGuid().ToString("N")+".json");}
        private static string[] Args(long sequence,string reply){return new[]{"--client-check",sequence.ToString(),reply};}
        private static int Main(string[] args)
        {
            try{
                root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
                using(rsa=new RSACryptoServiceProvider(3072)){
                    rsa.PersistKeyInCsp=false;key=rsa.ToXmlString(false);string install=Path.Combine(root,"installed");var source=Package(4);
                    Check(ClientCheck.Read(new string[0],install,key,source)==null,"normal setup mode unaffected");
                    string reply=Reply(install);var check=ClientCheck.Read(Args(3,reply),install,key,source);
                    Check(check.Needed(install,key,source),"newer signed feed updates direct client");Check(source.Calls==1,"only one channel request");
                    var fixedFeed=new PreparedFeedTransport(source,check.Feed);Check(fixedFeed.Feed(CancellationToken.None).Result.SequenceEqual(source.Value)&&source.Calls==1,"prepared feed reused for installation");
                    var current=ClientCheck.Read(Args(4,Reply(install)),install,key,source);Check(!current.Needed(install,key,source),"up-to-date direct client continues");
                    var ahead=ClientCheck.Read(Args(5,Reply(install)),install,key,source);Check(!ahead.Needed(install,key,source),"older feed does not downgrade");
                    var offline=new Source{Offline=true};var unavailable=ClientCheck.Read(Args(3,Reply(install)),install,key,offline);Check(!unavailable.Needed(install,key,offline),"offline current client can start");
                    var invalid=new Source{Value=Encoding.UTF8.GetBytes("invalid")};var malformed=ClientCheck.Read(Args(3,Reply(install)),install,key,invalid);Check(!malformed.Needed(install,key,invalid),"invalid manifest cannot block existing client");
                    Reject(()=>ClientCheck.Read(Args(0,Reply(install)),install,key,source),"invalid current sequence");
                    Reject(()=>ClientCheck.Read(Args(3,Path.Combine(root,"outside.json")),install,key,source),"reply outside installer requests");
                    Reject(()=>ClientCheck.Read(Args(3,Path.Combine(install,"Installer","Requests","not-random.json")),install,key,source),"non-random reply path");
                    var engine=new InstallerEngine(install,key,fixedFeed,p=>{},true);var prepared=engine.Prepare(CancellationToken.None).GetAwaiter().GetResult();
                    Check(engine.Installed()==null,"direct update waits before activation");engine.Commit(prepared,CancellationToken.None);
                    Check(check.Complete(prepared,key)&&File.Exists(reply),"completed update writes signed reply");
                    Check(File.ReadAllBytes(reply).SequenceEqual(source.Value),"reply only contains public signed release");
                    var accepted=ClientStartupUpdates.AcceptReply(File.ReadAllBytes(reply),install,key,3);Check(accepted.Executable==prepared.Executable,"client independently verifies installed reply");
                    Reject(()=>ClientStartupUpdates.AcceptReply(File.ReadAllBytes(reply),install,key,4),"reply must be newer");
                    Reject(()=>ClientStartupUpdates.AcceptReply(Encoding.UTF8.GetBytes("broken"),install,key,3),"invalid reply rejected");
                    string passport=Convert.ToBase64String(source.Value);
                    Check(ClientStartupUpdates.AcceptHandoff(passport,install,prepared.Executable,key,4),"verified new client skips duplicate update request once");
                    Check(!ClientStartupUpdates.AcceptHandoff(passport,install,prepared.Executable,key,3),"handoff sequence bound to current executable");
                    Check(!ClientStartupUpdates.AcceptHandoff(passport,install,Path.Combine(root,"other.exe"),key,4),"handoff path bound to version directory");
                    Check(!ClientStartupUpdates.AcceptHandoff(null,install,prepared.Executable,key,4),"normal startup has no bypass");
                    Check(!ClientStartupUpdates.AcceptHandoff(new string('x',8193),install,prepared.Executable,key,4),"handoff size bounded");
                    var offlineWithNew=ClientCheck.Read(Args(3,Reply(install)),install,key,offline);Check(offlineWithNew.Needed(install,key,offline),"verified installed update available while offline");
                    File.WriteAllText(prepared.Executable,"modified");Check(!ClientStartupUpdates.AcceptHandoff(passport,install,prepared.Executable,key,4),"modified new executable does not receive a bypass");
                    Reject(()=>ClientStartupUpdates.AcceptReply(source.Value,install,key,3),"modified installed reply not launched");
                    Check(!ClientStartupUpdates.BeforeStartup(new[]{"--offline"}),"existing explicit offline mode has no updater process");
                }
                Console.WriteLine("PASS: "+checks+" direct-client update checks; no updater, Player or window started.");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
