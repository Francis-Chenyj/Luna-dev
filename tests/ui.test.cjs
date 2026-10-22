const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const C=require('../web/core.js');
const code=fs.readFileSync(require.resolve('../web/app.js'),'utf8');
const html=fs.readFileSync(require.resolve('../web/index.html'),'utf8');
const heroes=[{id:266,name:'亚托克斯',title:'暗裔剑魔',en:'Aatrox',keywords:'剑魔',lanes:{top:100,jungle:20}},{id:103,name:'阿狸',title:'九尾妖狐',en:'Ahri',keywords:'狐狸',lanes:{middle:100}}];
const lanes=[{key:'top',label:'上路'},{key:'jungle',label:'打野'},{key:'middle',label:'中路'}];
function draftSnapshot(enabled=true,state='champ-select'){
  const team=()=>Array.from({length:5},(_,slot)=>({slot,championId:0,selection:'empty',pickOrder:0,lane:''}));
  return {enabled,state,revision:1,teams:{ally:team(),enemy:team()}};
}
function draftPick(data,team,slot,id,selection='locked',lane='',pickOrder=0){Object.assign(data.teams[team][slot],{championId:id,selection,lane,pickOrder});return data;}

test('all eight scope choices survive reload and history without refetching on scope-only changes',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  const before=env.requests.filter(r=>r.path.startsWith('/api/detail')).length;
  const groups=['all','all_segment','high_mid_segment','high_segment','high_low_segment','mid_segment','mid_low_segment','low_segment'];
  for(const group of groups){
    env.$('segment').value=group;await env.$('segment').emit('change');
    const reloaded=await setup({hash:env.location.hash});t.after(()=>reloaded.close());
    assert.equal(reloaded.$('segment').value,group);
    assert.equal(new URLSearchParams(env.location.hash.slice(1)).get('group')||'all',group);
  }
  await env.back();assert.equal(env.$('segment').value,'mid_low_segment');
  await env.forward();assert.equal(env.$('segment').value,'low_segment');
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,before);
  env.location.hash='#hero=266&lane=top&group=bogus';await env.window.emit('popstate');
  assert.equal(env.$('segment').value,'all');assert.doesNotMatch(env.location.hash,/bogus/);
  env.$('segment').value='high_segment';await env.$('segment').emit('change');await env.$('reset-button').emit('click');
  assert.equal(env.$('segment').value,'all');assert.doesNotMatch(env.location.hash,/group=/);
});
test('missing-filter action downloads only the current champion/profile and deduplicates both columns',async t=>{
  const env=await setup({server:{detail:q=>q.get('thresholdLevel')==='2'?{data:null,cache:{}}:detail(q)}});t.after(()=>env.close());
  await env.choose(266);env.$('threshold').value='2';await env.$('threshold').emit('input');await env.advance(300);
  assert.match(env.$('profile-availability').textContent,/尚未完整保存/);
  assert.match(env.$('father-body').innerHTML,/下载此英雄的当前条件/);
  env.server.heroStatus=()=>({state:'queued',busy:true});
  const target={closest:selector=>selector==='[data-download-profile]'?{dataset:{downloadProfile:'detail-266-top-father-2-5'}}:null};
  await env.$('results').emit('click',{target});await env.$('results').emit('click',{target});await flush();
  const calls=env.requests.filter(r=>r.path.startsWith('/api/hero/update'));
  assert.equal(calls.length,1);assert.match(calls[0].path,/detail-266-top-father-2-5/);
  assert.match(env.$('father-body').innerHTML,/disabled/);assert.match(env.$('son-body').innerHTML,/disabled/);
  await env.home();await env.choose(103);await env.$('results').emit('click',{target});await flush();
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/hero/update')).length,1);
  const offline=await setup({hash:'#hero=266&lane=top&threshold=2',server:{health:{app:'luna-local',offline:true},detail:()=>({data:null})}});t.after(()=>offline.close());
  assert.match(offline.$('father-body').innerHTML,/离线模式无法下载/);assert.doesNotMatch(offline.$('father-body').innerHTML,/data-download-profile/);
  assert.match(offline.$('profile-availability').textContent,/离线模式请恢复默认条件/);assert.doesNotMatch(offline.$('profile-availability').textContent,/可在下方下载/);
});
test('waiting storage measurement explains the download lock and preserves an earlier report',async t=>{
  const env=await setup({server:{space:{success:true,busy:false,waiting:true,report:null}}});t.after(()=>env.close());
  await env.$('settings-button').emit('click');await env.$('tab-data').emit('click');await flush();
  assert.match(env.$('space-summary').textContent,/结束后自动统计/);
  env.server.space.report={checkedAt:'2026-10-16T00:00:00Z',totalBytes:1048576,bytes:{program:1048576}};
  await env.advance(2000);assert.match(env.$('space-summary').textContent,/1.00 MiB/);assert.match(env.$('space-summary').textContent,/结束后自动统计/);
});

test('language switches preserve the current lane, filters and pending query without another request',async t=>{
  const env=await setup({server:{detail:()=> 'pending'}});t.after(()=>env.close());
  await env.choose(266,'jungle');const hash=env.location.hash,before=env.requests.filter(r=>r.path.startsWith('/api/detail')).length;
  env.$('language-select').value='en';await env.$('language-select').emit('change');await flush();
  assert.equal(env.document.documentElement.lang,'en');assert.equal(env.$('selected-name').textContent,'Aatrox');
  assert.equal(env.location.hash,hash);assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,before);
  assert.match(env.$('lane-note').textContent,/Jungle/);assert.match(env.$('father-body').innerHTML,/Reading local matchups/);
  assert.equal(env.pending.filter(p=>!p.entry.aborted).length,2);
  env.$('language-select').value='zh-CN';await env.$('language-select').emit('change');assert.equal(env.$('selected-name').textContent,'亚托克斯');
});
test('English rendering preserves relationship scores, source dates and category filters',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.$('language-select').value='en';await env.$('language-select').emit('change');
  const result=env.$('father-body').innerHTML;assert.match(result,/All ranks/);assert.match(result,/Relationship value 2\.00/);assert.match(result,/Source generated/);
  assert.doesNotMatch(result,/严父|大儿|数据生成|本机更新/);
  await env.search('狐狸');assert.equal(env.$('selected-name').textContent,'Ahri');assert.match(env.$('lane-unavailable-title').textContent,/No Top data for Ahri/);
});
test('theme follows system changes only when selected and saving failures restore prior preferences',async t=>{
  const env=await setup({dark:true});t.after(()=>env.close());assert.equal(env.document.documentElement.dataset.theme,'dark');
  assert.equal(env.$('theme-color').attributes.content,'#111715');
  env.appearance.matches=false;await env.appearance.emit('change');assert.equal(env.document.documentElement.dataset.theme,'light');
  assert.equal(env.$('theme-color').attributes.content,'#f4f7f1');
  env.$('theme-select').value='dark';await env.$('theme-select').emit('change');await env.appearance.emit('change');assert.equal(env.document.documentElement.dataset.theme,'dark');
  assert.equal(env.$('theme-color').attributes.content,'#111715');
  env.server.saveFailure=true;env.$('theme-select').value='light';await env.$('theme-select').emit('change');
  assert.equal(env.$('theme-select').value,'dark');assert.equal(env.document.documentElement.dataset.theme,'dark');assert.match(env.$('settings-save-status').textContent,/未能保存/);
  assert.equal(env.$('theme-color').attributes.content,'#111715');
});
test('startup preference is visible, saved across sessions and cannot bypass offline mode',async t=>{
  const env=await setup({server:{health:{app:'luna-local',offline:true}}});t.after(()=>env.close());
  env.$('auto-update-toggle').checked=false;await env.$('auto-update-toggle').emit('change');
  assert.equal(env.server.settings.preferences.autoUpdate,false);assert.match(env.$('settings-run-mode').textContent,/所有联网更新均不可用/);
  assert.equal(env.$('update-library').disabled,true);assert.ok(env.requests.every(r=>!r.path.startsWith('/api/library/update')));
  const reopened=await setup({server:{settings:structuredClone(env.server.settings),health:{app:'luna-local',offline:true}}});t.after(()=>reopened.close());assert.equal(reopened.$('auto-update-toggle').checked,false);
});
test('settings tabs have keyboard navigation and local documents render as inert text',async t=>{
  const env=await setup();t.after(()=>env.close());await env.$('settings-button').emit('click');assert.equal(env.$('settings-dialog').open,true);
  await env.$('tab-general').emit('keydown',{key:'End',preventDefault(){}});assert.equal(env.$('tab-about').attributes['aria-selected'],'true');assert.equal(env.$('settings-about').hidden,false);
  await env.$('about-license').emit('click');assert.equal(env.$('document-content').textContent,'MIT test document');
  await env.$('open-logs').emit('click');assert.ok(env.requests.some(r=>r.path==='/api/folders/open?kind=logs'));
});
test('diagnostic copy only includes allowlisted fields and has a clipboard-denied fallback',async t=>{
  const env=await setup({server:{diagnostics:{version:'1.5.0',mode:'offline',automaticUpdatesThisRun:false,secret:'token-secret',path:'C:/private/account',errors:[{category:'network',count:2,message:'raw secret'},{category:'token-secret',count:1}]}}});t.after(()=>env.close());
  await env.$('copy-diagnostics').emit('click');assert.equal(env.copied.length,1);assert.match(env.copied[0],/1\.5\.0/);assert.match(env.copied[0],/网络连接错误: 2/);assert.doesNotMatch(env.copied[0],/secret|private|account/);
  const denied=await setup({clipboardDenied:true});t.after(()=>denied.close());await denied.$('copy-diagnostics').emit('click');assert.equal(denied.$('diagnostics-preview').hidden,false);assert.match(denied.$('diagnostics-status').textContent,/Ctrl\+C/);
});
test('another window preference change is picked up without resetting the current query',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);const count=env.requests.filter(r=>r.path.startsWith('/api/detail')).length;
  env.server.settings.preferences.language='en';env.server.settings.preferences.theme='light';await env.window.emit('focus');await flush();
  assert.equal(env.$('selected-name').textContent,'Aatrox');assert.equal(env.document.documentElement.dataset.theme,'light');assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,count);
});
const catalog=()=>({data:{heroes:structuredClone(heroes),lanes,version:'test',updatedAt:new Date().toISOString()},cache:{source:'live'}});
function detail(q,name='缓存英雄',needsRefresh=false) {
  return {data:{champion:{championId:Number(q.get('championId'))},mode:q.get('mode'),lane:q.get('lane'),sections:[{key:'all_segment',label:'全分段',items:[{championId:9999,championNameEn:name,championNameCn:'测试称号',fatherValue:2}]}]},cache:{source:needsRefresh?'cache':'live',fetchedAt:'2026-09-20T00:00:00Z',needsRefresh}};
}
class Element {
  append(...children){(this.children??=[]).push(...children);}
  constructor(){this.hidden=false;this.disabled=false;this.value='';this.textContent='';this.innerHTML='';this.dataset={};this.listeners={};this.attributes={};this.classes=new Set();this.classList={add:x=>this.classes.add(x),remove:x=>this.classes.delete(x),toggle:(x,on)=>on?this.classes.add(x):this.classes.delete(x)};}
  addEventListener(name,fn){(this.listeners[name]??=[]).push(fn);}
  async emit(name,event={}){for(const fn of this.listeners[name]||[])await fn(event);}
  setAttribute(k,v){this.attributes[k]=v;}removeAttribute(k){delete this.attributes[k];}
  getAttribute(k){return this.attributes[k]??null;}
  focus(){}select(){}scrollIntoView(){}querySelectorAll(){return [];}showModal(){this.open=true;}close(){this.open=false;}
}
async function flush(){for(let i=0;i<12;i++)await new Promise(setImmediate);}
async function setup(options={}) {
  const elements=new Map([...html.matchAll(/<[^>]*\bid="([^"]+)"[^>]*>/g)].map(m=>{const node=new Element();node.hidden=/\bhidden\b/.test(m[0]);node.disabled=/\bdisabled\b/.test(m[0]);return [m[1],node];}));
  const $=id=>{if(/^suggestion-\d+$/.test(id)&&!elements.has(id))elements.set(id,new Element());assert.ok(elements.has(id),'Unknown DOM id: '+id);return elements.get(id);};
  const workspace=new Element(),document=new Element(),window=new Element();
  document.getElementById=$;document.createElement=()=>new Element();document.body=new Element();document.documentElement=new Element();document.querySelectorAll=()=>[];document.querySelector=selector=>selector==='.workspace'?workspace:selector==='.local-badge'?$('service-status'):null;
  for(const node of elements.values())node.focus=()=>{document.activeElement=node;};
  const appearance=new Element();appearance.matches=!!options.dark;
  window.chrome={webview:new Element()};
  const copied=[];window.matchMedia=()=>appearance;window.navigator={clipboard:{async writeText(value){if(options.clipboardDenied)throw new Error('Denied');copied.push(value);}}};
  const requests=[],pending=[],timers=new Map();let tick=0,nextTimer=0;
  const schedule=(fn,delay,interval=false)=>{const id=++nextTimer;timers.set(id,{fn,time:tick+delay,delay,interval});return id;};
  const server={down:!!options.down,catalog:catalog(),health:{app:'luna-local',offline:false},library:{success:true,complete:true,saved:4,expected:4,profiles:['1-5'],revision:'initial',sourceGeneratedAt:'2026-10-10T22:10:00Z',job:{busy:false,state:'idle',message:'本地查询',done:0,total:0,profile:'1-5'}},detail:(q)=>detail(q,'结果-'+q.get('lane')), ...options.server};
  server.settings??={success:true,preferences:{schema:1,language:'zh-CN',theme:'system',autoUpdate:true,closeAction:'ask'},systemLanguage:'zh-CN',mode:server.health.offline?'offline':'normal',automaticUpdatesThisRun:!server.health.offline,warning:'',version:'1.5.2',desktopWindowAvailable:true};
  const location={hash:options.hash||'',pathname:'/',search:'',reload(){}};
  const entries=[location.hash];let historyIndex=0;
  const history={
    pushState(a,b,url){location.hash=url.startsWith('#')?url:'';entries.splice(++historyIndex,entries.length,location.hash);},
    replaceState(a,b,url){location.hash=url.startsWith('#')?url:'';entries[historyIndex]=location.hash;}
  };
  const fetch=async(path,{signal})=>{
    const entry={path,signal,aborted:false,at:tick};requests.push(entry);
    if(server.down)throw new TypeError('Network unavailable');
    const url=new URL(path,'http://127.0.0.1:17653');
    let result;
    if(url.pathname==='/api/space'||url.pathname==='/api/space/clean')result=server.space||{success:true,busy:false,report:{checkedAt:'2026-10-15T01:00:00Z',totalBytes:100,bytes:{program:100},partial:false}};
    else if(url.pathname==='/api/app-update')result=server.appUpdate||{success:true,state:'noRelease',busy:false,managed:true,offline:server.health.offline};
    else if(['/api/app-update/check','/api/app-update/download','/api/app-update/cancel'].includes(url.pathname)){server.appUpdate={success:true,state:url.pathname.endsWith('/cancel')?'paused':'downloading',busy:!url.pathname.endsWith('/cancel'),newVersion:'1.7.0',managed:true};result=server.appUpdate;}
    else if(url.pathname==='/api/maintenance')result=server.maintenance||{success:true,available:true,path:'D:/isolated/Luna'};
    else if(url.pathname==='/api/maintenance/run')result=server.maintenanceFailure?{success:false,httpStatus:400,message:'maintenance.sourceFolder'}:{success:true};
    else if(url.pathname==='/api/catalog')result=server.catalog;
    else if(url.pathname==='/api/settings')result=server.settingsFailure?{success:false,httpStatus:503,message:'settings.loadFailed'}:structuredClone(server.settings);
    else if(url.pathname==='/api/draft')result=typeof server.draft==='function'?await server.draft():server.draft||draftSnapshot(false,'disabled');
    else if(url.pathname==='/api/draft/connect'){
      result=server.draftConnect?await server.draftConnect():typeof server.draft==='object'?server.draft:draftSnapshot(true,'searching');
      if(!server.draftConnect)server.draft=result;
    }
    else if(url.pathname==='/api/draft/disconnect'){
      result=server.draftDisconnect?await server.draftDisconnect():draftSnapshot(false,'disabled');
      if(!server.draftDisconnect)server.draft=result;
    }
    else if(url.pathname==='/api/settings/save'){
      if(server.saveFailure)result={success:false,httpStatus:400,message:'settings.saveFailed'};
      else{
        const key=url.searchParams.get('key'),value=url.searchParams.get('value');
        server.settings.preferences[key]=['autoUpdate','appAutoCheck','appAutoDownload'].includes(key)?value==='true':value;
        if(key==='appAutoDownload'&&value==='true')server.settings.preferences.appAutoCheck=true;
        if(key==='appAutoCheck'&&value==='false')server.settings.preferences.appAutoDownload=false;
        if(['appAutoDownload','appAutoCheck'].includes(key)&&value==='true'&&server.settings.mode==='normal')server.appUpdate={success:true,state:'checking',busy:true,managed:true};
        result=structuredClone(server.settings);
      }
    }
    else if(url.pathname==='/api/diagnostics')result=server.diagnostics||{success:true,version:'1.5.0',mode:'normal',automaticUpdatesThisRun:true,errors:[]};
    else if(url.pathname==='/api/folders/open')result={success:true};
    else if(url.pathname==='/api/about/document')result={success:true,text:'MIT test document'};
    else if(url.pathname==='/api/health')result=server.health;
    else if(url.pathname==='/api/library')result=typeof server.library==='function'?await server.library(url.searchParams):server.library;
    else if(url.pathname==='/api/library/focus')result=server.focus?server.focus(url.searchParams):{success:true};
    else if(url.pathname==='/api/hero/status')result=server.heroStatus?server.heroStatus(url.searchParams):{success:true,state:'idle',busy:false};
    else if(url.pathname==='/api/hero/update')result=server.heroUpdate?server.heroUpdate(url.searchParams):{success:true,state:'queued',busy:true,message:'已排入优先更新'};
    else if(url.pathname==='/api/library/update'){server.library.job={busy:true,state:'downloading',message:'更新中',done:1,total:4,profile:'1-5'};result={success:true};}
    else if(url.pathname==='/api/library/cancel'){server.library.job={busy:false,state:'cancelled',message:'更新已停止',done:1,total:4,profile:'1-5'};result={success:true};}
    else if(url.pathname==='/api/window/close'){
      if(server.closeFailure)result={success:false,httpStatus:400,error:'settings.saveFailed'};
      else {if(url.searchParams.get('remember')==='true')server.settings.preferences.closeAction=url.searchParams.get('choice');result={success:true};}
    }
    else if(url.pathname==='/api/quit')result={success:true};
    else if(url.pathname==='/api/detail')result=server.detail(url.searchParams,entry);
    else throw new Error('Unexpected endpoint '+path);
    if(result==='pending')result=await new Promise((resolve,reject)=>{
      pending.push({entry,resolve});
      const cancel=()=>{entry.aborted=true;reject(Object.assign(new Error('Aborted'),{name:'AbortError'}));};
      if(signal.aborted)cancel();else signal.addEventListener('abort',cancel,{once:true});
    });
    return {ok:result?.success!==false,status:result?.httpStatus??(result?.success===false?503:200),json:async()=>result};
  };
  Object.assign(window,{LunaCore:C,LunaI18n:require('../web/i18n.js').create()});
  const context={window,document,location,history,localStorage:{getItem:()=>null,setItem(){}},fetch,URLSearchParams,AbortController,TypeError,SyntaxError,Error,console,performance:{now:()=>tick},setTimeout:(f,d)=>schedule(f,d),clearTimeout:id=>timers.delete(id),setInterval:(f,d)=>schedule(f,d,true),clearInterval:id=>timers.delete(id)};
  vm.createContext(context);vm.runInContext(fs.readFileSync(require.resolve('../web/settings.js'),'utf8'),context,{filename:'settings.js'});
  vm.runInContext(fs.readFileSync(require.resolve('../web/draft.js'),'utf8'),context,{filename:'draft.js'});
  vm.runInContext(code,context,{filename:'app.js'});await flush();
  return {$,server,requests,pending,workspace,window,location,document,appearance,copied,
    async choose(id,lane){await $('quick-heroes').emit('click',{target:{closest:()=>({dataset:{hero:String(id)}})}});await flush();if(lane)await this.lane(lane);},
    async lane(lane){await $('lane-buttons').emit('click',{target:{closest:()=>({disabled:false,dataset:{lane}})}});await flush();},
    async search(text){$('hero-search').value=text;await $('search-form').emit('submit',{preventDefault(){}});await flush();},
    async home(){await $('home-link').emit('click',{preventDefault(){}});await flush();},
    async back(){assert.ok(historyIndex>0);location.hash=entries[--historyIndex];await window.emit('popstate');await flush();},
    async forward(){assert.ok(historyIndex<entries.length-1);location.hash=entries[++historyIndex];await window.emit('popstate');await flush();},
    async suggest(text){$('hero-search').value=text;await $('hero-search').emit('input');await this.advance(70);await $('suggestions').emit('click',{target:{closest:()=>({dataset:{index:'0'}})}});await flush();},
    async advance(ms){const end=tick+ms;while(true){const due=[...timers].filter(([,t])=>t.time<=end).sort((a,b)=>a[1].time-b[1].time)[0];if(!due)break;const [id,t]=due;tick=t.time;if(t.interval)t.time+=t.delay;else timers.delete(id);t.fn();await flush();}tick=end;await flush();},
    async close(){await window.emit('pagehide');await flush();}
  };
}
test('home exposes library management before any champion is selected',async t=>{
  const env=await setup();t.after(()=>env.close());
  assert.equal(env.$('welcome').hidden,false);assert.equal(env.$('home-library-button').hidden,false);
  await env.$('home-library-button').emit('click');assert.equal(env.$('library-dialog').open,true);
  assert.ok(env.requests.every(r=>!r.path.startsWith('/api/library/update')));
  assert.doesNotMatch(html.match(/<p[^>]*class="intro"[^>]*>(.*?)<\/p>/s)?.[1]||'',/螃蟹/);
});

test('application maintenance requires confirmation and defaults to keeping user data',async t=>{
  const env=await setup();t.after(()=>env.close());
  await env.$('settings-button').emit('click');await env.$('tab-updates').emit('click');await flush();
  assert.equal(env.$('app-check-toggle').checked,true);assert.equal(env.$('app-download-toggle').checked,false);
  await env.$('app-uninstall').emit('click');
  assert.equal(env.$('maintenance-dialog').open,true);assert.equal(env.$('maintenance-path').textContent,'D:/isolated/Luna');assert.equal(env.$('maintenance-remove-data').checked,false);
  await env.$('maintenance-cancel').emit('click');assert.ok(env.requests.every(r=>!r.path.startsWith('/api/maintenance/run')));
  await env.$('app-uninstall').emit('click');await env.$('maintenance-confirm').emit('click');
  assert.equal(env.requests.filter(r=>r.path==='/api/maintenance/run?action=uninstall&removeData=false').length,1);
});

test('automatic download displays coupled server preferences and progress immediately without a second check request',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  await env.$('settings-button').emit('click');await env.$('tab-updates').emit('click');await flush();
  env.$('app-check-toggle').checked=false;await env.$('app-check-toggle').emit('change');await flush();
  env.$('app-download-toggle').checked=true;await env.$('app-download-toggle').emit('change');await flush();
  assert.equal(env.$('app-check-toggle').checked,true);assert.equal(env.$('app-download-toggle').checked,true);
  assert.match(env.$('app-update-status').textContent,/正在检查程序更新/);
  assert.match(env.$('settings-save-status').textContent,/已开启自动检查和自动下载/);
  assert.ok(env.requests.every(r=>r.path!=='/api/app-update/check'));
  assert.match(env.$('father-body').innerHTML,/结果-top/);
  env.$('app-download-toggle').checked=false;await env.$('app-download-toggle').emit('change');await flush();
  assert.equal(env.$('app-check-toggle').checked,true);assert.match(env.$('settings-save-status').textContent,/保留版本检查/);
  env.$('app-check-toggle').checked=false;await env.$('app-check-toggle').emit('change');await flush();
  assert.equal(env.$('app-check-toggle').checked,false);assert.equal(env.$('app-download-toggle').checked,false);
  assert.match(env.$('settings-save-status').textContent,/已关闭自动检查和自动下载/);
});

test('failed update preference saves restore both switches and never report that a check started',async t=>{
  const env=await setup({server:{saveFailure:true}});t.after(()=>env.close());
  await env.$('settings-button').emit('click');await env.$('tab-updates').emit('click');await flush();
  env.$('app-download-toggle').checked=true;await env.$('app-download-toggle').emit('change');await flush();
  assert.equal(env.$('app-check-toggle').checked,true);assert.equal(env.$('app-download-toggle').checked,false);
  assert.match(env.$('settings-save-status').textContent,/未能保存/);
  assert.ok(env.requests.every(r=>r.path!=='/api/app-update/check'));
});

test('a successful restart shows installation completion without a stale update badge or install button',async t=>{
  const env=await setup({server:{appUpdate:{success:true,state:'installed',busy:false,newVersion:'',managed:true}}});t.after(()=>env.close());
  await env.$('settings-button').emit('click');await env.$('tab-updates').emit('click');await flush();
  assert.match(env.$('app-update-status').textContent,/已成功更新到当前版本/);
  assert.equal(env.$('app-update-badge').hidden,true);assert.equal(env.$('app-apply').hidden,true);assert.equal(env.$('app-download').hidden,true);
  env.$('language-select').value='en';await env.$('language-select').emit('change');await flush();
  assert.match(env.$('app-update-status').textContent,/Successfully updated/);
});

test('offline and manual-only launches explain saved update choices without pretending to start networking',async t=>{
  for(const mode of ['offline','manual']){
    const env=await setup({server:{health:{app:'luna-local',offline:mode==='offline'}}});t.after(()=>env.close());
    env.server.settings.mode=mode;await env.$('settings-button').emit('click');await env.$('tab-updates').emit('click');await flush();
    env.$('app-download-toggle').checked=true;await env.$('app-download-toggle').emit('change');await flush();
    assert.equal(env.$('app-update-policy-note').hidden,false);
    assert.match(env.$('settings-save-status').textContent,/下次普通启动/);
    env.$('language-select').value='en';await env.$('language-select').emit('change');await flush();
    assert.match(env.$('app-update-policy-note').textContent,/next normal launch/);
    assert.ok(env.requests.every(r=>r.path!=='/api/app-update/check'));
  }
});

test('explicit data removal is one-shot and failed maintenance leaves champion lookup available',async t=>{
  const env=await setup({server:{maintenanceFailure:true}});t.after(()=>env.close());await env.choose(266);
  await env.$('app-uninstall').emit('click');env.$('maintenance-remove-data').checked=true;await env.$('maintenance-confirm').emit('click');
  assert.ok(env.requests.some(r=>r.path.endsWith('action=uninstall&removeData=true')));assert.equal(env.$('maintenance-confirm').disabled,false);
  assert.match(env.$('father-body').innerHTML,/结果-top/);await env.$('maintenance-cancel').emit('click');await env.$('app-uninstall').emit('click');
  assert.equal(env.$('maintenance-remove-data').checked,false);await env.$('maintenance-cancel').emit('click');await env.home();await env.search('阿狸');assert.match(env.location.hash,/lane=middle/);
});

test('source protection, offline update status and release notes cannot trigger installation',async t=>{
  const env=await setup({server:{health:{app:'luna-local',offline:true},appUpdate:{success:true,state:'ready',offline:true,managed:false,newVersion:'1.7.0',notes:'<img src=x onerror=alert(1)>'},maintenance:{success:true,available:false,path:'D:/source'}}});t.after(()=>env.close());
  await env.$('settings-button').emit('click');await env.$('tab-updates').emit('click');await flush();
  assert.equal(env.$('app-check').disabled,true);assert.equal(env.$('app-apply').disabled,true);assert.equal(env.$('app-uninstall').disabled,true);
  assert.equal(env.$('app-update-notes').textContent,'<img src=x onerror=alert(1)>');assert.equal(env.$('app-update-notes').innerHTML,'');
  await env.$('app-uninstall').emit('click');assert.notEqual(env.$('maintenance-dialog').open,true);assert.ok(env.requests.every(r=>!r.path.startsWith('/api/maintenance/run')));
});

test('space view shows every measured category and records partial scans without blocking lookup',async t=>{
  const env=await setup({server:{space:{success:true,busy:false,report:{totalBytes:1048576,checkedAt:'2026-10-15T00:00:00Z',partial:true,bytes:{program:524288,browser:524288}}}}});t.after(()=>env.close());
  await env.$('settings-button').emit('click');await env.$('tab-data').emit('click');await flush();
  assert.match(env.$('space-summary').textContent,/1\.00 MiB/);assert.equal(env.$('space-breakdown').children.length,8);
  await env.$('space-clean').emit('click');assert.ok(env.requests.some(r=>r.path==='/api/space/clean'));await env.search('阿狸');assert.match(env.location.hash,/lane=middle/);
});

test('new-version badge opens Application and automatic download remains an explicit preference',async t=>{
  const env=await setup({server:{appUpdate:{success:true,state:'available',newVersion:'1.7.0',managed:true}}});t.after(()=>env.close());
  await env.advance(15001);assert.equal(env.$('app-update-badge').hidden,false);await env.$('settings-button').emit('click');assert.equal(env.$('settings-updates').hidden,false);
  assert.ok(env.requests.every(r=>!r.path.startsWith('/api/app-update/download')));
  env.$('app-download-toggle').checked=true;await env.$('app-download-toggle').emit('change');assert.equal(env.server.settings.preferences.appAutoDownload,true);
});

test('first hero gets a sensible default, then search preserves a supported session lane',async t=>{
  const data=catalog();data.data.heroes[1].lanes={top:5,middle:100};
  const first=await setup({server:{catalog:data}});t.after(()=>first.close());
  await first.search('阿狸');assert.match(first.location.hash,/lane=middle/);
  const env=await setup({server:{catalog:data}});t.after(()=>env.close());
  await env.choose(266);await env.search('狐狸');
  assert.match(env.location.hash,/hero=103&lane=top/);
  assert.match(env.$('father-body').innerHTML,/结果-top/);
  assert.equal(env.$('lane-unavailable').hidden,true);
});

test('unavailable lane waits for an explicit choice without a query, update hint, retry or stale result',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  const before=env.requests.length;await env.search('阿狸');
  assert.match(env.location.hash,/hero=103&lane=top/);
  assert.equal(env.$('lane-unavailable').hidden,false);
  assert.match(env.$('lane-unavailable-title').textContent,/已保留上路.*阿狸暂无/);
  assert.match(env.$('lane-unavailable-copy').textContent,/可查询：中路/);
  assert.equal(env.$('results').hidden,true);assert.equal(env.$('filters').hidden,true);
  assert.equal(env.$('father-body').innerHTML,'');assert.equal(env.$('son-body').innerHTML,'');
  assert.equal(env.$('refresh-button').disabled,true);
  await env.$('refresh-button').emit('click');await env.advance(6000);
  assert.ok(env.requests.slice(before).every(r=>!/^\/api\/(detail|hero\/|library\/focus)/.test(r.path)));
  await env.lane('middle');
  assert.equal(env.$('lane-unavailable').hidden,true);assert.equal(env.$('results').hidden,false);
  assert.match(env.location.hash,/hero=103&lane=middle/);
  assert.match(env.$('father-body').innerHTML,/结果-middle/);
});

test('an unsupported new hero never erases the previous session lane or pending cancellation',async t=>{
  const env=await setup({server:{detail:()=> 'pending'}});t.after(()=>env.close());
  await env.choose(266);await env.lane('jungle');await env.search('阿狸');
  assert.match(env.location.hash,/lane=jungle/);assert.ok(env.pending.every(p=>p.entry.aborted));
  env.server.detail=q=>detail(q,'恢复打野');await env.search('剑魔');
  assert.match(env.location.hash,/lane=jungle/);assert.match(env.$('father-body').innerHTML,/恢复打野/);
  const count=env.requests.filter(r=>r.path.startsWith('/api/detail')).length;
  await env.advance(6000);assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,count);
});

test('suggestions and result cards obey the same lane context as typed searches',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.$('hero-search').value='狐狸';await env.$('hero-search').emit('input');await env.advance(70);
  await env.$('suggestions').emit('click',{target:{closest:()=>({dataset:{index:'0'}})}});await flush();
  assert.match(env.location.hash,/hero=103&lane=top/);assert.equal(env.$('lane-unavailable').hidden,false);
  await env.lane('middle');
  await env.$('results').emit('click',{target:{closest:selector=>selector==='[data-related]'?{dataset:{related:'266'}}:null}});await flush();
  assert.match(env.location.hash,/hero=266&lane=middle/);assert.equal(env.$('lane-unavailable').hidden,false);
});

test('history restores its explicit lane while returning home starts a fresh context',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);await env.lane('jungle');
  await env.choose(103);await env.back();
  assert.match(env.location.hash,/hero=266&lane=jungle/);assert.match(env.$('father-body').innerHTML,/结果-jungle/);
  assert.match(env.$('lane-note').textContent,/恢复分路：打野/);
  await env.forward();assert.match(env.location.hash,/hero=103&lane=middle/);
  await env.back();await env.home();
  assert.equal(env.$('lane-unavailable').hidden,true);await env.search('剑魔');
  assert.match(env.location.hash,/lane=top/);
});

test('home search and home suggestions choose the new champion default after a manual lane',async t=>{
  for(const selection of ['search','suggest']){
    const env=await setup();t.after(()=>env.close());await env.choose(266,'jungle');
    await env.home();assert.equal(env.$('welcome').hidden,false);
    const before=env.requests.length;await env[selection]('狐狸');
    assert.match(env.location.hash,/hero=103&lane=middle/);
    assert.equal(env.$('lane-unavailable').hidden,true);
    assert.match(env.$('lane-note').textContent,/默认分路：中路/);
    assert.match(env.$('father-body').innerHTML,/结果-middle/);
    const details=env.requests.slice(before).filter(r=>r.path.startsWith('/api/detail'));
    assert.equal(details.length,2);assert.ok(details.every(r=>r.path.includes('lane=middle')));
  }
});

test('recent chips start fresh on both home and detail, even for the same champion',async t=>{
  for(const fromHome of [false,true]){
    const env=await setup();t.after(()=>env.close());await env.choose(266,'jungle');
    if(fromHome)await env.home();
    await env.choose(103);assert.match(env.location.hash,/hero=103&lane=middle/);
    assert.equal(env.$('lane-unavailable').hidden,true);
    await env.choose(266,'jungle');await env.choose(266);
    assert.match(env.location.hash,/hero=266&lane=top/);
    assert.match(env.$('lane-note').textContent,/默认分路：上路/);
  }
});

test('explicit lane selection remains the context for detail searches and matchup cards',async t=>{
  const data=catalog();data.data.heroes[1].lanes={jungle:5,middle:100};
  const env=await setup({server:{catalog:data}});t.after(()=>env.close());
  await env.choose(266,'jungle');assert.match(env.$('lane-note').textContent,/已选分路：打野/);
  await env.search('狐狸');assert.match(env.location.hash,/hero=103&lane=jungle/);
  assert.match(env.$('lane-note').textContent,/沿用分路：打野/);
  await env.$('results').emit('click',{target:{closest:s=>s==='[data-related]'?{dataset:{related:'266'}}:null}});await flush();
  assert.match(env.location.hash,/hero=266&lane=jungle/);
  assert.match(env.$('father-body').innerHTML,/结果-jungle/);
});

test('back to home resets context; back and forward to a detail restore its own lane',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266,'jungle');await env.home();await env.search('狐狸');
  await env.back();assert.equal(env.location.hash,'');assert.equal(env.$('welcome').hidden,false);
  await env.back();assert.match(env.location.hash,/hero=266&lane=jungle/);
  await env.forward();assert.equal(env.location.hash,'');
  await env.forward();assert.match(env.location.hash,/hero=103&lane=middle/);
  await env.back();await env.search('剑魔');assert.match(env.location.hash,/hero=266&lane=top/);
});

test('home cancels pending reads and retries; late responses cannot replace the fresh query',async t=>{
  const env=await setup({server:{detail:()=> 'pending'}});t.after(()=>env.close());await env.choose(266,'jungle');
  await env.home();assert.ok(env.pending.every(p=>p.entry.aborted));
  env.server.detail=q=>detail(q,'新的中路结果');await env.search('狐狸');
  for(const p of env.pending)p.resolve(detail(new URL(p.entry.path,'http://local').searchParams,'过期打野结果'));
  await env.advance(6000);
  assert.match(env.$('father-body').innerHTML,/新的中路结果/);assert.doesNotMatch(env.$('father-body').innerHTML,/过期打野结果/);
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')&&r.path.includes('championId=103')).length,2);
});

test('history entries without a valid lane use their own champion default, not the active lane',async t=>{
  for(const query of ['','#lane=unknown']){
    const env=await setup();t.after(()=>env.close());await env.choose(266,'jungle');
    env.location.hash='#hero=103'+(query?'&lane=unknown':'');await env.window.emit('popstate');await flush();
    assert.match(env.location.hash,/hero=103&lane=middle/);
    assert.equal(env.$('lane-unavailable').hidden,true);
    assert.match(env.$('father-body').innerHTML,/结果-middle/);
  }
});

test('empty relationships do not cause a valid retained lane to switch to the default',async t=>{
  const data=catalog();data.data.heroes[1].lanes={top:5,middle:100};
  const env=await setup({server:{catalog:data,detail:q=>{const r=detail(q);r.data.sections=[];return r;}}});t.after(()=>env.close());
  await env.choose(266);await env.search('狐狸');
  assert.match(env.location.hash,/hero=103&lane=top/);
  assert.equal(env.$('lane-unavailable').hidden,true);assert.equal(env.$('father-count').textContent,0);
  assert.match(env.$('father-body').innerHTML,/没有返回此类关系/);
  assert.ok(env.requests.filter(r=>r.path.startsWith('/api/detail')).every(r=>r.path.includes('lane=top')));
});

test('a home restart changes only the lane, keeping selected filters intact',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266,'jungle');
  env.$('threshold').value='2';env.$('sensitivity').value='4';await env.$('threshold').emit('input');await env.advance(300);
  env.$('segment').value='high_segment';await env.$('segment').emit('change');
  await env.home();await env.search('狐狸');
  assert.match(env.location.hash,/hero=103&lane=middle&threshold=2&sensitivity=4/);
  assert.equal(env.$('segment').value,'high_segment');
});

test('direct links preserve a known but unavailable lane and reject an unknown lane',async t=>{
  const env=await setup({hash:'#hero=103&lane=top&threshold=2&sensitivity=4'});t.after(()=>env.close());
  assert.equal(env.$('lane-unavailable').hidden,false);
  assert.ok(env.requests.every(r=>!r.path.startsWith('/api/detail')));
  await env.lane('middle');assert.match(env.location.hash,/threshold=2&sensitivity=4/);
  const other=await setup({hash:'#hero=103&lane=unknown'});t.after(()=>other.close());
  assert.match(other.$('father-body').innerHTML,/结果-middle/);
});

test('a champion without any lanes does not set a fake first-use default',async t=>{
  const data=catalog();data.data.heroes[0].lanes={};
  const env=await setup({server:{catalog:data}});t.after(()=>env.close());
  await env.choose(266);assert.equal(env.$('lane-unavailable').hidden,false);
  assert.ok(env.requests.every(r=>!r.path.startsWith('/api/detail')));
  await env.choose(103);assert.match(env.location.hash,/lane=middle/);
  assert.match(env.$('father-body').innerHTML,/结果-middle/);
});

test('manual hero refresh fetches only the selected scope and reloads when its pair is complete',async t=>{
  let status={state:'idle',busy:false};
  const env=await setup({server:{heroStatus:()=>status,heroUpdate:()=>status={state:'queued',busy:true,message:'正在排队'}}});t.after(()=>env.close());
  await env.choose(266);await env.$('refresh-button').emit('click');await flush();
  assert.equal(env.$('refresh-button').disabled,true);assert.match(env.$('father-body').innerHTML,/结果-top/);
  const calls=env.requests.filter(r=>r.path.startsWith('/api/hero/update'));assert.equal(calls.length,1);
  assert.match(decodeURIComponent(calls[0].path),/key=detail-266-top-father-1-5/);
  assert.ok(env.requests.every(r=>!r.path.startsWith('/api/library/update')));
  env.server.detail=q=>detail(q,'单英雄新数据');status={state:'complete',busy:false,savedAt:'2026-10-11T12:00:00Z',message:'英雄已更新'};
  await env.advance(2000);assert.match(env.$('father-body').innerHTML,/单英雄新数据/);assert.match(env.$('son-body').innerHTML,/单英雄新数据/);
  assert.equal(env.$('refresh-button').disabled,false);
});

test('older status response cannot re-enable a newly queued manual refresh',async t=>{
  const env=await setup({server:{heroStatus:()=> 'pending'}});t.after(()=>env.close());await env.choose(266);
  const old=env.pending.find(p=>p.entry.path.startsWith('/api/hero/status'));assert.ok(old);
  await env.$('refresh-button').emit('click');await flush();assert.equal(env.$('refresh-button').disabled,true);
  old.resolve({state:'idle',busy:false});await flush();assert.equal(env.$('refresh-button').disabled,true);
  env.$('threshold').value='2';env.$('sensitivity').value='5';await env.$('threshold').emit('input');
  assert.equal(env.$('refresh-button').disabled,false);
});

test('failed hero update and late response for another hero leave local results intact',async t=>{
  const env=await setup({server:{heroUpdate:()=> 'pending'}});t.after(()=>env.close());await env.choose(266);
  const click=env.$('refresh-button').emit('click');await flush();await env.choose(103,'middle');
  env.pending.find(p=>p.entry.path.startsWith('/api/hero/update')).resolve({state:'complete',busy:false,savedAt:'test',message:'旧英雄完成'});
  await click;assert.doesNotMatch(env.$('hero-update-status').textContent,/旧英雄完成/);assert.match(env.$('father-body').innerHTML,/结果-middle/);
  env.server.heroUpdate=()=>({success:false,error:'测试网络不可达'});await env.$('refresh-button').emit('click');
  assert.match(env.$('father-body').innerHTML,/结果-middle/);assert.equal(env.$('refresh-button').disabled,false);
});

test('offline query completion never enables a network update button',async t=>{
  const env=await setup({server:{health:{app:'luna-local',offline:true}}});t.after(()=>env.close());await env.choose(266);
  assert.equal(env.$('refresh-button').disabled,true);await env.$('refresh-button').emit('click');
  assert.ok(env.requests.every(r=>!r.path.startsWith('/api/hero/update')));
});

test('switching hero cancels previous requests and ignores their results',async t=>{
  const env=await setup({server:{detail:q=>q.get('championId')==='266'?'pending':detail(q,'阿狸结果')}});t.after(()=>env.close());
  await env.choose(266);assert.equal(env.pending.length,2);await env.choose(103,'middle');
  assert.ok(env.pending.every(p=>p.entry.aborted));assert.equal(env.$('selected-name').textContent,'阿狸');
  assert.match(env.$('father-body').innerHTML,/阿狸结果/);
  for(const p of env.pending)p.resolve(detail(new URL(p.entry.path,'http://local').searchParams,'旧英雄结果'));
  await flush();assert.doesNotMatch(env.$('father-body').innerHTML,/旧英雄结果/);
});
test('catalog refresh preserves an unavailable lane until the user explicitly changes it',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.catalog=catalog();env.server.catalog.data.heroes[0].lanes={middle:100};
  env.server.library.revision+='updated';await env.advance(15000);await flush();
  assert.match(env.location.hash,/lane=top/);assert.equal(env.$('results').hidden,true);
  assert.match(env.$('lane-unavailable-title').textContent,/已保留上路/);
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,2);
  await env.lane('middle');
  assert.match(env.location.hash,/lane=middle/);assert.match(env.$('father-body').innerHTML,/结果-middle/);
  const detailRequests=env.requests.filter(r=>r.path.startsWith('/api/detail'));
  assert.ok(detailRequests.slice(-2).every(r=>r.path.includes('lane=middle')));
  assert.match(env.$('lane-buttons').innerHTML,/data-lane="top"[^>]*disabled/);
});
test('catalog removal clears selection, and a hero without lanes has no stale results',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.catalog=catalog();env.server.catalog.data.heroes[0].lanes={};
  env.server.library.revision+='updated';await env.advance(15000);await flush();
  assert.equal(env.$('lane-unavailable').hidden,false);assert.equal(env.$('results').hidden,true);assert.match(env.$('lane-unavailable-copy').textContent,/暂未提供此英雄/);assert.doesNotMatch(env.$('father-body').innerHTML,/结果-top/);
  env.server.catalog.data.heroes=env.server.catalog.data.heroes.filter(h=>h.id!==266);
  env.server.library.revision+='updated';await env.advance(15000);await flush();
  assert.equal(env.$('results').hidden,true);assert.equal(env.$('welcome').hidden,false);assert.equal(env.location.hash,'');
});
test('service interruption is visible and reconnect recovers the current query',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.down=true;await env.advance(10000);
  assert.equal(env.$('service-status').dataset.state,'disconnected');assert.equal(env.$('connection-notice').hidden,false);
  assert.match(env.$('father-body').innerHTML,/结果-top/);
  env.server.down=false;await env.$('reconnect-button').emit('click');await flush();
  assert.equal(env.$('service-status').dataset.state,'connected');assert.equal(env.$('connection-notice').hidden,true);
});
test('startup connection failure can recover without reloading the page',async t=>{
  const env=await setup({down:true});t.after(()=>env.close());assert.equal(env.$('fatal').hidden,false);
  env.server.down=false;await env.$('reconnect-button').emit('click');await flush();
  assert.equal(env.$('fatal').hidden,true);assert.equal(env.$('hero-search').disabled,false);
});
test('going back or exiting cancels delayed filter work and reports stopped state',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.$('threshold').value='2';await env.$('threshold').emit('input');env.location.hash='';await env.window.emit('popstate');
  const count=env.requests.length;await env.advance(400);assert.equal(env.requests.length,count);assert.equal(env.$('welcome').hidden,false);
  await env.choose(266);env.$('threshold').value='3';await env.$('threshold').emit('input');
  await env.$('exit-button').emit('click');const afterExit=env.requests.length;await env.advance(20000);
  assert.equal(env.requests.length,afterExit);assert.equal(env.$('service-status').dataset.state,'stopped');assert.equal(env.workspace.hidden,true);assert.equal(env.$('exit-screen').hidden,false);
});

test('an old record is displayed immediately without initiating any online update',async t=>{
  const env=await setup({server:{detail:q=>detail(q,'完整本地结果',true)}});t.after(()=>env.close());
  await env.choose(266);
  assert.match(env.$('father-body').innerHTML,/完整本地结果/);
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,2);
  assert.ok(env.requests.every(r=>!r.path.includes('refresh=1')&&!r.path.startsWith('/api/library/update')));
});

test('a pending local priority hint never delays or clears local results',async t=>{
  const env=await setup({server:{focus:()=> 'pending'}});t.after(()=>env.close());
  await env.choose(266,'top');assert.match(env.$('father-body').innerHTML,/结果-top/);assert.match(env.$('son-body').innerHTML,/结果-top/);
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/library/focus')).length,1);
  await env.choose(266);assert.equal(env.requests.filter(r=>r.path.startsWith('/api/library/focus')).length,1);
  await env.advance(1200);assert.equal(env.$('service-status').dataset.state,'connected');assert.equal(env.$('toast').hidden,true);
});

test('a verified hero pair refreshes while the whole-library job is still running',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.library.job={busy:true,automatic:true,state:'focused',message:'优先更新当前英雄',done:0,total:856,profile:'1-5',focusedUpdated:0};
  await env.advance(15000);assert.match(env.$('update-status').textContent,/优先更新/);
  env.server.detail=q=>{const r=detail(q,'英雄优先更新结果');Object.assign(r.cache,{source:'focused',scope:'champion',sourceGeneratedAt:'2026-10-11T01:00:00Z',localUpdatedAt:'2026-10-11T02:00:00Z'});return r;};
  env.server.library.revision+='focused';env.server.library.job.state='downloading';env.server.library.job.focusedUpdated=1;
  await env.advance(2000);assert.match(env.$('father-body').innerHTML,/英雄优先更新结果/);assert.match(env.$('son-body').innerHTML,/本英雄双向数据已更新/);
  assert.match(env.$('father-body').innerHTML,/联网更新的数据/);assert.match(env.$('update-status').textContent,/补齐全库/);
  assert.equal(env.$('toast').hidden,true);
});

test('offline queries never register network-update priorities',async t=>{
  const env=await setup({server:{health:{app:'luna-local',offline:true}}});t.after(()=>env.close());await env.choose(266);
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/library/focus')).length,0);assert.match(env.$('father-body').innerHTML,/结果-top/);
});

test('missing profile is visibly different from a valid empty relation list',async t=>{
  const env=await setup({server:{detail:q=>({data:null,cache:{warning:'本地尚未保存这个筛选组合'}})}});t.after(()=>env.close());
  await env.choose(266);assert.match(env.$('father-body').innerHTML,/本地尚未保存/);assert.equal(env.$('father-count').textContent,'—');
  env.server.detail=q=>{const r=detail(q);r.data.sections=[];return r;};
  await env.choose(103,'middle');assert.match(env.$('father-body').innerHTML,/本地结果已完整读取/);assert.equal(env.$('father-count').textContent,0);
});

test('manual background update and cancellation leave query results usable',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  await env.$('update-library').emit('click');await flush();
  assert.equal(env.$('cancel-update').hidden,false);assert.equal(env.$('update-library').disabled,true);
  assert.match(env.$('father-body').innerHTML,/结果-top/);
  await env.choose(103,'middle');assert.match(env.$('father-body').innerHTML,/结果-middle/);
  await env.$('cancel-update').emit('click');assert.equal(env.$('cancel-update').hidden,true);
  assert.match(env.$('father-body').innerHTML,/结果-middle/);
});

test('failed data update does not clear or block the selected local results',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.library.job={busy:false,state:'error',message:'原站不可用；保留旧库',done:2,total:4,profile:'1-5'};
  await env.advance(15000);assert.match(env.$('father-body').innerHTML,/结果-top/);
  assert.equal(env.$('service-status').dataset.state,'connected');assert.match(env.$('library-message').textContent,/原有数据仍可查询/);
});

test('offline launch disables network data update controls',async t=>{
  const env=await setup({server:{health:{app:'luna-local',offline:true}}});t.after(()=>env.close());
  assert.equal(env.$('update-library').disabled,true);assert.equal(env.$('all-profiles').disabled,true);
  await env.choose(266,'top');assert.match(env.$('father-body').innerHTML,/结果-top/);
});

test('storage status shows measured downloaded bytes without triggering an update',async t=>{
  const env=await setup();t.after(()=>env.close());
  env.server.library.storage={ready:true,downloadedBytes:6605102,freedBytes:3302551,checkedAt:'2026-10-11T06:00:00Z',deferred:false,busy:false};
  await env.advance(15000);
  assert.match(env.$('library-storage').textContent,/已下载数据 6\.61 MB/);
  assert.match(env.$('library-storage').textContent,/本次运行已释放 3\.30 MB/);
  assert.match(env.$('library-storage-note').textContent,/上一份可用备份/);
  assert.match(env.$('library-storage-note').textContent,/不含程序自带文件/);
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/library/update')).length,0);
});

test('deferred space cleanup is explained while queries and updates stay available',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.library.storage={ready:true,downloadedBytes:9900000,freedBytes:0,deferred:true,busy:false,message:'部分文件暂未清理；为保护数据已保留，下次检查再处理。'};
  await env.advance(15000);
  assert.match(env.$('library-storage-note').textContent,/为保护数据已保留/);
  assert.ok(env.$('library-storage-note').classes.has('warning'));
  assert.equal(env.$('update-library').disabled,false);
  assert.match(env.$('father-body').innerHTML,/结果-top/);
});

test('temporary errors recover automatically and only the failed column is retried',async t=>{
  const calls={father:0,son:0};
  const env=await setup({server:{detail:q=>{
    const mode=q.get('mode');calls[mode]++;
    return mode==='father'&&calls.father<3?{success:false,httpStatus:503,message:'暂时繁忙'}:detail(q,mode+'成功');
  }}});t.after(()=>env.close());
  await env.choose(266);
  assert.match(env.$('father-body').innerHTML,/自动重试，第 1 \/ 2/);
  assert.match(env.$('son-body').innerHTML,/son成功/);
  await env.advance(300);assert.match(env.$('father-body').innerHTML,/自动重试，第 2 \/ 2/);
  await env.advance(600);
  assert.deepEqual(calls,{father:3,son:1});
  assert.match(env.$('father-body').innerHTML,/father成功/);
  assert.doesNotMatch(env.$('result-meta').innerHTML,/重试|未能/);
});

test('three hung reads finish below five seconds and never loop indefinitely',async t=>{
  const env=await setup({server:{detail:()=> 'pending'}});t.after(()=>env.close());
  await env.choose(266);await env.advance(4500);
  const reads=env.requests.filter(r=>r.path.startsWith('/api/detail'));
  assert.equal(reads.length,6);assert.ok(reads.every(r=>r.aborted));
  assert.deepEqual(reads.map(r=>r.at),[0,0,1500,1500,3300,3300]);
  for(const mode of ['father','son']) {
    assert.match(env.$(mode+'-body').innerHTML,/自动重试已停止/);
    assert.match(env.$(mode+'-body').innerHTML,/重新查询/);
    assert.doesNotMatch(env.$(mode+'-body').innerHTML,/loading-mark/);
    assert.equal(env.$(mode+'-count').textContent,'—');
  }
  await env.advance(30000);
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,6);
});

test('same-query results survive retrying, terminal failure, and later recovery',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.detail=()=> 'pending';await env.choose(266);
  assert.match(env.$('father-body').innerHTML,/已显示上次成功结果/);
  assert.match(env.$('father-body').innerHTML,/结果-top/);
  await env.advance(4500);
  assert.match(env.$('father-body').innerHTML,/已保留本窗口上次成功读取的同条件结果/);
  assert.match(env.$('father-body').innerHTML,/结果-top/);
  assert.equal(env.$('father-count').textContent,1);
  env.server.detail=q=>detail(q,'新结果');await env.choose(266);
  assert.match(env.$('father-body').innerHTML,/新结果/);
  assert.doesNotMatch(env.$('father-body').innerHTML,/已保留|结果-top|重新查询/);
});

test('cached results never cross champions, lanes or filter profiles, including debounce',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.detail=()=> 'pending';
  await env.$('lane-buttons').emit('click',{target:{closest:()=>({disabled:false,dataset:{lane:'jungle'}})}});await flush();
  assert.doesNotMatch(env.$('father-body').innerHTML,/结果-top/);
  await env.choose(266,'top');assert.match(env.$('father-body').innerHTML,/结果-top/);
  env.$('threshold').value='2';env.$('sensitivity').value='5';await env.$('threshold').emit('input');
  assert.doesNotMatch(env.$('father-body').innerHTML,/结果-top/);
  await env.advance(4800);
  assert.match(env.$('father-body').innerHTML,/自动重试已停止/);
  await env.choose(103,'middle');assert.doesNotMatch(env.$('father-body').innerHTML,/结果-top/);
});

test('switching champions during backoff cancels scheduled retries and ignores old responses',async t=>{
  const env=await setup({server:{detail:q=>q.get('championId')==='266'?{success:false,httpStatus:503,message:'忙'}:detail(q,'阿狸')}});t.after(()=>env.close());
  await env.choose(266);await env.choose(103,'middle');await env.advance(6000);
  assert.equal(env.requests.filter(r=>r.path.includes('championId=266')).length,2);
  assert.match(env.$('father-body').innerHTML,/阿狸/);
  assert.doesNotMatch(env.$('father-body').innerHTML,/重试/);
});

test('missing data, damaged records and non-retryable errors stop immediately',async t=>{
  for(const response of [
    {data:null,cache:{warning:'本地尚未保存',reason:'not_saved'}},
    {data:null,cache:{warning:'本地数据损坏',reason:'damaged'}},
    {success:false,httpStatus:400,message:'查询条件无效'},
    null,'not a payload',{success:true},
    {data:{champion:{championId:999},mode:'father',lane:'top',sections:[]}}
  ]) {
    const env=await setup({server:{detail:()=>response}});t.after(()=>env.close());
    await env.choose(266);await env.advance(6000);
    assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,2);
    assert.doesNotMatch(env.$('father-body').innerHTML,/loading-mark/);
    assert.equal(env.$('father-count').textContent,'—');
  }
});

test('disconnected service is probed once and exact cached results remain available',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  const before=env.requests.length;env.server.down=true;await env.choose(266);
  const requests=env.requests.slice(before);
  assert.equal(requests.filter(r=>r.path==='/api/health').length,1);
  assert.equal(requests.filter(r=>r.path.startsWith('/api/detail')).length,2);
  assert.equal(env.$('service-status').dataset.state,'disconnected');
  assert.match(env.$('father-body').innerHTML,/重新双击/);
  assert.match(env.$('father-body').innerHTML,/结果-top/);
  assert.doesNotMatch(env.$('father-body').innerHTML,/loading-mark/);
  env.server.down=false;env.server.detail=q=>detail(q,'恢复后结果');await env.advance(10000);
  assert.match(env.$('father-body').innerHTML,/恢复后结果/);
  assert.equal(env.$('service-status').dataset.state,'connected');
});

test('connection error with a healthy service retries without a false disconnected warning',async t=>{
  const calls={father:0,son:0};
  const env=await setup({server:{detail:q=>{
    if(++calls[q.get('mode')]===1)throw new TypeError('temporary socket failure');
    return detail(q,'恢复成功');
  }}});t.after(()=>env.close());
  await env.choose(266);await env.advance(300);
  assert.equal(env.$('service-status').dataset.state,'connected');
  assert.match(env.$('father-body').innerHTML,/恢复成功/);
  assert.deepEqual(calls,{father:2,son:2});
});

test('service recovery re-queries even if a library response already restored the green badge',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.down=true;await env.choose(266);
  env.server.down=false;env.server.detail=q=>detail(q,'自动恢复的新结果');
  await env.$('catalog-button').emit('click');await flush();
  assert.equal(env.$('service-status').dataset.state,'connected');
  assert.match(env.$('father-body').innerHTML,/本地服务暂时无法连接/);
  await env.advance(10000);
  assert.match(env.$('father-body').innerHTML,/自动恢复的新结果/);
  assert.doesNotMatch(env.$('father-body').innerHTML,/本地服务暂时无法连接/);
});

test('restoring defaults from a missing profile changes controls and shows the exact default data',async t=>{
  const env=await setup({server:{detail:q=>q.get('thresholdLevel')==='1'?detail(q,'默认数据'):{data:null,cache:{warning:'未下载'}}}});t.after(()=>env.close());
  await env.choose(266);env.$('threshold').value='2';env.$('sensitivity').value='5';await env.$('threshold').emit('input');await env.advance(300);
  assert.match(env.$('father-body').innerHTML,/恢复默认条件查看/);
  await env.$('results').emit('click',{target:{closest:selector=>selector==='[data-defaults]'?{}:null}});await flush();
  assert.equal(env.$('threshold').value,1);assert.equal(env.$('sensitivity').value,5);
  assert.match(env.location.hash,/threshold=1&sensitivity=5/);
  assert.match(env.$('father-body').innerHTML,/默认数据/);
});

test('a valid empty result stays zero with an explicit retention notice when service goes down',async t=>{
  const env=await setup({server:{detail:q=>{const r=detail(q);r.data.sections=[];return r;}}});t.after(()=>env.close());
  await env.choose(266);env.server.down=true;await env.choose(266);
  assert.equal(env.$('father-count').textContent,0);
  assert.match(env.$('father-body').innerHTML,/已保留本窗口/);
  assert.match(env.$('father-body').innerHTML,/没有返回此类关系/);
});

function withSource(q,cache) {const result=detail(q);result.cache=cache;return result;}

test('automatic startup status stays separate from the local query and permits cancellation',async t=>{
  const env=await setup();t.after(()=>env.close());
  env.server.library.automaticUpdates=true;
  env.server.library.job={busy:true,automatic:true,state:'scheduled',message:'启动后约 5 秒自动检查',done:0,total:0,profile:'1-5'};
  await env.advance(15000);await env.choose(266);
  assert.match(env.$('update-status').textContent,/即将在后台检查/);
  assert.match(env.$('father-body').innerHTML,/结果-top/);assert.equal(env.$('cancel-update').hidden,false);
  assert.ok(env.requests.every(r=>!r.path.startsWith('/api/library/update')));
  await env.$('cancel-update').emit('click');assert.match(env.$('update-status').textContent,/本次更新已停止/);
});

test('automatic failure keeps results, their timestamps and the connected state without a toast',async t=>{
  const env=await setup({server:{detail:q=>withSource(q,{source:'library',sourceGeneratedAt:'2026-10-10T22:00:00Z',localUpdatedAt:'2026-10-11T02:00:00Z'})}});t.after(()=>env.close());await env.choose(266);
  const original=env.$('father-body').innerHTML;
  env.server.library.job={busy:true,automatic:true,state:'downloading',message:'下载中',done:5,total:856,profile:'1-5'};await env.advance(15000);
  env.server.library.job={busy:false,automatic:true,state:'error',message:'自动更新未完成，原有离线库仍可查询。',done:5,total:856,profile:'1-5'};await env.advance(2000);
  assert.equal(env.$('father-body').innerHTML,original);assert.match(env.$('update-status').textContent,/继续使用本地数据/);
  assert.equal(env.$('service-status').dataset.state,'connected');assert.equal(env.$('toast').hidden,true);
});

test('complete automatically published library refreshes the selected result; partial progress does not',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  env.server.library.job={busy:true,automatic:true,state:'downloading',message:'更新中',done:5,total:856,profile:'1-5'};
  const before=env.requests.filter(r=>r.path.startsWith('/api/detail')).length;await env.advance(15000);
  assert.equal(env.requests.filter(r=>r.path.startsWith('/api/detail')).length,before);
  env.server.detail=q=>withSource(q,{source:'library',localUpdatedAt:'2026-10-11T03:00:00Z',sourceGeneratedAt:'2026-10-11T02:00:00Z'});
  env.server.library.revision+='published';env.server.library.job={busy:false,automatic:true,state:'complete',message:'已更新',done:856,total:856,profile:'1-5'};await env.advance(2000);
  assert.match(env.$('father-body').innerHTML,/联网更新的数据/);assert.match(env.$('father-body').innerHTML,/2026-10-11T03:00:00Z/);
  assert.match(env.$('update-status').textContent,/数据已更新/);assert.equal(env.$('toast').hidden,true);
});

test('offline and manual-only startup explain their update behavior without hiding local data',async t=>{
  const manual=await setup({server:{library:{success:true,automaticUpdates:false,complete:true,saved:4,expected:4,profiles:['1-5'],revision:'manual',job:{busy:false,state:'idle',message:'手动更新',done:0,total:0,profile:'1-5'}}}});t.after(()=>manual.close());
  assert.match(manual.$('update-status').textContent,/仅手动更新/);assert.equal(manual.$('update-library').disabled,false);
  const offline=await setup({server:{health:{app:'luna-local',offline:true}}});t.after(()=>offline.close());
  assert.match(offline.$('update-status').textContent,/离线模式/);assert.match(offline.$('library-auto-note').textContent,/不会自动或手动联网/);
  await offline.choose(266);assert.match(offline.$('father-body').innerHTML,/结果-top/);
});

test('bundled data shows two visible time rows without attributing publisher retrieval to this computer',async t=>{
  const env=await setup({server:{detail:q=>withSource(q,{source:'bundled',sourceGeneratedAt:'2026-10-10T22:10:41Z',fetchedAt:'2026-10-11T01:02:03Z',localUpdatedAt:'2026-10-11T01:02:03Z'})}});t.after(()=>env.close());
  await env.choose(266);const view=env.$('father-body').innerHTML;
  assert.match(view,/程序自带数据/);assert.match(view,/<dt>数据生成<\/dt>/);assert.match(view,/<dt>本机更新<\/dt><dd>此份随程序提供<\/dd>/);
  assert.match(view,/<time datetime="2026-10-10T22:10:41Z">/);assert.doesNotMatch(view,/2026-10-11T01:02:03Z|备用旧数据/);
});

test('downloaded and fallback directions retain their own source dates and completed-download dates',async t=>{
  const env=await setup({server:{detail:q=>withSource(q,q.get('mode')==='father'?{source:'library',sourceGeneratedAt:'2026-10-10T22:00:00Z',localUpdatedAt:'2026-10-11T02:00:00Z'}:{source:'previous',isFallback:true,sourceGeneratedAt:'2026-10-01T22:00:00Z',localUpdatedAt:'2026-10-02T02:00:00Z'})}});t.after(()=>env.close());
  await env.choose(266);
  assert.match(env.$('father-body').innerHTML,/联网更新的数据/);assert.match(env.$('father-body').innerHTML,/<time datetime="2026-10-11T02:00:00Z">/);
  assert.match(env.$('son-body').innerHTML,/备用旧数据/);assert.match(env.$('son-body').innerHTML,/上一份联网数据/);assert.match(env.$('son-body').innerHTML,/<time datetime="2026-10-02T02:00:00Z">/);
  assert.doesNotMatch(env.$('son-body').innerHTML,/2026-10-11T02:00:00Z/);
});

test('bundled fallback and shipped legacy seed are identified as backups without local download dates',async t=>{
  const env=await setup({server:{detail:q=>withSource(q,{source:q.get('mode')==='father'?'bundled':'legacy',origin:'bundled',isFallback:true,fetchedAt:'2026-09-20T00:00:00Z'})}});t.after(()=>env.close());
  await env.choose(266);
  for(const id of ['father-body','son-body']) {const view=env.$(id).innerHTML;assert.match(view,/备用旧数据/);assert.match(view,/程序自带/);assert.match(view,/此份随程序提供/);assert.match(view,/<dt>数据生成<\/dt><dd>未记录<\/dd>/);assert.doesNotMatch(view,/1970|2026-09-20/);}
});

test('missing or invalid timestamps stay unknown instead of being fabricated from query time',async t=>{
  const env=await setup({server:{detail:q=>withSource(q,{source:'legacy',sourceGeneratedAt:null,localUpdatedAt:'not-a-date',fetchedAt:'2026-09-20T00:00:00Z'})}});t.after(()=>env.close());
  await env.choose(266);const view=env.$('father-body').innerHTML;
  assert.match(view,/旧版查询缓存/);assert.equal((view.match(/<dd>未记录<\/dd>/g)||[]).length,2);assert.doesNotMatch(view,/1970|2026-09-20/);
  env.server.detail=q=>withSource(q,null);await env.choose(266);
  assert.match(env.$('father-body').innerHTML,/本地数据/);assert.equal((env.$('father-body').innerHTML.match(/<dd>未记录<\/dd>/g)||[]).length,2);
});

test('retained results expose backup provenance while recovering and restore the source badge after success',async t=>{
  const saved={source:'library',sourceGeneratedAt:'2026-10-10T22:00:00Z',localUpdatedAt:'2026-10-11T02:00:00Z'};
  const env=await setup({server:{detail:q=>withSource(q,saved)}});t.after(()=>env.close());await env.choose(266);
  env.server.detail=()=>({success:false,httpStatus:503,error:'temporary'});await env.choose(266);await env.advance(5000);
  let view=env.$('father-body').innerHTML;assert.match(view,/备用旧数据/);assert.match(view,/上次成功结果 · 联网更新的数据/);assert.match(view,/<time datetime="2026-10-11T02:00:00Z">/);
  env.server.detail=q=>withSource(q,saved);await env.choose(266);view=env.$('father-body').innerHTML;assert.doesNotMatch(view,/备用旧数据/);assert.match(view,/source-badge downloaded/);
});

test('source version checks are visible but do not relabel data or change its local update time',async t=>{
  const env=await setup();t.after(()=>env.close());
  Object.assign(env.server.library,{source:'library',localUpdatedAt:'2026-10-10T22:00:00Z',sourceCheckedAt:'2026-10-11T01:00:00Z'});
  await env.advance(15000);const saved=env.$('library-local-update').textContent,checked=env.$('library-source-check').textContent;
  assert.match(saved,/本机更新/);assert.match(env.$('library-date').textContent,/联网更新的数据/);assert.match(checked,/检查版本不会改变本机更新时间/);
  env.server.library.sourceCheckedAt='2026-10-11T02:00:00Z';await env.advance(15000);
  assert.equal(env.$('library-local-update').textContent,saved);assert.notEqual(env.$('library-source-check').textContent,checked);
  assert.ok(env.requests.every(r=>!r.path.startsWith('/api/library/update')));
});

test('native close opens one cancellable dialog without changing query context',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266,'jungle');
  const hash=env.location.hash,before=env.requests.filter(x=>x.path.startsWith('/api/detail')).length;
  await env.window.chrome.webview.emit('message',{data:'luna-close-request'});
  assert.equal(env.$('close-dialog').open,true);assert.equal(env.$('close-tray').checked,true);
  env.$('close-remember').checked=true;
  await env.window.chrome.webview.emit('message',{data:'luna-close-request'});assert.equal(env.$('close-remember').checked,true);
  await env.$('close-cancel').emit('click');assert.equal(env.$('close-dialog').open,false);
  assert.equal(env.location.hash,hash);assert.equal(env.requests.filter(x=>x.path.startsWith('/api/detail')).length,before);
  assert.ok(!env.requests.some(x=>x.path.startsWith('/api/window/close')));
});
test('tray choice persists only when requested and settings can reset it',async t=>{
  const env=await setup();t.after(()=>env.close());
  await env.window.chrome.webview.emit('message',{data:'luna-close-request'});env.$('close-remember').checked=true;
  await env.$('close-confirm').emit('click');assert.equal(env.$('close-dialog').open,false);
  assert.equal(env.server.settings.preferences.closeAction,'tray');assert.equal(env.$('close-action-select').value,'tray');
  env.$('close-action-select').value='ask';await env.$('close-action-select').emit('change');assert.equal(env.server.settings.preferences.closeAction,'ask');
  await env.window.chrome.webview.emit('message',{data:'luna-close-request'});env.$('close-exit').checked=true;
  await env.$('close-confirm').emit('click');assert.equal(env.server.settings.preferences.closeAction,'ask');
  assert.ok(env.requests.some(x=>x.path==='/api/window/close?choice=exit&remember=false'));
});
test('failed remember/close keeps the dialog and query available for retry',async t=>{
  const env=await setup({server:{closeFailure:true}});t.after(()=>env.close());await env.choose(266);
  const before=env.$('father-body').innerHTML;
  await env.window.chrome.webview.emit('message',{data:'luna-close-request'});env.$('close-remember').checked=true;
  await env.$('close-confirm').emit('click');assert.equal(env.$('close-dialog').open,true);assert.equal(env.$('close-confirm').disabled,false);
  assert.equal(env.server.settings.preferences.closeAction,'ask');assert.equal(env.$('father-body').innerHTML,before);
  assert.match(env.$('close-status').textContent,/未能|重试/);
  env.server.closeFailure=false;await env.$('close-confirm').emit('click');assert.equal(env.$('close-dialog').open,false);
});
test('browser mode explicitly disables tray preference controls',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.settings.desktopWindowAvailable=false;
  await env.window.emit('focus');await flush();assert.equal(env.$('close-action-select').disabled,true);assert.equal(env.$('close-unavailable').hidden,false);
});

test('an ordinary browser sharing the native server cannot offer tray preferences',async t=>{
  const env=await setup();t.after(()=>env.close());delete env.window.chrome;
  await env.$('settings-button').emit('click');env.server.settings.version='1.5.2-browser-check';await env.window.emit('focus');await flush();
  assert.equal(env.$('close-action-select').disabled,true);assert.equal(env.$('close-unavailable').hidden,false);
});

test('first Up selects the last suggestion, arrows wrap, and Enter confirms the active hero',async t=>{
  const env=await setup();t.after(()=>env.close());
  env.$('hero-search').value='a';
  const key=key=>env.$('hero-search').emit('keydown',{key,preventDefault(){}});
  await key('ArrowUp');assert.equal(env.$('hero-search').getAttribute('aria-activedescendant'),'suggestion-1');
  await key('ArrowDown');assert.equal(env.$('hero-search').getAttribute('aria-activedescendant'),'suggestion-0');
  await key('ArrowUp');await env.$('search-form').emit('submit',{preventDefault(){}});await flush();
  assert.equal(new URLSearchParams(env.location.hash.slice(1)).get('hero'),'266');
});

test('Escape, outside clicks and keyboard focus leaving search cancel pending suggestions',async t=>{
  for(const close of [env=>env.$('hero-search').emit('keydown',{key:'Escape'}),env=>env.document.emit('click',{target:{closest:()=>null}}),env=>env.document.emit('focusin',{target:{closest:()=>null}})]){
    const env=await setup();t.after(()=>env.close());env.$('hero-search').value='a';await env.$('hero-search').emit('input');
    await close(env);await env.advance(70);
    assert.equal(env.$('suggestions').hidden,true);assert.equal(env.$('hero-search').getAttribute('aria-expanded'),'false');
    assert.equal(env.$('hero-search').getAttribute('aria-activedescendant'),null);
  }
});

test('typing replaces the active suggestion immediately and arrows resolve the newest pending text',async t=>{
  const env=await setup();t.after(()=>env.close());env.$('hero-search').value='a';
  await env.$('hero-search').emit('keydown',{key:'ArrowDown',preventDefault(){}});
  env.$('hero-search').value='Ahri';await env.$('hero-search').emit('input');
  assert.equal(env.$('hero-search').getAttribute('aria-activedescendant'),null);
  await env.$('hero-search').emit('keydown',{key:'ArrowDown',preventDefault(){}});
  await env.$('search-form').emit('submit',{preventDefault(){}});await env.advance(70);
  assert.equal(new URLSearchParams(env.location.hash.slice(1)).get('hero'),'103');assert.equal(env.$('suggestions').hidden,true);
});

test('a failed settings read never invents an old version number',async t=>{
  const env=await setup({server:{settingsFailure:true}});t.after(()=>env.close());
  assert.equal(env.$('about-version').textContent,'—');assert.match(env.$('settings-save-status').textContent,/未能读取/);
  env.server.settingsFailure=false;await env.window.emit('focus');await flush();
  assert.equal(env.$('about-version').textContent,env.server.settings.version);
});

test('library coverage is bound to its requested filters across language changes and late replies',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266);
  let release;env.server.library=q=>q.get('thresholdLevel')==='2'?new Promise(resolve=>{release=resolve;}):env.server.library;
  env.$('threshold').value='2';await env.$('threshold').emit('input');
  assert.match(env.$('library-coverage').textContent,/阈值 2.*正在读取/);assert.doesNotMatch(env.$('library-coverage').textContent,/完整离线库/);
  env.$('language-select').value='en';await env.$('language-select').emit('change');await flush();
  assert.match(env.$('library-coverage').textContent,/threshold 2.*Reading library coverage/);assert.doesNotMatch(env.$('library-coverage').textContent,/Complete library/);
  release({success:true,complete:false,saved:0,expected:4,profiles:['1-5'],revision:'initial',job:{busy:false,state:'idle',message:'Local lookup',done:0,total:0,profile:'1-5'}});await flush();
  assert.match(env.$('library-coverage').textContent,/threshold 2.*not yet saved.*0 \/ 4/);
});

test('draft is disabled initially and never discovers, queries or downloads without an explicit action',async t=>{
  const env=await setup();t.after(()=>env.close());
  assert.equal(env.$('draft-connect').hidden,false);assert.equal(env.$('draft-disconnect').hidden,true);
  assert.equal(env.requests.filter(x=>x.path==='/api/draft').length,1);
  await env.advance(5000);assert.equal(env.requests.filter(x=>x.path==='/api/draft').length,1);
  assert.ok(env.requests.every(x=>!/^\/api\/(?:detail|draft\/connect|hero\/update|library\/update)/.test(x.path)));
  for(const side of ['ally','enemy'])for(let slot=0;slot<5;slot++)assert.equal(env.$('draft-'+side+'-'+slot).disabled,true);
});

test('draft connect waits without blocking lookup, then syncs only visible picks into stable slots',async t=>{
  const env=await setup();t.after(()=>env.close());await env.$('draft-connect').emit('click');
  assert.match(env.$('draft-status').textContent,/寻找本机客户端/);
  const button=env.$('draft-ally-0');
  env.server.draft=draftPick(draftPick(draftSnapshot(),'ally',0,266,'hovered','',1),'enemy',0,103,'hovered','',1);
  await env.advance(1000);
  assert.equal(env.$('draft-ally-0'),button);assert.equal(button.disabled,false);assert.match(env.$('draft-ally-0-meta').textContent,/预选.*第 1 轮.*分路未确认/);
  assert.equal(env.$('draft-enemy-0').disabled,true);assert.match(env.$('draft-enemy-0-name').textContent,/尚未选择/);
  assert.equal(env.requests.filter(x=>x.path.startsWith('/api/detail')).length,0);
  env.server.draft.teams.enemy[0].selection='locked';await env.advance(1000);
  assert.equal(env.$('draft-enemy-0').disabled,false);assert.match(env.$('draft-count').textContent,/2 个英雄/);
  assert.ok(env.requests.every(x=>!/^\/api\/(?:hero\/update|library\/update)/.test(x.path)));
});

test('clicking a draft hero performs one local pair query and uses its own default role with saved filters',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(266,'jungle');
  env.$('threshold').value='2';await env.$('threshold').emit('input');await env.advance(300);
  env.$('segment').value='high_segment';await env.$('segment').emit('change');
  env.server.draft=draftPick(draftSnapshot(),'enemy',2,103);await env.$('draft-connect').emit('click');
  const before=env.requests.length;await env.$('draft-enemy-2').emit('click');await flush();
  const reads=env.requests.slice(before).filter(x=>x.path.startsWith('/api/detail'));
  assert.equal(reads.length,2);assert.ok(reads.every(x=>x.path.includes('championId=103')&&x.path.includes('lane=middle')&&x.path.includes('thresholdLevel=2')));
  assert.equal(env.$('segment').value,'high_segment');assert.match(env.$('lane-note').textContent,/分路未确认.*本地常用分路.*中路/);
  assert.equal(env.document.activeElement,env.$('selected-name'));
  assert.ok(env.requests.every(x=>!/^\/api\/(?:hero\/update|library\/update)/.test(x.path)));
});

test('an explicit client role is preserved even when the local hero has no records for it',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',0,103,'locked','top');await env.$('draft-connect').emit('click');
  const before=env.requests.length;await env.$('draft-ally-0').emit('click');await flush();
  assert.equal(env.$('lane-unavailable').hidden,false);assert.match(env.$('lane-note').textContent,/客户端分路.*上路/);
  assert.ok(env.requests.slice(before).every(x=>!x.path.startsWith('/api/detail')));
});

test('unknown heroes and empty slots cannot query an invalid champion or leak raw identity fields',async t=>{
  const env=await setup();t.after(()=>env.close());const data=draftPick(draftSnapshot(),'ally',0,999999);data.nickname='SENSITIVE_NAME';data.credentials='SENSITIVE_TOKEN';data.teams.ally[0].summonerName='SENSITIVE_NAME';
  env.server.draft=data;await env.$('draft-connect').emit('click');const before=env.requests.length;
  await env.$('draft-ally-0').emit('click');await env.$('draft-enemy-0').emit('click');await flush();
  assert.equal(env.$('draft-ally-0').disabled,true);assert.match(env.$('draft-ally-0-name').textContent,/#999999/);
  assert.match(env.$('draft-ally-0').getAttribute('aria-label'),/本地目录尚未收录/);
  assert.ok(env.requests.slice(before).every(x=>!x.path.startsWith('/api/detail')));
  const visible=['draft-status','draft-count','draft-ally-0-name','draft-ally-0-meta'].map(id=>env.$(id).textContent).join(' ')+' '+env.$('draft-ally-0').getAttribute('aria-label');
  assert.doesNotMatch(visible,/SENSITIVE/);
});

test('draft polling preserves focus, supports hover changes and swaps without overwriting an open query',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',0,266,'hovered');await env.$('draft-connect').emit('click');
  await env.choose(103);const selected=env.$('selected-name').textContent,button=env.$('draft-ally-0');button.focus();
  env.server.draft=draftPick(draftPick(draftSnapshot(),'ally',0,103),'ally',1,266,'locked','top',2);await env.advance(1000);
  assert.equal(env.document.activeElement,button);assert.equal(env.$('draft-ally-0'),button);assert.match(env.$('draft-ally-0-name').textContent,/阿狸/);assert.equal(env.$('selected-name').textContent,selected);
  assert.equal(env.requests.filter(x=>x.path.startsWith('/api/detail')).length,2);
});

test('leaving champion select clears every slot and moves focus only if its hero becomes unavailable',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',0,266);await env.$('draft-connect').emit('click');await env.choose(266);
  env.$('draft-ally-0').focus();env.server.draft=draftSnapshot(true,'in-game');await env.advance(1000);
  assert.equal(env.$('draft-ally-0').disabled,true);assert.equal(env.document.activeElement,env.$('draft-disconnect'));
  assert.match(env.$('draft-status').textContent,/已离开英雄选择/);assert.match(env.$('father-body').innerHTML,/结果-top/);
});

test('a disconnected or malformed draft clears stale picks and recovers without clearing local results',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',0,266);await env.$('draft-connect').emit('click');await env.choose(266);
  env.server.draft=async()=>{throw new Error('SENSITIVE_TOKEN');};await env.advance(1000);
  assert.equal(env.$('draft-ally-0').disabled,true);assert.equal(env.$('draft-retry').hidden,false);assert.doesNotMatch(env.$('draft-status').textContent,/SENSITIVE/);
  env.server.draft={enabled:true,state:'champ-select',teams:{ally:[],enemy:[]}};await env.advance(1000);assert.equal(env.$('draft-ally-0').disabled,true);
  env.server.draft=draftPick(draftSnapshot(),'enemy',4,103,'locked','',2);await env.advance(1000);
  assert.equal(env.$('draft-enemy-4').disabled,false);assert.match(env.$('father-body').innerHTML,/结果-top/);
});

test('disconnect invalidates an older status reply, stops polling and keeps the current query',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',0,266);await env.$('draft-connect').emit('click');await env.choose(266);
  let late;env.server.draft=()=>new Promise(resolve=>{late=resolve;});await env.advance(1000);
  await env.$('draft-disconnect').emit('click');late(draftPick(draftSnapshot(),'enemy',0,103));await flush();
  assert.equal(env.$('draft-connect').hidden,false);assert.equal(env.$('draft-enemy-0').disabled,true);assert.match(env.$('father-body').innerHTML,/结果-top/);
  const count=env.requests.filter(x=>x.path==='/api/draft').length;await env.advance(5000);assert.equal(env.requests.filter(x=>x.path==='/api/draft').length,count);
});

test('draft localizes accessible labels and role explanations without another hero query',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',0,103,'locked','',2);await env.$('draft-connect').emit('click');await env.$('draft-ally-0').emit('click');await flush();
  const before=env.requests.filter(x=>x.path.startsWith('/api/detail')).length;
  env.$('language-select').value='en';await env.$('language-select').emit('change');
  assert.match(env.$('draft-ally-0').getAttribute('aria-label'),/Your team.*Ahri.*Locked.*Round 2.*Role unconfirmed/);
  assert.match(env.$('lane-note').textContent,/Role unconfirmed.*most common local role/);
  assert.equal(env.requests.filter(x=>x.path.startsWith('/api/detail')).length,before);
});

test('draft avatar failure falls back once without turning the champion button into a broken query',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',0,266);await env.$('draft-connect').emit('click');
  await env.$('draft-ally-0-icon').emit('error');await env.advance(1000);
  assert.equal(env.$('draft-ally-0-icon').hidden,true);assert.equal(env.$('draft-ally-0-placeholder').hidden,false);assert.equal(env.$('draft-ally-0').disabled,false);
  env.server.draft.teams.ally[0].championId=103;await env.advance(1000);
  assert.equal(env.$('draft-ally-0-icon').hidden,false);assert.equal(env.$('draft-ally-0-icon').getAttribute('src'),'/icons/103.png');
});

test('closing the page cancels draft polling and ignores a pending connection response',async t=>{
  const env=await setup();t.after(()=>env.close());let release;env.server.draftConnect=()=>new Promise(resolve=>{release=resolve;});
  const connecting=env.$('draft-connect').emit('click');await flush();await env.close();release(draftPick(draftSnapshot(),'ally',0,266));await connecting;
  assert.equal(env.$('draft-ally-0').disabled,true);const count=env.requests.filter(x=>x.path==='/api/draft').length;await env.advance(5000);assert.equal(env.requests.filter(x=>x.path==='/api/draft').length,count);
});

test('all ten draft picks are addressed by slot, including reordered rows, and duplicate slots fail closed',async t=>{
  const env=await setup();t.after(()=>env.close());const full=draftSnapshot();
  for(const side of ['ally','enemy'])for(let slot=0;slot<5;slot++)draftPick(full,side,slot,slot%2?103:266,'locked','',Math.floor(slot/2)+1);
  full.teams.ally.reverse();full.teams.enemy.reverse();env.server.draft=full;await env.$('draft-connect').emit('click');
  for(const side of ['ally','enemy'])for(let slot=0;slot<5;slot++){assert.equal(env.$('draft-'+side+'-'+slot).disabled,false);assert.match(env.$('draft-'+side+'-'+slot+'-name').textContent,slot%2?/阿狸/:/亚托克斯/);}
  assert.match(env.$('draft-count').textContent,/10 个英雄/);assert.equal(env.requests.filter(x=>x.path.startsWith('/api/detail')).length,0);
  full.teams.ally[0].slot=full.teams.ally[1].slot;await env.advance(1000);
  for(const side of ['ally','enemy'])for(let slot=0;slot<5;slot++)assert.equal(env.$('draft-'+side+'-'+slot).disabled,true);
  assert.match(env.$('draft-status').textContent,/暂时无法读取/);
});

test('failed stop remains cleared and stops reading until the user retries the disconnect',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',0,266);await env.$('draft-connect').emit('click');
  env.server.draftDisconnect=async()=>{throw new Error('Stop not confirmed');};await env.$('draft-disconnect').emit('click');
  assert.equal(env.$('draft-ally-0').disabled,true);assert.match(env.$('draft-status').textContent,/停止请求尚未确认.*暂停读取/);
  const count=env.requests.filter(x=>x.path==='/api/draft').length;await env.advance(10000);assert.equal(env.requests.filter(x=>x.path==='/api/draft').length,count);
  delete env.server.draftDisconnect;await env.$('draft-retry').emit('click');
  assert.equal(env.$('draft-connect').hidden,false);assert.equal(env.$('draft-disconnect').hidden,true);assert.match(env.$('draft-status').textContent,/尚未连接/);
});

test('a pending connect is deduplicated and connection errors never reveal raw transport details',async t=>{
  const env=await setup();t.after(()=>env.close());let reject;env.server.draftConnect=()=>new Promise((resolve,fail)=>{reject=fail;});
  const pending=env.$('draft-connect').emit('click');await flush();await env.$('draft-connect').emit('click');
  assert.equal(env.requests.filter(x=>x.path==='/api/draft/connect').length,1);assert.equal(env.$('draft-connect').disabled,true);
  reject(new Error('SENSITIVE_PASSWORD_AND_PATH'));await pending;
  assert.match(env.$('draft-status').textContent,/暂时无法读取/);assert.doesNotMatch(env.$('draft-status').textContent,/SENSITIVE/);
  env.server.draft=draftSnapshot(true,'ready');await env.advance(1000);assert.match(env.$('draft-status').textContent,/等待进入英雄选择/);
});

test('only a BP-origin query offers a direct return to the previously selected slot',async t=>{
  const env=await setup();t.after(()=>env.close());await env.choose(103);assert.equal(env.$('draft-return').hidden,true);
  env.server.draft=draftPick(draftSnapshot(),'enemy',3,266);await env.$('draft-connect').emit('click');await env.$('draft-enemy-3').emit('click');await flush();
  assert.equal(env.$('draft-return').hidden,false);const reads=env.requests.filter(x=>x.path.startsWith('/api/detail')).length;
  await env.$('draft-return').emit('click');assert.equal(env.document.activeElement,env.$('draft-enemy-3'));
  assert.equal(env.requests.filter(x=>x.path.startsWith('/api/detail')).length,reads);
  await env.search('Ahri');assert.equal(env.$('draft-return').hidden,true);
  await env.$('draft-enemy-3').emit('click');await env.home();assert.equal(env.$('draft-return').hidden,true);
});

test('returning to an expired BP slot focuses an available connection control without reopening old data',async t=>{
  const env=await setup();t.after(()=>env.close());env.server.draft=draftPick(draftSnapshot(),'ally',4,266);await env.$('draft-connect').emit('click');await env.$('draft-ally-4').emit('click');await flush();
  env.server.draft=draftSnapshot(true,'in-game');await env.advance(1000);await env.$('draft-return').emit('click');
  assert.equal(env.document.activeElement,env.$('draft-disconnect'));assert.match(env.$('father-body').innerHTML,/结果-top/);
  await env.$('draft-disconnect').emit('click');env.$('selected-name').focus();await env.$('draft-return').emit('click');
  assert.equal(env.document.activeElement,env.$('draft-connect'));
});
