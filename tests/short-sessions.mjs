import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {performance} from 'node:perf_hooks';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
const [normal,fixture,output]=process.argv.slice(2),sleep=ms=>new Promise(r=>setTimeout(r,ms));
const manifest=JSON.parse(fs.readFileSync(path.join(normal,'data/library/1-5/manifest.json'),'utf8').replace(/^\uFEFF/,''));
const expectedVersion=JSON.parse(fs.readFileSync(path.join(normal,'installed-files.json'),'utf8').replace(/^\uFEFF/,'')).version;
assert.match(expectedVersion,/^\d+\.\d+\.\d+$/,'Runtime manifest must identify its actual version');
const keys=Object.keys(manifest.entries),samples=[];
const hash=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const headers={'X-Luna':'1'};
const detail=key=>{const [,id,lane,mode,t,s]=key.split('-');return '/api/detail?'+new URLSearchParams({championId:id,lane,mode,thresholdLevel:t,sensitivity:s});};
function files(root){return fs.existsSync(root)?fs.readdirSync(root,{recursive:true}).filter(f=>f.endsWith('.json.gz')).map(f=>path.join(root,f)):[];}
async function run(root,round,controlled){
  const port=controlled?19861:19860,base='http://127.0.0.1:'+port,started=performance.now();
  const previous=new Map(files(path.join(root,'cache/library')).map(p=>[p,hash(p)]));
  const proc=spawn(path.join(root,controlled?'SessionHarness.exe':'Luna.exe'),controlled?[String(round),'--port',String(port)]:['--offline','--port',String(port)],{cwd:root,windowsHide:true,stdio:'ignore'});
  let serviceMs,windowMs,firstResultMs,queryTimes=[],errors=[];
  const get=async(endpoint,method='GET')=>{const response=await fetch(base+endpoint,{method,headers,signal:AbortSignal.timeout(3000)});if(!response.ok)throw new Error(endpoint+': '+response.status);return response.json();};
  try{
    while(performance.now()-started<15000){try{const health=await get('/api/health');assert.equal(health.version,expectedVersion);serviceMs=performance.now()-started;break;}catch{await sleep(50);}}
    assert.ok(serviceMs,'Service did not start');
    if(controlled)await get('/api/library/focus?key=detail-103-middle-father-1-5','POST');
    let nextKey=0;
    while(performance.now()-started<30000){
      const watch=performance.now();try{const value=await get(detail(keys[nextKey++%keys.length]));assert.ok(value.data);queryTimes.push(performance.now()-watch);firstResultMs??=performance.now()-started;}catch(error){errors.push(error.message);}
      if(!windowMs){try{const window=await get('/api/window');if(window.ready)windowMs=performance.now()-started;}catch{}}
      await sleep(150);
    }
    const beforeExit=await get('/api/library'),pair=await get(detail('detail-103-middle-father-1-5'));
    const elapsed=performance.now()-started;await get('/api/quit','POST');
    await Promise.race([new Promise(r=>proc.once('exit',r)),sleep(5000)]);assert.notEqual(proc.exitCode,null,'Exit left the process running');
    for(const [file,digest]of previous)assert.equal(hash(file),digest,'Completed data changed/redownloaded');
    queryTimes.sort((a,b)=>a-b);assert.equal(errors.length,0,errors.join(';'));assert.ok(queryTimes.at(-1)<5000,'Local query exceeded five seconds');assert.ok(windowMs,'Native window did not become ready');
    const row={mode:controlled?'controlled-upstream':'actual-offline-runtime',round,elapsedMs:Math.round(elapsed),serviceMs:+serviceMs.toFixed(1),windowMs:+windowMs.toFixed(1),firstResultMs:+firstResultMs.toFixed(1),queries:queryTimes.length,p50Ms:+queryTimes[Math.floor(queryTimes.length*.5)].toFixed(2),p95Ms:+queryTimes[Math.floor(queryTimes.length*.95)].toFixed(2),maxMs:+queryTimes.at(-1).toFixed(2),savedRecords:files(path.join(root,'cache/library')).length,previousRecordsRetained:previous.size,pairSource:pair.cache?.source,jobState:beforeExit.job.state};
    samples.push(row);console.log(JSON.stringify(row));fs.writeFileSync(path.join(output,'short-sessions.json'),JSON.stringify({passed:false,samples},null,2));
  }finally{if(proc.exitCode===null){try{await get('/api/quit','POST');}catch{}await sleep(200);if(proc.exitCode===null)proc.kill();}}
}
for(let round=1;round<=3;round++)await run(normal,round,false);
for(let round=1;round<=3;round++)await run(fixture,round,true);
const requests=fs.readFileSync(path.join(fixture,'requests.csv'),'utf8').trim().split(/\r?\n/).map(line=>line.replace(/^\uFEFF/,'').split(','));
const duplicates=[];const seen=new Map();for(const [round,key]of requests){if(seen.has(key)&&seen.get(key)!==round)duplicates.push(key);seen.set(key,round);}
assert.equal(duplicates.length,0,'Completed records were downloaded again across sessions');
assert.ok(samples.filter(x=>x.mode==='controlled-upstream').every(x=>x.pairSource==='focused'),'Priority champion pair was not preserved');
assert.ok(samples.filter(x=>x.mode==='controlled-upstream').every((x,i,a)=>i===0||x.savedRecords>a[i-1].savedRecords),'Resume made no progress');
fs.writeFileSync(path.join(output,'short-sessions.json'),JSON.stringify({passed:true,samples,totalDetailDownloads:requests.length,crossSessionDuplicateDownloads:duplicates.length,note:'Three real packaged offline launches and three native launches of identical application sources with controlled upstream replies. Each session runs for 30 seconds and exits through the real quit endpoint. Timings measure process start, native NavigationCompleted and loopback HTTP queries, not human typing/full result painting or a reboot-cold OS cache.'},null,2));
console.log('PASS: six actual 30-second sessions; retained progress; no cross-session duplicate record downloads; local queries below five seconds.');
