using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Reflection;
using System.Collections.Generic;

namespace AnotherClient.Installer
{
    // Publisher only. Neither this tool nor the private key is included in the downloader.
    internal static class Publisher
    {
        private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("AnotherClient.UpdateSigning.v1");
        private static string KeyPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnotherClientPublisher","update-signing-key.dpapi");}}
        private static void Restrict(string directory)
        {
            var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
            var inheritance=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,inheritance,PropagationFlags.None,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,inheritance,PropagationFlags.None,AccessControlType.Allow));
            Directory.SetAccessControl(directory,acl);
        }
        private static RSACryptoServiceProvider Key(bool create)
        {
            SafeFiles.NoReparse(KeyPath);var rsa=new RSACryptoServiceProvider(3072);rsa.PersistKeyInCsp=false;
            if(File.Exists(KeyPath)){
                byte[] clear=ProtectedData.Unprotect(File.ReadAllBytes(KeyPath),Entropy,DataProtectionScope.CurrentUser);
                try{rsa.FromXmlString(Encoding.UTF8.GetString(clear));}finally{Array.Clear(clear,0,clear.Length);}
            }else{
                if(!create)throw new IOException("Publisher signing key is unavailable in this Windows account.");
                string directory=Path.GetDirectoryName(KeyPath);Directory.CreateDirectory(directory);Restrict(directory);
                byte[] clear=Encoding.UTF8.GetBytes(rsa.ToXmlString(true));
                try{SafeFiles.WriteAtomic(KeyPath,ProtectedData.Protect(clear,Entropy,DataProtectionScope.CurrentUser));}finally{Array.Clear(clear,0,clear.Length);}
            }
            if(rsa.KeySize!=3072)throw new InvalidDataException("Unexpected publisher key size.");return rsa;
        }
        internal static int Main(string[] args)
        {
            try{
                if(args.Length==3&&args[0]=="read-channel"){
                    var trusted=ReleaseTrust.Verify(File.ReadAllBytes(args[1]),File.ReadAllText(args[2]));
                    Console.WriteLine(ReleaseTrust.Json.Serialize(trusted.Spec));return 0;
                }else if(args.Length==3&&args[0]=="verify-client"){
                    // Read resources and run offline configuration/manifest validation; never invoke Main.
                    string executable=Path.GetFullPath(args[1]);var client=Assembly.Load(File.ReadAllBytes(executable));
                    string catalogText;
                    using(var input=client.GetManifestResourceStream("AnotherClient.ReleaseCatalog")){
                        if(input==null)throw new IOException("Client has no embedded release identity.");
                        using(var reader=new StreamReader(input))catalogText=reader.ReadToEnd();
                    }
                    var catalog=ReleaseTrust.Json.Deserialize<Dictionary<string,object>>(catalogText);
                    var embedded=ReleaseTrust.Json.Deserialize<Dictionary<string,object>>(Convert.ToString(catalog["manifestTemplate"]).Replace("__ANOTHER_CLIENT_SELF_SHA256__",ReleaseTrust.HashFile(executable,System.Threading.CancellationToken.None)));
                    var external=ReleaseTrust.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));
                    if(Convert.ToInt64(embedded["updateChannelSequence"])!=Convert.ToInt64(external["updateChannelSequence"]))throw new IOException("Embedded and external update sequences differ. Rebuild the client release identity before publishing.");
                    var a=ReleaseTrust.Json.ConvertToType<Dictionary<string,string>>(embedded["files"]);
                    var b=ReleaseTrust.Json.ConvertToType<Dictionary<string,string>>(external["files"]);
                    if(a.Count!=b.Count)throw new IOException("Embedded and external client inventories differ.");
                    foreach(var entry in a){string value;if(!b.TryGetValue(entry.Key,out value)||value!=entry.Value)throw new IOException("Embedded client file digest differs: "+entry.Key);}
                    // Exercise the shipped client's offline manifest policy, including pinned engine hashes.
                    var configuration=client.GetType("AnotherClient.Player.PlayerConfiguration",true).GetMethod("Load",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{Path.GetDirectoryName(Path.GetFullPath(args[2]))});
                    client.GetType("AnotherClient.Player.PackageManifest",true).GetMethod("Load",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{configuration});
                    Console.WriteLine("Verified embedded client sequence "+Convert.ToString(embedded["updateChannelSequence"])+" and "+a.Count+" file digests. No client started.");
                }else if(args.Length==2&&args[0]=="public-key"){
                    using(var rsa=Key(true))SafeFiles.WriteAtomic(Path.GetFullPath(args[1]),Encoding.UTF8.GetBytes(rsa.ToXmlString(false)));
                }else if(args.Length==3&&args[0]=="sign"){
                    byte[] payload=File.ReadAllBytes(args[1]);
                    using(var rsa=Key(false)){
                        var envelope=new SignedEnvelope{schemaVersion=1,payload=Convert.ToBase64String(payload),signature=Convert.ToBase64String(rsa.SignData(payload,CryptoConfig.MapNameToOID("SHA256")))};
                        byte[] encoded=Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(envelope));
                        ReleaseTrust.Verify(encoded,rsa.ToXmlString(false));SafeFiles.WriteAtomic(Path.GetFullPath(args[2]),encoded);
                    }
                }else throw new ArgumentException("Usage: Publisher public-key <public.xml> | sign <release.json> <channel.json> | verify-client <client.exe> <manifest.json> | read-channel <channel.json> <public.xml>");
                Console.WriteLine("Publisher operation completed.");return 0;
            }catch(Exception e){while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;var code=e.GetType().GetField("Code");Console.Error.WriteLine(code==null?e.Message:Convert.ToString(code.GetValue(e)));return 1;}
        }
    }
}
