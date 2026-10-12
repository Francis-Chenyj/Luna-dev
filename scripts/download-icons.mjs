import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const heroes = JSON.parse(fs.readFileSync(path.join(root,'data/catalog.json'),'utf8')).data.heroes;
const directory = path.join(root,'data/icons');
fs.mkdirSync(directory,{recursive:true});
const queue = heroes.filter(h=>h.sourceIcon && !fs.existsSync(path.join(directory, `${h.id}.png`)));
let done=0,failed=0;
async function worker() {
  while(queue.length) {
    const h=queue.shift();
    if(!/^\/assets\/champion-icons\/[\d.]+\/\d+\.png$/.test(h.sourceIcon)) continue;
    try {
      const response=await fetch('https://www.malzaharai.top'+h.sourceIcon,{signal:AbortSignal.timeout(20000)});
      if(!response.ok) throw new Error(String(response.status));
      const data=Buffer.from(await response.arrayBuffer());
      if(data.subarray(0,8).toString('hex')!=='89504e470d0a1a0a') throw new Error('Not PNG');
      fs.writeFileSync(path.join(directory,`${h.id}.png`),data);
      done++;
    } catch(e) { failed++; console.log(`Icon ${h.id}: ${e.message}`); }
  }
}
await Promise.all([worker(),worker(),worker()]);
console.log(`Saved ${done} portraits; ${failed} unavailable (app has a fallback icon).`);
