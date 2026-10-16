using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Luna {
    internal sealed class LocalHttpException : IOException {
        internal readonly int Status;
        internal LocalHttpException(int status, string message) : base(message) { Status = status; }
    }

    // Deliberately small, body-free HTTP subset for the loopback application.
    // Parsing and deadlines are independent of routes and global app state.
    internal sealed class LocalHttpRequest {
        internal string Method, Target;
        internal readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool Token(string text) {
            if (text.Length == 0) return false;
            foreach (char c in text) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || "!#$%&'*+-.^_`|~".IndexOf(c) >= 0)) return false;
            return true;
        }
        internal static LocalHttpRequest Read(NetworkStream stream, int deadlineMilliseconds = 10000) {
            var watch = Stopwatch.StartNew(); var bytes = new List<byte>();
            while (bytes.Count < 16384) {
                long remaining = deadlineMilliseconds - watch.ElapsedMilliseconds;
                if (remaining <= 0) throw new LocalHttpException(408, "请求头读取超时");
                stream.ReadTimeout = (int)remaining;
                int next;
                try { next = stream.ReadByte(); }
                catch (IOException ex) {
                    var socket = ex.InnerException as SocketException;
                    if (watch.ElapsedMilliseconds >= deadlineMilliseconds || socket != null && socket.SocketErrorCode == SocketError.TimedOut) throw new LocalHttpException(408, "请求头读取超时");
                    throw;
                }
                if (next < 0) return null;
                if (next > 126 || next < 32 && next != 9 && next != 10 && next != 13) throw new LocalHttpException(400, "无效请求头字符");
                bytes.Add((byte)next); int n = bytes.Count;
                if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) return Parse(Encoding.ASCII.GetString(bytes.ToArray()));
            }
            throw new LocalHttpException(400, "请求过长");
        }
        static LocalHttpRequest Parse(string text) {
            string[] lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None), first = lines[0].Split(' ');
            if (first.Length != 3 || !Token(first[0]) || first[2] != "HTTP/1.1" && first[2] != "HTTP/1.0" || !first[1].StartsWith("/") || first[1].StartsWith("//") || first[1].IndexOfAny(new[] { '\\', '\t', '\r', '\n', '#' }) >= 0)
                throw new LocalHttpException(400, "无效请求行");
            var request = new LocalHttpRequest { Method = first[0], Target = first[1] };
            for (int i = 1; i < lines.Length - 2; i++) {
                string line = lines[i]; int colon = line.IndexOf(':');
                if (colon <= 0 || !Token(line.Substring(0, colon)) || line.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new LocalHttpException(400, "无效请求头");
                string name = line.Substring(0, colon);
                if (request.Headers.ContainsKey(name)) throw new LocalHttpException(400, "重复请求头");
                request.Headers.Add(name, line.Substring(colon + 1).Trim(' ', '\t'));
            }
            return request;
        }
    }

    // Browser disconnects cancel queued work and upstream requests.
    internal sealed class LocalHttpCancellation : IDisposable {
        readonly CancellationTokenSource source;
        readonly Timer monitor;
        internal CancellationToken Token { get { return source.Token; } }
        internal LocalHttpCancellation(TcpClient client, CancellationToken shutdown) {
            source = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            monitor = new Timer(delegate {
                try { if (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0) Cancel(); }
                catch (SocketException) { Cancel(); }
                catch (ObjectDisposedException) { Cancel(); }
            }, null, 100, 100);
        }
        void Cancel() { try { source.Cancel(); } catch (ObjectDisposedException) { } }
        public void Dispose() { monitor.Dispose(); source.Dispose(); }
    }
}
