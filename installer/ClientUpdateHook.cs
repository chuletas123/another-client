using System;
using System.IO;
using System.Reflection;
namespace AnotherClient.Player
{
    // The bridge is a resource in the client EXE, so a missing loose updater DLL
    // cannot turn an optional update check into an early assembly-load crash.
    internal static class ClientUpdateHook
    {
        internal static bool BeforeStartup(string[] args)
        {
            if(args!=null&&args.Length!=0)return false;
            try{
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AnotherClient.UpdateBridge"))using(var memory=new MemoryStream()){
                    if(stream==null||stream.Length>2*1024*1024)return false;stream.CopyTo(memory);
                    var assembly=Assembly.Load(memory.ToArray());
                    var type=assembly.GetType("AnotherClient.Updates.ClientStartupUpdates",true);
                    return (bool)type.GetMethod("BeforeStartup",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{args});
                }
            }catch(Exception error){if(error is OutOfMemoryException)throw;return false;}
        }
    }
}
