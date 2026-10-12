using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

internal static class StabilityTests {
    static readonly Type App = typeof(Luna.Program);
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string[] Lanes = {"top","jungle","middle","bottom","support"};
    const string Key = "detail-266-top-father-1-5";
    static readonly string A = new string('a',64), B = new string('b',64), C = new string('c',64), D = new string('d',64), E = new string('e',64);
    static object Call(string name, params object[] args) {
        try { return App.GetMethod(name, BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args); }
        catch(TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw; }
    }
    static void Set(string name, object value) { App.GetField(name,BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,value); }
    static object Field(string name) { return App.GetField(name,BindingFlags.NonPublic|BindingFlags.Static).GetValue(null); }
    static Dictionary<string,object> Map(object value) { return (Dictionary<string,object>)value; }
    static Dictionary<string,object> ConvertObject(object value) { lock(Json) return Map(Json.DeserializeObject(Json.Serialize(value))); }
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static void Pass(string text) { Console.WriteLine("PASS: "+text); }
    static string ReadValue() { return Map(Map(Call("LocalDetail",Key))["data"])["testValue"].ToString(); }
    static Dictionary<string,object> Cache() { return Map(ConvertObject(Call("LocalDetail",Key))["cache"]); }
    static Dictionary<string,object> Detail(string key,string value) {
        var parts=key.Split('-');
        return ConvertObject(new {success=true,champion=new {championId=int.Parse(parts[1])},lane=parts[2],mode=parts[3],sections=new object[0],testValue=value});
    }
    static Dictionary<string,object> Catalog() {
        return ConvertObject(new { data=new { heroes=new[] {new {id=266,name="Aatrox",title="Aatrox",en="Aatrox",keywords="",lanes=Lanes.ToDictionary(l=>l,l=>1)}}, lanes=Lanes.Select(l=>new {key=l,label=l}).ToArray(),version="test",updatedAt=DateTime.UtcNow.ToString("o") }, fetchedAt=DateTime.UtcNow.ToString("o")});
    }
    static void Baseline() {
        var dir=Path.Combine(Root,"data","library","1-5");Directory.CreateDirectory(dir);
        var catalog=Catalog(); var keys=(List<string>)Call("ExpectedKeys",catalog,"1-5");
        var entries=new Dictionary<string,object>();
        foreach(string key in keys) {
            string path=Path.Combine(dir,key+".json.gz");
            Call("WriteCompressed",path,new {key=key,fingerprint=A,fetchedAt="2026-09-01T00:00:00Z",data=Detail(key,"baseline")});
            entries[key]=Call("FileHash",File.ReadAllBytes(path));
        }
        Call("WriteRecord",Path.Combine(dir,"manifest.json"),new {schema=1,profile="1-5",fingerprint=A,sourceGeneratedAt="2026-09-01T00:00:00Z",builtAt="2026-09-01T00:00:00Z",expected=keys.Count,entries=entries,catalog=catalog});
        Directory.CreateDirectory(Path.Combine(Root,"cache"));Call("InitializeLibrary");
    }
    static Dictionary<string,object> Job() { return Map(ConvertObject(Call("LibraryInfo","1-5"))["job"]); }
    static void WaitJob() {
        var clock=Stopwatch.StartNew();while((bool)Job()["busy"]&&clock.ElapsedMilliseconds<40000)Thread.Sleep(20);
        Check(!(bool)Job()["busy"],"Update timed out");
    }
    static int Main() {
        try {
            ServicePointManager.DefaultConnectionLimit=8;ThreadPool.SetMinThreads(12,12);Baseline();
            int network=0;
            Set("LibraryTransport",new Func<string,CancellationToken,Dictionary<string,object>>((url,token)=>{Interlocked.Increment(ref network);throw new IOException("Network must not run for a query");}));
            var clock=Stopwatch.StartNew();for(int i=0;i<100;i++)Check(ReadValue()=="baseline","Offline data mismatch");
            Check(network==0&&clock.ElapsedMilliseconds<1500,"Local query used network or was slow");
            Check(Map(Call("LocalDetail","detail-266-top-father-5-1"))["data"]==null,"Different profile reused default data");
            Check(Cache()["source"].ToString()=="bundled" && Cache()["localUpdatedAt"].ToString()=="" && !(bool)Cache()["isFallback"],"Publisher download time was exposed as a local update");
            Check(ConvertObject(Call("LibraryInfo","1-5"))["sourceCheckedAt"].ToString()=="","A local query fabricated an upstream check time");
            Pass("100 local queries never contact upstream; missing profile stays distinct from empty data");

            string path=Path.Combine(Root,"data","library","1-5",Key+".json.gz");byte[] original=File.ReadAllBytes(path);
            File.WriteAllText(path,"damaged");Check(Map(Call("LocalDetail",Key))["data"]==null,"Corrupt file was accepted");
            Call("WriteRecord",Path.Combine(Root,"cache",Key+".json"),new {data=Detail(Key,"legacy"),fetchedAt="2026-08-01T00:00:00Z"});
            Check(ReadValue()=="legacy","Valid legacy record was not preserved");
            Check((bool)Cache()["isFallback"] && Cache()["sourceGeneratedAt"].ToString()=="" && Cache()["localUpdatedAt"].ToString()=="2026-08-01T00:00:00Z","Legacy provenance fabricated source time or lost retrieval time");
            string seedKey="detail-266-top-father-5-2";
            Directory.CreateDirectory(Path.Combine(Root,"data","seeds"));
            Call("WriteRecord",Path.Combine(Root,"data","seeds",seedKey+".json"),new {data=Detail(seedKey,"seed"),fetchedAt="2026-08-01T00:00:00Z"});
            var seedCache=Map(ConvertObject(Call("LocalDetail",seedKey))["cache"]);
            Check(seedCache["origin"].ToString()=="bundled" && seedCache["localUpdatedAt"].ToString()=="","Shipped legacy seed was presented as this computer's download");
            File.WriteAllBytes(path,original);Check(ReadValue()=="baseline","Complete library did not take priority over legacy");
            var mismatched=Detail(Key,"bad");mismatched["mode"]="son";Check(!(bool)Call("ValidDetail",mismatched,Key),"Wrong direction accepted");
            var son=Detail("detail-266-top-son-1-5","son");son["sections"]=new object[]{ConvertObject(new {key="high_segment_father",label="high",items=new object[0]})};
            Check((bool)Call("ValidDetail",son,"detail-266-top-son-1-5"),"Source son mode legitimately uses father group keys");
            Pass("checksums, schema, exact direction and legacy fallback are enforced");

            var source=new FixtureSource {Fingerprint=B,Fail=true};Set("LibraryTransport",new Func<string,CancellationToken,Dictionary<string,object>>(source.Fetch));
            Call("StartLibraryUpdate","1-5",false);WaitJob();Check(Job()["state"].ToString()=="error","Expected failed update");
            Check(ReadValue()=="baseline","Partial data replaced complete baseline");
            int saved=Directory.GetFiles(Path.Combine(Root,"cache","library",B,"1-5"),"*.json.gz").Length;
            Check(saved>0&&saved<10,"Failed update did not leave resumable work");source.Fail=false;source.Details=0;
            Call("StartLibraryUpdate","1-5",false);WaitJob();Check(Job()["state"].ToString()=="complete",Job()["message"].ToString());
            Check(ReadValue()==B&&source.Details==10-saved,"Update did not resume exactly missing records");
            string savedAt=Cache()["localUpdatedAt"].ToString();
            string checkedAt=ConvertObject(Call("LibraryInfo","1-5"))["sourceCheckedAt"].ToString();
            var manifestB=Map(Call("ReadRecord",Path.Combine(Root,"cache","library",B,"1-5","manifest.json")));
            Check(savedAt==manifestB["builtAt"].ToString() && savedAt!="" && Cache()["source"].ToString()=="library" && !(bool)Cache()["isFallback"],"Published library has wrong provenance");
            source.Details=0;Call("StartLibraryUpdate","1-5",false);WaitJob();Check(source.Details==0,"Unchanged dataset downloaded again");
            Check(Cache()["localUpdatedAt"].ToString()==savedAt && ConvertObject(Call("LibraryInfo","1-5"))["localUpdatedAt"].ToString()==savedAt,"A version check changed the actual download time");
            Check(ConvertObject(Call("LibraryInfo","1-5"))["sourceCheckedAt"].ToString()!=checkedAt,"Successful source check was not recorded separately");
            Pass("failed update keeps old library; retry resumes; unchanged dataset skips detail requests");

            source.Fingerprint=C;source.StatusCalls=0;source.ChangeAfterStart=true;
            Call("StartLibraryUpdate","1-5",false);WaitJob();Check(Job()["state"].ToString()=="error","Mixed upstream editions were accepted");
            Check(ReadValue()==B,"Source version change replaced current library");
            Pass("dataset fingerprint is checked before and after collection; mixed editions never publish");

            source.ChangeAfterStart=false;source.Fingerprint=E;source.Block=true;
            Call("StartLibraryUpdate","1-5",false);Check(source.Started.Wait(15000),"Blocked update never reached details");
            clock.Restart();Check(ReadValue()==B&&clock.ElapsedMilliseconds<500,"Query waited on active update");
            Call("CancelLibraryUpdate");WaitJob();Check(Job()["state"].ToString()=="cancelled"&&ReadValue()==B,"Cancellation damaged current data");
            source.Block=false;Call("StartLibraryUpdate","1-5",false);WaitJob();Check(ReadValue()==E,Job()["message"].ToString());
            string current=Path.Combine(Root,"cache","library",E,"1-5",Key+".json.gz");File.WriteAllText(current,"broken");
            Check(ReadValue()==B,"Previous generation was not available after corrupt current record");
            Check((bool)Cache()["isFallback"] && Cache()["source"].ToString()=="previous" && Cache()["localUpdatedAt"].ToString()==savedAt,"Fallback did not preserve previous library provenance");
            Check(!(bool)ConvertObject(Call("LibraryInfo","1-5"))["complete"],"Damaged library was described as complete");
            Pass("query remains fast during updates; cancellation preserves data; damaged new record uses previous edition");

            source.Fingerprint=D;Call("StartLibraryUpdate","1-5",false);WaitJob();
            Check(ReadValue()==D,"Repair update did not publish new complete data");
            var pointer=Map(Call("ReadRecord",Path.Combine(Root,"cache","library","active-1-5.json")));
            Check(pointer["previous"].ToString()==B,"Repair update rotated damaged current edition into backup");
            Check(!Directory.Exists(Path.Combine(Root,"cache","library",E,"1-5")),"Obsolete damaged edition was not removed after successful repair");
            Check(!Directory.Exists(Path.Combine(Root,"cache","library",C,"1-5")),"Invalidated partial edition was retained after confirmed source change");
            Pass("repair update keeps a usable previous edition and automatically prunes obsolete data");

            string manifestDPath=Path.Combine(Root,"cache","library",D,"1-5","manifest.json");
            string manifestBPath=Path.Combine(Root,"cache","library",B,"1-5","manifest.json");
            string pointerPath=Path.Combine(Root,"cache","library","active-1-5.json");
            string manifestDText=File.ReadAllText(manifestDPath),manifestBText=File.ReadAllText(manifestBPath),pointerText=File.ReadAllText(pointerPath);
            File.WriteAllText(manifestDPath,"broken");Call("InitializeLibrary");
            Check(ReadValue()==B && (bool)Cache()["isFallback"],"Unavailable active manifest concealed the fallback state");
            File.WriteAllText(manifestBPath,"broken");Call("InitializeLibrary");
            Check(ReadValue()=="baseline" && (bool)Cache()["isFallback"] && Cache()["source"].ToString()=="bundled" && Cache()["localUpdatedAt"].ToString()=="","Fallback to bundled library was mislabeled");
            Check((bool)ConvertObject(Call("LibraryInfo","1-5"))["isFallback"],"Library management concealed its fallback state");
            File.WriteAllText(pointerPath,"broken");Call("InitializeLibrary");
            Check((bool)Cache()["isFallback"],"Unreadable activation pointer concealed the fallback state");
            File.WriteAllText(pointerPath,"{}");Call("InitializeLibrary");
            Check((bool)Cache()["isFallback"],"Activation pointer with missing current edition concealed the fallback state");
            File.WriteAllText(manifestDPath,manifestDText);File.WriteAllText(manifestBPath,manifestBText);File.WriteAllText(pointerPath,pointerText);Call("InitializeLibrary");
            Check(ReadValue()==D && !(bool)Cache()["isFallback"],"Repair did not clear fallback status");
            Pass("source/download/check timestamps stay distinct; corrupt manifests and pointers expose truthful backup provenance");

            Set("Offline",true);bool denied=false;try{Call("StartLibraryUpdate","1-5",false);}catch(ArgumentException){denied=true;}
            Check(denied&&ReadValue()==D,"Offline startup permitted a network update");Set("Offline",false);
            Pass("offline mode rejects update while retaining local queries");

            using(var remote=new MockSource("{}",200))using(var cancel=new CancellationTokenSource()) {
                var task=Task.Run(()=>Call("DownloadJson",remote.Url,cancel.Token));Check(remote.Started.Wait(3000),"Transport request did not start");
                cancel.Cancel();try{task.Wait(3000);throw new Exception("Expected cancellation");}catch(AggregateException ex){Check(ex.GetBaseException() is OperationCanceledException,"Unexpected cancel failure");}
                Check(((SemaphoreSlim)Field("NetworkSlots")).CurrentCount==3,"Cancelled network request leaked a slot");
            }
            Pass("cancelling active HTTP transport releases its network slot");
            Console.WriteLine("All stability checks passed in an isolated temporary directory.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    sealed class FixtureSource {
        public string Fingerprint;public bool Fail,ChangeAfterStart,Block;public int Details,StatusCalls;
        public ManualResetEventSlim Started=new ManualResetEventSlim();
        public Dictionary<string,object> Fetch(string url,CancellationToken token) {
            if(url.EndsWith("/status")) {int n=Interlocked.Increment(ref StatusCalls);return ConvertObject(new {success=true,ready=true,package=new {sha256=ChangeAfterStart&&n>1?D:Fingerprint},meta=new {generatedAt="2026-10-11T00:00:00Z"},lanes=Lanes.Select(l=>new {key=l,championCount=1}).ToArray()});}
            if(url.Contains("hero_list.js"))return ConvertObject(new {hero=new[] {new {heroId="266",title="Aatrox",name="Aatrox",alias="Aatrox",keywords=""}},version="test"});
            var q=HttpUtility.ParseQueryString(new Uri(url).Query);
            if(url.Contains("/champions?"))return ConvertObject(new {success=true,champions=new[] {new {championId=266,championNameEn="Aatrox",championNameCn="Aatrox",games=1}}});
            if(url.Contains("/detail?")) {
                Interlocked.Increment(ref Details);
                if(Block){Started.Set();token.WaitHandle.WaitOne();token.ThrowIfCancellationRequested();}
                if(Fail&&q["lane"]=="middle"&&q["mode"]=="father")throw new IOException("Simulated source interruption");
                string key="detail-"+q["championId"]+"-"+q["lane"]+"-"+q["mode"]+"-"+q["thresholdLevel"]+"-"+q["sensitivity"];
                return Detail(key,Fingerprint);
            }
            throw new IOException("Unexpected endpoint");
        }
    }
    sealed class MockSource : IDisposable {
        readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        readonly Thread worker;
        public readonly ManualResetEventSlim Started = new ManualResetEventSlim();
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
        public readonly string Url;
        public MockSource(string body, int status) {
            listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/detail";
            worker = new Thread(() => {
                try {
                    using (var client = listener.AcceptTcpClient())
                    using (var stream = client.GetStream()) {
                        client.ReceiveTimeout = 3000;
                        var header = new StringBuilder(); int next;
                        while ((next = stream.ReadByte()) >= 0) { header.Append((char)next); if (header.ToString().EndsWith("\r\n\r\n")) break; }
                        Started.Set(); Release.Wait(10000);
                        var bytes = Encoding.UTF8.GetBytes(body);
                        var prefix = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Test\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                        stream.Write(prefix, 0, prefix.Length); stream.Write(bytes, 0, bytes.Length);
                    }
                } catch (IOException) { } catch (SocketException) { }
            });
            worker.IsBackground = true; worker.Start();
        }
        public void Dispose() { Release.Set(); listener.Stop(); worker.Join(1500); }
    }
}
