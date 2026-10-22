using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Luna;

internal static class ClientDraftTests {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly ClientDraftConnection Connection = new ClientDraftConnection(1234, 54321, "fixture-token");
    static int failures;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Run(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); } catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }
    static object Member(int cell, int champion, int intent = 0, string lane = "TOP") {
        return new { cellId = cell, championId = champion, championPickIntent = intent, assignedPosition = lane,
            gameName = "PRIVATE-NAME", tagLine = "PRIVATE-TAG", summonerId = 987654321, puuid = "PRIVATE-PUUID", obfuscatedPuuid = "PRIVATE-OBFUSCATED", playerAlias = "PRIVATE-ALIAS" };
    }
    static object Action(int cell, int champion, bool completed, bool ally, string type = "pick") {
        return new { actorCellId = cell, championId = champion, completed = completed, isAllyAction = ally, type = type, id = cell + 9000, isInProgress = !completed, pickTurn = 50 };
    }
    static string Session(object[] allies, object[] enemies, object[][] actions, bool spectating = false) {
        return Json.Serialize(new { myTeam = allies, theirTeam = enemies, actions = actions, isSpectating = spectating,
            gameId = 987654321, chatDetails = new { multiUserChatPassword = "PRIVATE-CHAT-PASSWORD", mucJwtDto = new { jwt = "PRIVATE-JWT" } } });
    }
    static string Draft(int ally = 11, int enemy = 22) {
        return Session(new[] { Member(0, ally), Member(1, 0, 33, "UTILITY") }, new[] { Member(5, enemy), Member(6, 44, 55) },
            new[] { new[] { Action(0, 99, true, true, "ban") }, new[] { Action(5, enemy, true, false) }, new[] { Action(0, 11, true, true), Action(1, 33, false, true) }, new[] { Action(6, 44, false, false) } });
    }
    static void Empty(ClientDraftSnapshot snapshot) { Check(snapshot.teams.ally.Length == 5 && snapshot.teams.enemy.Length == 5, "Not exactly ten slots"); Check(snapshot.teams.ally.Concat(snapshot.teams.enemy).All(x => x.championId == 0 && x.selection == "empty" && x.pickOrder == 0 && x.lane == ""), "Stale or private state remains"); }
    static void Reject(string body, string code = "invalid-response") {
        try { new ClientDraftProjector().Project(body); throw new Exception("Invalid fixture accepted"); }
        catch (ClientDraftException error) { Check(error.Code == code, "Wrong safe error code"); Check(!error.ToString().Contains("PRIVATE"), "Raw data appears in error"); }
    }
    static void Projection() {
        var projector = new ClientDraftProjector(); var teams = projector.Project(Draft());
        Check(teams.ally.Length == 5 && teams.enemy.Length == 5, "Missing placeholders");
        Check(teams.enemy[0].championId == 22 && teams.enemy[0].selection == "locked" && teams.enemy[0].pickOrder == 1, "Enemy locked pick not projected");
        Check(teams.ally[0].championId == 11 && teams.ally[0].selection == "locked" && teams.ally[0].pickOrder == 2, "Ban affected pick order");
        Check(teams.ally[1].championId == 33 && teams.ally[1].selection == "hovered" && teams.ally[1].lane == "support" && teams.ally[1].pickOrder == 2, "Ally intent or simultaneous group lost");
        Check(teams.enemy[1].championId == 0 && teams.enemy[1].selection == "empty", "Enemy incomplete pick/intent leaked");
        string projected = Json.Serialize(teams);
        foreach (string forbidden in new[] { "PRIVATE", "gameName", "summonerId", "puuid", "chatDetails", "gameId", "token", "cellId" }) Check(projected.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) < 0, "Projection retains forbidden field");
        Check(teams.ally[0].slot == 0 && teams.ally[4].slot == 4, "Slot numbering changed");
    }
    static void TradesAndPartialSnapshots() {
        var projector = new ClientDraftProjector(); projector.Project(Draft());
        var traded = projector.Project(Draft(77, 88));
        Check(traded.ally[0].championId == 77 && traded.enemy[0].championId == 88, "Historical action overwrote traded champion");
        var partial = projector.Project(Session(new[] { Member(1, 66) }, new object[0], new object[0][]));
        Check(partial.ally[1].championId == 66 && partial.ally[0].championId == 0, "Partial update moved surviving slot");
        var reorder = projector.Project(Session(new[] { Member(1, 33), Member(0, 11) }, new object[0], new[] { new[] { Action(1, 33, true, true) }, new[] { Action(0, 11, true, true) } }));
        Check(reorder.ally[0].championId == 11 && reorder.ally[1].championId == 33, "Array reorder moved stable slots");
        Check(reorder.ally[0].pickOrder == 2 && reorder.ally[1].pickOrder == 1, "Pick-order swap not reflected");
        projector.Reset(); var reset = projector.Project(Session(new[] { Member(1, 66) }, new object[0], new object[0][]));
        Check(reset.ally[0].championId == 66, "Session reset retained positions");
        var invisible = projector.Project(Session(new object[0], new[] { Member(5, 22, 33) }, new object[0][]));
        Check(invisible.enemy[0].championId == 0, "Enemy without completed action revealed");
    }
    static void Malformed() {
        Reject("null"); Reject("{bad PRIVATE}"); Reject("{}");
        Reject(Session(new[] { Member(0, 11), Member(0, 22) }, new object[0], new object[0][]));
        Reject(Session(new[] { Member(0, 11) }, new[] { Member(0, 22) }, new object[0][]));
        Reject(Session(new[] { Member(0, 11) }, new object[0], new[] { new[] { Action(0, 11, true, true), Action(0, 22, true, true) } }));
        Reject(Session(Enumerable.Range(0, 6).Select(i => Member(i, 11)).ToArray(), new object[0], new object[0][]), "unsupported-mode");
        Reject(Session(new object[0], new object[0], new object[0][], true), "unsupported-mode");
        Reject(new string(' ', ClientDraftProtocol.MaxResponseBytes + 1));
        var weird = new ClientDraftProjector().Project(Session(new[] { Member(0, -1, 100001, "PRIVATE-LANE") }, new object[0], new object[0][]));
        Check(weird.ally[0].championId == 0 && weird.ally[0].lane == "", "Invalid hero or lane escaped projection");
    }
    static void ProtocolBoundaries() {
        var parsed = ClientDraftProtocol.ParseCommandLine("\"C:\\League Client\\LeagueClientUx.exe\" \"--app-port=54321\" --remoting-auth-token=fixture-token --app-pid=1234 --riotclient-app-port=12345", 99);
        Check(parsed != null && parsed.Port == 54321 && parsed.ProcessId == 1234 && parsed.Token == "fixture-token", "Quoted command line not recognized");
        Check(ClientDraftProtocol.ParseCommandLine("--app-port=0 --remoting-auth-token=x --app-pid=1", 1) == null, "Invalid port accepted");
        Check(ClientDraftProtocol.ParseCommandLine("--app-port=1000 --app-port=1001 --remoting-auth-token=x --app-pid=1", 1) == null, "Ambiguous port accepted");
        Check(ClientDraftProtocol.ParseCommandLine("--riotclient-app-port=1000 --riotclient-auth-token=x --app-pid=1", 1) == null, "Riot Client credentials mistaken for LCU");
        Check(ClientDraftProtocol.ParseLockfile("LeagueClient:1:54321:fixture-token:https") != null, "Valid lockfile rejected");
        Check(ClientDraftProtocol.ParseLockfile("LeagueClient:1:54321:fixture-token:http") == null && ClientDraftProtocol.ParseLockfile("Other:1:54321:x:https") == null, "Invalid lockfile accepted");
        var global = ServicePointManager.ServerCertificateValidationCallback;
        var request = ClientDraftProtocol.CreateRequest(Connection, ClientDraftProtocol.PhasePath);
        Check(request.Method == "GET" && request.RequestUri.Host == "127.0.0.1" && request.RequestUri.Scheme == "https", "Request is not local HTTPS GET");
        Check(!request.AllowAutoRedirect && request.Proxy == null && request.ServerCertificateValidationCallback != null, "Transport can redirect or proxy credentials");
        Check(global == ServicePointManager.ServerCertificateValidationCallback, "Global TLS validation changed");
        Check(request.Timeout == 3000 && request.ReadWriteTimeout == 3000, "Transport timeout missing");
        request.Abort();
        foreach (string path in new[] { "https://example.com/", "/lol-summoner/v1/current-summoner", ClientDraftProtocol.SessionPath + "?personal=1" }) {
            bool rejected = false; try { ClientDraftProtocol.CreateRequest(Connection, path); } catch (ArgumentException) { rejected = true; } Check(rejected, "Endpoint allowlist bypassed");
        }
        // WMI may return a row with null command-line/path for an elevated
        // launcher. Exercise the limited-query fallback against our own child,
        // whose arguments contain fixture credentials only.
        using (var child = Process.Start(new ProcessStartInfo {
            FileName = Process.GetCurrentProcess().MainModule.FileName,
            Arguments = "--limited-query-fixture --app-port=54321 --remoting-auth-token=fixture-token --app-pid=1234",
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        })) {
            Check(child.WaitForExit(5000), "Limited-query fixture did not exit");
            Check(child.ExitCode == 0, "Limited process query could not recover fixture connection");
        }
        // A conclusive native result must not depend on WMI availability.
        foreach (var nativeResult in new[] {
            new ClientDraftDiscovery(new[] { Connection }),
            new ClientDraftDiscovery(new ClientDraftConnection[0]),
            new ClientDraftDiscovery(null, "multiple-clients")
        }) {
            int fallbackCalls = 0;
            var discovered = ClientDraftProtocol.DiscoverWithFallback(CancellationToken.None, token => nativeResult,
                token => { fallbackCalls++; throw new Exception("PRIVATE-WMI-FAILURE"); });
            Check(object.ReferenceEquals(discovered, nativeResult) && fallbackCalls == 0, "Conclusive native discovery depended on WMI");
        }
        int fallbackReads = 0;
        var recovered = ClientDraftProtocol.DiscoverWithFallback(CancellationToken.None,
            token => new ClientDraftDiscovery(null, "discovery-unavailable"),
            token => { fallbackReads++; return new ClientDraftDiscovery(new[] { Connection }); });
        Check(recovered.Connections.Length == 1 && fallbackReads == 1, "WMI fallback did not recover unavailable native discovery");
        var unavailable = ClientDraftProtocol.DiscoverWithFallback(CancellationToken.None,
            token => { throw new Exception("PRIVATE-NATIVE-FAILURE"); },
            token => { throw new Exception("PRIVATE-WMI-FAILURE"); });
        Check(unavailable.Connections.Length == 0 && unavailable.Error == "discovery-unavailable", "Discovery exception escaped safe error boundary");
        using (var cancelled = new CancellationTokenSource()) {
            int afterCancellation = 0; bool stopped = false;
            try {
                ClientDraftProtocol.DiscoverWithFallback(cancelled.Token,
                    token => { cancelled.Cancel(); return new ClientDraftDiscovery(null, "discovery-unavailable"); },
                    token => { afterCancellation++; return new ClientDraftDiscovery(new[] { Connection }); });
            } catch (OperationCanceledException) { stopped = true; }
            Check(stopped && afterCancellation == 0, "Cancelled native discovery continued into WMI");
        }
    }
    sealed class Script : IDisposable {
        internal readonly AutoResetEvent Paused = new AutoResetEvent(false), Advance = new AutoResetEvent(false);
        internal int DiscoveryCalls, Reads, Interval; internal string Phase = "ChampSelect", Body = Draft(); internal int Status = 200;
        internal ClientDraftDiscovery Discovery = new ClientDraftDiscovery(new[] { Connection });
        internal Exception Failure;
        internal ClientDraftService Create() { return new ClientDraftService(discovery: Discover, transport: Read, wait: Wait); }
        ClientDraftDiscovery Discover(CancellationToken token) { Interlocked.Increment(ref DiscoveryCalls); token.ThrowIfCancellationRequested(); return Discovery; }
        ClientDraftResponse Read(ClientDraftConnection connection, string path, CancellationToken token) {
            Interlocked.Increment(ref Reads); token.ThrowIfCancellationRequested();
            Check(path == ClientDraftProtocol.PhasePath || path == ClientDraftProtocol.SessionPath, "Unexpected endpoint");
            if (Failure != null) throw Failure;
            return path == ClientDraftProtocol.PhasePath ? new ClientDraftResponse(Status, Json.Serialize(Phase)) : new ClientDraftResponse(Status, Body);
        }
        void Wait(int milliseconds, CancellationToken token) { Interval = milliseconds; Paused.Set(); if (WaitHandle.WaitAny(new[] { Advance, token.WaitHandle }) == 1) token.ThrowIfCancellationRequested(); }
        internal void Await() { Check(Paused.WaitOne(5000), "Worker did not reach controlled wait"); }
        internal void Next() { Advance.Set(); Await(); }
        public void Dispose() { Paused.Dispose(); Advance.Dispose(); }
    }
    static void ServiceLifecycle() {
        using (var fixture = new Script()) using (var service = fixture.Create()) {
            Empty(service.ReadSnapshot()); Check(!service.ReadSnapshot().enabled && fixture.DiscoveryCalls == 0 && fixture.Reads == 0, "Default service performed discovery or requests");
            service.Connect(); fixture.Await(); var first = service.ReadSnapshot();
            Check(first.enabled && first.state == "champ-select" && fixture.Interval == 1000, "Draft did not synchronize");
            service.Connect(); Check(fixture.DiscoveryCalls == 1, "Connect created a second worker");
            first.teams.ally[0].championId = 999; first.teams.enemy = new ClientDraftSlot[0];
            Check(service.ReadSnapshot().teams.ally[0].championId == 11 && service.ReadSnapshot().teams.enemy.Length == 5, "Caller mutated owned snapshot");
            long revision = service.ReadSnapshot().revision; fixture.Next(); Check(service.ReadSnapshot().revision == revision, "Unchanged snapshot generated updates");
            fixture.Phase = "InProgress"; fixture.Next(); Empty(service.ReadSnapshot()); Check(service.ReadSnapshot().state == "in-game" && fixture.Interval == 2000, "Game transition retained BP data");
            fixture.Phase = "Lobby"; fixture.Next(); Check(service.ReadSnapshot().state == "ready", "Lobby state incorrect");
            fixture.Phase = "PRIVATE-UNKNOWN-PHASE"; fixture.Next(); var unknown = service.ReadSnapshot(); Empty(unknown);
            Check(unknown.state == "unavailable" && unknown.error == "unsupported-phase" && !Json.Serialize(unknown).Contains("PRIVATE"), "Unknown phase leaked");
            fixture.Phase = "ChampSelect"; fixture.Body = Draft(77, 88); fixture.Next(); Check(service.ReadSnapshot().teams.ally[0].championId == 77, "Failed to recover new session");
            var stopped = service.Disconnect(); Empty(stopped); Check(!stopped.enabled && stopped.state == "disabled", "Disconnect left service enabled");
        }
        // The phase can leave BP while a session response is in flight. Confirm
        // it before exposing that session; a failed confirmation fails closed.
        foreach (string nextPhase in new[] { "Lobby", "InProgress", "unauthorized" }) {
            using (var paused = new ManualResetEventSlim()) {
                int reads = 0;
                using (var service = new ClientDraftService(discovery: token => new ClientDraftDiscovery(new[] { Connection }),
                    transport: (connection, path, token) => {
                        int call = Interlocked.Increment(ref reads);
                        if (call == 1) { Check(path == ClientDraftProtocol.PhasePath, "Initial phase missing"); return new ClientDraftResponse(200, "\"ChampSelect\""); }
                        if (call == 2) { Check(path == ClientDraftProtocol.SessionPath, "Session not requested after BP phase"); return new ClientDraftResponse(200, Draft()); }
                        Check(call == 3 && path == ClientDraftProtocol.PhasePath, "Session phase not rechecked before publication");
                        return nextPhase == "unauthorized" ? new ClientDraftResponse(401, "PRIVATE-AUTH-ERROR") : new ClientDraftResponse(200, Json.Serialize(nextPhase));
                    }, wait: (delay, token) => { paused.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); })) {
                    service.Connect(); Check(paused.Wait(5000), "Phase transition worker stalled");
                    var snapshot = service.ReadSnapshot(); Empty(snapshot);
                    Check(reads == 3 && snapshot.revision == 2, "Obsolete BP was published before confirmation");
                    string expected = nextPhase == "Lobby" ? "ready" : nextPhase == "InProgress" ? "in-game" : "unavailable";
                    Check(snapshot.state == expected, "Phase confirmation did not clear stale BP");
                    if (nextPhase == "unauthorized") Check(snapshot.error == "authentication-unavailable", "Confirmation authentication failure not classified");
                    Check(!Json.Serialize(snapshot).Contains("PRIVATE"), "Phase confirmation leaked raw data");
                }
            }
        }
    }
    static void DiscoveryAndErrors() {
        foreach (int mode in new[] { 0, 1, 2, 3 }) using (var fixture = new Script()) using (var service = fixture.Create()) {
            if (mode == 0) fixture.Discovery = new ClientDraftDiscovery(new ClientDraftConnection[0]);
            if (mode == 1) fixture.Discovery = new ClientDraftDiscovery(new[] { Connection, Connection });
            if (mode == 2) fixture.Discovery = new ClientDraftDiscovery(new ClientDraftConnection[0], "PRIVATE-DISCOVERY-EXCEPTION");
            if (mode == 3) fixture.Status = 401;
            service.Connect(); fixture.Await(); var snapshot = service.ReadSnapshot(); Empty(snapshot);
            Check(snapshot.state == (mode == 0 ? "searching" : mode == 1 ? "multiple" : "unavailable"), "Discovery/authentication state incorrect");
            Check(!Json.Serialize(snapshot).Contains("PRIVATE"), "Discovery error leaked");
            if (mode < 3) Check(fixture.Reads == 0 && fixture.Interval == 3000, "Unavailable discovery made a request or busy-looped");
        }
        using (var fixture = new Script()) using (var service = fixture.Create()) {
            fixture.Failure = new ClientDraftException("timeout"); service.Connect(); fixture.Await();
            Check(service.ReadSnapshot().error == "timeout", "Timeout not classified");
            fixture.Failure = new Exception("PRIVATE-EXCEPTION-BODY"); fixture.Next();
            Check(service.ReadSnapshot().error == "connection-unavailable" && !Json.Serialize(service.ReadSnapshot()).Contains("PRIVATE"), "Exception content leaked");
            fixture.Failure = null; fixture.Body = "{bad PRIVATE}"; fixture.Next(); Empty(service.ReadSnapshot()); Check(service.ReadSnapshot().error == "invalid-response", "Malformed response not isolated");
        }
        using (var paused = new ManualResetEventSlim()) using (var service = new ClientDraftService(discovery: token => new ClientDraftDiscovery(new[] { Connection }),
            transport: (connection, path, token) => path == ClientDraftProtocol.PhasePath ? new ClientDraftResponse(200, "\"ChampSelect\"") : new ClientDraftResponse(404, "PRIVATE-ERROR-BODY"),
            wait: (delay, token) => { paused.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); })) {
            service.Connect(); Check(paused.Wait(5000), "404 worker stalled"); Empty(service.ReadSnapshot()); Check(service.ReadSnapshot().state == "ready", "No-session 404 treated as current draft");
        }
    }
    static void LateResponsesAndBoundedDispose() {
        using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) using (var returned = new ManualResetEventSlim()) using (var newReady = new ManualResetEventSlim()) {
            int calls = 0;
            var service = new ClientDraftService(discovery: token => new ClientDraftDiscovery(new[] { Connection }),
                transport: (connection, path, token) => {
                    if (Interlocked.Increment(ref calls) == 1) { entered.Set(); release.Wait(); Check(token.IsCancellationRequested, "Old token lost cancellation"); returned.Set(); return new ClientDraftResponse(200, "\"ChampSelect\""); }
                    return new ClientDraftResponse(200, "\"Lobby\"");
                }, wait: (delay, token) => { newReady.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); });
            try {
                service.Connect(); Check(entered.Wait(5000), "Old read did not start"); service.Disconnect(); service.Connect(); Check(newReady.Wait(5000), "Reconnect waited for old read");
                Check(service.ReadSnapshot().state == "ready", "New connection not ready"); long revision = service.ReadSnapshot().revision;
                release.Set(); Check(returned.Wait(5000), "Old read did not finish"); Thread.Sleep(50);
                Check(service.ReadSnapshot().state == "ready" && service.ReadSnapshot().revision == revision && calls == 2, "Old response overwrote new connection or fetched session");
            } finally { release.Set(); service.Dispose(); }
        }
        using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) using (var done = new ManualResetEventSlim()) {
            var service = new ClientDraftService(discovery: token => { entered.Set(); release.Wait(); Check(token.IsCancellationRequested, "Token invalid after bounded disposal"); done.Set(); return new ClientDraftDiscovery(new[] { Connection }); }, transport: (connection, path, token) => { throw new Exception("Late discovery attempted transport"); });
            service.Connect(); Check(entered.Wait(5000), "Discovery did not start"); var watch = Stopwatch.StartNew(); service.Dispose();
            Check(watch.ElapsedMilliseconds < 2500, "Dispose exceeded bound"); Empty(service.ReadSnapshot());
            release.Set(); Check(done.Wait(5000), "Late discovery did not return"); Thread.Sleep(50); service.Connect();
            Check(!service.ReadSnapshot().enabled && service.ReadSnapshot().state == "disabled", "Disposed service restarted");
        }
    }
    static void RealTransportTimeoutAndCancellation() {
        // Deliberately never complete the TLS handshake. No game client is contacted.
        foreach (bool cancel in new[] { false, true }) {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using (var token = new CancellationTokenSource()) {
                TcpClient accepted = null; var watch = Stopwatch.StartNew();
                var request = Task.Run(() => {
                    try { ClientDraftProtocol.Read(new ClientDraftConnection(1, ((IPEndPoint)listener.LocalEndpoint).Port, "fixture-only"), ClientDraftProtocol.PhasePath, token.Token); throw new Exception("Unresponsive TLS server succeeded"); }
                    catch (OperationCanceledException) { Check(cancel, "Unrequested cancellation"); }
                    catch (ClientDraftException error) { Check(!cancel && error.Code == "timeout", "Unexpected timeout classification"); }
                });
                try {
                    var accept = listener.AcceptTcpClientAsync(); Check(accept.Wait(3000), "Loopback transport did not connect"); accepted = accept.Result;
                    if (cancel) token.Cancel();
                    Check(request.Wait(6500), "Transport failed to terminate");
                    Check(watch.ElapsedMilliseconds < (cancel ? 2500 : 6500), "Transport timeout or cancellation exceeded bound");
                    // Returning promptly is insufficient if the abandoned TLS
                    // connection still owns a socket. Drain the ClientHello and
                    // require the peer to observe EOF without server-side close.
                    accepted.ReceiveTimeout = 2000;
                    byte[] pendingBytes = new byte[4096]; int total = 0, received;
                    while ((received = accepted.GetStream().Read(pendingBytes, 0, pendingBytes.Length)) > 0) {
                        total += received; Check(total < 65536, "Unexpected loopback handshake traffic");
                    }
                } finally { token.Cancel(); if (accepted != null) accepted.Close(); listener.Stop(); try { request.Wait(2000); } catch { } }
            }
        }
    }
    static int Main(string[] args) {
        if (args.Contains("--limited-query-fixture")) {
            int id = Process.GetCurrentProcess().Id;
            var connection = ClientDraftProtocol.ParseCommandLine(ClientDraftProtocol.ReadLimitedCommandLine(id), id);
            return connection != null && connection.ProcessId == 1234 && connection.Port == 54321 && connection.Token == "fixture-token" ? 0 : 1;
        }
        Run("BP projection excludes identities and hidden opponent intents", Projection);
        Run("BP swaps, simultaneous rounds and partial snapshots keep stable slots", TradesAndPartialSnapshots);
        Run("BP malformed, duplicate, oversized and unsupported sessions fail closed", Malformed);
        Run("BP credential parsing, fixed endpoints and request-scoped TLS boundaries", ProtocolBoundaries);
        Run("BP disabled by default, snapshots isolated and phase lifecycle clears data", ServiceLifecycle);
        Run("BP discovery, multiple clients, 404, timeout and private errors", DiscoveryAndErrors);
        Run("BP old responses cannot overwrite reconnect and disposal is bounded", LateResponsesAndBoundedDispose);
        Run("BP real loopback TLS stall cancels and obeys total deadline", RealTransportTimeoutAndCancellation);
        return failures == 0 ? 0 : 1;
    }
}
