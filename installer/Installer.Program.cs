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
        [STAThread]private static int Main()
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
            bool created;
            using(var single=new Mutex(true,"Local\\AnotherClient.Installer."+WindowsIdentity.GetCurrent().User.Value,out created)){
                if(!created)return 0;
                try{return Run();}
                catch(Exception e){MessageBox.Show("Another Client could not be installed.\n\n"+e.Message,"Another Client",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
                finally{single.ReleaseMutex();}
            }
        }
        private static int Run()
        {
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
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var card=new StartupCard(false);int exit=0;
                using(var relay=new StartupProgressRelay(p=>{if(!cancellation.IsCancellationRequested)card.SetProgress(p.Message,p.Percent,p.Indeterminate);})){
                    var engine=new InstallerEngine(root,key,source,p=>relay.Report(new StartupProgress(p.Message,p.Percent,p.Indeterminate)));
                    card.CancelRequested+=delegate{cancellation.Cancel();card.SetCancelling();};
                    card.SetProgress("Preparing Another Client...",0,true);card.ShowStartup();
                    app.Dispatcher.BeginInvoke(new Action(async delegate{
                        try{
                            var installed=await Task.Run(()=>engine.Prepare(cancellation.Token));
                            cancellation.Token.ThrowIfCancellationRequested();
                            // Shell links are created on the STA UI thread. No shell script is launched.
                            card.CancelControl.IsEnabled=false;
                            string launcher=ShellInstallation.InstallLauncher(root,cancellation.Token);
                            ShellInstallation.InstallShortcuts(root,launcher);
                            relay.Report(new StartupProgress("Starting Another Client...",100,false));relay.Flush();
                            card.CompleteAndClose();
                            Process.Start(new ProcessStartInfo(installed.Executable){WorkingDirectory=Path.GetDirectoryName(installed.Executable),UseShellExecute=false});
                        }catch(OperationCanceledException){exit=0;card.CompleteAndClose();}
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
