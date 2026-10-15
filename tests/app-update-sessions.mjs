import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn} from 'node:child_process';
import assert from 'node:assert/strict';
import {performance} from 'node:perf_hooks';

const [runtime,output]=process.argv.slice(2);
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const samples=[],sessions=[];
const log=async()=>{try{return (await fs.readFile(path.join(runtime,'fixture-requests.jsonl'),'utf8')).trim().split(/\r?\n/).filter(Boolean).map(line=>JSON.parse(line.replace(/^\uFEFF/,'')));}catch{return [];}};
const size=async name=>{try{return (await fs.stat(path.join(runtime,'cache/app-update',name))).size;}catch{return 0;}};
let active;
try{
  for(let run=1;run<=3;run++){
    const before=await log(),partBefore=await size('package.part');
    const started=performance.now();
    active=spawn(path.join(runtime,'Luna.exe'),['--no-browser','--port','18672'],{cwd:runtime,windowsHide:true,stdio:'ignore'});
    const api=async(route,method='GET')=>{const res=await fetch('http://127.0.0.1:18672'+route,{method,headers:{'X-Luna':'1'},signal:AbortSignal.timeout(3000)});assert.ok(res.ok,route+' '+res.status);return res.json();};
    let health;
    for(let tries=0;tries<100;tries++){try{health=await api('/api/health');break;}catch{await delay(80);}}
    assert.equal(health?.version,'1.6.1');
    const readyMs=performance.now()-started;
    if(run===1){const saved=await api('/api/settings/save?key=appAutoDownload&value=true','POST');assert.equal(saved.preferences.appAutoCheck,true);assert.equal(saved.preferences.appAutoDownload,true);}
    let failures=0;
    while(performance.now()-started<30000){
      const now=performance.now();const data=await api('/api/detail?championId=266&lane=top&mode=father&thresholdLevel=1&sensitivity=5');
      samples.push(performance.now()-now);if(!data.data)failures++;await delay(250);
    }
    const status=await api('/api/app-update');assert.equal(failures,0);
    await api('/api/quit','POST');await new Promise((resolve,reject)=>{if(active.exitCode!==null)return resolve();const timer=setTimeout(()=>reject(Error('App did not exit')),6000);active.once('exit',()=>{clearTimeout(timer);resolve();});});active=null;
    const after=await log(),partAfter=await size('package.part');
    const newRequests=after.slice(before.length);
    sessions.push({run,seconds:(performance.now()-started)/1000,readyMs,partBefore,partAfter,state:status.state,requests:newRequests,queryFailures:failures});
    if(run===1)assert.ok(partAfter>0&&status.state==='downloading','First short session should retain a partial download');
    if(run===2){const downloads=newRequests.filter(x=>x.kind==='download');assert.equal(downloads.length,1);assert.equal(downloads[0].offset,partBefore);assert.equal(status.state,'ready');}
    if(run===3){assert.equal(newRequests.filter(x=>x.kind==='download'||x.kind==='check').length,0);assert.equal(status.state,'ready');}
    console.log(JSON.stringify(sessions.at(-1)));
  }
  samples.sort((a,b)=>a-b);
  const report={passed:true,fixture:'Current updater compiled as a lower-version fixture, loopback transports and the signed package selected by fixture.json; no historical updater or GitHub/CDN publication claim',sessions,queries:samples.length,queryMedianMs:samples[Math.floor(samples.length/2)],queryP95Ms:samples[Math.floor(samples.length*.95)],queryMaxMs:samples.at(-1)};
  await fs.writeFile(path.join(output,'short-app-update-sessions.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
}finally{if(active&&active.exitCode===null){try{await fetch('http://127.0.0.1:18672/api/quit',{method:'POST',headers:{'X-Luna':'1'},signal:AbortSignal.timeout(2000)});}catch{}}}
