using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace AnotherClient.Installer
{
    internal static class ShellInstallation
    {
        [ComImport,Guid("00021401-0000-0000-C000-000000000046")]private class ShellLink {}
        [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath(IntPtr file,int count,IntPtr data,int flags);void GetIDList(out IntPtr id);void SetIDList(IntPtr id);
            void GetDescription(IntPtr text,int count);void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string text);
            void GetWorkingDirectory(IntPtr text,int count);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string path);
            void GetArguments(IntPtr text,int count);void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string arguments);
            void GetHotkey(out short hotkey);void SetHotkey(short hotkey);void GetShowCmd(out int show);void SetShowCmd(int show);
            void GetIconLocation(IntPtr file,int count,out int index);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string file,int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,int reserved);void Resolve(IntPtr owner,int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
        }
        internal static void Shortcut(string link,string executable,string icon,string workingDirectory)
        {
            SafeFiles.NoReparse(link);SafeFiles.EnsureDirectory(Path.GetDirectoryName(link));
            object instance=new ShellLink();
            try{
                var shortcut=(IShellLinkW)instance;shortcut.SetPath(executable);shortcut.SetWorkingDirectory(workingDirectory);
                shortcut.SetDescription("Start Another Client and check for updates.");shortcut.SetIconLocation(icon,0);shortcut.SetShowCmd(1);
                ((IPersistFile)instance).Save(link,true);
            }finally{Marshal.FinalReleaseComObject(instance);}
        }
        internal static string InstallLauncher(string root,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();SafeFiles.EnsureDirectory(root);
            string current=Assembly.GetExecutingAssembly().Location;
            string stable=SafeFiles.Child(root,"AnotherClientLauncher.exe");SafeFiles.NoReparse(stable);
            if(!String.Equals(Path.GetFullPath(current),stable,StringComparison.OrdinalIgnoreCase)){
                byte[] bytes=File.ReadAllBytes(current);
                if(!File.Exists(stable)||ReleaseTrust.HashFile(stable,token)!=ReleaseTrust.Hash(bytes))SafeFiles.WriteAtomic(stable,bytes);
            }
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AnotherClient.Icon"))
            using(var memory=new MemoryStream()){
                if(stream==null)throw new InvalidDataException("The installer icon is missing.");stream.CopyTo(memory);
                SafeFiles.WriteAtomic(SafeFiles.Child(root,"ac.ico"),memory.ToArray());
            }
            return stable;
        }
        internal static void InstallShortcuts(string root,string launcher)
        {
            string icon=SafeFiles.Child(root,"ac.ico");
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Ac player.lnk"),launcher,icon,root);
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Another Client","Ac player.lnk"),launcher,icon,root);
        }
    }
}
