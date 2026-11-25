using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AnotherClient.Installer
{
    internal interface IComponents
    {
        bool FrameworkReady {get;}bool VisualCReady {get;}bool WebViewReady {get;}
        Task<string> DownloadVisualC(string root,Action<InstallProgress> progress,CancellationToken token);
        string WebViewInstaller(Installation installation);
        int Install(string executable,string arguments,bool elevate);
    }
    internal static class ComponentInstallation
    {
        internal static async Task Ensure(Installation installation,IComponents environment,Action<InstallProgress> progress,Action<bool> critical,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(!environment.FrameworkReady)throw new IOException("Another Client requires .NET Framework 4.8 or later. Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48 and try again.");
            progress(new InstallProgress("Checking Microsoft components...",90,true));
            if(!environment.VisualCReady){
                string setup=await environment.DownloadVisualC(installation.Root,progress,token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();progress(new InstallProgress("Installing Microsoft Visual C++...",94,true));
                int code;critical(true);
                try{token.ThrowIfCancellationRequested();code=environment.Install(setup,"/install /quiet /norestart",true);}finally{critical(false);}
                if(code==3010)throw new IOException("Microsoft Visual C++ needs a Windows restart. Restart Windows and open Ac player again. No automatic restart was requested.");
                if(code!=0&&code!=1638)throw new IOException("Microsoft Visual C++ installation did not complete (code "+code+").");
                if(!environment.VisualCReady)throw new IOException("Microsoft Visual C++ is still unavailable. Open Ac player again after installing the x64 runtime.");
            }
            token.ThrowIfCancellationRequested();
            if(!environment.WebViewReady){
                string setup=environment.WebViewInstaller(installation);
                progress(new InstallProgress("Installing Microsoft WebView2...",96,true));int code;critical(true);
                try{token.ThrowIfCancellationRequested();code=environment.Install(setup,"/silent /install",false);}finally{critical(false);}
                if(code!=0)throw new IOException("Microsoft WebView2 installation did not complete (code "+code+").");
                if(!environment.WebViewReady)throw new IOException("Microsoft WebView2 is still unavailable. Open Ac player again after the runtime installation completes.");
            }
            token.ThrowIfCancellationRequested();progress(new InstallProgress("Starting Another Client...",99));
        }
    }
    internal sealed class WindowsComponents:IComponents
    {
        private static readonly Version MinimumVC=new Version(14,51,36247,0);
        public bool FrameworkReady {get{
            using(var baseKey=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry32))using(var key=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))return key!=null&&Convert.ToInt32(key.GetValue("Release",0))>=528040;
        }}
        public bool VisualCReady {get{
            foreach(string name in new[]{"MSVCP140.dll","VCRUNTIME140.dll","VCRUNTIME140_1.dll","CONCRT140.dll"}){
                string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),name);
                if(!File.Exists(path))return false;var info=FileVersionInfo.GetVersionInfo(path);
                if(new Version(info.FileMajorPart,info.FileMinorPart,info.FileBuildPart,info.FilePrivatePart)<MinimumVC)return false;
            }return true;
        }}
        public bool WebViewReady {get{
            foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})
                using(var baseKey=RegistryKey.OpenBaseKey(hive,view))using(var key=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}")){
                    Version version;if(key!=null&&Version.TryParse(Convert.ToString(key.GetValue("pv")),out version)&&version>new Version(0,0,0,0))return true;
                }
            return false;
        }}
        internal static bool MicrosoftDownload(Uri uri)
        {
            return uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&
                (uri.Host=="aka.ms"&&(uri.AbsolutePath=="/vc14/vc_redist.x64.exe"||System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,@"^/vs/[0-9]{1,2}/release/vc_redist\.x64\.exe$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))||uri.Host=="download.visualstudio.microsoft.com"||uri.Host=="download.microsoft.com");
        }
        public async Task<string> DownloadVisualC(string root,Action<InstallProgress> progress,CancellationToken token)
        {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            string directory=SafeFiles.Child(root,"Installer/Components");SafeFiles.EnsureDirectory(directory);
            string target=SafeFiles.Child(directory,"VC_redist.x64.exe");SafeFiles.NoReparse(target);
            if(File.Exists(target)){
                try{VerifyVisualC(target);return target;}catch(Exception e){if(!(e is IOException)&&!(e is System.Security.Cryptography.CryptographicException))throw;}
            }
            string partial=SafeFiles.Child(directory,Guid.NewGuid().ToString("N")+".partial");
            try{
                using(var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=Timeout.InfiniteTimeSpan})
                using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("AnotherClientInstaller/1.0");deadline.CancelAfter(TimeSpan.FromSeconds(30));
                    Uri uri=new Uri("https://aka.ms/vc14/vc_redist.x64.exe");HttpResponseMessage response=null;
                    for(int redirect=0;redirect<5;redirect++){
                        if(!MicrosoftDownload(uri))throw new IOException("Unexpected Microsoft runtime download origin.");
                        response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
                        int code=(int)response.StatusCode;
                        if(code>=300&&code<400&&response.Headers.Location!=null){var next=new Uri(uri,response.Headers.Location);response.Dispose();response=null;uri=next;continue;}
                        if(!response.IsSuccessStatusCode){response.Dispose();throw new IOException("Microsoft runtime download returned HTTP "+code+".");}break;
                    }
                    if(response==null)throw new IOException("Too many Microsoft runtime download redirects.");deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                    using(response)using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,256*1024,true)){
                        long? length=response.Content.Headers.ContentLength;long count=0;byte[] block=new byte[256*1024];int read;
                        if(length.HasValue&&(length.Value<1||length.Value>128L*1024*1024))throw new IOException("Unexpected Microsoft runtime download size.");
                        progress(new InstallProgress("Downloading Microsoft Visual C++...",90,!length.HasValue));
                        while((read=await GitHubTransport.Read(input,block,token).ConfigureAwait(false))>0){
                            count+=read;if(count>128L*1024*1024||length.HasValue&&count>length.Value)throw new IOException("Microsoft runtime download exceeded its size limit.");
                            await output.WriteAsync(block,0,read,token).ConfigureAwait(false);
                            progress(new InstallProgress("Downloading Microsoft Visual C++...",length.HasValue?90+(int)(4.0*count/length.Value):90,!length.HasValue));
                        }
                        if(length.HasValue&&count!=length.Value||count==0)throw new IOException("Microsoft runtime download was incomplete.");
                        output.Flush(true);
                    }
                }
                token.ThrowIfCancellationRequested();VerifyVisualC(partial);
                SafeFiles.WriteAtomic(target,File.ReadAllBytes(partial));return target;
            }finally{if(File.Exists(partial))File.Delete(partial);}
        }
        private static void VerifyVisualC(string path)
        {
            if(new FileInfo(path).Length>128L*1024*1024)throw new IOException("The Visual C++ installer is too large.");
            MicrosoftSignature.Verify(path);
            var info=FileVersionInfo.GetVersionInfo(path);
            if(!String.Equals(info.OriginalFilename,"vc_redist.x64.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("The downloaded Microsoft component is not the x64 Visual C++ redistributable.");
            if(new Version(info.FileMajorPart,info.FileMinorPart,info.FileBuildPart,info.FilePrivatePart)<MinimumVC)throw new IOException("The Visual C++ installer is older than the runtime required by this client.");
        }
        public string WebViewInstaller(Installation installation)
        {
            string folder=Path.GetDirectoryName(installation.Executable),relative="2026E/WebView2RuntimeInstaller/MicrosoftEdgeWebview2Setup.exe";
            var manifest=InstallerEngine.Manifest(folder,installation.Release.Spec);string hash;
            if(!manifest.TryGetValue(relative,out hash))throw new IOException("The WebView2 installer is missing from the verified package.");
            string path=SafeFiles.Child(folder,relative);SafeFiles.NoReparse(path);
            if(ReleaseTrust.HashFile(path,CancellationToken.None)!=hash)throw new IOException("The WebView2 installer failed verification.");return path;
        }
        public int Install(string executable,string arguments,bool elevate)
        {
            SafeFiles.NoReparse(executable);
            using(var retained=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read)){
                if(elevate)VerifyVisualC(executable);else MicrosoftSignature.Verify(executable);
                var start=new ProcessStartInfo(executable,arguments){WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=true};
                if(elevate)start.Verb="runas";
                try{using(var process=Process.Start(start)){if(process==null)throw new IOException("The Microsoft installer could not be started.");process.WaitForExit();return process.ExitCode;}}
                catch(Win32Exception e){if(e.NativeErrorCode==1223)throw new IOException("Windows permission for Microsoft Visual C++ was cancelled. The client package is preserved; open Ac player to retry.");throw;}
            }
        }
    }
    internal static class MicrosoftSignature
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct FileSubject{internal uint size;[MarshalAs(UnmanagedType.LPWStr)]internal string path;internal IntPtr file,subject;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct TrustData{internal uint size;internal IntPtr policy,sip;internal uint ui,revocation,choice;internal IntPtr subject;internal uint action;internal IntPtr state,url;internal uint flags,context;internal IntPtr signatureSettings;}
        [DllImport("wintrust.dll",ExactSpelling=true)]private static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
        internal static void Verify(string path)
        {
            SafeFiles.NoReparse(path);var file=new FileSubject{size=(uint)Marshal.SizeOf(typeof(FileSubject)),path=path};
            IntPtr subject=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileSubject)));Marshal.StructureToPtr(file,subject,false);
            var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            var data=new TrustData{size=(uint)Marshal.SizeOf(typeof(TrustData)),ui=2,revocation=1,choice=1,subject=subject,action=1,flags=0x80};
            try{
                if(WinVerifyTrust(new IntPtr(-1),ref action,ref data)!=0)throw new IOException("A Microsoft component has an invalid or untrusted signature.");
                using(var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    if(certificate.GetNameInfo(X509NameType.SimpleName,false)!="Microsoft Corporation")throw new IOException("A component was not signed by Microsoft Corporation.");
            }finally{data.action=2;WinVerifyTrust(new IntPtr(-1),ref action,ref data);Marshal.DestroyStructure(subject,typeof(FileSubject));Marshal.FreeHGlobal(subject);}
        }
    }
}
