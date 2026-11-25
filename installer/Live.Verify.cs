using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace AnotherClient.Installer
{
    internal static class LiveVerify
    {
        private static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
            try{Run(args).GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
        }
        private static async Task Run(string[] args)
        {
            string root=Path.GetFullPath(args[0]);string key=File.ReadAllText(args[1]);
            string phase=null;DateTime last=DateTime.MinValue;
            using(var transport=new GitHubTransport()){
                var engine=new InstallerEngine(root,key,transport,p=>{if(phase!=p.Message||DateTime.UtcNow-last>TimeSpan.FromSeconds(15)){Console.WriteLine(p.Message+" "+p.Percent+"%");phase=p.Message;last=DateTime.UtcNow;}});
                var installed=await engine.Prepare(CancellationToken.None);
                InstallerEngine.VerifyFolder(Path.GetDirectoryName(installed.Executable),installed.Release.Spec,CancellationToken.None,true);
                Console.WriteLine("PASS: public signed channel, download and all "+installed.Release.Spec.fileCount+" files verified. Version: "+installed.Release.Spec.version);
                var again=await engine.Prepare(CancellationToken.None);if(again.Release.Spec.sequence!=installed.Release.Spec.sequence)throw new Exception("Launch selected a different version.");
                Console.WriteLine("PASS: subsequent check reuses the installed version. No Player started and no desktop shortcut created.");
            }
        }
    }
}
