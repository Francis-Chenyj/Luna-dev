const test = require('node:test');
const assert = require('node:assert/strict');
const C = require('../web/core.js');
const catalog = require('../data/catalog.json').data;
const father = require('../data/seeds/detail-266-top-father-1-5.json').data;
const son = require('../data/seeds/detail-266-top-son-1-5.json').data;
const aliases = require('../data/search-aliases.json');

test('173 位英雄的官方英文姓名、英文称号、繁体译名和整理的搜索词均可离线精确匹配',()=>{
  assert.equal(Object.keys(aliases.entries).length,catalog.heroes.length);
  let checks=0;
  for(const h of catalog.heroes){
    const a=aliases.entries[h.id];assert.ok(a);
    for(const q of [a.englishName,a.englishTitle,a.traditionalName,a.traditionalTitle,...a.aliases]){
      assert.equal(C.rank(h,q),100,h.id+' '+q);
      assert.ok(C.search(catalog.heroes,q,173).some(x=>x.id===h.id),h.id+' '+q);checks++;
    }
  }
  assert.ok(checks>=1500);
  for(const [q,id] of [['Wukong',62],['Nunu & Willump',20],["Kai'Sa",145],["K’Sante",897],['the Blind Monk',64],['刀妹',39],['挖掘机',421],['熬夜波比',711],['华为',910],['铁女',526],['小火龙',901]])
    assert.equal(C.search(catalog.heroes,q)[0].id,id,q);
});
test('有歧义的外号保留多个精确候选，不自动吞掉另一个英雄',()=>{
  for(const [q,ids] of [['球女',[61,134]],['卡萨',[38,145]],['鸟人',[133,497]],['羊',[203,516]]]){
    const found=C.search(catalog.heroes,q).filter(h=>C.rank(h,q)===100).map(h=>h.id);
    for(const id of ids)assert.ok(found.includes(id),q+' '+id);
  }
});
test('窗口缓存同时遵守条数、估算字节数和闲置时间上限',()=>{
  let now=0;const cache=C.createResultCache({maxEntries:2,maxBytes:100,ttlMs:1000,now:()=>now});
  cache.set('a',{v:'one'});cache.set('b',{v:'two'});cache.get('a');cache.set('c',{v:'three'});
  assert.equal(cache.get('b'),undefined);assert.deepEqual(cache.get('a'),{v:'one'});assert.ok(cache.bytes<=100);
  cache.set('huge',{v:'x'.repeat(100)});assert.equal(cache.get('huge'),undefined);assert.ok(cache.bytes<=100);
  now=1001;cache.prune();assert.equal(cache.size,0);assert.equal(cache.bytes,0);
  cache.set('x',{v:'ok'});cache.clear();assert.equal(cache.size,0);assert.equal(cache.bytes,0);
});

test('中文姓名、称号、别名、拼音和英文均识别同一英雄', () => {
  for (const input of ['亚托克斯','暗裔剑魔','剑魔','jianmo','ytks','AATROX','  Aatrox  ','266']) assert.equal(C.search(catalog.heroes,input)[0].id,266,input);
  for (const input of ['盖伦','德玛西亚之力','德玛']) assert.equal(C.search(catalog.heroes,input)[0].id,86,input);
  for (const input of ['盲僧','李青','瞎子']) assert.equal(C.search(catalog.heroes,input)[0].id,64,input);
});
test('完整搜索词优先于模糊匹配，且不存在的英雄返回空结果',()=>{
  assert.equal(C.search(catalog.heroes,'易')[0].id,11);
  assert.deepEqual(C.search(catalog.heroes,'完全不存在的英雄abcxyz'),[]);
  assert.ok(C.search(catalog.heroes,'虚空').length>1);
});
test('厄加特的螃蟹与蟹别名唯一精确匹配，并独立于上游英雄目录',()=>{
  const urgot=catalog.heroes.find(h=>h.id===6);
  assert.ok(urgot);
  for(const input of ['螃蟹','蟹','  螃蟹  ','厄加特','无畏战车','Urgot']) {
    const matches=C.search(catalog.heroes,input);
    assert.equal(matches[0].id,6,input);
    assert.deepEqual(matches.filter(h=>C.rank(h,input)===100).map(h=>h.id),[6],input);
  }
  const updatedCatalog=catalog.heroes.map(h=>h.id===6?{...h,keywords:''}:h);
  assert.equal(C.search(updatedCatalog,'螃蟹')[0].id,6);
  assert.equal(C.search(updatedCatalog,'蟹')[0].id,6);
});
test('全库英雄姓名、称号均可找到，英雄 ID 唯一',()=>{
  assert.equal(new Set(catalog.heroes.map(h=>h.id)).size,catalog.heroes.length);
  for(const h of catalog.heroes) {
    for(const q of [h.name,h.title]) assert.ok(C.search(catalog.heroes,q).some(x=>x.id===h.id),`${h.id} ${q}`);
  }
});
test('自动选样本最多的分路，同时保留用户指定的有效分路',()=>{
  const h={lanes:{top:10,middle:500,support:25}};
  assert.equal(C.bestLane(h),'middle');assert.equal(C.bestLane(h,'top'),'top');assert.equal(C.bestLane(h,'bottom'),'middle');
});
test('严格保留原站七类分组、排序、数值、共轭标记，双向结果独立',()=>{
  assert.equal(C.sections(father).length,7);assert.equal(C.sections(son).length,7);
  assert.deepEqual(C.sections(father),father.sections);assert.deepEqual(C.sections(son),son.sections);
  assert.equal(C.sections(father)[0].items[0].championId,19);
  assert.equal(C.sections(son)[0].items[0].championId,8);
  assert.notDeepEqual(C.sections(father),C.sections(son));
  assert.equal(C.sections(father)[1].items[0].isConjugate,true);
});
test('大儿沿用 API fatherValue 字段，不能反转或误显示为胜率',()=>{
  assert.equal(C.valueText({fatherValue:20.53},'son'),'大儿值 20.53');
  assert.equal(C.valueText({fatherValue:19.7},'father'),'严父值 19.70');
  assert.equal(C.valueText({displayText:'原站自定义',fatherValue:5},'son'),'原站自定义');
  assert.equal(C.valueText({},'son'),'大儿值 —');
});
test('模式后缀不会导致大儿分组过滤丢失',()=>{
  assert.equal(C.groupKey('high_mid_segment_father'),'high_mid_segment');
  assert.equal(C.groupKey('all_segment'),'all_segment');
  assert.equal(C.groupKey('low_segment_son'),'low_segment');
});
test('英雄、分路、阈值、样本严谨度都进入缓存身份',()=>{
  const keys = [[266,'top',1,5],[266,'middle',1,5],[86,'top',1,5],[266,'top',2,5],[266,'top',1,4]].map(x=>C.queryKey(...x));
  assert.equal(new Set(keys).size,5);
});
test('页面输出转义源站文本',()=>{
  assert.equal(C.escape('<script>"&\''),'&lt;script&gt;&quot;&amp;&#39;');
});
