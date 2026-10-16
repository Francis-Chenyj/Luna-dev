using System;
using System.IO;
using System.Text;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Luna {
    internal sealed class DataSourceClient {
        readonly Func<string, CancellationToken, Dictionary<string, object>> LibraryTransport;
        readonly SemaphoreSlim NetworkSlots = new SemaphoreSlim(3, 3);
        internal Dictionary<string, object> DownloadJson(string url, CancellationToken token) {
            if (!NetworkSlots.Wait(5000, token)) throw new IOException("数据连接繁忙，请稍后重试。");
            try {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 18000; request.ReadWriteTimeout = 18000;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.UserAgent = "Luna/" + PreferenceStore.AppVersion; request.Accept = "application/json";
                // Honor Windows proxy settings; TLS certificate validation remains enabled.
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var memory = new MemoryStream()) {
                    byte[] block = new byte[8192]; int n;
                    while ((n = stream.Read(block, 0, block.Length)) > 0) {
                        token.ThrowIfCancellationRequested();
                        memory.Write(block, 0, n); if (memory.Length > 16 * 1024 * 1024) throw new IOException("数据包超过限制");
                    }
                    return DataFormat.Obj(DataFormat.Json().DeserializeObject(Encoding.UTF8.GetString(memory.ToArray())));
                }
            } catch { token.ThrowIfCancellationRequested(); throw; }
            finally { NetworkSlots.Release(); }
        }
        internal DataSourceClient(Func<string, CancellationToken, Dictionary<string, object>> transport = null) { LibraryTransport = transport ?? DownloadJson; }
        internal Dictionary<string, object> Fetch(string url, CancellationToken token) { return LibraryTransport(url, token); }
        public void Dispose() { NetworkSlots.Dispose(); }
    }
}
