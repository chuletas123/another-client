using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using AnotherClient.Installer;

namespace AnotherClient.Updates
{
    public static class ClientStartupUpdates
    {
        private const string Passport="ANOTHER_CLIENT_VERIFIED_UPDATE";
        internal static long CurrentSequence(Assembly client)
        {
            using(var input=client.GetManifestResourceStream("AnotherClient.ReleaseCatalog"))using(var reader=new StreamReader(input)){
                var catalog=ReleaseTrust.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(reader.ReadToEnd());
                var manifest=ReleaseTrust.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(Convert.ToString(catalog["manifestTemplate"]));
                long sequence=Convert.ToInt64(manifest["updateChannelSequence"],CultureInfo.InvariantCulture);
                if(sequence<1)throw new IOException("The client update identity is invalid.");return sequence;
            }
        }
        private static byte[] Resource(string name)
        {
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var output=new MemoryStream()){
                if(input==null)throw new IOException("The update component is incomplete.");input.CopyTo(output);return output.ToArray();
            }
        }
        internal static bool AcceptHandoff(string encoded,string root,string executable,string key,long sequence)
        {
            if(String.IsNullOrEmpty(encoded)||encoded.Length>8192)return false;
            var trusted=ReleaseTrust.Verify(Convert.FromBase64String(encoded),key);
            if(trusted.Spec.sequence!=sequence)return false;
            string expected=new Installation(root,trusted).Executable;
            return String.Equals(Path.GetFullPath(executable),expected,StringComparison.OrdinalIgnoreCase)&&ReleaseTrust.HashFile(executable,CancellationToken.None)==trusted.Spec.exeSha256;
        }
        internal static Installation AcceptReply(byte[] reply,string root,string key,long sequence)
        {
            var release=ReleaseTrust.Verify(reply,key);
            if(release.Spec.sequence<=sequence)throw new IOException("An update did not provide a newer version.");
            var installation=new Installation(root,release);
            InstallerEngine.VerifyFolder(Path.GetDirectoryName(installation.Executable),release.Spec,CancellationToken.None,false);return installation;
        }
        public static bool BeforeStartup(string[] args)
        {
            string passport=Environment.GetEnvironmentVariable(Passport);
            Environment.SetEnvironmentVariable(Passport,null,EnvironmentVariableTarget.Process);
            // Preserve explicit offline/verification/development modes without adding network work.
            if(args!=null&&args.Length!=0)return false;
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Another Client");
            string reply=null;
            try{
                string key=System.Text.Encoding.UTF8.GetString(Resource("AnotherClient.UpdatePublicKey"));
                var client=Assembly.GetEntryAssembly();string executable=client.Location;long sequence=CurrentSequence(client);
                try{if(AcceptHandoff(passport,root,executable,key,sequence))return false;}catch(Exception e){if(e is OutOfMemoryException)throw;}
                byte[] bootstrapper=Resource("AnotherClient.UpdateBootstrapper");string hash=ReleaseTrust.Hash(bootstrapper);
                string folder=SafeFiles.Child(root,"Installer/Bootstrapper/"+hash.Substring(0,16));SafeFiles.EnsureDirectory(folder);
                string updater=SafeFiles.Child(folder,"AnotherClientUpdater.exe");SafeFiles.NoReparse(updater);
                if(!File.Exists(updater)||ReleaseTrust.HashFile(updater,CancellationToken.None)!=hash)SafeFiles.WriteAtomic(updater,bootstrapper);
                string requests=SafeFiles.Child(root,"Installer/Requests");SafeFiles.EnsureDirectory(requests);
                reply=SafeFiles.Child(requests,Guid.NewGuid().ToString("N")+".json");
                int code;
                var start=new ProcessStartInfo(updater,"--client-check "+sequence.ToString(CultureInfo.InvariantCulture)+" \""+reply+"\""){UseShellExecute=false,WorkingDirectory=folder,CreateNoWindow=true};
                using(var retained=new FileStream(updater,FileMode.Open,FileAccess.Read,FileShare.Read))using(var process=Process.Start(start)){
                    if(process==null)return false;process.WaitForExit();code=process.ExitCode;
                }
                if(code==ClientCheck.ContinueCurrent)return false;
                if(code==0)return true; // User cancelled or another update already owns the installer.
                if(code!=ClientCheck.Updated||!File.Exists(reply)||new FileInfo(reply).Length>65536)return false;
                byte[] envelope=File.ReadAllBytes(reply);var installation=AcceptReply(envelope,root,key,sequence);
                var launch=new ProcessStartInfo(installation.Executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(installation.Executable)};
                string encoded=Convert.ToBase64String(envelope);if(encoded.Length<=8192)launch.EnvironmentVariables[Passport]=encoded;
                using(var process=Process.Start(launch)){return process!=null;}
            }catch(Exception e){if(e is OutOfMemoryException)throw;return false;}
            finally{if(reply!=null&&File.Exists(reply)){try{SafeFiles.NoReparse(reply);File.Delete(reply);}catch(Exception e){if(e is OutOfMemoryException)throw;}}}
        }
    }
}
