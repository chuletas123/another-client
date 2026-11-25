using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

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
                if(args.Length==2&&args[0]=="public-key"){
                    using(var rsa=Key(true))SafeFiles.WriteAtomic(Path.GetFullPath(args[1]),Encoding.UTF8.GetBytes(rsa.ToXmlString(false)));
                }else if(args.Length==3&&args[0]=="sign"){
                    byte[] payload=File.ReadAllBytes(args[1]);
                    using(var rsa=Key(false)){
                        var envelope=new SignedEnvelope{schemaVersion=1,payload=Convert.ToBase64String(payload),signature=Convert.ToBase64String(rsa.SignData(payload,CryptoConfig.MapNameToOID("SHA256")))};
                        byte[] encoded=Encoding.UTF8.GetBytes(ReleaseTrust.Json.Serialize(envelope));
                        ReleaseTrust.Verify(encoded,rsa.ToXmlString(false));SafeFiles.WriteAtomic(Path.GetFullPath(args[2]),encoded);
                    }
                }else throw new ArgumentException("Usage: Publisher public-key <public.xml> | sign <release.json> <channel.json>");
                Console.WriteLine("Publisher operation completed.");return 0;
            }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
        }
    }
}
