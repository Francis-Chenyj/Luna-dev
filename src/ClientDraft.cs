using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Luna {
    // Only this deliberately small projection may leave the connection service.
    internal sealed class ClientDraftSlot {
        public int slot { get; internal set; }
        public int championId { get; internal set; }
        public string selection { get; internal set; }
        public int pickOrder { get; internal set; }
        public string lane { get; internal set; }
        internal ClientDraftSlot Copy() { return new ClientDraftSlot { slot = slot, championId = championId, selection = selection, pickOrder = pickOrder, lane = lane }; }
    }
    internal sealed class ClientDraftTeams {
        public ClientDraftSlot[] ally { get; internal set; }
        public ClientDraftSlot[] enemy { get; internal set; }
        internal ClientDraftTeams Copy() { return new ClientDraftTeams { ally = ally.Select(x => x.Copy()).ToArray(), enemy = enemy.Select(x => x.Copy()).ToArray() }; }
        internal static ClientDraftTeams Empty() { return new ClientDraftTeams { ally = EmptySide(), enemy = EmptySide() }; }
        static ClientDraftSlot[] EmptySide() { return Enumerable.Range(0, 5).Select(i => new ClientDraftSlot { slot = i, championId = 0, selection = "empty", pickOrder = 0, lane = "" }).ToArray(); }
    }
    internal sealed class ClientDraftSnapshot {
        public bool enabled { get; internal set; }
        public string state { get; internal set; }
        public long revision { get; internal set; }
        public ClientDraftTeams teams { get; internal set; }
        public string error { get; internal set; }
        internal ClientDraftSnapshot Copy() { return new ClientDraftSnapshot { enabled = enabled, state = state, revision = revision, teams = teams.Copy(), error = error }; }
    }
    internal sealed class ClientDraftConnection {
        internal readonly int ProcessId, Port;
        internal readonly string Token;
        internal ClientDraftConnection(int processId, int port, string token) {
            if (processId <= 0 || port < 1 || port > 65535 || string.IsNullOrEmpty(token) || token.Length > 512 || token.Any(c => c < 33 || c > 126)) throw new ArgumentException("Invalid local client connection.");
            ProcessId = processId; Port = port; Token = token;
        }
    }
    internal sealed class ClientDraftDiscovery {
        internal readonly ClientDraftConnection[] Connections;
        internal readonly string Error;
        internal ClientDraftDiscovery(ClientDraftConnection[] connections, string error = "") { Connections = connections ?? new ClientDraftConnection[0]; Error = error; }
    }
    internal sealed class ClientDraftResponse {
        internal readonly int StatusCode;
        internal readonly string Body;
        internal ClientDraftResponse(int statusCode, string body) { StatusCode = statusCode; Body = body; }
    }
    internal sealed class ClientDraftException : Exception {
        internal readonly string Code;
        internal ClientDraftException(string code) : base("Local client synchronization unavailable.") { Code = code; }
    }

    // The projector retains only cell-to-slot positions. It never retains a raw
    // session, player identity, chat token, match identifier or authentication data.
    internal sealed class ClientDraftProjector {
        readonly Dictionary<int, int> allySlots = new Dictionary<int, int>(), enemySlots = new Dictionary<int, int>();
        sealed class Member { internal int Cell, Champion, Intent; internal string Lane; }
        sealed class Pick { internal int Champion, Order; internal bool Completed; }
        internal void Reset() { allySlots.Clear(); enemySlots.Clear(); }
        internal ClientDraftTeams Project(string json) {
            try {
                var root = Object(ClientDraftProtocol.Json(json));
                if (root == null || !root.ContainsKey("myTeam") || !root.ContainsKey("theirTeam")) throw new ClientDraftException("invalid-response");
                if (Boolean(root, "isSpectating")) throw new ClientDraftException("unsupported-mode");
                var allies = Members(root["myTeam"]); var enemies = Members(root["theirTeam"]);
                var overlap = new HashSet<int>(allies.Select(x => x.Cell));
                if (enemies.Any(x => overlap.Contains(x.Cell))) throw new ClientDraftException("invalid-response");
                var allyPicks = new Dictionary<int, Pick>(); var enemyPicks = new Dictionary<int, Pick>();
                object actionValue; if (root.TryGetValue("actions", out actionValue)) {
                    var groups = Array(actionValue); if (groups == null || groups.Count > 40) throw new ClientDraftException("invalid-response");
                    int order = 0, total = 0;
                    foreach (object groupValue in groups) {
                        var group = Array(groupValue); if (group == null || group.Count > 20 || (total += group.Count) > 200) throw new ClientDraftException("invalid-response");
                        var picks = group.Cast<object>().Select(Object).Where(a => a != null && Text(a, "type") == "pick").ToArray();
                        if (picks.Length == 0) continue;
                        order++;
                        foreach (var action in picks) {
                            int cell = Integer(action, "actorCellId", -1); if (cell < 0 || cell > 99) continue;
                            // Missing alliance information must not accidentally become an enemy action.
                            object isAlly; if (!action.TryGetValue("isAllyAction", out isAlly) || !(isAlly is bool)) continue;
                            var target = (bool)isAlly ? allyPicks : enemyPicks;
                            if (target.ContainsKey(cell)) throw new ClientDraftException("invalid-response");
                            target[cell] = new Pick { Champion = Champion(action, "championId"), Order = order, Completed = Boolean(action, "completed") };
                        }
                    }
                }
                var result = ClientDraftTeams.Empty();
                Fill(result.ally, allies, allyPicks, allySlots, true);
                Fill(result.enemy, enemies, enemyPicks, enemySlots, false);
                return result;
            } catch (ClientDraftException) { throw; }
            catch { throw new ClientDraftException("invalid-response"); }
        }
        static void Fill(ClientDraftSlot[] output, List<Member> members, Dictionary<int, Pick> picks, Dictionary<int, int> positions, bool isAlly) {
            var currentCells = new HashSet<int>(members.Select(m => m.Cell));
            foreach (var member in members.OrderBy(m => m.Cell)) {
                if (!positions.ContainsKey(member.Cell)) {
                    if (positions.Count == 5) foreach (int old in positions.Keys.Where(c => !currentCells.Contains(c)).ToArray()) positions.Remove(old);
                    positions[member.Cell] = Enumerable.Range(0, 5).First(i => !positions.ContainsValue(i));
                }
                var slot = output[positions[member.Cell]]; Pick pick; picks.TryGetValue(member.Cell, out pick);
                bool completed = pick != null && pick.Completed;
                // An enemy intent or incomplete action is never enough to reveal a hero.
                if (!isAlly && !completed) continue;
                int champion = member.Champion > 0 ? member.Champion : completed && pick.Champion > 0 ? pick.Champion : isAlly ? member.Intent : 0;
                if (champion <= 0) continue;
                slot.championId = champion; slot.selection = completed ? "locked" : "hovered";
                slot.pickOrder = pick != null ? pick.Order : 0; slot.lane = member.Lane;
            }
        }
        static List<Member> Members(object value) {
            var array = Array(value); if (array == null || array.Count > 5) throw new ClientDraftException("unsupported-mode");
            var result = new List<Member>(); var seen = new HashSet<int>();
            foreach (object item in array) {
                var member = Object(item); if (member == null) throw new ClientDraftException("invalid-response");
                int cell = Integer(member, "cellId", -1);
                if (cell < 0 || cell > 99 || !seen.Add(cell)) throw new ClientDraftException("invalid-response");
                result.Add(new Member { Cell = cell, Champion = Champion(member, "championId"), Intent = Champion(member, "championPickIntent"), Lane = Lane(Text(member, "assignedPosition")) });
            }
            return result;
        }
        static string Lane(string value) {
            switch (value.ToLowerInvariant()) { case "top": return "top"; case "jungle": return "jungle"; case "middle": return "middle"; case "bottom": return "bottom"; case "support": case "utility": return "support"; default: return ""; }
        }
        static int Champion(Dictionary<string, object> value, string key) { int n = Integer(value, key, 0); return n > 0 && n <= 100000 ? n : 0; }
        static Dictionary<string, object> Object(object value) { return value as Dictionary<string, object>; }
        static IList Array(object value) { return value as IList; }
        static string Text(Dictionary<string, object> value, string key) { object item; return value.TryGetValue(key, out item) && item is string ? (string)item : ""; }
        static bool Boolean(Dictionary<string, object> value, string key) { object item; return value.TryGetValue(key, out item) && item is bool && (bool)item; }
        static int Integer(Dictionary<string, object> value, string key, int fallback) { object item; return value.TryGetValue(key, out item) && item is int ? (int)item : fallback; }
    }

    internal static class ClientDraftProtocol {
        internal const string PhasePath = "/lol-gameflow/v1/gameflow-phase", SessionPath = "/lol-champ-select/v1/session";
        internal const int MaxResponseBytes = 256 * 1024, RequestTimeoutMilliseconds = 3000;
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder image, ref uint length);
        [DllImport("ntdll.dll")]
        static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int size, out int returned);
        [StructLayout(LayoutKind.Sequential)]
        struct UnicodeText { internal ushort Length, MaximumLength; internal IntPtr Buffer; }
        internal static object Json(string text) {
            if (text == null || text.Length > MaxResponseBytes || Encoding.UTF8.GetByteCount(text) > MaxResponseBytes) throw new ClientDraftException("invalid-response");
            try { return new JavaScriptSerializer { MaxJsonLength = MaxResponseBytes, RecursionLimit = 32 }.DeserializeObject(text); }
            catch { throw new ClientDraftException("invalid-response"); }
        }
        internal static HttpWebRequest CreateRequest(ClientDraftConnection connection, string path) {
            if (connection == null || (path != PhasePath && path != SessionPath)) throw new ArgumentException("Unsupported local client request.");
            var request = (HttpWebRequest)WebRequest.Create("https://127.0.0.1:" + connection.Port + path);
            request.Method = "GET"; request.Proxy = null; request.AllowAutoRedirect = false; request.KeepAlive = false;
            request.Timeout = RequestTimeoutMilliseconds; request.ReadWriteTimeout = RequestTimeoutMilliseconds;
            request.Accept = "application/json"; request.UserAgent = "Luna-Local-Draft";
            request.Headers[HttpRequestHeader.Authorization] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + connection.Token));
            // LCU serves its own certificate. This exception belongs to this one
            // fixed loopback request; external data/update requests stay unchanged.
            request.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => certificate != null && request.RequestUri.Scheme == "https" && request.RequestUri.Host == "127.0.0.1" && request.RequestUri.Port == connection.Port;
            return request;
        }
        internal static ClientDraftResponse Read(ClientDraftConnection connection, string path, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            var request = CreateRequest(connection, path);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                deadline.CancelAfter(RequestTimeoutMilliseconds);
                try {
                    // Synchronous GetResponse can remain inside a TLS handshake
                    // beyond HttpWebRequest.Timeout on .NET Framework. Bound the
                    // asynchronous handshake explicitly before reading the body.
                    var pending = Task<WebResponse>.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, null);
                    try { pending.Wait(deadline.Token); }
                    catch (AggregateException) { /* GetAwaiter below rethrows the original transport error. */ }
                    catch (OperationCanceledException) {
                        request.Abort();
                        // An obsolete successful response must still release its
                        // socket even if the caller has already timed out.
                        pending.ContinueWith(done => {
                            if (done.Status == TaskStatus.RanToCompletion) done.Result.Dispose();
                            else { var observed = done.Exception; }
                        }, TaskScheduler.Default);
                        token.ThrowIfCancellationRequested(); throw new ClientDraftException("timeout");
                    }
                    using (deadline.Token.Register(request.Abort))
                    using (var response = (HttpWebResponse)pending.GetAwaiter().GetResult()) {
                        if (response.ContentLength > MaxResponseBytes) throw new ClientDraftException("response-too-large");
                        using (var stream = response.GetResponseStream()) using (var memory = new MemoryStream()) {
                            byte[] block = new byte[4096]; int n;
                            while ((n = stream.Read(block, 0, block.Length)) != 0) {
                                deadline.Token.ThrowIfCancellationRequested();
                                if (memory.Length + n > MaxResponseBytes) throw new ClientDraftException("response-too-large");
                                memory.Write(block, 0, n);
                            }
                            deadline.Token.ThrowIfCancellationRequested();
                            return new ClientDraftResponse((int)response.StatusCode, new UTF8Encoding(false, true).GetString(memory.ToArray()));
                        }
                    }
                } catch (WebException error) {
                    token.ThrowIfCancellationRequested();
                    if (deadline.IsCancellationRequested || error.Status == WebExceptionStatus.Timeout) throw new ClientDraftException("timeout");
                    using (var response = error.Response as HttpWebResponse) {
                        if (response != null) return new ClientDraftResponse((int)response.StatusCode, "");
                    }
                    throw new ClientDraftException("connection-unavailable");
                } catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); throw new ClientDraftException("timeout"); }
                catch (ClientDraftException) { throw; }
                catch { token.ThrowIfCancellationRequested(); throw new ClientDraftException("invalid-response"); }
            }
        }
        internal static ClientDraftConnection ParseCommandLine(string commandLine, int uxProcessId) {
            if (uxProcessId <= 0 || string.IsNullOrEmpty(commandLine) || commandLine.Length > 32768) return null;
            int port, pid;
            string portText = Argument(commandLine, "app-port"), token = Argument(commandLine, "remoting-auth-token"), pidText = Argument(commandLine, "app-pid");
            if (!int.TryParse(portText, out port) || !int.TryParse(pidText, out pid)) return null;
            try { return new ClientDraftConnection(pid, port, token); } catch (ArgumentException) { return null; }
        }
        static string Argument(string text, string name) {
            var matches = Regex.Matches(text, "(?:^|\\s)\"?--" + Regex.Escape(name) + "=(?:\"([^\"]*)\"|([^\\s\"]+))\"?(?=\\s|$)");
            if (matches.Count != 1) return "";
            return matches[0].Groups[1].Success ? matches[0].Groups[1].Value : matches[0].Groups[2].Value;
        }
        internal static ClientDraftConnection ParseLockfile(string text) {
            if (text == null || text.Length > 2048) return null;
            string[] fields = text.Trim().Split(':'); int pid, port;
            if (fields.Length != 5 || fields[0] != "LeagueClient" || fields[4] != "https" || !int.TryParse(fields[1], out pid) || !int.TryParse(fields[2], out port)) return null;
            try { return new ClientDraftConnection(pid, port, fields[3]); } catch (ArgumentException) { return null; }
        }
        internal static ClientDraftDiscovery Discover(CancellationToken token) {
            return DiscoverWithFallback(token, DiscoverNative, DiscoverWmi);
        }
        internal static ClientDraftDiscovery DiscoverWithFallback(CancellationToken token,
            Func<CancellationToken, ClientDraftDiscovery> native, Func<CancellationToken, ClientDraftDiscovery> fallback) {
            token.ThrowIfCancellationRequested();
            ClientDraftDiscovery result;
            try { result = native(token); }
            catch (OperationCanceledException) { throw; }
            catch { result = null; }
            token.ThrowIfCancellationRequested();
            // No client, a unique readable client and multiple clients are all
            // conclusive native results. A broken WMI service must not block them.
            if (result != null && (string.IsNullOrEmpty(result.Error) || result.Error == "multiple-clients")) return result;
            try { return fallback(token) ?? new ClientDraftDiscovery(null, "discovery-unavailable"); }
            catch (OperationCanceledException) { throw; }
            catch { return new ClientDraftDiscovery(null, "discovery-unavailable"); }
        }
        static ClientDraftDiscovery DiscoverNative(CancellationToken token) {
            Process[] processes = null;
            try {
                token.ThrowIfCancellationRequested(); processes = Process.GetProcessesByName("LeagueClientUx");
                token.ThrowIfCancellationRequested();
                if (processes.Length > 1) return new ClientDraftDiscovery(null, "multiple-clients");
                if (processes.Length == 0) return new ClientDraftDiscovery(null);
                int processId = processes[0].Id;
                var connection = ParseCommandLine(ReadLimitedCommandLine(processId), processId);
                token.ThrowIfCancellationRequested();
                if (connection == null) connection = ReadLockfile(ReadLimitedProcessImage(processId), token);
                return connection == null ? new ClientDraftDiscovery(null, "discovery-unavailable") : new ClientDraftDiscovery(new[] { connection });
            } catch (OperationCanceledException) { throw; }
            catch { return new ClientDraftDiscovery(null, "discovery-unavailable"); }
            finally { if (processes != null) foreach (var process in processes) process.Dispose(); }
        }
        static ClientDraftDiscovery DiscoverWmi(CancellationToken token) {
            var found = new List<ClientDraftConnection>(); int processCount = 0;
            try {
                token.ThrowIfCancellationRequested();
                var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false };
                using (var search = new ManagementObjectSearcher("root\\CIMV2", "SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE Name = 'LeagueClientUx.exe'", options))
                using (var processes = search.Get()) {
                    foreach (ManagementObject process in processes) using (process) {
                        token.ThrowIfCancellationRequested(); if (++processCount > 1) return new ClientDraftDiscovery(new ClientDraftConnection[0], "multiple-clients");
                        int processId = Convert.ToInt32(process["ProcessId"]);
                        var connection = ParseCommandLine(process["CommandLine"] as string, processId);
                        if (connection == null) {
                            token.ThrowIfCancellationRequested();
                            string executable = process["ExecutablePath"] as string;
                            if (string.IsNullOrEmpty(executable)) executable = ReadLimitedProcessImage(processId);
                            connection = ReadLockfile(executable, token);
                        }
                        if (connection != null) found.Add(connection);
                    }
                }
                return new ClientDraftDiscovery(found.ToArray(), processCount > 0 && found.Count == 0 ? "discovery-unavailable" : "");
            } catch (OperationCanceledException) { throw; }
            catch { return new ClientDraftDiscovery(new ClientDraftConnection[0], "discovery-unavailable"); }
        }
        internal static string ReadLimitedCommandLine(int processId) {
            IntPtr process = IntPtr.Zero, buffer = IntPtr.Zero;
            try {
                if (processId <= 0) return null;
                process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION only.
                if (process == IntPtr.Zero) return null;
                int size;
                NtQueryInformationProcess(process, 60, IntPtr.Zero, 0, out size); // ProcessCommandLineInformation.
                int header = Marshal.SizeOf(typeof(UnicodeText));
                if (size < header || size > 131072) return null;
                int capacity = size; buffer = Marshal.AllocHGlobal(capacity);
                if (NtQueryInformationProcess(process, 60, buffer, capacity, out size) != 0 || size > capacity) return null;
                var text = (UnicodeText)Marshal.PtrToStructure(buffer, typeof(UnicodeText));
                long first = buffer.ToInt64(), data = text.Buffer.ToInt64();
                // The native call returns a string inside our local buffer. Do
                // not follow a pointer outside it or an invalid UTF-16 length.
                if (text.Length == 0 || (text.Length & 1) != 0 || text.Length > text.MaximumLength || data < first + header || data > first + capacity - text.Length) return null;
                return Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
            } catch { return null; }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); if (process != IntPtr.Zero) CloseHandle(process); }
        }
        static string ReadLimitedProcessImage(int processId) {
            IntPtr process = IntPtr.Zero;
            try {
                process = OpenProcess(0x1000, false, processId);
                if (process == IntPtr.Zero) return null;
                var image = new StringBuilder(32768); uint size = (uint)image.Capacity;
                return QueryFullProcessImageName(process, 0, image, ref size) ? image.ToString() : null;
            } catch { return null; }
            finally { if (process != IntPtr.Zero) CloseHandle(process); }
        }
        static ClientDraftConnection ReadLockfile(string executable, CancellationToken token) {
            try {
                if (string.IsNullOrEmpty(executable) || !string.Equals(Path.GetFileName(executable), "LeagueClientUx.exe", StringComparison.OrdinalIgnoreCase)) return null;
                string directory = Path.GetDirectoryName(executable);
                if (!File.Exists(Path.Combine(directory, "LeagueClient.exe"))) return null;
                token.ThrowIfCancellationRequested();
                string text;
                using (var file = new FileStream(Path.Combine(directory, "lockfile"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    if (file.Length > 2048) return null;
                    using (var reader = new StreamReader(file, Encoding.UTF8)) text = reader.ReadToEnd();
                }
                var connection = ParseLockfile(text); if (connection == null) return null;
                using (var process = Process.GetProcessById(connection.ProcessId)) {
                    if (process.HasExited || !string.Equals(process.ProcessName, "LeagueClient", StringComparison.OrdinalIgnoreCase)) return null;
                }
                return connection;
            } catch { token.ThrowIfCancellationRequested(); return null; }
        }
    }

    internal sealed class ClientDraftService : IDisposable {
        readonly object sync = new object();
        readonly Func<CancellationToken, ClientDraftDiscovery> discover;
        readonly Func<ClientDraftConnection, string, CancellationToken, ClientDraftResponse> read;
        readonly Action<int, CancellationToken> wait;
        readonly bool available;
        readonly HashSet<Run> runs = new HashSet<Run>();
        ClientDraftSnapshot snapshot = new ClientDraftSnapshot { enabled = false, state = "disabled", revision = 0, error = "", teams = ClientDraftTeams.Empty() };
        Run current; long generation; bool disposed;
        sealed class Run {
            readonly object sync = new object(); readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            internal readonly CancellationToken Token; internal readonly long Generation; internal Thread Thread; bool released;
            internal Run(long generation) { Generation = generation; Token = cancellation.Token; }
            internal void Cancel() { lock (sync) if (!released) { try { cancellation.Cancel(); } catch (AggregateException) { } } }
            internal void Release() { lock (sync) { released = true; cancellation.Dispose(); } }
        }
        internal ClientDraftService(bool available = true, Func<CancellationToken, ClientDraftDiscovery> discovery = null,
            Func<ClientDraftConnection, string, CancellationToken, ClientDraftResponse> transport = null, Action<int, CancellationToken> wait = null) {
            this.available = available; discover = discovery ?? ClientDraftProtocol.Discover; read = transport ?? ClientDraftProtocol.Read;
            this.wait = wait ?? ((milliseconds, token) => { if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested(); });
        }
        internal ClientDraftSnapshot ReadSnapshot() { lock (sync) return snapshot.Copy(); }
        internal ClientDraftSnapshot Connect() {
            lock (sync) {
                if (disposed || snapshot.enabled) return snapshot.Copy();
                if (!available) { Set(false, "unavailable", "feature-unavailable", null); return snapshot.Copy(); }
                var run = new Run(++generation); current = run; runs.Add(run);
                Set(true, "searching", "", null);
                run.Thread = new Thread(() => Work(run)) { IsBackground = true, Name = "Luna local draft", Priority = ThreadPriority.BelowNormal };
                try { run.Thread.Start(); }
                catch { current = null; runs.Remove(run); run.Release(); Set(false, "unavailable", "worker-unavailable", null); }
                return snapshot.Copy();
            }
        }
        internal ClientDraftSnapshot Disconnect() {
            Run stop; ClientDraftSnapshot result;
            lock (sync) { generation++; stop = current; current = null; Set(false, "disabled", "", null); result = snapshot.Copy(); }
            if (stop != null) stop.Cancel(); return result;
        }
        void Work(Run run) {
            var projector = new ClientDraftProjector(); ClientDraftConnection connection = null;
            var elapsed = Stopwatch.StartNew(); long nextDiscovery = 0;
            try {
                while (!run.Token.IsCancellationRequested) {
                    int interval = 3000;
                    try {
                        if (connection == null || elapsed.ElapsedMilliseconds >= nextDiscovery) {
                            var discovery = discover(run.Token); run.Token.ThrowIfCancellationRequested();
                            nextDiscovery = elapsed.ElapsedMilliseconds + 10000;
                            if (discovery == null) throw new ClientDraftException("discovery-unavailable");
                            if (discovery.Error == "multiple-clients" || discovery.Connections.Length > 1) { connection = null; projector.Reset(); Publish(run, "multiple", "multiple-clients", null); Pause(run, interval); continue; }
                            if (!string.IsNullOrEmpty(discovery.Error)) throw new ClientDraftException("discovery-unavailable");
                            if (discovery.Connections.Length == 0) { connection = null; projector.Reset(); Publish(run, "searching", "", null); Pause(run, interval); continue; }
                            var next = discovery.Connections[0]; if (next == null) throw new ClientDraftException("discovery-unavailable");
                            if (connection != null && (next.ProcessId != connection.ProcessId || next.Port != connection.Port || next.Token != connection.Token)) projector.Reset();
                            connection = next;
                        }
                        var phaseReply = read(connection, ClientDraftProtocol.PhasePath, run.Token); run.Token.ThrowIfCancellationRequested();
                        CheckStatus(phaseReply);
                        string phase = ClientDraftProtocol.Json(phaseReply.Body) as string;
                        if (phase == "ChampSelect") {
                            var session = read(connection, ClientDraftProtocol.SessionPath, run.Token); run.Token.ThrowIfCancellationRequested();
                            if (session != null && session.StatusCode == 404) { projector.Reset(); Publish(run, "ready", "", null); }
                            else {
                                CheckStatus(session); var teams = projector.Project(session.Body); run.Token.ThrowIfCancellationRequested();
                                // The two endpoints are not an atomic snapshot.
                                // Recheck the phase after a potentially slow session
                                // read so a completed/dodged BP is not republished.
                                var confirmed = read(connection, ClientDraftProtocol.PhasePath, run.Token); run.Token.ThrowIfCancellationRequested();
                                CheckStatus(confirmed);
                                string currentPhase = ClientDraftProtocol.Json(confirmed.Body) as string;
                                if (currentPhase == "ChampSelect") Publish(run, "champ-select", "", teams);
                                else { projector.Reset(); PublishNonDraftPhase(run, currentPhase); }
                            }
                            interval = 1000;
                        } else {
                            projector.Reset();
                            PublishNonDraftPhase(run, phase);
                            interval = 2000;
                        }
                    } catch (OperationCanceledException) { if (run.Token.IsCancellationRequested) break; connection = null; projector.Reset(); Publish(run, "unavailable", "timeout", null); }
                    catch (ClientDraftException error) { connection = null; projector.Reset(); Publish(run, "unavailable", SafeError(error.Code), null); }
                    catch { connection = null; projector.Reset(); Publish(run, "unavailable", "connection-unavailable", null); }
                    Pause(run, interval);
                }
            } catch (OperationCanceledException) { }
            catch { Publish(run, "unavailable", "connection-unavailable", null); }
            finally { connection = null; projector.Reset(); lock (sync) { runs.Remove(run); if (current == run) current = null; } run.Release(); }
        }
        void PublishNonDraftPhase(Run run, string phase) {
            switch (phase) {
                case "InProgress": case "GameStart": case "Reconnect": case "WatchInProgress": Publish(run, "in-game", "", null); break;
                case "None": case "Lobby": case "Matchmaking": case "ReadyCheck": case "CheckedIntoTournament": case "WaitingForStats": case "PreEndOfGame": case "EndOfGame": case "TerminatedInError": Publish(run, "ready", "", null); break;
                default: throw new ClientDraftException("unsupported-phase");
            }
        }
        void Pause(Run run, int milliseconds) { run.Token.ThrowIfCancellationRequested(); wait(milliseconds, run.Token); run.Token.ThrowIfCancellationRequested(); }
        static void CheckStatus(ClientDraftResponse reply) {
            if (reply == null) throw new ClientDraftException("invalid-response");
            if (reply.StatusCode == 401 || reply.StatusCode == 403) throw new ClientDraftException("authentication-unavailable");
            if (reply.StatusCode != 200) throw new ClientDraftException("client-unavailable");
        }
        static string SafeError(string code) {
            switch (code) { case "timeout": case "response-too-large": case "invalid-response": case "unsupported-mode": case "unsupported-phase": case "discovery-unavailable": case "authentication-unavailable": case "client-unavailable": return code; default: return "connection-unavailable"; }
        }
        void Publish(Run run, string state, string error, ClientDraftTeams teams) {
            lock (sync) if (!disposed && snapshot.enabled && generation == run.Generation && !run.Token.IsCancellationRequested) Set(true, state, error, teams);
        }
        void Set(bool enabled, string state, string error, ClientDraftTeams teams) {
            teams = teams ?? ClientDraftTeams.Empty();
            if (snapshot.enabled == enabled && snapshot.state == state && snapshot.error == error && Equal(snapshot.teams.ally, teams.ally) && Equal(snapshot.teams.enemy, teams.enemy)) return;
            snapshot = new ClientDraftSnapshot { enabled = enabled, state = state, error = error, teams = teams.Copy(), revision = snapshot.revision + 1 };
        }
        static bool Equal(ClientDraftSlot[] left, ClientDraftSlot[] right) { return left.Zip(right, (a, b) => a.slot == b.slot && a.championId == b.championId && a.selection == b.selection && a.pickOrder == b.pickOrder && a.lane == b.lane).All(x => x); }
        public void Dispose() {
            Run[] pending;
            lock (sync) { if (disposed) return; disposed = true; generation++; current = null; Set(false, "disabled", "", null); pending = runs.ToArray(); }
            foreach (var run in pending) run.Cancel();
            var watch = Stopwatch.StartNew();
            foreach (var run in pending) if (run.Thread != Thread.CurrentThread) run.Thread.Join((int)Math.Max(0, 1500 - watch.ElapsedMilliseconds));
        }
    }
}
