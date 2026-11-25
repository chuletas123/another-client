using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AnotherClient.Player
{
    // This class deliberately has no Windows Runtime or Composition references.
    // Unsupported systems must be able to draw an ordinary opaque WPF window.
    internal static class StartupMaterialPolicy
    {
        internal static bool Supports(int major,int build,bool composition,bool transparency,bool highContrast,bool remote,bool batterySaver)
        {
            return major>=10&&build>=22000&&composition&&transparency&&!highContrast&&!remote&&!batterySaver;
        }
        internal static bool CanUseBlur()
        {
            try{
                var version=new NativeVersion{Size=(uint)Marshal.SizeOf(typeof(NativeVersion)),ServicePack=String.Empty};
                if(RtlGetVersion(ref version)!=0)return false;
                bool enabled;if(DwmIsCompositionEnabled(out enabled)!=0)return false;
                var contrast=new Contrast{Size=(uint)Marshal.SizeOf(typeof(Contrast))};
                if(!SystemParametersInfo(0x42,contrast.Size,ref contrast,0))return false;
                bool transparency=true;
                using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    if(key!=null)transparency=Convert.ToInt32(key.GetValue("EnableTransparency",1))!=0;
                PowerStatus power;bool saver=GetSystemPowerStatus(out power)&&power.Saver!=0;
                return Supports((int)version.Major,(int)version.Build,enabled,transparency,(contrast.Flags&1)!=0,GetSystemMetrics(0x1000)!=0,saver);
            }catch(Exception error){if(!Recoverable(error))throw;return false;}
        }
        internal static bool Recoverable(Exception error)
        {
            return !(error is OutOfMemoryException||error is StackOverflowException||error is AccessViolationException);
        }
        internal static T TryCreate<T>(Func<bool> allowed,Func<T> factory) where T:class
        {
            try{return allowed()?factory():null;}catch(Exception error){if(!Recoverable(error))throw;return null;}
        }
        internal static void Release(IDisposable resource)
        {
            if(resource==null)return;
            try{resource.Dispose();}catch(Exception error){if(!Recoverable(error))throw;}
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct NativeVersion{internal uint Size,Major,Minor,Build,Platform;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]internal string ServicePack;}
        [StructLayout(LayoutKind.Sequential)]private struct Contrast{internal uint Size,Flags;internal IntPtr Scheme;}
        [StructLayout(LayoutKind.Sequential)]private struct PowerStatus{internal byte Ac,Battery,Percent,Saver;internal uint Life,FullLife;}
        [DllImport("ntdll.dll",CharSet=CharSet.Unicode)]private static extern int RtlGetVersion(ref NativeVersion version);
        [DllImport("dwmapi.dll")]private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)]out bool enabled);
        [DllImport("user32.dll",SetLastError=true)]private static extern bool SystemParametersInfo(uint action,uint parameter,ref Contrast value,uint flags);
        [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
        [DllImport("kernel32.dll")]private static extern bool GetSystemPowerStatus(out PowerStatus status);
    }
}
