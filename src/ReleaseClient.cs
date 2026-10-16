using System;
using System.IO;
using System.Text;
using System.Net;
using System.Linq;
using System.Threading;

namespace Luna {
    internal sealed class ReleaseClient {
        readonly Func<string, CancellationToken, string> ReleaseTransport;
        readonly Func<string, long, CancellationToken, HttpWebResponse> AppDownloadTransport;
        HttpWebResponse ReleaseResponse(string url, long offset, CancellationToken token) {
            for (int redirects = 0; redirects < 5; redirects++) {
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo)) throw new IOException("Untrusted update URL");
                var request = (HttpWebRequest)WebRequest.Create(uri); request.UserAgent = "Luna/" + PreferenceStore.AppVersion;
                request.Timeout = 8000; request.ReadWriteTimeout = 5000; request.AllowAutoRedirect = false;
                if (offset > 0) request.AddRange(offset);
                HttpWebResponse response;
                using (token.Register(request.Abort)) { token.ThrowIfCancellationRequested(); response = (HttpWebResponse)request.GetResponse(); }
                int code = (int)response.StatusCode;
                if (code >= 300 && code < 400) { string next = response.Headers["Location"]; response.Dispose(); url = new Uri(uri, next).AbsoluteUri; continue; }
                return response;
            }
            throw new IOException("Too many release redirects");
        }
        string ReadReleaseText(string url, CancellationToken token) {
            using (var response = ReleaseResponse(url, 0, token)) using (var input = response.GetResponseStream()) using (var output = new MemoryStream()) {
                using (token.Register(response.Close)) {
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); if (output.Length > 2 * 1024 * 1024) throw new IOException("Release metadata too large"); }
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
        internal ReleaseClient(Func<string, CancellationToken, string> text = null, Func<string, long, CancellationToken, HttpWebResponse> download = null) { ReleaseTransport = text ?? ReadReleaseText; AppDownloadTransport = download ?? ReleaseResponse; }
        internal string Read(string url, CancellationToken token) { return ReleaseTransport(url, token); }
        internal HttpWebResponse Download(string url, long offset, CancellationToken token) { return AppDownloadTransport(url, offset, token); }
    }
}
