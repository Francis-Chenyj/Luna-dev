(() => {
  'use strict';
  const I = window.LunaI18n, t = I.t, C = window.LunaCore, $ = id => document.getElementById(id), e = C.escape;
  const state = {heroes:[],lanes:[],hero:null,lane:'',laneOrigin:'default',threshold:1,sensitivity:5,group:'all',seq:0,controller:null,results:{},queryBusy:false,suggestions:[],active:-1,history:[],offline:false,connection:'checking',exited:false,initialized:false};
  const application = new AbortController();
  // Window-local LRU: only validated, exactly matching results; never an error or a guessed profile.
  const recentResults = C.createResultCache({now:()=>performance.now()});
  const resultKey = mode => C.queryKey(state.hero.id,state.lane,state.threshold,state.sensitivity)+':'+mode;
  function remember(key,payload) {
    recentResults.set(key,payload);
  }
  function prepareResults() {
    state.results={};
    for(const mode of ['father','son']) {
      const key=resultKey(mode),saved=recentResults.get(key);
      state.results[mode]={...saved,phase:'loading',retained:!!saved};
    }
  }
  function queryError(message,kind,retryable=false) {return Object.assign(new Error(message),{kind,retryable});}
  function pause(ms,signal) {
    return new Promise((resolve,reject)=>{
      if(signal.aborted){reject(queryError(t("查询已取消"),'cancelled'));return;}
      const abort=()=>{clearTimeout(timer);signal.removeEventListener('abort',abort);reject(queryError(t("查询已取消"),'cancelled'));};
      const timer=setTimeout(()=>{signal.removeEventListener('abort',abort);resolve();},ms);
      signal.addEventListener('abort',abort,{once:true});
    });
  }
  let checkingConnection=false, initializing=false, contacts=0;
  let searchTimer, filterTimer, toastTimer, healthTimer, libraryTimer;
  let libraryRevision=null, libraryBusy=false, libraryChecking=false, lastLibraryData=null, lastHeroData=null;
  let lastFocusKey=null;
  let heroBusy=false,heroChecking=false,heroStatusKey=null,lastHeroCompletion=null,heroRequestEpoch=0;
  const selectedUpdateKey=()=>state.hero&&Object.hasOwn(state.hero.lanes,state.lane)?'detail-'+state.hero.id+'-'+state.lane+'-father-'+state.threshold+'-'+state.sensitivity:null;
  function resetHeroUpdateStatus() {
    const key=selectedUpdateKey();
    if(key!==heroStatusKey){heroStatusKey=key;heroBusy=false;lastHeroCompletion=null;heroRequestEpoch++;}
    $('refresh-button').disabled=state.offline||!key||heroBusy;
    $('refresh-button').textContent=heroBusy?t("正在更新此英雄…"):t("↻ 联网更新此英雄");
    $('hero-update-status').textContent=!key?t("请先选择有数据的分路。"):state.offline?t("离线模式，可继续使用已保存的数据。"):t("仅当前分路与筛选条件，严父和大儿一起更新。");
    $('hero-update-status').classList.remove('warning');
  }
  function showHeroUpdate(data,key,refresh=true) {
    if(state.exited||key!==selectedUpdateKey())return;
    lastHeroData={data,key};heroBusy=!!data.busy;
    $('refresh-button').disabled=state.offline||heroBusy;
    $('refresh-button').textContent=heroBusy?(data.state==='queued'?t("已排入优先更新…"):t("正在更新此英雄…")):t("↻ 联网更新此英雄");
    $('hero-update-status').textContent=I.jobMessage(data,true)||t("仅当前分路与筛选条件，严父和大儿一起更新。");
    $('hero-update-status').classList.toggle('warning',data.state==='error');
    if(refresh&&data.state==='complete'&&data.savedAt&&lastHeroCompletion!==key+data.savedAt){
      lastHeroCompletion=key+data.savedAt;loadResults();
    }
  }
  async function readHeroUpdate() {
    const key=selectedUpdateKey();if(!key||heroChecking||state.exited||state.offline)return;
    heroChecking=true;const epoch=heroRequestEpoch;
    try {const data=await api('/api/hero/status?'+new URLSearchParams({key}),{timeout:1500,trackConnection:false});if(epoch===heroRequestEpoch)showHeroUpdate(data,key);}
    catch {} finally {heroChecking=false;}
  }
  function toast(message) { $('toast').textContent=I.message(message); $('toast').hidden=false; clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('toast').hidden=true,4500); }
  function setConnection(connection) {
    if(state.exited && connection!=='stopped') return;
    state.connection=connection;
    const badge=$('service-status');
    badge.dataset.state=connection;
    badge.innerHTML='<i></i> '+({checking:t("正在连接"),connected:state.offline?t("本地运行 · 离线"):t("本地运行"),disconnected:t("连接中断"),stopped:t("已退出")})[connection];
    $('connection-notice').hidden=connection!=='disconnected';
  }
  async function api(path, {signal,timeout=45000,method='GET',trackConnection=true}={}) {
    const controller = new AbortController();
    const cancel = () => controller.abort();
    const parents=[application.signal,signal].filter(Boolean);
    parents.forEach(parent=>{if(parent.aborted)cancel();parent.addEventListener('abort',cancel,{once:true});});
    const timer=setTimeout(cancel,timeout);
    try {
      const response=await fetch(path,{method,headers:{'X-Luna':'1'},signal:controller.signal,cache:'no-store'});
      const payload=await response.json();
      if(!payload||typeof payload!=='object'||Array.isArray(payload))throw queryError(t("本地数据格式异常，请检查离线库或重新解压程序包。"),'invalid');
      if(trackConnection) { contacts++; setConnection('connected'); }
      if(!response.ok || payload.success===false) throw queryError(I.message(payload.message) || t('本地查询未能完成，请重试。'),'http',[408,429,500,502,503,504].includes(response.status));
      return payload;
    } catch(error) {
      if(parents.some(parent=>parent.aborted)) throw error;
      if(error.name==='AbortError') throw queryError(t("本地读取暂时超时。"),'timeout',true);
      if(error instanceof TypeError) { if(trackConnection)setConnection('disconnected');throw queryError(t("暂时无法连接本地服务。"),'connection',true); }
      if(error instanceof SyntaxError) throw queryError(t("本地数据格式异常，请在数据管理中检查离线库，或重新解压程序包。"),'invalid');
      throw error;
    } finally { clearTimeout(timer); parents.forEach(parent=>parent.removeEventListener('abort',cancel)); }
  }
  async function checkConnection() {
    recentResults.prune();
    if(checkingConnection || state.exited) return;
    checkingConnection=true; const before=contacts, wasDisconnected=state.connection==='disconnected';
    $('reconnect-button').disabled=true;
    try {
      const health=await api('/api/health',{timeout:3000,trackConnection:false});
      if(state.exited) return;
      if(health.app!=='luna-local') throw new Error(t("服务不匹配"));
      state.offline=health.offline; contacts++;setConnection('connected');
      await settings.read();
      if(!state.initialized) await init();
      // Another local request may restore the badge before the heartbeat. Failed
      // query columns still need recovery even when that badge is already green.
      else if(wasDisconnected||Object.values(state.results).some(r=>r?.kind==='service')) { toast('本地服务已恢复连接。');if(state.hero&&!state.queryBusy)loadResults(); }
    } catch { if(!state.exited && contacts===before)setConnection('disconnected'); }
    finally { checkingConnection=false;$('reconnect-button').disabled=false; }
  }
  function cancelQuery() { clearTimeout(filterTimer);state.controller?.abort();state.queryBusy=false;state.seq++; }
  function clearSelection() {
    cancelQuery();clearTimeout(searchTimer);closeSuggestions();state.hero=null;state.lane='';state.laneOrigin='default';state.results={};heroStatusKey=null;heroBusy=false;heroRequestEpoch++;
    document.body.classList.remove('has-selection');
    for(const id of ['selection-panel','filters','results','lane-unavailable'])$(id).hidden=true;
    $('results').classList.remove('updating');$('welcome').hidden=false;$('result-meta').textContent='';
  }
  const heroById = id => state.heroes.find(h=>h.id===Number(id));
  const laneLabel = key => t(({top:t("上路"),jungle:t("打野"),middle:t("中路"),bottom:t("下路"),support:t("辅助")})[key] || key);
  const heroName = h => I.language==='en' ? C.englishName(h)||h.name : h.name;
  const heroTitle = h => I.language==='en' ? C.englishTitle(h)||'' : h.title;
  const validTime = value => typeof value==='string' && value.trim()!=='' && !Number.isNaN(new Date(value).getTime());
  const formatTime = value => validTime(value)?new Date(value).toLocaleString(I.language==='en'?'en-US':'zh-CN',{hour12:false}):t("未记录");
  function provenance(cache={},retained=false) {
    cache=cache||{};
    const bundled=cache.source==='bundled'||(cache.source==='legacy'&&cache.origin==='bundled');
    const origin=({library:t("联网更新的数据"),focused:t("联网更新的数据"),bundled:t("程序自带数据"),previous:t("上一份联网数据"),legacy:bundled?t("程序自带的旧版记录"):t("旧版查询缓存")})[cache.source] || t("本地数据");
    const fallback=retained||cache.isFallback||cache.source==='previous'||cache.source==='legacy';
    return {label:fallback?t("备用旧数据"):origin,kind:fallback?'fallback':bundled?'bundled':['library','focused'].includes(cache.source)?'downloaded':'unknown',
      detail:fallback?(retained?t("上次成功结果 · "):'')+origin:cache.source==='focused'?t("本英雄双向数据已更新 · 其他英雄分别更新"):'',
      localUpdated:bundled?t("此份随程序提供"):formatTime(cache.localUpdatedAt),bundled};
  }
  function sourcePanel(cache={},retained=false) {
    cache=cache||{};
    const info=provenance(cache,retained);
    const time=value=>validTime(value)?`<time datetime="${e(value)}">${e(formatTime(value))}</time>`:t("未记录");
    return `<div class="result-provenance" aria-label="${e(t('数据来源与时间'))}"><div class="source-heading"><span class="source-badge ${info.kind}">${e(info.label)}</span>${info.detail?`<span class="source-detail">${e(info.detail)}</span>`:''}</div><dl class="source-times"><dt>${e(t('数据生成'))}</dt><dd>${time(cache.sourceGeneratedAt)}</dd><dt>${e(t('本机更新'))}</dt><dd>${info.bundled?e(info.localUpdated):time(cache.localUpdatedAt)}</dd></dl>${cache.warning?`<p class="source-warning">${e(I.message(cache.warning))}</p>`:''}</div>`;
  }
  function updateCatalog(payload) {
    if(!Array.isArray(payload.data?.heroes) || !Array.isArray(payload.data?.lanes)) throw new Error(t("本地英雄目录不完整，请重新解压程序。"));
    state.heroes=payload.data.heroes; state.lanes=payload.data.lanes;
    $('hero-count').textContent=state.heroes.length;
    $('data-status').textContent=t("{0} 位英雄 · 查询只读取本地数据",state.heroes.length);
    renderQuick();
  }
  function renderQuick() {
    const ids=[...new Set([...state.history,266,86,103,64,99,222])].slice(0,7);
    $('quick-label').textContent=state.history.length?t("最近 / 常用"):t("快速查询");
    $('quick-heroes').innerHTML=ids.map(id=>heroById(id)).filter(Boolean).map(h=>`<button type="button" class="quick-chip" data-hero="${h.id}"><img src="/icons/${h.id}.png" alt="">${e(heroName(h))}</button>`).join('');
  }
  function closeSuggestions() { $('suggestions').hidden=true; $('hero-search').setAttribute('aria-expanded','false'); $('hero-search').removeAttribute('aria-activedescendant'); state.active=-1; }
  function renderSuggestions() {
    const query=$('hero-search').value;
    state.suggestions=C.search(state.heroes,query,20); state.active=-1;
    $('suggestions').innerHTML=state.suggestions.length?state.suggestions.map((h,i)=>`<button type="button" class="suggestion" role="option" aria-selected="false" id="suggestion-${i}" data-index="${i}"><img src="/icons/${h.id}.png" alt=""><span><strong>${e(heroName(h))} <small>${e(heroTitle(h))} · ${e(C.englishName(h))}</small></strong></span><span class="suggestion-lanes">${e(Object.keys(h.lanes).map(laneLabel).join(' / ') || t("暂无分路数据"))}</span></button>`).join(''):t("<div class=\"suggestion-empty\">没有找到匹配的英雄，试试完整姓名或称号。</div>");
    $('suggestions').hidden=false; $('hero-search').setAttribute('aria-expanded','true');
  }
  function setActive(index) {
    const count=state.suggestions.length; if(!count) return;
    state.active=(index+count)%count;
    $('suggestions').querySelectorAll('.suggestion').forEach((node,i)=>{node.classList.toggle('active',i===state.active);node.setAttribute('aria-selected',String(i===state.active));});
    const node=$(`suggestion-${state.active}`);node?.scrollIntoView({block:'nearest'});
    $('hero-search').setAttribute('aria-activedescendant',`suggestion-${state.active}`);
  }
  function submitSearch(event) {
    event?.preventDefault(); clearTimeout(searchTimer);
    const query=$('hero-search').value;
    if(!C.normalize(query)) { $('search-message').textContent=t("请输入一个英雄的姓名、称号或别名。"); $('hero-search').focus(); return; }
    const matches=C.search(state.heroes,query,30), exact=matches.filter(h=>C.rank(h,query)===100);
    const selected=state.active>=0 && !$('suggestions').hidden?state.suggestions[state.active]:exact.length===1?exact[0]:matches.length===1?matches[0]:null;
    if(selected) { chooseHero(selected); return; }
    $('search-message').textContent=matches.length?t("匹配到多位英雄，请从候选列表中选择。"):t("未找到这个英雄，请检查姓名或称号。");
    renderSuggestions();
  }
  function writeLocation(replace=false) {
    if(!state.hero || state.exited) return;
    const query=new URLSearchParams({hero:state.hero.id,lane:state.lane,threshold:state.threshold,sensitivity:state.sensitivity});
    const hash='#'+query.toString(); if(location.hash!==hash) history[replace?'replaceState':'pushState'](null,'',hash);
  }
  function chooseHero(hero, {source='search',lane}={}) {
    if(!hero || state.exited) return;
    clearTimeout(searchTimer);
    clearTimeout(filterTimer);
    // History restores its saved lane. Detail searches and matchup cards keep
    // the active lane; home searches and recent/quick chips start a new context.
    const historyLane=source==='history'&&state.lanes.some(l=>l.key===lane)?lane:'';
    const retainedLane=state.hero&&(source==='search'||source==='related')?state.lane:'';
    state.lane=historyLane||retainedLane||(Object.keys(hero.lanes).length?C.bestLane(hero):'');
    state.laneOrigin=historyLane?'history':retainedLane?'retained':'default';
    state.hero=hero;
    $('hero-search').value=''; $('search-message').textContent=''; closeSuggestions();
    state.history=[hero.id,...state.history.filter(id=>id!==hero.id)].slice(0,6);
    try { localStorage.setItem('counter-history',JSON.stringify(state.history)); } catch {}
    renderQuick(); renderSelection();
    writeLocation(source==='history');
    loadResults();
  }
  function renderSelection(refresh=true) {
    const h=state.hero; if(!h) return;
    document.body.classList.add('has-selection');
    const available=Object.hasOwn(h.lanes,state.lane);
    $('selection-panel').hidden=false;$('filters').hidden=!available;$('results').hidden=!available;$('welcome').hidden=true;
    $('lane-unavailable').hidden=available;
    if(!available){
      const choices=state.lanes.filter(l=>Object.hasOwn(h.lanes,l.key)).map(l=>laneLabel(l.key));
      $('lane-unavailable-title').textContent=state.lane?t("已保留{0} · {1}暂无该分路数据",laneLabel(state.lane),heroName(h)):t("{0}暂无分路数据",heroName(h));
      $('lane-unavailable-copy').textContent=choices.length?t("可查询：{0}。请点击上方可用分路继续，或搜索其他英雄；不会自动切换分路。",choices.join(I.language==='en'?', ':'、')):t("原站目录暂未提供此英雄的可查询分路。可搜索其他英雄，或在首页“更新离线库”中检查数据。");
    }
    $('selected-icon').src=`/icons/${h.id}.png`;$('selected-title').textContent=heroTitle(h);$('selected-name').textContent=heroName(h);$('selected-en').textContent=I.language==='en'?'':C.englishName(h);
    $('lane-buttons').innerHTML=state.lanes.map(l=>`<button type="button" class="lane-button ${l.key===state.lane?'active':''}" data-lane="${l.key}" aria-pressed="${l.key===state.lane}" ${Object.hasOwn(h.lanes,l.key)?'':'disabled'} title="${Object.hasOwn(h.lanes,l.key)?t("查看{0}数据",laneLabel(l.key)):t("原站当前没有该英雄的此分路数据")}">${e(laneLabel(l.key))}</button>`).join('');
    const laneReason=({default:t("默认分路"),retained:t("沿用分路"),history:t("恢复分路"),manual:t("已选分路")})[state.laneOrigin];
    $('lane-note').textContent=state.lane?t("{0}：{1} · 可点击其他可用分路切换",laneReason,laneLabel(state.lane)):t("原站暂无此英雄的分路数据");
    $('threshold').value=state.threshold;$('threshold-output').value=state.threshold;$('sensitivity').value=state.sensitivity;$('sensitivity-output').value=state.sensitivity;
    if(refresh){resetHeroUpdateStatus();readHeroUpdate();}
  }
  function renderResult(mode) {
    const result=state.results[mode];
    const body=$(`${mode}-body`), count=$(`${mode}-count`);
    if(!result) return;
    const pending=['loading','retrying'].includes(result.phase);
    const retryText=result.phase==='retrying'?t("正在自动重试，第 {0} / 2 次…",result.retry):t("正在读取本地关系…");
    let notice='';
    if(pending)notice=`<span class="loading-mark"></span>${result.retained?t("已显示上次成功结果 · "):''}${retryText}`;
    else if(result.error)notice=`${result.retained?t("已保留本窗口上次成功读取的同条件结果。<br>"):''}${e(I.message(result.error))}${result.kind==='service'?t("<p class=\"recovery-hint\">重新双击“启动Luna.cmd”。服务恢复后，当前窗口会自动重新查询；也可点击顶部“重新连接”。</p>"):`<div class="recovery-actions"><button class="secondary-button" data-retry="1">${e(t('重新查询'))}</button>${result.kind==='invalid'?t("<button class=\"quiet-button\" data-library=\"1\">查看离线库</button>"):''}</div>`}`;
    else if(result.phase==='missing')notice=`${result.retained?t("已保留本窗口上次成功读取的同条件结果。<br>"):''}${e(I.message(result.issue) || t('此筛选组合尚未保存。'))}<div class="recovery-actions"><button class="secondary-button" data-library="1">${e(t('查看离线库'))}</button>${state.threshold!==1||state.sensitivity!==5?t("<button class=\"quiet-button\" data-defaults=\"1\">恢复默认条件查看</button>"):''}</div>`;
    if(!result.data) {
      body.innerHTML=`<div class="state-block ${result.error?'error':''}">${notice}</div>`;
      count.textContent=pending?'…':'—';return;
    }
    const visible=C.sections(result.data).filter(s=>state.group==='all'||C.groupKey(s.key)===state.group);
    count.textContent=visible.reduce((sum,s)=>sum+s.items.length,0);
    body.innerHTML=(notice?`<div class="recovery-notice ${pending?'':'warning'}" role="status">${notice}</div>`:'')+sourcePanel(result.cache,result.retained)+(visible.length?visible.map(section=>`<details class="segment-section ${C.groupKey(section.key)==='all_segment'?'is-all':''}" open><summary>${e(I.language==='en'?I.groupLabel(C.groupKey(section.key)):section.label)}<small>${e(t('{0} 位',section.items.length))}</small></summary>${section.items.length?`<div class="matchup-grid">${section.items.map(item=>card(item,mode)).join('')}</div>`:`<p class="empty-section">${e(I.language==='en'?t('当前条件下没有符合这类关系的英雄。'):section.emptyText || t("当前条件下没有符合这类关系的英雄。"))}</p>`}</details>`).join(''):t("<div class=\"state-block\">本地结果已完整读取；原站在当前分路、阈值和场次条件下没有返回此类关系。</div>"));
  }
  function card(item,mode) {
    const h=heroById(item.championId), name=h?heroName(h):item.championNameEn || item.championNameCn;
    const title=h?heroTitle(h):I.language==='en'?'':item.championNameCn || ''; 
    const tooltip=[title,name,I.metricTooltip(item),item.isConjugate?t("原站标记：共轭关系。不同分段可能出现相反关系。"):'', t("点击查询此英雄")].filter(Boolean).join('\n');
    const id=Number(item.championId);
    return `<button class="matchup-card ${item.isConjugate?'conjugate':''}" data-related="${id}" title="${e(tooltip)}"><img src="/icons/${id}.png" alt="" loading="lazy"><span class="matchup-text"><strong>${e(name)}</strong><small>${e(title)}</small><span class="metric">${e(I.valueText(item,mode,C))}${item.isConjugate?t("<span class=\"conjugate-label\">共轭</span>"):''}</span></span></button>`;
  }
  function renderMeta() {
    if(!state.hero || !Object.hasOwn(state.hero.lanes,state.lane)) {$('result-meta').innerHTML='';return;}
    const values=Object.values(state.results),pending=values.some(r=>['loading','retrying'].includes(r?.phase));
    const warning=values.some(r=>r?.cache?.warning||r?.retained||r?.phase==='missing');
    const errors=values.some(r=>r?.error),retained=values.some(r=>r?.retained),service=values.some(r=>r?.kind==='service');
    const retry=Math.max(0,...values.map(r=>r?.phase==='retrying'?r.retry:0));
    const mixed=values[0]?.cache?.fingerprint&&values[1]?.cache?.fingerprint&&values[0].cache.fingerprint!==values[1].cache.fingerprint;
    let right=pending?(retry?t("正在自动重试 {0} / 2 · 本轮总等待不超过 5 秒",retry):t("正在读取本地结果…")):errors?(service?t("本地服务暂未连接，请重新启动程序"):t("本轮查询已结束，可手动重试")):warning?t("具体数据状态见各侧列表"):t("本地查询 · 保持原站排序 · 点击英雄继续查询");
    if(retained)right=t("已显示可用的同条件结果 · ")+right;
    if(mixed&&!pending)right+=t(" · 两侧数据时间不同，请查看各侧日期");
    $('result-meta').innerHTML=`<span>${e(heroName(state.hero))} · ${e(laneLabel(state.lane)||t("暂无分路"))} · ${e(t('阈值 {0} · 严谨度 {1}',state.threshold,state.sensitivity))}</span><span class="${warning||errors?'warning':''}">${e(right)}</span>`;
  }
  async function loadResults(startedAt=performance.now()) {
    if(!state.hero || state.exited) return;
    clearTimeout(filterTimer); state.controller?.abort();
    state.controller=new AbortController(); const signal=state.controller.signal, seq=++state.seq;
    const hero=state.hero,lane=state.lane,threshold=state.threshold,sensitivity=state.sensitivity;
    const current=()=>seq===state.seq&&!signal.aborted&&!state.exited;
    if(!Object.hasOwn(hero.lanes,lane)) {
      state.results={};state.queryBusy=false;
      for(const mode of ['father','son']){$(`${mode}-body`).innerHTML='';$(`${mode}-count`).textContent='—';}
      $('results').classList.remove('updating');renderMeta();return;
    }
    state.queryBusy=true;prepareResults();$('results').classList.remove('updating');
    renderResult('father');renderResult('son');renderMeta(true);
    // A local priority hint runs independently; results never await it or an external request.
    const focusKey=`detail-${hero.id}-${lane}-father-${threshold}-${sensitivity}`;
    if(!state.offline&&focusKey!==lastFocusKey) {
      lastFocusKey=focusKey;
      api('/api/library/focus?'+new URLSearchParams({key:focusKey}),{method:'POST',timeout:1200,trackConnection:false}).catch(()=>{});
    }
    const requestController=new AbortController(),stop=()=>requestController.abort();
    signal.addEventListener('abort',stop,{once:true});
    let expired=false,probe=null;
    const deadline=setTimeout(()=>{expired=true;stop();},Math.max(0,5000-(performance.now()-startedAt)));
    const probeService=()=>probe ||= (async()=>{
      try {
        const health=await api('/api/health',{signal:requestController.signal,timeout:600,trackConnection:false});
        return health.app==='luna-local';
      } catch(error) {return error.kind==='http'&&error.retryable;}
    })();
    await Promise.allSettled(['father','son'].map(async mode=>{
      const query=new URLSearchParams({championId:hero.id,lane,mode,thresholdLevel:threshold,sensitivity});
      const key=resultKey(mode);
      const validate=payload=>{
        if(Number(payload.data?.champion?.championId)!==hero.id||payload.data?.mode!==mode||payload.data?.lane!==lane||!Array.isArray(payload.data?.sections)) throw queryError(t("本地结果与当前查询条件不一致，请在数据管理中检查离线库。"),'invalid');
      };
      let failure;
      for(let attempt=0;attempt<3;attempt++) {
        try {
          if(expired)throw queryError(t("本轮查询已达到 5 秒上限。"),'timeout');
          const remaining=5000-(performance.now()-startedAt);
          if(remaining<=0){expired=true;throw queryError(t("本轮查询已达到 5 秒上限。"),'timeout');}
          const payload=await api('/api/detail?'+query,{signal:requestController.signal,timeout:Math.min(1200,remaining),trackConnection:false});
          if(!current())return;
          contacts++;setConnection('connected');
          if(!Object.hasOwn(payload,'data'))throw queryError(t("本地结果格式不完整，请检查离线库或重新解压程序包。"),'invalid');
          if(payload.data) {
            validate(payload);remember(key,payload);state.results[mode]={...payload,phase:'ready'};
          } else {
            state.results[mode]={...state.results[mode],phase:'missing',issue:payload.cache?.warning,retained:!!state.results[mode]?.data};
          }
          renderResult(mode);renderMeta();return;
        } catch(error) {
          if(!current())return;
          failure=error;
          if(expired)break;
          if(error.kind==='connection') {
            const available=await probeService();
            if(!current())return;
            if(expired)break;
            if(!available){setConnection('disconnected');failure=queryError(t("本地服务暂时无法连接。"),'service');break;}
            contacts++;setConnection('connected');
          }
          if(!error.retryable||attempt===2)break;
          state.results[mode]={...state.results[mode],phase:'retrying',retry:attempt+1};
          renderResult(mode);renderMeta();
          try {await pause(attempt===0?300:600,requestController.signal);}catch{break;}
        }
      }
      if(!current())return;
      state.results[mode]={...state.results[mode],phase:'failed',error:expired?t("本轮查询已达到 5 秒上限，自动重试已停止。"):failure?.kind==='timeout'?t("本地读取仍未完成，自动重试已停止。"):failure?.message||t("本地查询未能完成。"),kind:failure?.kind,retained:!!state.results[mode]?.data};
      renderResult(mode);renderMeta();
    }));
    clearTimeout(deadline);signal.removeEventListener('abort',stop);
    if(!current()) return;
    state.queryBusy=false;renderMeta();
  }
  function filterChanged() {
    if(!state.hero || state.exited) return;
    state.threshold=Number($('threshold').value);state.sensitivity=Number($('sensitivity').value);
    resetHeroUpdateStatus();
    $('threshold-output').value=state.threshold;$('sensitivity-output').value=state.sensitivity;
    cancelQuery();prepareResults();renderResult('father');renderResult('son');renderMeta(true);
    const startedAt=performance.now();
    filterTimer=setTimeout(()=>{writeLocation();loadResults(startedAt);},300);
  }
  function restoreLocation() {
    const q=new URLSearchParams(location.hash.slice(1)),h=heroById(q.get('hero'));
    if(!h) return false;
    const level=value=>/^[1-5]$/.test(value || '')?Number(value):null;
    state.threshold=level(q.get('threshold')) || 1;state.sensitivity=level(q.get('sensitivity')) || 5;
    chooseHero(h,{source:'history',lane:q.get('lane')});return true;
  }
  $('search-form').addEventListener('submit',submitSearch);
  $('home-link').addEventListener('click',event=>{
    event.preventDefault();if(state.exited)return;
    clearSelection();history.pushState(null,'',location.pathname+location.search);$('hero-search').focus();
  });
  $('hero-search').addEventListener('input',()=>{state.active=-1;$('search-message').textContent='';clearTimeout(searchTimer);searchTimer=setTimeout(renderSuggestions,70);});
  $('hero-search').addEventListener('focus',()=>{if($('hero-search').value.trim()) renderSuggestions();});
  $('hero-search').addEventListener('keydown',event=>{
    if(event.isComposing||event.keyCode===229) return;
    if(event.key==='ArrowDown'||event.key==='ArrowUp') {event.preventDefault();if($('suggestions').hidden) renderSuggestions();setActive(state.active+(event.key==='ArrowDown'?1:-1));}
    if(event.key==='Escape') closeSuggestions();
  });
  $('suggestions').addEventListener('click',event=>{const item=event.target.closest('[data-index]');if(item) chooseHero(state.suggestions[Number(item.dataset.index)]);});
  document.addEventListener('click',event=>{if(!event.target.closest('.search-section')) closeSuggestions();});
  document.addEventListener('keydown',event=>{if(!state.exited&&!document.querySelector('dialog[open]')&&(event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='k'){event.preventDefault();$('hero-search').focus();$('hero-search').select();}});
  $('quick-heroes').addEventListener('click',event=>{const button=event.target.closest('[data-hero]');if(button) chooseHero(heroById(button.dataset.hero),{source:'recent'});});
  $('lane-buttons').addEventListener('click',event=>{const button=event.target.closest('[data-lane]');if(button&&!button.disabled&&state.hero&&Object.hasOwn(state.hero.lanes,button.dataset.lane)&&button.dataset.lane!==state.lane){state.lane=button.dataset.lane;state.laneOrigin='manual';renderSelection();writeLocation();loadResults();}});
  $('results').addEventListener('click',event=>{
    if(event.target.closest('[data-defaults]')) {restoreDefaults();return;}
    if(event.target.closest('[data-library]')) {openLibrary();return;}
    if(event.target.closest('[data-retry]')) {loadResults();return;}
    const button=event.target.closest('[data-related]');if(!button) return;
    const hero=heroById(button.dataset.related);
    if(hero){chooseHero(hero,{source:'related'});$('selection-panel').scrollIntoView({behavior:'smooth',block:'start'});}else toast('本地目录尚未收录此英雄，请在“数据管理”中更新离线库。');
  });
  $('threshold').addEventListener('input',filterChanged);$('sensitivity').addEventListener('input',filterChanged);
  $('segment').addEventListener('change',()=>{state.group=$('segment').value;renderResult('father');renderResult('son');});
  function restoreDefaults() {state.threshold=1;state.sensitivity=5;state.group='all';$('segment').value='all';renderSelection();writeLocation();loadResults();}
  $('reset-button').addEventListener('click',restoreDefaults);
  $('refresh-button').addEventListener('click',async()=>{
    const key=selectedUpdateKey();if(!key||state.offline||heroBusy)return;
    heroBusy=true;resetHeroUpdateStatus();const epoch=++heroRequestEpoch;
    try {
      const data=await api('/api/hero/update?'+new URLSearchParams({key}),{method:'POST',timeout:5000});
      if(epoch===heroRequestEpoch)showHeroUpdate(data,key);await readLibrary();
    } catch(error) {
      if(epoch===heroRequestEpoch&&key===selectedUpdateKey()){heroBusy=false;resetHeroUpdateStatus();$('hero-update-status').textContent=error.message;$('hero-update-status').classList.add('warning');}
    }
  });
  $('help-button').addEventListener('click',()=>$('help-dialog').showModal());$('close-help').addEventListener('click',()=>$('help-dialog').close());
  $('help-dialog').addEventListener('click',event=>{if(event.target===$('help-dialog')){const r=event.target.getBoundingClientRect();if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom) event.target.close();}});
  async function reloadCatalog() {
    try {
      const payload=await api('/api/catalog',{timeout:5000});
      if(state.exited) return;
      cancelQuery();clearTimeout(searchTimer);closeSuggestions();updateCatalog(payload);
      if(state.hero){
        const hero=heroById(state.hero.id);
        if(!hero){clearSelection();history.replaceState(null,'',location.pathname+location.search);toast('当前英雄已不在最新目录中，请重新选择。');}
        else {
          state.hero=hero;
          if(!state.lane&&Object.keys(hero.lanes).length){state.lane=C.bestLane(hero);state.laneOrigin='default';}
          renderSelection();writeLocation(true);loadResults();
        }
      }
    } catch(error) {if(!state.exited)toast(error.message);}
  }
  function renderLibrary(data) {
    const job=data.job;
    const autoEnabled=!state.offline&&data.automaticUpdates!==false;
    let updateLabel=state.offline?t("离线模式 · 仅使用本地数据"):autoEnabled?t("启动后自动更新已开启"):t("本次启动仅手动更新");
    if(!state.offline) {
      if(job.busy)updateLabel=({scheduled:t("即将在后台检查 · 本地可查"),waiting:t("后台等待更新 · 本地可查"),checking:t("后台检查中 · 本地可查"),hero:t("正在联网更新英雄 · 本地可查"),focused:t("优先更新当前与最近英雄 · 本地可查"),verifying:t("后台校验中 · 本地可查")})[job.state] || t("补齐全库 {0} / {1}",job.done,job.total);
      else if(job.state==='error')updateLabel=t("更新未完成 · 继续使用本地数据");
      else if(job.state==='cancelled')updateLabel=t("本次更新已停止 · 本地可查");
      else if(job.state==='complete')updateLabel=job.focusedUpdated>0?t("英雄数据已更新 · 本地可查"):job.done>0?t("数据已更新 · 本地可查"):t("检查完成 · 数据未变化");
    }
    $('update-status').textContent=updateLabel;
    $('update-status').classList.toggle('warning',job.state==='error'&&!state.offline);
    $('library-auto-note').textContent=state.offline
      ? t("离线模式不会自动或手动联网更新。保存的开关在下次普通启动时生效。")
      : autoEnabled?t("普通启动约 1 秒后检查版本，优先更新当前与最近英雄，再续传完整库。英雄两侧一起校验通过就可使用，不必等待全库完成。停止后本次不自动重启。"):t("本次启动已关闭自动更新，可手动检查更新。");
    $('library-coverage').textContent=t("当前条件：阈值 {0} / 严谨度 {1} · {2} · {3} / {4} 条",state.threshold,state.sensitivity,data.complete?t("完整离线库"):t("尚未保存完整库"),data.saved,data.expected);
    const librarySource=provenance(data);
    $('library-date').textContent=data.saved?t("当前优先使用：{0} · 数据生成：{1}",librarySource.label+(librarySource.detail?' · '+librarySource.detail:''),formatTime(data.sourceGeneratedAt)):t("恢复默认条件即可使用随包提供的全英雄离线库。");
    $('library-local-update').textContent=data.saved?t("完整库本机更新：{0}。部分英雄可能已有更近的更新，以结果卡片的来源和时间为准。",librarySource.localUpdated):t("本机更新：当前条件尚未保存完整库。");
    $('library-source-check').textContent=t("上次成功检查原站：{0}。检查版本不会改变本机更新时间。",formatTime(data.sourceCheckedAt));
    $('library-profiles').textContent=t("已完整保存 {0} / 25 种筛选组合：{1}（阈值 / 严谨度）",data.profiles.length,data.profiles.map(p=>p.replace('-',' / ')).join(', '));
    const storage=data.storage;
    const megabytes=bytes=>(Math.max(0,Number(bytes)||0)/1000000).toFixed(2)+' MB';
    $('library-storage').textContent=storage?.ready
      ? t("已下载数据 {0} · 本次运行已释放 {1}{2}",megabytes(storage.downloadedBytes),megabytes(storage.freedBytes),storage.busy?t(" · 正在整理…"):'')
      : t("正在检查已下载数据占用…");
    $('library-storage-note').textContent=storage?.deferred
      ? t('部分文件暂未清理；为保护数据已保留，下次检查再处理。')
      : t("每种条件保留当前库与上一份可用备份；未完成下载 7 天无进展后清理。单英雄缓存最多 64 组 / 8 MiB；窗口查询缓存最多 96 条 / 估算 8 MiB，闲置 15 分钟淘汰。此处统计为上次检查的磁盘占用，不含程序自带文件。");
    $('library-storage-note').classList.toggle('warning',!!storage?.deferred);
    $('library-storage').title=storage?.checkedAt?t("上次空间检查：")+formatTime(storage.checkedAt):'';
    $('library-message').textContent=I.jobMessage(job);
    $('library-message').classList.toggle('warning',job.state==='error');
    $('library-progress').hidden=!job.busy||!job.total;
    $('library-progress').max=job.total||1;$('library-progress').value=job.done;
    $('library-progress-text').textContent=job.busy&&job.total?t("本次进度 {0} / {1} · 正在处理 {2} 条件",job.done,job.total,job.profile.replace('-',' / ')):'';
    $('update-library').disabled=state.offline||job.busy;
    $('cancel-update').hidden=!job.busy;
    $('all-profiles').disabled=state.offline||job.busy;
    $('update-library').textContent=data.complete?t("检查并更新离线库"):t("下载当前条件并更新离线库");
    $('offline-update-note').hidden=!state.offline;
    $('catalog-button').textContent=job.busy?(job.kind==='hero'?t("英雄更新中…"):t("离线库更新中…")):t("数据管理");
  }
  async function readLibrary() {
    if(libraryChecking||state.exited||!state.initialized) return;
    libraryChecking=true;
    try {
      const threshold=state.threshold,sensitivity=state.sensitivity;
      const query=new URLSearchParams({thresholdLevel:threshold,sensitivity});
      const data=await api('/api/library?'+query,{timeout:5000});
      if(state.exited) return;
      if(threshold!==state.threshold||sensitivity!==state.sensitivity) return;
      const job=data.job, wasBusy=libraryBusy;libraryBusy=job.busy;
      lastLibraryData=data;renderLibrary(data);
      if((libraryRevision!==null&&libraryRevision!==data.revision)||(wasBusy&&!job.busy&&job.state==='complete')) await reloadCatalog();
      libraryRevision=data.revision;
      if(wasBusy&&!job.busy&&!job.automatic)toast(I.jobMessage(job));
      await readHeroUpdate();
    } catch(error){if(!state.exited)$('library-message').textContent=error.message;}
    finally {
      libraryChecking=false;clearTimeout(libraryTimer);
      if(!state.exited)libraryTimer=setTimeout(readLibrary,libraryBusy||heroBusy?2000:15000);
    }
  }
  function openLibrary() { $('library-dialog').showModal();readLibrary(); }
  $('catalog-button').addEventListener('click',openLibrary);
  $('home-library-button').addEventListener('click',openLibrary);
  $('update-status').addEventListener('click',openLibrary);
  $('close-library').addEventListener('click',()=>$('library-dialog').close());
  $('update-library').addEventListener('click',async()=>{
    $('update-library').disabled=true;
    try {
      const query=new URLSearchParams({thresholdLevel:state.threshold,sensitivity:state.sensitivity,all:$('all-profiles').checked?'1':'0'});
      await api('/api/library/update?'+query,{method:'POST',timeout:5000});await readLibrary();
    } catch(error) {toast(error.message);$('update-library').disabled=state.offline;}
  });
  $('cancel-update').addEventListener('click',async()=>{
    try {await api('/api/library/cancel',{method:'POST',timeout:5000});await readLibrary();}catch(error){toast(error.message);}
  });
  $('exit-button').addEventListener('click',async()=>{
    try {
      await api('/api/quit',{method:'POST',timeout:5000});
      state.exited=true;clearInterval(healthTimer);clearTimeout(libraryTimer);clearTimeout(searchTimer);clearTimeout(toastTimer);cancelQuery();application.abort();recentResults.clear();
      setConnection('stopped');$('toast').hidden=true;document.querySelector('.workspace').hidden=true;$('exit-screen').hidden=false;
      $('exit-button').disabled=true;$('settings-button').disabled=true;
      for(const dialog of document.querySelectorAll('dialog[open]'))dialog.close();
    } catch(error){toast(error.message);}
  });
  $('reconnect-button').addEventListener('click',checkConnection);
  window.addEventListener('focus',()=>{if(state.initialized)checkConnection();});
  window.addEventListener('popstate',()=>{if(!state.exited&&!restoreLocation())clearSelection();});
  window.addEventListener('pagehide',()=>{cancelQuery();application.abort();clearInterval(healthTimer);clearTimeout(libraryTimer);recentResults.clear();});
  window.addEventListener('pageshow',event=>{if(event.persisted)location.reload();});
  async function init() {
    if(initializing || state.exited) return;
    initializing=true;
    try {
      try {const stored=JSON.parse(localStorage.getItem('counter-history')||'[]');if(Array.isArray(stored))state.history=stored.filter(Number.isInteger).slice(0,6);}catch{}
      await settings.read();
      const [catalog,health]=await Promise.all([api('/api/catalog',{timeout:5000}),api('/api/health',{timeout:3000})]);
      if(state.exited) return;
      if(health.app!=='luna-local')throw new Error(t("当前地址不是Luna服务，请重新启动程序。"));
      state.offline=health.offline;updateCatalog(catalog);
      state.initialized=true;setConnection('connected');$('fatal').hidden=true;
      if(state.offline)toast('离线模式：默认条件已保存全英雄关系，可直接查询。');
      restoreLocation();
      $('hero-search').disabled=false;$('search-button').disabled=false;
      $('hero-search').focus();
      readLibrary();
    } catch(error){if(!state.exited){$('fatal').hidden=false;$('fatal').textContent=error.message;$('data-status').textContent=t("程序初始化失败");}}
    finally {initializing=false;}
  }
  function renderLanguage() {
    setConnection(state.connection);
    if(state.initialized){$('data-status').textContent=t('{0} 位英雄 · 查询只读取本地数据',state.heroes.length);renderQuick();}
    if(state.hero){
      renderSelection(false);
      for(const mode of ['father','son']){
        const body=$(mode+'-body'),collapsed=[...body.querySelectorAll('details')].map(node=>!node.open);
        renderResult(mode);body.querySelectorAll('details').forEach((node,i)=>{if(collapsed[i])node.open=false;});
      }
      renderMeta();
      if(lastHeroData&&lastHeroData.key===selectedUpdateKey())showHeroUpdate(lastHeroData.data,lastHeroData.key,false);else resetHeroUpdateStatus();
    }
    if(!$('suggestions').hidden&&$('hero-search').value){const active=state.active;renderSuggestions();if(active>=0)setActive(active);}
    if(lastLibraryData)renderLibrary(lastLibraryData);
    for(const id of ['toast','fatal','search-message']){const node=$(id);if(node.textContent)node.textContent=I.message(node.textContent);}
  }
  const settings=window.LunaSettings({api,toast,openLibrary,rerender:renderLanguage,isExited:()=>state.exited});
  healthTimer=setInterval(checkConnection,10000);
  init();
})();
