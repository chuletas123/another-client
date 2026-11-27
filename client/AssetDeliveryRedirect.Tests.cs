using System;using System.IO;using System.Linq;using System.Net;using System.Net.Http;using System.Reflection;using System.Threading;using System.Threading.Tasks;
class AssetDeliveryRedirectTests{
static int checks;static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
sealed class Backend:HttpMessageHandler{internal int Calls;internal string LastPath;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;LastPath=request.RequestUri.PathAndQuery;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{60,114,111,98,108,111,120,62})});}}
static int Main(string[]a){try{Run(a).GetAwaiter().GetResult();Console.WriteLine("PASS: "+checks+" redirect, avatar asset changes, cancellation and isolation checks; no Player or window started.");return 0;}catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
static async Task Run(string[]a){
 var assembly=Assembly.LoadFile(Path.GetFullPath(a[0]));var wrap=assembly.GetType("AnotherClient.Avatar.AvatarPreviewTransport",true).GetMethod("Wrap");var backend=new Backend();
 using(var client=new HttpMessageInvoker((HttpMessageHandler)wrap.Invoke(null,new object[]{backend}))){
  string origin="https://163.245.199.221";string[] ids={"12499718918","118667987299643","111665983957272","90170514157622","13623290442","48474356","114989176049162","137966412181034","84803434739105"};
  foreach(string id in ids)foreach(string slash in new[]{"","/"}){
   string query="?id="+id+"&serverplaceid=0&expectedAssetType=Shirt&version=2&contentRepresentationPriorityList=ab%2Bc%3D";
   using(var request=new HttpRequestMessage(HttpMethod.Get,origin+"/services/assetdelivery/v1/asset"+slash+query))using(var response=await client.SendAsync(request,CancellationToken.None)){
    Check(response.StatusCode==HttpStatusCode.Found,"Native resolver received model bytes instead of a location");
    Check(response.Headers.Location.OriginalString==origin+"/services/assetdelivery/another-client/assets/content"+query,"Resource identity or representation changed: "+response.Headers.Location.OriginalString);
    Check(response.Headers.CacheControl.NoStore,"Redirect can be cached as a resource body");
    Check((await response.Content.ReadAsByteArrayAsync()).Length==0,"Resolver unnecessarily downloaded resource bytes");
   }
  }
  Check(backend.Calls==0,"Resolving avatars should not fetch bytes twice or replay an old cached binary");
  using(var response=await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,origin+"/services/assetdelivery/v1/asset?id=42"),CancellationToken.None))Check(response.StatusCode==HttpStatusCode.Found,"HEAD resolver not adapted");
  using(var response=await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,origin+"/services/assetdelivery/another-client/assets/content?id=42"),CancellationToken.None)){
   Check(response.StatusCode==HttpStatusCode.OK,"Content endpoint must keep its binary response");Check((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[]{60,114,111,98,108,111,120,62}),"Content bytes changed");}
  foreach(string path in new[]{"/services/avatar/v2/avatar/users/3/avatar","/services/avatar/another-client/avatar-editor/finish","/services/thumbnails/v1/users/avatar","/services/assetdelivery/v1/assets/batch","/services/assetdelivery/v1/asset/other?id=42","/services/assetdelivery/v1/asset?id=0","/services/assetdelivery/v1/asset?id=3&id=4","/services/assetdelivery/v1/asset?id=abc","/services/assetdelivery/v1/asset?id=9007199254740992"}){
   int before=backend.Calls;using(var response=await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,origin+path),CancellationToken.None))Check(response.StatusCode==HttpStatusCode.OK&&backend.Calls==before+1,"Unrelated or invalid request intercepted: "+path);}
  using(var response=await client.SendAsync(new HttpRequestMessage(HttpMethod.Post,origin+"/services/assetdelivery/v1/asset?id=42"),CancellationToken.None))Check(response.StatusCode==HttpStatusCode.OK,"Write semantics changed");
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,origin+"/services/assetdelivery/v1/asset?id=42"),cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Cancellation ignored");}
 }
 // Exercise the exact URL mapping from the shipped transport, without starting it.
 var player=Assembly.LoadFile(Path.GetFullPath(a[1]));var policyType=player.GetType("AnotherClient.Player.NativeTransportPolicy",true);var ctor=policyType.GetConstructors().Single();var parameters=new object[ctor.GetParameters().Length];parameters[0]="https://163.245.199.221";var policy=ctor.Invoke(parameters);
 string content="https://163.245.199.221/services/assetdelivery/another-client/assets/content?id=42&version=7";
 string native=(string)policyType.GetMethod("NativeAssetUrl").Invoke(policy,new object[]{content});Check(native=="https://assetdelivery.roblox.com/another-client/assets/content?id=42&version=7","The engine will not recognize the returned location");
}
}
