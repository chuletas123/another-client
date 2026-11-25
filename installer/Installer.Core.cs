using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AnotherClient.Installer
{
    internal sealed class InstallProgress
    {
        internal readonly string Message;internal readonly int Percent;internal readonly bool Indeterminate;
        internal InstallProgress(string message,int percent,bool pending=false){Message=message;Percent=Math.Max(0,Math.Min(100,percent));Indeterminate=pending;}
    }
    internal sealed class ReleaseSpec
    {
        public int schemaVersion {get;set;}public string product {get;set;}public string channel {get;set;}public string platform {get;set;}public string version {get;set;}
        public string archiveUrl {get;set;}public string archiveSha256 {get;set;}public string manifestSha256 {get;set;}public string exeSha256 {get;set;}public string topFolder {get;set;}
        public long sequence {get;set;}public long archiveBytes {get;set;}public long uncompressedBytes {get;set;}public int fileCount {get;set;}
        internal string DirectoryName {get{return "v"+sequence.ToString(CultureInfo.InvariantCulture)+"-"+archiveSha256.Substring(0,12);}}
    }
    internal sealed class SignedEnvelope {public int schemaVersion {get;set;}public string payload {get;set;}public string signature {get;set;}}
    internal sealed class TrustedRelease
    {
        internal readonly ReleaseSpec Spec;internal readonly byte[] Envelope;
        internal TrustedRelease(ReleaseSpec spec,byte[] envelope){Spec=spec;Envelope=envelope;}
    }
    internal static class ReleaseTrust
    {
        internal const string Repository="chuletas123/another-client";
        internal const string FeedUrl="https://raw.githubusercontent.com/chuletas123/another-client/main/channels/alpha.json";
        internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=2*1024*1024,RecursionLimit=32};
        internal static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
        internal static string Hash(byte[] data){using(var hash=SHA256.Create())return Hex(hash.ComputeHash(data));}
        internal static string HashFile(string path,CancellationToken token,Action<long> onBytes=null)
        {
            using(var hash=SHA256.Create())using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                byte[] block=new byte[1024*1024];long total=0;int read;
                while((read=stream.Read(block,0,block.Length))>0){token.ThrowIfCancellationRequested();hash.TransformBlock(block,0,read,null,0);total+=read;if(onBytes!=null)onBytes(total);}
                hash.TransformFinalBlock(new byte[0],0,0);return Hex(hash.Hash);
            }
        }
        internal static TrustedRelease Verify(byte[] envelope,string publicKey)
        {
            if(envelope==null||envelope.Length==0||envelope.Length>65536)throw new InvalidDataException("Invalid update manifest size.");
            var outer=Json.Deserialize<SignedEnvelope>(Encoding.UTF8.GetString(envelope));
            if(outer==null||outer.schemaVersion!=1||String.IsNullOrEmpty(outer.payload)||String.IsNullOrEmpty(outer.signature))throw new InvalidDataException("Invalid update signature envelope.");
            byte[] data=Convert.FromBase64String(outer.payload),signature=Convert.FromBase64String(outer.signature);
            if(data.Length>32768||signature.Length!=384)throw new InvalidDataException("Invalid update signature size.");
            using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(publicKey);if(!rsa.VerifyData(data,CryptoConfig.MapNameToOID("SHA256"),signature))throw new InvalidDataException("Update signature verification failed.");}
            var spec=Json.Deserialize<ReleaseSpec>(Encoding.UTF8.GetString(data));
            if(spec==null||spec.schemaVersion!=1||spec.product!="AnotherClient"||spec.channel!="alpha"||spec.platform!="windows-x64"||
               spec.sequence<1||spec.archiveBytes<1||spec.archiveBytes>2L*1024*1024*1024||spec.uncompressedBytes<1||spec.uncompressedBytes>4L*1024*1024*1024||
               spec.fileCount<1||spec.fileCount>30000||String.IsNullOrEmpty(spec.version)||spec.version.Length>80)throw new InvalidDataException("Unsupported update manifest.");
            foreach(string value in new[]{spec.archiveSha256,spec.manifestSha256,spec.exeSha256})if(value==null||!Regex.IsMatch(value,"^[a-f0-9]{64}$"))throw new InvalidDataException("Invalid update digest.");
            SafeFiles.Relative(spec.topFolder);
            if(spec.topFolder.Contains("/"))throw new InvalidDataException("Invalid package folder.");
            Uri url;
            if(!Uri.TryCreate(spec.archiveUrl,UriKind.Absolute,out url)||url.Scheme!="https"||url.Host!="github.com"||!url.IsDefaultPort||url.UserInfo.Length!=0||url.Fragment.Length!=0||
               !url.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal)||url.Query.Length!=0)throw new InvalidDataException("Invalid package download origin.");
            return new TrustedRelease(spec,(byte[])envelope.Clone());
        }
    }
    internal static class SafeFiles
    {
        internal static string Relative(string value)
        {
            if(String.IsNullOrEmpty(value)||value.Length>1024||value.Contains("\\")||value.StartsWith("/",StringComparison.Ordinal)||value.EndsWith("/",StringComparison.Ordinal))throw new InvalidDataException("Unsafe package path.");
            foreach(string part in value.Split('/')) {
                if(part.Length==0||part.Length>255||part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal)||
                   part.Any(c=>c<32||"<>:\"|?*".IndexOf(c)>=0)||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase))throw new InvalidDataException("Unsafe package path.");
            }
            return value;
        }
        internal static string Child(string root,string relative)
        {
            Relative(relative);string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
            string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Path left the installation directory.");
            return full;
        }
        internal static void NoReparse(string path)
        {
            for(string at=Path.GetFullPath(path);!String.IsNullOrEmpty(at);at=Path.GetDirectoryName(at)) {
                if((File.Exists(at)||Directory.Exists(at))&&(File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Installation through a directory link is not supported.");
                if(Path.GetPathRoot(at)==at)break;
            }
        }
        internal static void EnsureDirectory(string path){NoReparse(path);Directory.CreateDirectory(path);NoReparse(path);}
        internal static void WriteAtomic(string path,byte[] data,string backup=null)
        {
            NoReparse(path);EnsureDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){stream.Write(data,0,data.Length);stream.Flush(true);}
                if(File.Exists(path)){if(backup!=null)NoReparse(backup);File.Replace(temp,path,backup,true);}else File.Move(temp,path);
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
        internal static void DeleteOwnedTree(string allowedParent,string target)
        {
            string root=Path.GetFullPath(allowedParent).TrimEnd('\\','/')+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(target);
            if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||full.TrimEnd('\\','/')==root.TrimEnd('\\','/'))throw new IOException("Refusing to delete outside installer staging.");
            NoReparse(full);
            if(!Directory.Exists(full))return;
            foreach(string child in Directory.GetFileSystemEntries(full)) {
                NoReparse(child);
                if(Directory.Exists(child))DeleteOwnedTree(full,child);
                else{File.SetAttributes(child,FileAttributes.Normal);File.Delete(child);}
            }
            Directory.Delete(full,false);
        }
    }
    internal interface IReleaseTransport
    {
        Task<byte[]> Feed(CancellationToken token);
        Task Download(ReleaseSpec spec,string target,Action<long> progress,CancellationToken token);
    }
    internal sealed class GitHubTransport : IReleaseTransport,IDisposable
    {
        private readonly HttpClient client;
        internal GitHubTransport():this(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
        internal GitHubTransport(HttpMessageHandler handler){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};client.DefaultRequestHeaders.UserAgent.ParseAdd("AnotherClientInstaller/1.0");}
        private static void Allowed(Uri uri,bool feed)
        {
            if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0)throw new IOException("Unsafe download redirect.");
            if(feed){if(uri.Host!="raw.githubusercontent.com"||uri.AbsolutePath!="/"+ReleaseTrust.Repository+"/main/channels/alpha.json")throw new IOException("Unexpected update manifest origin.");}
            else if(uri.Host!="github.com"&&uri.Host!="release-assets.githubusercontent.com"&&uri.Host!="objects.githubusercontent.com")throw new IOException("Unexpected release download origin.");
        }
        private async Task<HttpResponseMessage> Open(Uri uri,bool feed,CancellationToken token)
        {
            for(int redirect=0;redirect<5;redirect++) {
                Allowed(uri,feed);
                using(var request=new HttpRequestMessage(HttpMethod.Get,uri)) {
                    request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue{NoCache=true};
                    var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
                    int status=(int)response.StatusCode;
                    if(status>=300&&status<=399&&response.Headers.Location!=null){var next=new Uri(uri,response.Headers.Location);response.Dispose();uri=next;continue;}
                    if(!response.IsSuccessStatusCode){response.Dispose();throw new IOException("Download service returned HTTP "+status.ToString(CultureInfo.InvariantCulture)+".");}
                    return response;
                }
            }
            throw new IOException("Too many download redirects.");
        }
        internal static async Task<int> Read(Stream stream,byte[] buffer,CancellationToken token)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(45));
                using(deadline.Token.Register(()=>stream.Dispose())) {
                    try{return await stream.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false);}
                    catch(Exception){if(token.IsCancellationRequested)throw new OperationCanceledException(token);if(deadline.IsCancellationRequested)throw new IOException("The download stopped responding.");throw;}
                }
            }
        }
        public async Task<byte[]> Feed(CancellationToken token)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                using(var response=await Open(new Uri(ReleaseTrust.FeedUrl),true,deadline.Token).ConfigureAwait(false))
                using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using(var memory=new MemoryStream()) {
                    byte[] block=new byte[8192];int count;
                    while((count=await Read(stream,block,deadline.Token).ConfigureAwait(false))>0){if(memory.Length+count>65536)throw new InvalidDataException("Update manifest is too large.");memory.Write(block,0,count);}
                    return memory.ToArray();
                }
            }
        }
        public async Task Download(ReleaseSpec spec,string target,Action<long> progress,CancellationToken token)
        {
            using(var headers=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                headers.CancelAfter(TimeSpan.FromSeconds(30));
                using(var response=await Open(new Uri(spec.archiveUrl),false,headers.Token).ConfigureAwait(false)) {
                    headers.CancelAfter(Timeout.InfiniteTimeSpan);
                    if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=spec.archiveBytes)throw new InvalidDataException("Package size changed.");
                    using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using(var file=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true)) {
                        byte[] block=new byte[256*1024];long total=0;int count;
                        while((count=await Read(stream,block,token).ConfigureAwait(false))>0){if(total+count>spec.archiveBytes)throw new InvalidDataException("Package exceeds its signed size.");await file.WriteAsync(block,0,count,token).ConfigureAwait(false);total+=count;progress(total);}
                        await file.FlushAsync(token).ConfigureAwait(false);file.Flush(true);
                        if(total!=spec.archiveBytes)throw new InvalidDataException("Package download was incomplete.");
                    }
                }
            }
        }
        public void Dispose(){client.Dispose();}
    }
    internal sealed class Installation
    {
        internal readonly string Root;internal readonly TrustedRelease Release;
        internal string Executable {get{return SafeFiles.Child(Root,"Versions/"+Release.Spec.DirectoryName+"/Another Client.exe");}}
        internal Installation(string root,TrustedRelease release){Root=root;Release=release;}
    }
    internal sealed class InstallerEngine
    {
        internal readonly string Root;private readonly string publicKey;private readonly IReleaseTransport transport;private readonly Action<InstallProgress> progress;private readonly bool deferred;
        internal InstallerEngine(string root,string key,IReleaseTransport source,Action<InstallProgress> onProgress,bool deferActivation=false){Root=Path.GetFullPath(root);publicKey=key;transport=source;progress=onProgress??(p=>{});deferred=deferActivation;}
        private string Installer {get{return SafeFiles.Child(Root,"Installer");}}
        private void Report(string text,int percent,bool pending=false){progress(new InstallProgress(text,percent,pending));}
        internal Installation Installed(string pointer="current.json")
        {
            try{
                string path=SafeFiles.Child(Installer,pointer);SafeFiles.NoReparse(path);
                if(new FileInfo(path).Length>65536)return null;
                var release=ReleaseTrust.Verify(File.ReadAllBytes(path),publicKey);
                string folder=SafeFiles.Child(Root,"Versions/"+release.Spec.DirectoryName);
                VerifyFolder(folder,release.Spec,CancellationToken.None,false);
                return new Installation(Root,release);
            }catch(Exception e){if(e is OutOfMemoryException)throw;return null;}
        }
        internal async Task<Installation> Prepare(CancellationToken token)
        {
            try{return await PrepareCore(token).ConfigureAwait(false);}
            catch(Exception e){
                if(token.IsCancellationRequested)throw new OperationCanceledException(token);
                if(e is OutOfMemoryException)throw;
                var usable=Installed()??Installed("previous.json");
                if(usable==null)throw;
                Report("Update unavailable. Starting Another Client...",100);return usable;
            }
        }
        private async Task<Installation> PrepareCore(CancellationToken token)
        {
            SafeFiles.NoReparse(Root);SafeFiles.EnsureDirectory(Installer);SafeFiles.EnsureDirectory(SafeFiles.Child(Root,"Versions"));
            Report("Preparing Another Client...",0,true);
            var installed=Installed()??Installed("previous.json");TrustedRelease latest;
            try{latest=ReleaseTrust.Verify(await transport.Feed(token).ConfigureAwait(false),publicKey);}
            catch(Exception e){if(token.IsCancellationRequested)throw new OperationCanceledException(token);if(e is OutOfMemoryException)throw;if(installed==null)throw;Report("Starting Another Client...",100);return installed;}
            token.ThrowIfCancellationRequested();
            if(installed!=null&&latest.Spec.sequence<=installed.Release.Spec.sequence){Report("Starting Another Client...",100);return installed;}
            string final=SafeFiles.Child(Root,"Versions/"+latest.Spec.DirectoryName);
            if(Directory.Exists(final)) {
                try{VerifyFolder(final,latest.Spec,token,true);if(!deferred)Activate(latest,token);return new Installation(Root,latest);}
                catch(OperationCanceledException){throw;}
                catch(Exception e){
                    if(!(e is IOException)&&!(e is InvalidDataException))throw;
                    // Keep the damaged version for recovery instead of changing its files in place.
                    SafeFiles.NoReparse(final);
                    Directory.Move(final,final+".invalid-"+Guid.NewGuid().ToString("N"));
                }
            }
            var drive=new DriveInfo(Path.GetPathRoot(Root));
            if(drive.AvailableFreeSpace<latest.Spec.archiveBytes+latest.Spec.uncompressedBytes+64L*1024*1024)throw new IOException("There is not enough free disk space to install Another Client.");
            string downloads=SafeFiles.Child(Installer,"Downloads");SafeFiles.EnsureDirectory(downloads);
            string archive=SafeFiles.Child(downloads,latest.Spec.archiveSha256+".zip");SafeFiles.NoReparse(archive);
            bool cached=File.Exists(archive)&&new FileInfo(archive).Length==latest.Spec.archiveBytes&&ReleaseTrust.HashFile(archive,token)==latest.Spec.archiveSha256;
            if(!cached) {
                string partial=SafeFiles.Child(downloads,Guid.NewGuid().ToString("N")+".partial");
                try{
                    Report("Downloading Another Client...",3);
                    await transport.Download(latest.Spec,partial,n=>Report("Downloading Another Client...",3+(int)(69.0*n/latest.Spec.archiveBytes)),token).ConfigureAwait(false);
                    Report("Verifying Another Client...",72);
                    if(ReleaseTrust.HashFile(partial,token,n=>Report("Verifying Another Client...",72+(int)(6.0*n/latest.Spec.archiveBytes)))!=latest.Spec.archiveSha256)throw new InvalidDataException("The downloaded package failed verification.");
                    if(File.Exists(archive))File.Delete(archive);File.Move(partial,archive);
                }finally{if(File.Exists(partial))File.Delete(partial);}
            }
            string staging=SafeFiles.Child(Installer,"Staging");SafeFiles.EnsureDirectory(staging);
            string transaction=SafeFiles.Child(staging,Guid.NewGuid().ToString("N"));SafeFiles.EnsureDirectory(transaction);
            try{
                Report("Installing Another Client...",78);
                Extract(archive,transaction,latest.Spec,token,n=>Report("Installing Another Client...",78+(int)(19.0*n/latest.Spec.uncompressedBytes)));
                VerifyFolder(transaction,latest.Spec,token,true);token.ThrowIfCancellationRequested();
                SafeFiles.NoReparse(final);Directory.Move(transaction,final);
                if(!deferred)Activate(latest,token);return new Installation(Root,latest);
            }finally{if(Directory.Exists(transaction))SafeFiles.DeleteOwnedTree(staging,transaction);}
        }
        internal void Commit(Installation installation,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(!String.Equals(installation.Root,Root,StringComparison.OrdinalIgnoreCase))throw new IOException("Installation root changed before activation.");
            var current=Installed();
            if(current!=null&&current.Release.Spec.sequence>installation.Release.Spec.sequence)throw new IOException("A newer client version is already active.");
            if(current!=null&&current.Release.Spec.archiveSha256==installation.Release.Spec.archiveSha256)return;
            VerifyFolder(Path.GetDirectoryName(installation.Executable),installation.Release.Spec,token,false);Activate(installation.Release,token);
        }
        private void Activate(TrustedRelease release,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Report("Installing Another Client...",98);
            string backup=Installed()!=null?SafeFiles.Child(Installer,"previous.json"):null;
            SafeFiles.WriteAtomic(SafeFiles.Child(Installer,"current.json"),release.Envelope,backup);
        }
        internal static Dictionary<string,string> Manifest(string folder,ReleaseSpec spec)
        {
            string path=SafeFiles.Child(folder,"client-manifest.json");SafeFiles.NoReparse(path);
            if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Client manifest is too large.");
            byte[] bytes=File.ReadAllBytes(path);if(ReleaseTrust.Hash(bytes)!=spec.manifestSha256)throw new InvalidDataException("Client manifest verification failed.");
            var value=ReleaseTrust.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));
            object raw;
            if(!value.TryGetValue("accountsCopied",out raw)||!(raw is bool)||(bool)raw||!value.TryGetValue("privateKeysCopied",out raw)||!(raw is bool)||(bool)raw)throw new InvalidDataException("Client package privacy metadata is invalid.");
            var files=ReleaseTrust.Json.ConvertToType<Dictionary<string,string>>(value["files"]);
            if(files.Count+1!=spec.fileCount||!files.ContainsKey("Another Client.exe")||files["Another Client.exe"]!=spec.exeSha256)throw new InvalidDataException("Client package inventory is invalid.");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in files){SafeFiles.Relative(pair.Key);if(!seen.Add(pair.Key)||!Regex.IsMatch(pair.Value,"^[a-f0-9]{64}$"))throw new InvalidDataException("Invalid package entry.");}
            return files;
        }
        internal static void VerifyFolder(string folder,ReleaseSpec spec,CancellationToken token,bool full)
        {
            SafeFiles.NoReparse(folder);var files=Manifest(folder,spec);
            foreach(var pair in files) {
                if(!full&&pair.Key.Contains("/"))continue;
                token.ThrowIfCancellationRequested();string path=SafeFiles.Child(folder,pair.Key);SafeFiles.NoReparse(path);
                if(ReleaseTrust.HashFile(path,token)!=pair.Value)throw new InvalidDataException("A client component failed verification: "+pair.Key);
            }
        }
        internal static void Extract(string archive,string destination,ReleaseSpec spec,CancellationToken token,Action<long> report)
        {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long declared=0,total=0;int files=0;
            using(var stream=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Read,false)) {
                foreach(var entry in zip.Entries) {
                    token.ThrowIfCancellationRequested();string path=entry.FullName;bool directory=path.EndsWith("/",StringComparison.Ordinal);
                    string clean=directory?path.TrimEnd('/'):path;SafeFiles.Relative(clean);
                    if(!names.Add(clean))throw new InvalidDataException("Duplicate package path.");
                    if(((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Package links are not supported.");
                    if(clean==spec.topFolder&&directory)continue;
                    string prefix=spec.topFolder+"/";
                    if(!clean.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidDataException("Unexpected package root.");
                    string relative=clean.Substring(prefix.Length);SafeFiles.Relative(relative);
                    if(!directory){checked{declared+=entry.Length;files++;}if(declared>spec.uncompressedBytes||files>spec.fileCount)throw new InvalidDataException("Package expanded beyond its signed limits.");}
                }
                if(files!=spec.fileCount||declared!=spec.uncompressedBytes)throw new InvalidDataException("Package contents are incomplete.");
                foreach(var entry in zip.Entries) {
                    token.ThrowIfCancellationRequested();if(entry.FullName.EndsWith("/",StringComparison.Ordinal))continue;
                    string relative=entry.FullName.Substring(spec.topFolder.Length+1);string target=SafeFiles.Child(destination,relative);SafeFiles.NoReparse(target);
                    SafeFiles.EnsureDirectory(Path.GetDirectoryName(target));
                    using(var input=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        byte[] block=new byte[256*1024];int read;long written=0;
                        while((read=input.Read(block,0,block.Length))>0){token.ThrowIfCancellationRequested();if(written+read>entry.Length)throw new InvalidDataException("Invalid package entry length.");output.Write(block,0,read);written+=read;total+=read;report(total);}
                        if(written!=entry.Length)throw new InvalidDataException("Truncated package entry.");
                    }
                }
            }
            var expected=Manifest(destination,spec);expected.Add("client-manifest.json",spec.manifestSha256);
            var actual=Directory.GetFiles(destination,"*",SearchOption.AllDirectories).Select(p=>p.Substring(destination.TrimEnd('\\').Length+1).Replace('\\','/')).ToArray();
            if(actual.Length!=expected.Count||actual.Any(p=>!expected.ContainsKey(p)))throw new InvalidDataException("Unexpected files in client package.");
        }
    }
}
