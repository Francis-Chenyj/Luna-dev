using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

internal static class HttpTransportTests {
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static int Main() {
        try {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            try {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (var shutdown = new CancellationTokenSource()) using (var browser = new TcpClient("127.0.0.1", port)) using (var client = listener.AcceptTcpClient()) using (var request = new Luna.LocalHttpCancellation(client, shutdown.Token)) {
                    Check(!request.Token.IsCancellationRequested, "A live connection started cancelled");
                    browser.Close();
                    Check(request.Token.WaitHandle.WaitOne(2000), "Browser disconnect did not cancel its request");
                    Check(!shutdown.IsCancellationRequested, "A disconnected browser cancelled the whole application");
                }
                using (var shutdown = new CancellationTokenSource()) using (var browser = new TcpClient("127.0.0.1", port)) using (var client = listener.AcceptTcpClient()) using (var request = new Luna.LocalHttpCancellation(client, shutdown.Token)) {
                    shutdown.Cancel(); Check(request.Token.IsCancellationRequested, "Application shutdown did not cancel the request");
                }
                using (var shutdown = new CancellationTokenSource()) using (var browser = new TcpClient("127.0.0.1", port)) using (var client = listener.AcceptTcpClient()) {
                    for (int i = 0; i < 100; i++) new Luna.LocalHttpCancellation(client, shutdown.Token).Dispose();
                    shutdown.Cancel(); Thread.Sleep(150);
                }
            } finally { listener.Stop(); }
            Console.WriteLine("PASS: connection disconnect, application shutdown and disposed monitor lifetime"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
