using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AnotherClient.Installer
{
    internal sealed class ClientCheck
    {
        internal const int ContinueCurrent=10,Updated=20;
        internal readonly long Sequence;internal readonly string Reply;internal readonly byte[] Feed;
        private ClientCheck(long sequence,string reply,byte[] feed){Sequence=sequence;Reply=reply;Feed=feed;}
        internal static ClientCheck Read(string[] args,string root,string key,IReleaseTransport transport)
        {
            if(args.Length==0)return null;
            if(args.Length!=3||args[0]!="--client-check")throw new ArgumentException("Unsupported installer arguments.");
            long sequence;if(!Int64.TryParse(args[1],NumberStyles.None,CultureInfo.InvariantCulture,out sequence)||sequence<1)throw new ArgumentException("Invalid client version.");
            string requests=SafeFiles.Child(root,"Installer/Requests");string reply=Path.GetFullPath(args[2]);
            if(!reply.StartsWith(requests+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(reply)!=requests||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(reply),"^[a-f0-9]{32}\\.json$"))throw new IOException("Invalid update reply location.");
            SafeFiles.NoReparse(reply);if(File.Exists(reply))throw new IOException("Update reply already exists.");
            byte[] feed=null;
            try{using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4))){feed=transport.Feed(deadline.Token).GetAwaiter().GetResult();ReleaseTrust.Verify(feed,key);}}
            catch(Exception e){if(e is OutOfMemoryException)throw;feed=null;}
            return new ClientCheck(sequence,reply,feed);
        }
        internal bool Needed(string root,string key,IReleaseTransport transport)
        {
            var engine=new InstallerEngine(root,key,transport,null);var local=engine.Installed();
            if(local!=null&&local.Release.Spec.sequence>Sequence)return true;
            return Feed!=null&&ReleaseTrust.Verify(Feed,key).Spec.sequence>Sequence;
        }
        internal bool Complete(Installation installation,string key)
        {
            if(installation.Release.Spec.sequence<=Sequence)return false;
            var release=ReleaseTrust.Verify(installation.Release.Envelope,key);
            InstallerEngine.VerifyFolder(Path.GetDirectoryName(installation.Executable),release.Spec,CancellationToken.None,false);
            SafeFiles.WriteAtomic(Reply,release.Envelope);return true;
        }
    }
    internal sealed class PreparedFeedTransport:IReleaseTransport
    {
        private readonly IReleaseTransport inner;private readonly byte[] feed;
        internal PreparedFeedTransport(IReleaseTransport transport,byte[] prepared){inner=transport;feed=prepared;}
        public Task<byte[]> Feed(CancellationToken token){token.ThrowIfCancellationRequested();if(feed==null)throw new IOException("Update service unavailable.");return Task.FromResult(feed);}
        public Task Download(ReleaseSpec release,string target,Action<long> progress,CancellationToken token){return inner.Download(release,target,progress,token);}
    }
}
