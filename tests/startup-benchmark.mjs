// Real payloads reshaped into 25 isolated filter profiles; no upstream requests.
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import crypto from 'node:crypto';
import http from 'node:http';
import net from 'node:net';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
const [runtime,baseline,newExe,output]=process.argv.slice(2);
assert.ok(runtime&&baseline&&newExe&&output);
assert.equal(fs.existsSync(output),false,'Choose a new benchmark directory');fs.mkdirSync(output,{recursive:true});
const root=path.join(output,'runtime');fs.cpSync(runtime,root,{recursive:true,filter:p=>!p.startsWith(path.join(runtime,'cache'))});
const manifest=JSON.parse(fs.readFileSync(path.join(root,'data/library/1-5/manifest.json')));
const records=Object.keys(manifest.entries).map(key=>JSON.parse(zlib.gunzipSync(fs.readFileSync(path.join(root,'data/library/1-5',key+'.json.gz')))));
const library=path.join(root,'cache/library');fs.mkdirSync(library,{recursive:true});
const fingerprints=['c'.repeat(64),'b'.repeat(64)];let files=0,bytes=0;
for(let t=1;t<=5;t++)for(let s=1;s<=5;s++){
  const profile=t+'-'+s;
  for(const [i,fingerprint] of fingerprints.entries()){
    const dir=path.join(library,fingerprint,profile);fs.mkdirSync(dir,{recursive:true});const entries={};
    for(const original of records){
      const record={...original,key:original.key.replace(/-1-5$/,'-'+profile),fingerprint};
      const compressed=zlib.gzipSync(JSON.stringify(record));fs.writeFileSync(path.join(dir,record.key+'.json.gz'),compressed);
      entries[record.key]=crypto.createHash('sha256').update(compressed).digest('hex');files++;bytes+=compressed.length;
    }
    fs.writeFileSync(path.join(dir,'manifest.json'),JSON.stringify({...manifest,profile,fingerprint,entries,sourceGeneratedAt:i?'2026-10-12T01:00:00Z':'2026-10-13T01:00:00Z',builtAt:'2026-10-14T01:00:00Z'}));
  }
  fs.writeFileSync(path.join(library,'active-'+profile+'.json'),JSON.stringify({current:fingerprints[0],previous:fingerprints[1]}));
}
console.log('Prepared',files,'records,',bytes,'compressed bytes');
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function freePort(){const server=net.createServer();await new Promise(r=>server.listen(0,'127.0.0.1',r));const port=server.address().port;await new Promise(r=>server.close(r));return port>65000?freePort():port;}
function call(port,route,method='GET'){return new Promise((resolve,reject)=>{const req=http.request({host:'127.0.0.1',port,path:route,method,headers:{'X-Luna':'1'},timeout:1000,agent:false},res=>{let body='';res.on('data',x=>body+=x);res.on('end',()=>{try{resolve(JSON.parse(body));}catch(e){reject(e);}});});req.on('error',reject);req.on('timeout',()=>req.destroy(new Error('timeout')));req.end();});}
const runs=[];
for(let round=1;round<=3;round++)for(const label of round%2?['1.5.2','1.6.0']:['1.6.0','1.5.2']){
  fs.copyFileSync(label==='1.6.0'?newExe:baseline,path.join(root,'Luna.exe'));
  const port=await freePort(),start=performance.now();const child=spawn(path.join(root,'Luna.exe'),['--no-browser','--offline','--maintenance-probe','--port',String(port)],{cwd:root,windowsHide:true,stdio:'ignore'});
  const exited=new Promise(r=>child.once('exit',r));let health;
  try{
    while(performance.now()-start<30000){try{health=await call(port,'/api/health');break;}catch{await sleep(20);}}
    assert.ok(health);const ready=performance.now()-start;
    const qstart=performance.now(),query=await call(port,'/api/detail?championId=103&lane=middle&mode=father&thresholdLevel=1&sensitivity=5');assert.ok(query.data);assert.equal(query.cache.source,'library');
    const row={version:label,round,readyMs:ready,firstResultMs:performance.now()-start,queryMs:performance.now()-qstart};runs.push(row);console.log(JSON.stringify(row));
    await call(port,'/api/quit','POST');await Promise.race([exited,sleep(5000).then(()=>{if(child.exitCode===null)throw new Error('Process failed to exit');})]);
  }finally{if(child.exitCode===null){child.kill();await exited;}}
}
fs.writeFileSync(path.join(output,'startup-benchmark.json'),JSON.stringify({passed:true,fixtureProfiles:25,currentAndBackup:true,records:files,compressedBytes:bytes,method:'New process with 25 valid profiles and one backup each; Windows filesystem cache not flushed; alternating versions; headless local HTTP readiness, not native rendering.',runs},null,2));
