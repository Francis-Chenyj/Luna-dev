(function (root, factory) {
  const aliases = typeof module === 'object' && module.exports ? require('./aliases.js') : root.LunaAliases;
  const api = factory(aliases || {entries:{}});
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.LunaCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function (aliases) {
  'use strict';
  const termCache = new WeakMap();
  const normalize = value => String(value ?? '').normalize('NFKC').toLowerCase().replace(/[\s\p{P}\p{S}]/gu, '');
  function terms(hero) {
    if (termCache.has(hero)) return termCache.get(hero);
    const a=aliases.entries[hero.id] || {};
    const values=[hero.name,hero.title,hero.sourceTitle,hero.en,String(hero.id),hero.title+hero.name,hero.name+hero.title,
      a.englishName,a.englishTitle,a.traditionalName,a.traditionalTitle,
      a.englishName&&a.englishTitle?a.englishName+' '+a.englishTitle:'',
      ...(hero.keywords || '').split(/[,，]/),...(a.aliases || [])];
    const prepared=[...new Set(values.map(normalize).filter(Boolean))];
    termCache.set(hero,prepared); return prepared;
  }
  function englishTitle(hero) { return aliases.entries[hero.id]?.englishTitle || ''; }
  function englishName(hero) { return aliases.entries[hero.id]?.englishName || hero.en; }
  // Bound retained payload estimates as well as entry count. Active page results
  // remain displayed even if a payload is too large to retain as a retry backup.
  function createResultCache({maxEntries=96,maxBytes=8*1024*1024,ttlMs=15*60*1000,now=()=>Date.now()}={}) {
    const items=new Map();let bytes=0;
    const remove=key=>{const item=items.get(key);if(item){bytes-=item.bytes;items.delete(key);}};
    const prune=()=>{const time=now();for(const [key,item] of items)if(time-item.at>=ttlMs)remove(key);};
    return {
      get(key){prune();const item=items.get(key);if(!item)return;items.delete(key);item.at=now();items.set(key,item);return item.payload;},
      set(key,payload){prune();remove(key);const size=JSON.stringify(payload).length*2;if(size>maxBytes)return;items.set(key,{payload,bytes:size,at:now()});bytes+=size;while(items.size>maxEntries||bytes>maxBytes)remove(items.keys().next().value);},
      prune,clear(){items.clear();bytes=0;},
      get size(){return items.size;},get bytes(){return bytes;}
    };
  }
  function rank(hero, query) {
    const q = normalize(query); if (!q) return 0;
    const t = terms(hero);
    if (t.includes(q)) return 100;
    if (t.some(s => s.startsWith(q))) return 70;
    if (t.some(s => s.includes(q))) return 40;
    return -1;
  }
  function popularity(hero) { return Math.max(0,...Object.values(hero.lanes || {}).map(Number)); }
  function search(heroes, query, limit = 30) {
    return heroes.map(hero => ({hero,score:rank(hero,query)})).filter(x=>x.score>=0)
      .sort((a,b)=>b.score-a.score || popularity(b.hero)-popularity(a.hero) || a.hero.id-b.hero.id).slice(0,limit).map(x=>x.hero);
  }
  function bestLane(hero, requested) {
    const lanes = Object.keys(hero.lanes || {});
    if (requested && lanes.includes(requested)) return requested;
    return lanes.sort((a,b)=>hero.lanes[b]-hero.lanes[a])[0] || 'top';
  }
  function sections(detail) {
    return Array.isArray(detail?.sections) ? detail.sections.map(section=>({...section,items:Array.isArray(section.items)?section.items:[]})) : [];
  }
  function valueText(item, mode) {
    if (item.displayText) return String(item.displayText);
    const value = Number(item.fatherValue);
    return `${mode === 'son' ? '大儿' : '严父'}值 ${Number.isFinite(value) ? value.toFixed(2) : '—'}`;
  }
  function groupKey(key) { return String(key).replace(/_(father|son)$/, ''); }
  function queryKey(id,lane,threshold,sensitivity) { return [id,lane,threshold,sensitivity].join(':'); }
  function escape(value) { return String(value ?? '').replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch])); }
  return {normalize,rank,search,bestLane,sections,valueText,groupKey,queryKey,escape,popularity,englishName,englishTitle,createResultCache};
});
