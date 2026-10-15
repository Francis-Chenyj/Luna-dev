const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs');
const I=require('../web/i18n.js').create();
test('champion counts use English singular and all translated accessibility attributes are recognized',()=>{
  I.setLanguage('en');assert.equal(I.t('{0} 位',0),'0 champions');assert.equal(I.t('{0} 位',1),'1 champion');assert.equal(I.t('{0} 位',2),'2 champions');
  I.setLanguage('zh-CN');assert.equal(I.t('{0} 位',1),'1 位');
  const html=fs.readFileSync(require.resolve('../web/index.html'),'utf8');
  for(const m of html.matchAll(/\b(data-i18n[\w-]*)=/g))assert.ok(['data-i18n','data-i18n-title','data-i18n-aria-label','data-i18n-placeholder'].includes(m[1]),'Unknown translation attribute '+m[1]);
  assert.match(html,/<button[^>]*id="close-dismiss"[^>]*data-i18n-aria-label="close.cancel"/);
});
test('all static UI messages and explicit app messages have English translations',()=>{
  const html=fs.readFileSync(require.resolve('../web/index.html'),'utf8'),code=fs.readFileSync(require.resolve('../web/app.js'),'utf8');
  // Browsers decode character references in attribute values before i18n sees them.
  const entities={quot:'"',apos:"'",amp:'&',lt:'<',gt:'>',nbsp:'\u00a0'};
  const decode=s=>s.replace(/&(#x[\da-f]+|#\d+|quot|apos|amp|lt|gt|nbsp);/gi,(_,entity)=>entity[0]==='#'?String.fromCodePoint(parseInt(entity.slice(entity[1].toLowerCase()==='x'?2:1),entity[1].toLowerCase()==='x'?16:10)):entities[entity.toLowerCase()]);
  const keys=[...html.matchAll(/data-i18n(?:-title|-aria-label|-placeholder)?="([^"]+)"/g)].map(m=>decode(m[1]));
  for(const match of code.matchAll(/\bt\(("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')/g))keys.push(require('node:vm').runInNewContext(match[1]));
  for(const key of keys)assert.ok(Object.hasOwn(I.translations,key)||Object.hasOwn(I.extra,key),'Missing English: '+key);
});
test('translations preserve interpolation placeholders and are available in both languages',()=>{
  const placeholders=s=>[...new Set(s.match(/\{\d+\}/g)||[])].sort();
  for(const [key,value] of Object.entries(I.translations))assert.deepEqual(placeholders(value),placeholders(key),key);
  for(const [key,pair] of Object.entries(I.extra)){assert.equal(pair.length,2);assert.ok(pair.every(x=>typeof x==='string'&&x.length));assert.deepEqual(placeholders(pair[0]),placeholders(pair[1]),key);}
});
test('English labels never reinterpret relationship values as win rates',()=>{
  I.setLanguage('en');const core=require('../web/core.js');
  assert.equal(I.valueText({fatherValue:2.58},'son',core),'Relationship value 2.58');
  assert.equal(I.valueText({displayText:'严父值 10.75',fatherValue:10.75},'father',core),'Relationship value 10.75');
  assert.equal(I.groupLabel('all_segment'),'All ranks');
});
test('theme text combinations meet WCAG AA normal-text contrast',()=>{
  const css=fs.readFileSync(require.resolve('../web/app.css'),'utf8');
  const luminance=hex=>hex.slice(1).match(/../g).map(x=>parseInt(x,16)/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4).reduce((sum,v,i)=>sum+v*[.2126,.7152,.0722][i],0);
  for(const selector of [':root',':root[data-theme=light]']){
    const block=css.slice(css.indexOf(selector+'{')).split('}')[0],p=Object.fromEntries([...block.matchAll(/--([\w-]+):(#[a-f\d]{6})\b/g)].map(m=>[m[1],m[2]]));
    const pairs=[...['bg','surface','raised','selection'].flatMap(bg=>['text','muted','lime','blue'].map(text=>[text,bg])),['rose','danger-bg'],['mint','success-bg'],['warning','warning-bg'],['primary-ink','lime']];
    for(const [fg,bg] of pairs){const a=luminance(p[fg]),b=luminance(p[bg]),ratio=(Math.max(a,b)+.05)/(Math.min(a,b)+.05);assert.ok(ratio>=4.5,`${selector} ${fg}/${bg}: ${ratio.toFixed(2)}`);}
  }
});
