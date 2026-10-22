using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Luna;

// Independent regression coverage for client lifecycle, data projection and the
// real HTTPS transport. Every process response and TLS peer is a local fixture;
// no installed League client, account data or certificate store is accessed.
internal static class ClientDraftBoundaryTests {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 static int failures;
 static void Check(bool value,string message) { if(!value) throw new Exception(message); }
 static void Until(Func<bool> condition,string message) { var watch=Stopwatch.StartNew();while(!condition()&&watch.ElapsedMilliseconds<5000)Thread.Sleep(5);Check(condition(),message); }
 static ClientDraftConnection Conn(int number) { return new ClientDraftConnection(number,30000+number,"private-token-"+number); }
 static string Session(int champion) { return Json.Serialize(new {myTeam=new[]{new{cellId=0,championId=champion,championPickIntent=champion,assignedPosition="MIDDLE",summonerId=123456789,puuid="PRIVATE-PUUID",displayName="PRIVATE-NAME"}},theirTeam=new object[0],actions=new[]{new[]{new{actorCellId=0,type="pick",isAllyAction=true,championId=champion,completed=true}}},chatDetails=new{multiUserChatPassword="PRIVATE-CHAT"},gameId=88888888}); }
 static void Pause(int ms,CancellationToken token) { if(token.WaitHandle.WaitOne(2))token.ThrowIfCancellationRequested(); }
 static void LateResponse() {
  using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var returned=new ManualResetEventSlim()) {
   int discoveries=0;
   using(var service=new ClientDraftService(discovery:token=>new ClientDraftDiscovery(new[]{Conn(Interlocked.Increment(ref discoveries))}),transport:(c,path,token)=> {
    if(path==ClientDraftProtocol.PhasePath)return new ClientDraftResponse(200,"\"ChampSelect\"");
    if(c.ProcessId==1){entered.Set();release.Wait();returned.Set();return new ClientDraftResponse(200,Session(266));}
    return new ClientDraftResponse(200,Session(103));
   },wait:Pause)) {
    try {
     service.Connect();Check(entered.Wait(5000),"First session did not enter blocked response");
     service.Disconnect();service.Connect();Until(()=>service.ReadSnapshot().teams.ally[0].championId==103,"New connection did not publish");
     release.Set();Check(returned.Wait(1000),"Old response did not return");Thread.Sleep(50);
     Check(service.ReadSnapshot().teams.ally[0].championId==103,"Old response replaced new draft");
     var copy=service.ReadSnapshot();copy.teams.ally[0].championId=9999;copy.teams.ally[1]=null;
     Check(service.ReadSnapshot().teams.ally[0].championId==103&&service.ReadSnapshot().teams.ally[1]!=null,"Snapshot shares mutable state");
    } finally {release.Set();}
   }
  }
 }
 static void BlockedDiscoveryShutdown() {
  using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var left=new ManualResetEventSlim()) {
   int reads=0;
   var service=new ClientDraftService(discovery:token=>{entered.Set();release.Wait();left.Set();return new ClientDraftDiscovery(new[]{Conn(1)});},transport:(c,p,t)=>{Interlocked.Increment(ref reads);return new ClientDraftResponse(200,"\"Lobby\"");});
   try {
    service.Connect();Check(entered.Wait(5000),"Discovery did not block");var watch=Stopwatch.StartNew();service.Dispose();
    Check(watch.ElapsedMilliseconds<2200,"Dispose waited indefinitely for discovery");Check(!service.ReadSnapshot().enabled,"Disposed service remains enabled");
    release.Set();Check(left.Wait(1000),"Blocked discovery did not return");Thread.Sleep(50);Check(reads==0,"Disposed generation contacted client after discovery returned");
   }finally{release.Set();service.Dispose();}
  }
 }
 static void ParallelToggles() {
  int calls=0;
  using(var service=new ClientDraftService(discovery:t=>new ClientDraftDiscovery(new[]{Conn(1)}),transport:(c,p,t)=>{Interlocked.Increment(ref calls);return new ClientDraftResponse(200,"\"Lobby\"");},wait:Pause)) {
   Parallel.For(0,200,new ParallelOptions{MaxDegreeOfParallelism=8},i=>{if((i&1)==0)service.Connect();else service.Disconnect();var s=service.ReadSnapshot();Check(s.teams.ally.Length==5&&s.teams.enemy.Length==5,"Malformed concurrent snapshot");});
   service.Disconnect();Thread.Sleep(100);int after=calls;Thread.Sleep(100);
   Check(calls==after&&!service.ReadSnapshot().enabled,"Disconnected workers continued polling");
   service.Connect();Until(()=>service.ReadSnapshot().state=="ready","Service failed to reconnect after concurrent toggles");
  }
 }
 static void AuthRotation() {
  int discoveries=0,oldReads=0;
  using(var service=new ClientDraftService(discovery:t=>new ClientDraftDiscovery(new[]{Conn(Interlocked.Increment(ref discoveries)==1?1:2)}),transport:(c,p,t)=> {
   if(c.ProcessId==1){Interlocked.Increment(ref oldReads);return new ClientDraftResponse(401,"PRIVATE-ERROR");}
   return new ClientDraftResponse(200,p==ClientDraftProtocol.PhasePath?"\"ChampSelect\"":Session(103));
  },wait:Pause)) {
   service.Connect();Until(()=>service.ReadSnapshot().teams.ally[0].championId==103,"Auth rotation did not rediscover and recover");
   Check(oldReads==1&&discoveries>=2,"Invalid credentials were reused");
   var snapshot=Json.Serialize(service.ReadSnapshot());Check(!snapshot.Contains("PRIVATE")&&!snapshot.Contains("private-token")&&!snapshot.Contains("summoner")&&!snapshot.Contains("puuid")&&!snapshot.Contains("gameId"),"Private fields reached public snapshot");
  }
 }
 static void MultipleClients() {
  int reads=0;
  using(var service=new ClientDraftService(discovery:t=>new ClientDraftDiscovery(new[]{Conn(1),Conn(2)}),transport:(c,p,t)=>{Interlocked.Increment(ref reads);return new ClientDraftResponse(200,"\"Lobby\"");},wait:Pause)) {
   service.Connect();Until(()=>service.ReadSnapshot().state=="multiple","Ambiguous clients were not rejected");Check(reads==0,"Ambiguous discovery contacted a client");
  }
 }
 static void RequestBoundary() {
  var request=ClientDraftProtocol.CreateRequest(Conn(1),ClientDraftProtocol.PhasePath);
  Check(request.RequestUri.Host=="127.0.0.1"&&request.RequestUri.Scheme=="https"&&request.Proxy==null&&!request.AllowAutoRedirect&&request.Method=="GET","Request escaped loopback read-only boundary");
  bool rejected=false;try{ClientDraftProtocol.CreateRequest(Conn(1),"/lol-summoner/v1/current-summoner");}catch(ArgumentException){rejected=true;}Check(rejected,"Player endpoint was accepted");
  rejected=false;try{ClientDraftProtocol.CreateRequest(Conn(1),"https://example.org/");}catch(ArgumentException){rejected=true;}Check(rejected,"External URL was accepted");
 }
 internal static int Main() { return RunLifecycle() + ClientDraftTlsBoundaryTests.RunTls(); }
 static int RunLifecycle() {
  var cases=new Action[]{LateResponse,BlockedDiscoveryShutdown,ParallelToggles,AuthRotation,MultipleClients,RequestBoundary};
  foreach(var item in cases)try{item();Console.WriteLine("PASS: "+item.Method.Name);}catch(Exception ex){failures++;Console.WriteLine("FAIL: "+item.Method.Name+" "+ex.Message);}
  return failures==0?0:1;
 }
}
internal static class ClientDraftTlsBoundaryTests {
 static X509Certificate2 CreateCertificate() {
  using (var key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false }) {
   var request = new CertificateRequest("CN=Luna fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
   // Import a separate temporary key handle for Schannel before releasing the
   // signing key. The certificate is never added to any certificate store.
   using (var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1)))
    return new X509Certificate2(created.Export(X509ContentType.Pfx, "fixture-only"), "fixture-only", X509KeyStorageFlags.Exportable);
  }
 }
 static int failures;
 static X509Certificate2 certificate;
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Write(SslStream stream,string text){byte[] bytes=Encoding.ASCII.GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush();}
 sealed class Server:IDisposable {
  internal readonly TcpListener Listener=new TcpListener(IPAddress.Loopback,0);
  internal readonly Task Worker;
  internal readonly int Port;
  internal bool RequestBoundaryChecked;
  TcpClient client;
  internal Server(Action<SslStream> respond){
   Listener.Start();Port=((IPEndPoint)Listener.LocalEndpoint).Port;
   Worker=Task.Run(()=>{
    try{client=Listener.AcceptTcpClient();client.ReceiveTimeout=3000;client.SendTimeout=3000;
     using(client)using(var tls=new SslStream(client.GetStream(),false)){
      tls.AuthenticateAsServer(certificate,false,SslProtocols.Tls12,false);
      var header=new StringBuilder();while(header.Length<16384&&!header.ToString().EndsWith("\r\n\r\n")){int b=tls.ReadByte();if(b<0)throw new IOException();header.Append((char)b);}
      string value=header.ToString();Check(value.StartsWith("GET "+ClientDraftProtocol.PhasePath+" HTTP/1.1\r\n")&&value.Contains("Authorization: Basic "+Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:fixture-token"))),"Unexpected request boundary");
      RequestBoundaryChecked=true;respond(tls);
     }
    }catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}
   });
  }
  internal ClientDraftResponse Read(){return ClientDraftProtocol.Read(new ClientDraftConnection(1,Port,"fixture-token"),ClientDraftProtocol.PhasePath,CancellationToken.None);}
  public void Dispose(){Listener.Stop();if(client!=null)client.Close();Check(Worker.Wait(6000),"Fixture TLS server failed to drain");}
 }
 static void Expected(string code,Action<SslStream> respond){using(var server=new Server(respond)){bool caught=false;try{server.Read();}catch(ClientDraftException ex){caught=ex.Code==code;}Check(caught,"Expected error classification: "+code);Check(server.RequestBoundaryChecked,"Request did not reach local TLS fixture");}}
 static void Redirect(){var trap=new TcpListener(IPAddress.Loopback,0);trap.Start();try{int port=((IPEndPoint)trap.LocalEndpoint).Port;using(var server=new Server(tls=>Write(tls,"HTTP/1.1 302 Found\r\nLocation: https://127.0.0.1:"+port+ClientDraftProtocol.PhasePath+"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"))){Check(server.Read().StatusCode==302,"Redirect status disappeared");Thread.Sleep(100);Check(!trap.Pending(),"Redirect was followed");Check(server.RequestBoundaryChecked,"Request boundary unchecked");}}finally{trap.Stop();}}
 static void DeclaredOversize(){Expected("response-too-large",tls=>Write(tls,"HTTP/1.1 200 OK\r\nContent-Length: "+(ClientDraftProtocol.MaxResponseBytes+1)+"\r\nConnection: close\r\n\r\n"));}
 static void ChunkedOversize(){Expected("response-too-large",tls=>{Write(tls,"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");string block=new string('x',16384);for(int i=0;i<17;i++)Write(tls,"4000\r\n"+block+"\r\n");Write(tls,"0\r\n\r\n");});}
 static void InvalidUtf8(){Expected("invalid-response",tls=>{Write(tls,"HTTP/1.1 200 OK\r\nContent-Length: 1\r\nConnection: close\r\n\r\n");tls.WriteByte(255);tls.Flush();});}
 static void SlowBodyDeadline(){var clock=Stopwatch.StartNew();Expected("timeout",tls=>{Write(tls,"HTTP/1.1 200 OK\r\nContent-Length: 20\r\nConnection: close\r\n\r\n");for(int i=0;i<20;i++){tls.WriteByte(32);tls.Flush();Thread.Sleep(700);}});Check(clock.ElapsedMilliseconds<6500,"Slow body exceeded total deadline");}
 internal static int RunTls(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;certificate=CreateCertificate();var callback=ServicePointManager.ServerCertificateValidationCallback;try{foreach(var action in new Action[]{Redirect,DeclaredOversize,ChunkedOversize,InvalidUtf8,SlowBodyDeadline})try{action();Check(callback==ServicePointManager.ServerCertificateValidationCallback,"Global TLS callback changed");Console.WriteLine("PASS: "+action.Method.Name);}catch(Exception ex){failures++;Console.WriteLine("FAIL: "+action.Method.Name+" "+ex.GetBaseException().Message);}}finally{certificate.Dispose();}return failures==0?0:1;}
}
