import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import zlib from 'node:zlib';
import crypto from 'node:crypto';
const base=process.argv[2]||'http://127.0.0.1:17653';
const offline=process.argv.includes('--offline');
const expectedVersion=fs.readFileSync(new URL('../src/AssemblyInfo.cs',import.meta.url),'utf8').match(/AssemblyInformationalVersion\("(\d+\.\d+\.\d+)"\)/)?.[1];
assert.ok(expectedVersion,'The source release version is required for HTTP verification');
const headers={'X-Luna':'1'};
// Consume responses with the native HTTP client. This also covers early
// request rejection without depending on a bundled fetch parser version.
function get(path,options={}) {
  return new Promise((resolve,reject)=>{
    const body=options.body??'',requestHeaders={...(options.headers??headers)};
    if(body&&!requestHeaders['Transfer-Encoding'])requestHeaders['Content-Length']=Buffer.byteLength(body);
    const request=http.request(base+path,{method:options.method??'GET',headers:requestHeaders,timeout:10000,agent:false},response=>{
      const chunks=[];response.on('data',chunk=>chunks.push(chunk));response.on('error',error=>reject(new Error(`${options.method??'GET'} ${path} (HTTP ${response.statusCode}, ${chunks.reduce((sum,chunk)=>sum+chunk.length,0)}/${response.headers['content-length']} bytes): ${error.message}`,{cause:error})));
      response.on('end',()=>{try{const bytes=Buffer.concat(chunks);resolve({status:response.statusCode,headers:response.headers,data:options.raw?bytes:JSON.parse(bytes.toString('utf8'))});}catch(error){reject(error);}});
    });request.on('error',error=>reject(new Error(`${options.method??'GET'} ${path}: ${error.message}`,{cause:error})));request.on('timeout',()=>request.destroy(new Error('Local test request timed out')));request.end(body);
  });
}
const health=await get('/api/health');assert.equal(health.data.app,'luna-local');assert.equal(health.data.offline,offline);assert.equal(health.data.version,expectedVersion);assert.equal(health.data.author,'FrancisChen');
const preferences=(await get('/api/settings')).data;
assert.equal(preferences.version,health.data.version);assert.ok(['system','zh-CN','en'].includes(preferences.preferences.language));
assert.equal(preferences.preferences.closeAction,'ask');
assert.equal(preferences.desktopWindowAvailable,false);
assert.equal((await get('/api/window')).data.available,false);
for(const endpoint of ['/api/window/show','/api/window/close?choice=tray&remember=true']){
  assert.equal((await get(endpoint)).status,405);
  assert.equal((await get(endpoint,{method:'POST',headers:{}})).status,403);
  assert.equal((await get(endpoint,{method:'POST',headers:{...headers,Origin:'https://untrusted.example'}})).status,403);
}
assert.equal((await get('/api/window/close?choice=tray&remember=true',{method:'POST'})).status,400);
assert.equal((await get('/api/settings')).data.preferences.closeAction,'ask','No window must not persist an attempted close');
for(const endpoint of ['/api/space','/api/app-update','/api/maintenance']){
  assert.equal((await get(endpoint,{headers:{}})).status,403);
  assert.equal((await get(endpoint,{headers:{...headers,Origin:'https://untrusted.example'}})).status,403);
  assert.equal((await get(endpoint)).status,200);
}
for(const endpoint of ['/api/space/clean','/api/app-update/check','/api/app-update/download','/api/app-update/cancel','/api/maintenance/run?action=uninstall&removeData=false']){
  assert.equal((await get(endpoint)).status,405);
  assert.equal((await get(endpoint,{method:'POST',headers:{}})).status,403);
  assert.equal((await get(endpoint,{method:'POST',headers:{...headers,Origin:'https://untrusted.example'}})).status,403);
  assert.equal((await get(endpoint,{method:'POST',body:'unexpected'})).status,400);
}
assert.equal((await get('/api/maintenance/run?action=unknown&removeData=false',{method:'POST'})).status,400);
assert.equal((await get('/api/app-update')).data.offline,offline);
if(offline) for(const action of ['check','download']) assert.equal((await get('/api/app-update/'+action,{method:'POST'})).status,400);
assert.equal((await get('/api/settings',{headers:{}})).status,403);
assert.equal((await get('/api/diagnostics',{headers:{}})).status,403);
assert.equal((await get('/api/settings/save?key=theme&value=light')).status,405);
assert.equal((await get('/api/settings/save?key=theme&value=light',{method:'POST',headers:{...headers,Origin:'https://untrusted.example'}})).status,403);
assert.equal((await get('/api/settings/save?key=theme&value=light',{method:'POST',headers:{}})).status,403);
assert.equal((await get('/api/settings/save?key=theme&value=light',{method:'POST',body:'unexpected'})).status,400);
// Exercise actual unread payloads repeatedly: premature socket close used to
// turn the intended 400/403/405 into intermittent ECONNRESET on Windows.
for(let attempt=0;attempt<20;attempt++) {
  const invalid=(await get('/api/settings/save?key=theme&value=dark',{method:'POST',body:'unexpected-body-'+attempt}));
  assert.equal(invalid.status,400);assert.equal(invalid.data.success,false);
  assert.equal((await get('/api/library/cancel',{method:'POST',body:'unexpected-body-'+attempt})).status,400);
}
assert.deepEqual((await get('/api/settings')).data.preferences,preferences.preferences,'Rejected requests must not save settings');
assert.equal((await get('/api/settings/save?key=theme&value=dark',{method:'POST',headers:{...headers,'Transfer-Encoding':'chunked'},body:'chunked-body'})).status,400);
assert.equal((await get('/api/settings',{method:'POST',body:'wrong-method'})).status,405);
assert.equal((await get('/api/settings/save?key=theme&value=dark',{method:'POST',headers:{},body:'missing-marker'})).status,403);
const incompleteStarted=performance.now();
assert.equal((await get('/api/settings/save?key=theme&value=dark',{method:'POST',headers:{...headers,'Content-Length':'999999'}})).status,400);
assert.ok(performance.now()-incompleteStarted<2000,'Rejecting an unfinished body must not wait for it to arrive');
for(const query of ['key=theme&value=unsafe','key=path&value=../outside','key=language&value=invalid','key=autoUpdate&value=maybe'])assert.equal((await get('/api/settings/save?'+query,{method:'POST'})).status,400);
try {
  const saved=(await get('/api/settings/save?key=theme&value=light',{method:'POST'})).data;
  assert.equal(saved.preferences.theme,'light');assert.equal(saved.preferences.language,preferences.preferences.language);assert.equal(saved.preferences.autoUpdate,preferences.preferences.autoUpdate);
  assert.equal(saved.automaticUpdatesThisRun,preferences.automaticUpdatesThisRun);
} finally {assert.equal((await get('/api/settings/save?'+new URLSearchParams({key:'theme',value:preferences.preferences.theme}),{method:'POST'})).status,200);}
assert.equal((await get('/api/folders/open?kind=logs')).status,405);
assert.equal((await get('/api/folders/open?kind=logs',{method:'POST',headers:{}})).status,403);
assert.equal((await get('/api/folders/open?kind=../../outside',{method:'POST'})).status,400);
assert.equal((await get('/api/about/document?name=../../LICENSE')).status,400);
assert.equal((await get('/api/about/document?name=license',{headers:{}})).status,403);
assert.match((await get('/api/about/document?name=license')).data.text,/Copyright \(c\) 2026 FrancisChen/);
assert.ok((await get('/api/about/document?name=changelog')).data.text.length>10);
assert.ok((await get('/api/about/document?name=third-party')).data.text.length>10);
const diagnostics=(await get('/api/diagnostics')).data;
assert.deepEqual(Object.keys(diagnostics).sort(),['automaticUpdatesThisRun','errors','mode','success','version']);
for(const error of diagnostics.errors){assert.deepEqual(Object.keys(error).sort(),['category','count']);assert.ok(['network','permission','storage','data','internal'].includes(error.category));}
for(const asset of ['i18n.js','locales.js','settings.js'])assert.equal((await get('/'+asset,{raw:true})).status,200);
const catalog=await get('/api/catalog?refresh=1');assert.equal(catalog.status,200);assert.ok(catalog.data.data.heroes.length>=173);assert.equal(catalog.data.data.lanes.length,5);
const manifest=JSON.parse(fs.readFileSync(new URL('../data/library/1-5/manifest.json',import.meta.url),'utf8'));
const keys=Object.keys(manifest.entries);assert.equal(keys.length,manifest.expected);
const expected=new Set(catalog.data.data.heroes.flatMap(h=>Object.keys(h.lanes).flatMap(l=>['father','son'].map(m=>`detail-${h.id}-${l}-${m}-1-5`))));
assert.deepEqual(new Set(keys),expected,'Every hero and valid lane must have both directions');
const durations=[];let cursor=0;
await Promise.all(Array.from({length:4},async()=>{
  while(cursor<keys.length) {
    const key=keys[cursor++],[,id,lane,mode,t,s]=key.split('-');
    const bytes=fs.readFileSync(new URL(`../data/library/1-5/${key}.json.gz`,import.meta.url));
    assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'),manifest.entries[key]);
    const saved=JSON.parse(zlib.gunzipSync(bytes));assert.equal(saved.key,key);assert.equal(saved.fingerprint,manifest.fingerprint);
    const start=performance.now();
    const response=await get(`/api/detail?championId=${id}&lane=${lane}&mode=${mode}&thresholdLevel=${t}&sensitivity=${s}&refresh=1`);
    durations.push(performance.now()-start);
    assert.equal(response.status,200,key);assert.ok(response.data.data,key+' missing local record');
    // JSON round-trip normalizes IEEE negative zero, which .NET serializes as ordinary zero.
    assert.deepEqual(response.data.data,JSON.parse(JSON.stringify(saved.data)),key+' differs from source snapshot');
    assert.equal(response.data.cache.needsRefresh,false);
    assert.equal(response.data.cache.sourceGeneratedAt,manifest.sourceGeneratedAt,key+' lost source generation time');
    assert.equal(response.data.cache.source,'bundled',key+' mislabeled bundled data');
    assert.equal(response.data.cache.localUpdatedAt,'',key+' attributed publisher collection to this computer');
    assert.equal(response.data.cache.isFallback,false,key+' healthy data marked as fallback');
  }
}));
const info=(await get('/api/library')).data;assert.equal(info.complete,true);assert.equal(info.saved,keys.length);assert.equal(info.job.busy,false);assert.equal(info.job.state,'idle');assert.equal(info.automaticUpdates,false,'Run this deterministic snapshot check with --offline or --no-auto-update');
for(const query of [
  'championId=266&lane=invalid&mode=father&thresholdLevel=1&sensitivity=5',
  'championId=266&lane=top&mode=father&thresholdLevel=6&sensitivity=5',
  'championId=266&lane=top&mode=rating_3500&thresholdLevel=1&sensitivity=5'
])assert.equal((await get('/api/detail?'+query)).status,400);
assert.equal((await get('/api/catalog',{headers:{}})).status,403);
assert.equal((await get('/api/catalog',{headers:{...headers,Origin:'https://untrusted.example'}})).status,403);
const hostStatus=await new Promise((resolve,reject)=>{const request=http.get(base+'/api/catalog',{headers:{...headers,Host:'untrusted.example'},agent:false,timeout:10000},response=>{response.resume();resolve(response.statusCode);});request.on('error',error=>reject(new Error('Invalid Host header check: '+error.message,{cause:error})));request.on('timeout',()=>request.destroy(new Error('Host check timed out')));});
assert.equal(hostStatus,403);
assert.equal((await get('/api/catalog',{method:'POST'})).status,405);
assert.equal((await get('/api/library/update')).status,405);
assert.equal((await get('/api/hero/update?key=detail-6-top-father-1-5')).status,405);
assert.equal((await get('/api/hero/update?key=detail-6-top-father-1-5',{method:'POST',headers:{}})).status,403);
assert.equal((await get('/api/hero/status?key=../../outside')).status,400);
assert.equal((await get('/api/hero/status?key=detail-99999-top-father-1-5')).status,400);
assert.equal((await get('/api/hero/status?key=detail-6-top-father-1-5')).data.state,'idle');
if(offline)assert.equal((await get('/api/hero/update?key=detail-6-top-father-1-5',{method:'POST'})).status,400);
assert.equal((await get('/api/library/focus?key=detail-6-top-father-1-5')).status,405);
assert.equal((await get('/api/library/focus?key=detail-6-top-father-1-5',{method:'POST',headers:{}})).status,403);
assert.equal((await get('/api/library/focus?key=../../outside',{method:'POST'})).status,400);
assert.equal((await get('/api/library/focus?key=detail-6-top-father-1-5',{method:'POST'})).status,200);
assert.equal((await get('/api/library')).data.job.busy,false,'A local priority hint must not start networking');
assert.equal((await get('/api/library/update',{method:'POST',headers:{}})).status,403);
assert.equal((await get('/api/library/cancel',{method:'POST',body:'unexpected'})).status,400);
assert.equal((await get('/api/quit')).status,405);
assert.equal((await get('/data/catalog.json')).status,404);
assert.equal((await get('/src/Program.cs')).status,404);
assert.equal((await get('/%2e%2e%2fsrc%2fProgram.cs')).status,404);
const changed=await get('/api/detail?championId=266&lane=top&mode=father&thresholdLevel=5&sensitivity=1');
assert.equal(changed.status,200);assert.equal(changed.data.data,null,'A different threshold must never reuse the default');
assert.equal((await get('/api/library?thresholdLevel=5&sensitivity=1')).data.complete,false);
if(offline) assert.equal((await get('/api/library/update',{method:'POST'})).status,400);
const page=await get('/',{raw:true});assert.equal(page.status,200);assert.ok(page.headers['content-security-policy'].includes("frame-ancestors 'none'"));assert.ok(page.data.toString('utf8').includes(expectedVersion));
const icon=await get('/app.ico',{raw:true});assert.equal(icon.status,200);assert.ok(icon.data.byteLength>1000);
durations.sort((a,b)=>a-b);
console.log(JSON.stringify({passed:true,mode:offline?'offline':'normal-manual-only',queries:keys.length,medianMs:+durations[Math.floor(keys.length*.5)].toFixed(2),p95Ms:+durations[Math.floor(keys.length*.95)].toFixed(2),maxMs:+durations.at(-1).toFixed(2),checks:'exact full-library source equality, updates disabled for deterministic snapshot comparison, valid empty results, filter isolation, local-only HTTP boundaries'},null,2));
