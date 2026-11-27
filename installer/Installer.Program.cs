using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AnotherClient.Player;

namespace AnotherClient.Player
{
    internal sealed class StartupProgress
    {
        internal readonly string Message;internal readonly int Percent;internal readonly bool Indeterminate;
        internal StartupProgress(string message,int percent,bool indeterminate){Message=message;Percent=percent;Indeterminate=indeterminate;}
    }
}
namespace AnotherClient.Installer
{
    internal static class Program
    {
        [STAThread]private static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
            bool created;
            using(var single=new Mutex(true,"Local\\AnotherClient.Installer."+WindowsIdentity.GetCurrent().User.Value,out created)){
                if(!created)return 0;
                try{return Run(args);}
                catch(Exception e){MessageBox.Show("Another Client could not be installed.\n\n"+e.Message,"Another Client",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
                finally{single.ReleaseMutex();}
            }
        }
        private static int Run(string[] args)
        {
            if(Environment.OSVersion.Version.Major<10)throw new IOException("Another Client requires 64-bit Windows 10 or Windows 11.");
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Another Client");
            SafeFiles.EnsureDirectory(root);
            string state=SafeFiles.Child(root,"Installer");SafeFiles.EnsureDirectory(state);
            string lockPath=SafeFiles.Child(state,"install.lock");SafeFiles.NoReparse(lockPath);
            using(var installationLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            using(var source=new GitHubTransport())
            using(var cancellation=new CancellationTokenSource()){
                string key;
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AnotherClient.UpdatePublicKey"))
                using(var reader=new StreamReader(stream)){key=reader.ReadToEnd();}
                var check=ClientCheck.Read(args,root,key,source);
                if(check!=null&&!check.Needed(root,key,source))return ClientCheck.ContinueCurrent;
                IReleaseTransport transport=check==null?(IReleaseTransport)source:new PreparedFeedTransport(source,check.Feed);
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var card=new StartupCard(false);int exit=0;
                using(var relay=new StartupProgressRelay(p=>{if(!cancellation.IsCancellationRequested)card.SetProgress(p.Message,p.Percent,p.Indeterminate);})){
                    var engine=new InstallerEngine(root,key,transport,p=>relay.Report(new StartupProgress(check==null?p.Message:"Updating Another Client...",p.Percent*90/100,p.Indeterminate)),true,check!=null);
                    card.CancelRequested+=delegate{cancellation.Cancel();card.SetCancelling();};
                    card.SetProgress(check==null?"Preparing Another Client...":"Updating Another Client...",0,true);card.ShowStartup();
                    app.Dispatcher.BeginInvoke(new Action(async delegate{
                        try{
                            var components=new WindowsComponents();
                            if(!components.FrameworkReady)throw new IOException("Another Client requires .NET Framework 4.8 or later. Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48 and try again.");
                            var installed=await Task.Run(()=>engine.Prepare(cancellation.Token));
                            if(check!=null&&installed.Release.Spec.sequence<=check.Sequence){exit=ClientCheck.ContinueCurrent;card.CompleteAndClose();return;}
                            await Task.Run(()=>ComponentInstallation.Ensure(installed,components,p=>relay.Report(new StartupProgress(engine.IsUpdating?"Updating Another Client...":p.Message,p.Percent,p.Indeterminate)),busy=>app.Dispatcher.Invoke(()=>card.CancelControl.IsEnabled=!busy),cancellation.Token));
                            await Task.Run(()=>engine.Commit(installed,cancellation.Token));
                            cancellation.Token.ThrowIfCancellationRequested();
                            // Shell links are created on the STA UI thread. No shell script is launched.
                            card.CancelControl.IsEnabled=false;
                            string launcher=ShellInstallation.InstallLauncher(root,cancellation.Token);
                            ShellInstallation.InstallShortcuts(root,launcher);
                            relay.Report(new StartupProgress("Starting Another Client...",100,false));relay.Flush();
                            card.CompleteAndClose();
                            if(check!=null){exit=check.Complete(installed,key)?ClientCheck.Updated:ClientCheck.ContinueCurrent;}
                            else {
                                var launch=new ProcessStartInfo(installed.Executable){WorkingDirectory=Path.GetDirectoryName(installed.Executable),UseShellExecute=false};
                                string passport=Convert.ToBase64String(installed.Release.Envelope);
                                if(passport.Length<=8192)launch.EnvironmentVariables["ANOTHER_CLIENT_VERIFIED_UPDATE"]=passport;
                                Process.Start(launch);
                            }
                        }catch(OperationCanceledException){
                            exit=cancellation.IsCancellationRequested?0:1;card.CompleteAndClose();
                            if(exit!=0)MessageBox.Show("The download service did not respond in time. Please open Ac player to retry.","Another Client",MessageBoxButton.OK,MessageBoxImage.Error);
                        }
                        catch(Exception e){
                            exit=1;card.CompleteAndClose();
                            MessageBox.Show("Another Client could not start.\n\n"+e.Message+"\n\nPlease try again. Your account data has been preserved.","Another Client",MessageBoxButton.OK,MessageBoxImage.Error);
                        }finally{app.Shutdown(exit);}
                    }),DispatcherPriority.Background);
                    app.Run();
                }
                card.Dispose();return exit;
            }
        }
    }
}
