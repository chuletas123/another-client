using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
namespace AnotherClient.Player
{
    internal static class MaterialTests
    {
        private static int checks;private static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
        private sealed class Broken:IDisposable{internal int Releases;public void Dispose(){Releases++;throw new COMException("device lost");}}
        private static int Main(string[] args)
        {
            try{
                Check(!StartupMaterialPolicy.Supports(10,19045,true,true,false,false,false),"Windows10 uses solid");
                Check(!StartupMaterialPolicy.Supports(10,20348,true,true,false,false,false),"Server2022 uses solid");
                Check(StartupMaterialPolicy.Supports(10,22000,true,true,false,false,false),"Windows11 supported");
                Check(StartupMaterialPolicy.Supports(10,28000,true,true,false,false,false),"new Windows supported");
                Check(!StartupMaterialPolicy.Supports(10,22631,false,true,false,false,false),"DWM unavailable");
                Check(!StartupMaterialPolicy.Supports(10,22631,true,false,false,false,false),"transparency disabled");
                Check(!StartupMaterialPolicy.Supports(10,22631,true,true,true,false,false),"high contrast");
                Check(!StartupMaterialPolicy.Supports(10,22631,true,true,false,true,false),"remote session");
                Check(!StartupMaterialPolicy.Supports(10,22631,true,true,false,false,true),"battery saver");
                int attempts=0;Check(StartupMaterialPolicy.TryCreate<object>(()=>false,()=>{attempts++;throw new Exception();})==null&&attempts==0,"unsupported path never loads compositor");
                foreach(var error in new Exception[]{new ArgumentException("E_INVALIDARG"),new COMException("DWM disabled"),new TypeLoadException(),new FileNotFoundException("WinRT projection"),new DllNotFoundException(),new EntryPointNotFoundException(),new MissingMethodException(),new NotSupportedException(),new InvalidCastException(),new TypeInitializationException("WinRT",new TypeLoadException()),new TargetInvocationException(new COMException()),new InvalidOperationException("device removed")}){
                    Check(StartupMaterialPolicy.TryCreate<object>(()=>true,()=>{throw error;})==null,"API failure becomes solid: "+error.GetType().Name);
                    Check(StartupMaterialPolicy.TryCreate<object>(()=>{throw error;},()=>new object())==null,"probe failure becomes solid: "+error.GetType().Name);
                }
                object material=new object();Check(ReferenceEquals(StartupMaterialPolicy.TryCreate(()=>true,()=>material),material),"compatible material retained");
                var broken=new Broken();StartupMaterialPolicy.Release(broken);Check(broken.Releases==1,"cleanup cannot block fallback");StartupMaterialPolicy.Release(null);
                Check(!StartupMaterialPolicy.Recoverable(new OutOfMemoryException()),"fatal error remains visible");
                // Reproduce the actual error kind returned by unsupported DWM attributes.
                Check(StartupMaterialPolicy.TryCreate<object>(()=>true,()=>{Marshal.ThrowExceptionForHR(unchecked((int)0x80070057));return new object();})==null,"native invalid argument no longer escapes");
                Console.WriteLine("PASS: "+checks+" material fallback checks; no windows opened.");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
