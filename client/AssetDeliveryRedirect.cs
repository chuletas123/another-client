using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AnotherClient.Avatar
{
    // The native resolver expects a location, not model bytes, at /v1/asset.
    // Keep resource bodies at the existing content endpoint on the same service.
    public static class AssetDeliveryRedirect
    {
        public static HttpMessageHandler Wrap(HttpMessageHandler inner)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            return new RedirectHandler(inner);
        }
        private sealed class RedirectHandler : DelegatingHandler
        {
            public RedirectHandler(HttpMessageHandler inner) : base(inner) { }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Uri uri = request.RequestUri;
                if (uri != null && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
                    request.Content == null && (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head) &&
                    (uri.AbsolutePath == "/services/assetdelivery/v1/asset" || uri.AbsolutePath == "/services/assetdelivery/v1/asset/") &&
                    HasAssetIdentity(uri.Query)) {
                    var destination = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/services/assetdelivery/another-client/assets/content" + uri.Query, UriKind.Absolute);
                    var response = new HttpResponseMessage(HttpStatusCode.Found);
                    response.RequestMessage = request;
                    response.Content = new ByteArrayContent(new byte[0]);
                    response.Headers.Location = destination;
                    response.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true, Private = true };
                    return Task.FromResult(response);
                }
                return base.SendAsync(request, token);
            }
        }
        private static bool HasAssetIdentity(string query)
        {
            if (query.Length < 2 || query.Length > 4096) return false;
            bool found = false;
            foreach (string part in query.Substring(1).Split('&')) {
                int at = part.IndexOf('='); if (at < 1) return false;
                string name, value;
                try { name = Uri.UnescapeDataString(part.Substring(0, at)); value = Uri.UnescapeDataString(part.Substring(at + 1)); }
                catch (UriFormatException) { return false; }
                if (name != "id" && name != "hash") continue;
                if (found) return false;
                if (name == "id") {
                    long id;
                    if (!Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id < 1 || id > 9007199254740991L) return false;
                } else {
                    if (value.Length != 32 && value.Length != 64) return false;
                    foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')) return false;
                }
                found = true;
            }
            return found;
        }
    }
}
