using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace AnotherClient.Installer
{
    internal static class ComponentTests
    {
        private static int checks;private static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
        private sealed class Fake:IComponents
        {
            internal bool Net=true,Vc=true,Web=true,SetReady=true;internal int Calls,Downloads,Code;internal Action Downloading;
            internal readonly List<string> Commands=new List<string>();
            public bool FrameworkReady {get{return Net;}}public bool VisualCReady {get{return Vc;}}public bool WebViewReady {get{return Web;}}
            public Task<string> DownloadVisualC(string root,Action<InstallProgress> progress,CancellationToken token){Downloads++;if(Downloading!=null)Downloading();return Task.FromResult("fake-vc");}
            public string WebViewInstaller(Installation installation){return "fake-web";}
            public int Install(string path,string arguments,bool elevate){Calls++;Commands.Add(path+" "+arguments+" elevate="+elevate);if(SetReady){if(path=="fake-vc")Vc=true;else Web=true;}return Code;}
        }
        private static void Run(Fake env,List<bool> critical,List<InstallProgress> progress,CancellationToken token=default(CancellationToken))
        {
            ComponentInstallation.Ensure(new Installation(Path.GetFullPath("."),null),env,progress.Add,critical.Add,token).GetAwaiter().GetResult();
        }
        private static void Reject(Action action,string label){try{action();}catch(IOException){checks++;return;}throw new Exception("Not rejected: "+label);}
        private static int Main(string[] args)
        {
            try{
                var critical=new List<bool>();var progress=new List<InstallProgress>();var ready=new Fake();Run(ready,critical,progress);
                Check(ready.Calls==0&&ready.Downloads==0&&critical.Count==0,"installed components are untouched");
                var missing=new Fake{Vc=false,Web=false};Run(missing,critical,progress);
                Check(missing.Downloads==1&&missing.Calls==2,"only missing components installed");
                Check(missing.Commands[0]=="fake-vc /install /quiet /norestart elevate=True","VC uses Microsoft arguments and UAC");
                Check(missing.Commands[1]=="fake-web /silent /install elevate=False","WebView uses per-user Microsoft arguments");
                Check(critical.SequenceEqual(new[]{true,false,true,false}),"cancel disabled only around dependency installers");
                Check(progress.Last().Percent==99,"component completion remains below launch completion");
                var failed=new Fake{Vc=false,Code=1603};Reject(()=>Run(failed,new List<bool>(),new List<InstallProgress>()),"installer failure");
                var stillMissing=new Fake{Vc=false,SetReady=false};Reject(()=>Run(stillMissing,new List<bool>(),new List<InstallProgress>()),"exit zero does not replace readiness check");
                var restart=new Fake{Vc=false,Code=3010};Reject(()=>Run(restart,new List<bool>(),new List<InstallProgress>()),"restart required is reported without rebooting");
                var noNet=new Fake{Net=false};Reject(()=>Run(noNet,new List<bool>(),new List<InstallProgress>()),"missing NET Framework");Check(noNet.Calls==0&&noNet.Downloads==0,"no downloads with unsupported NET");
                using(var cancel=new CancellationTokenSource()){
                    var env=new Fake{Vc=false,Downloading=()=>cancel.Cancel()};bool cancelled=false;
                    try{Run(env,new List<bool>(),new List<InstallProgress>(),cancel.Token);}catch(OperationCanceledException){cancelled=true;}
                    Check(cancelled&&env.Calls==0,"cancellation prevents component execution");
                }
                foreach(string bad in new[]{"http://aka.ms/vc14/vc_redist.x64.exe","https://aka.ms/unexpected","https://example.com/setup.exe","https://download.microsoft.com.evil.test/x","https://user@download.microsoft.com/x"})Check(!WindowsComponents.MicrosoftDownload(new Uri(bad)),"untrusted runtime URL rejected");
                Check(WindowsComponents.MicrosoftDownload(new Uri("https://aka.ms/vc14/vc_redist.x64.exe")),"official runtime permalink allowed");
                string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string unsigned=Path.Combine(root,"unsigned.exe");File.WriteAllText(unsigned,"not a signed executable");
                Reject(()=>MicrosoftSignature.Verify(unsigned),"unsigned component refused");
                // These calls read registry/file metadata. They never execute a component installer.
                var windows=new WindowsComponents();Console.WriteLine("Read-only local detection: NET="+windows.FrameworkReady+", VC="+windows.VisualCReady+", WebView2="+windows.WebViewReady);
                if(args.Length>1){MicrosoftSignature.Verify(args[1]);Check(true,"bundled WebView2 Microsoft signature");}
                if(args.Length>2&&args[2]=="download-vc"){
                    string file=windows.DownloadVisualC(root,p=>{},CancellationToken.None).GetAwaiter().GetResult();MicrosoftSignature.Verify(file);
                    Check(File.Exists(file),"official VC download and publisher verified without execution");
                }
                Console.WriteLine("PASS: "+checks+" component checks. No runtime installation or GUI executed.");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
