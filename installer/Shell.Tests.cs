using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace AnotherClient.Installer
{
    internal static class ShellTests
    {
        [STAThread]private static int Main(string[] args)
        {
            try{
                string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
                string launcher=ShellInstallation.InstallLauncher(root,CancellationToken.None);
                if(ReleaseTrust.HashFile(launcher,CancellationToken.None)!=ReleaseTrust.HashFile(Assembly.GetExecutingAssembly().Location,CancellationToken.None))throw new Exception("Stable launcher copy differs.");
                string icon=Path.Combine(root,"ac.ico");if(new FileInfo(icon).Length!=43002)throw new Exception("AC icon differs.");
                string link=Path.Combine(root,"Ac player.lnk");ShellInstallation.Shortcut(link,launcher,icon,root);
                object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));object shortcut=null;
                try{
                    shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});
                    var type=shortcut.GetType();
                    string target=(string)type.InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);
                    string working=(string)type.InvokeMember("WorkingDirectory",BindingFlags.GetProperty,null,shortcut,null);
                    string image=(string)type.InvokeMember("IconLocation",BindingFlags.GetProperty,null,shortcut,null);
                    if(!String.Equals(target,launcher,StringComparison.OrdinalIgnoreCase)||!String.Equals(working,root,StringComparison.OrdinalIgnoreCase)||!image.StartsWith(icon,StringComparison.OrdinalIgnoreCase))throw new Exception("Shortcut target or icon differs.");
                }finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);Marshal.FinalReleaseComObject(shell);}
                Console.WriteLine("PASS: stable executable, AC icon and shortcut target verified in an isolated folder. No desktop shortcut created and no window opened.");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
