import assert from 'node:assert/strict';
import path from 'node:path';
import {expect} from 'playwright/test';

// Runs in an isolated browser/runtime. All BP data is synthetic, and no client
// connection is made. This complements result-card layout checks with the board.
export async function runDraftAccessibility(page, context, origin, output) {
  const result={passed:false,checks:[],layouts:[],problems:[]};
  const catalog=await page.evaluate(async()=> (await (await fetch('/api/catalog',{headers:{'X-Luna':'1'}})).json()).data.heroes);
  const ids=[136,21,4,59,20,145,31,161,234,421];
  const heroes=ids.map(id=>catalog.find(hero=>hero.id===id));
  assert.ok(heroes.every(Boolean),'Long-name layout fixtures must exist in the bundled catalog');
  const blank=()=>Array.from({length:5},(_,slot)=>({slot,championId:0,selection:'empty',pickOrder:0,lane:''}));
  let snapshot={enabled:false,state:'disabled',revision:1,teams:{ally:blank(),enemy:blank()}};
  const full=()=>({enabled:true,state:'champ-select',revision:2,teams:{
    ally:heroes.slice(0,5).map((hero,slot)=>({slot,championId:hero.id,selection:'locked',pickOrder:slot+1,lane:''})),
    enemy:heroes.slice(5).map((hero,slot)=>({slot,championId:hero.id,selection:'locked',pickOrder:slot+1,lane:''}))}});
  const routeHandler=async route=>{
    const url=new URL(route.request().url());
    if(url.pathname.endsWith('/connect'))snapshot=full();
    if(url.pathname.endsWith('/disconnect'))snapshot={enabled:false,state:'disabled',revision:3,teams:{ally:blank(),enemy:blank()}};
    await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(snapshot)});
  };
  await context.route(origin+'/api/draft**',routeHandler);
  try{
    await page.reload();await expect(page.locator('#hero-search')).toBeEnabled();
    await page.locator('#draft-connect').click();await expect(page.locator('#draft-enemy-4')).toBeEnabled();
    await page.locator('#draft-ally-0').press('Enter');await expect(page.locator('.matchup-card').first()).toBeVisible();
    await page.locator('#draft-enemy-4').press('Enter');await expect(page.locator('#selected-name')).toBeFocused();
    await expect(page.locator('.matchup-card').first()).toBeVisible();
    result.selection=await page.evaluate(()=>({focus:document.activeElement.id,scrollY,boardTop:document.querySelector('#draft-panel').getBoundingClientRect().top,boardBottom:document.querySelector('#draft-panel').getBoundingClientRect().bottom,visibleSlots:[...document.querySelectorAll('[data-draft-slot]')].filter(el=>{const r=el.getBoundingClientRect();return r.bottom>0&&r.top<innerHeight;}).length,viewport:innerHeight}));
    const returnButton=page.locator('#draft-return');
    if(await returnButton.count()){
      await expect(returnButton).toBeVisible();await returnButton.click();
      await expect(page.locator('#draft-enemy-4')).toBeFocused();
      const visible=await page.locator('#draft-enemy-4').evaluate(el=>{const r=el.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight;});
      assert.ok(visible,'Returning to BP must bring the previously selected slot into view');
      result.checks.push('A query has a direct keyboard-accessible return to its BP slot');
    }else result.problems.push('A BP-origin query has no direct return control for the draft board');
    await expect(page.locator('#toast')).toBeHidden({timeout:6000});
    const inspect=()=>{
      const problems=[];
      if(document.documentElement.scrollWidth>innerWidth+2)problems.push('Page overflows horizontally');
      for(const button of document.querySelectorAll('[data-draft-slot]')){
        const outer=button.getBoundingClientRect();
        if(innerWidth<=480&&outer.width<65)problems.push(button.id+': narrow cards split readable names into fragments');
        if(button.scrollWidth>button.clientWidth+1)problems.push(button.id+': content overflows');
        for(const element of button.querySelectorAll('img,strong,small')){
          if(element.hidden)continue;const r=element.getBoundingClientRect();
          if(r.left<outer.left-1||r.right>outer.right+1||r.bottom>outer.bottom+1)problems.push(button.id+': portrait or text leaves its button');
        }
      }
      for(const id of ['draft-status','draft-count','draft-title']){const el=document.getElementById(id);if(el.scrollWidth>el.clientWidth+1)problems.push(id+': text overflows');}
      return {width:innerWidth,dpr:devicePixelRatio,language:document.documentElement.lang,theme:document.documentElement.dataset.theme,problems};
    };
    for(const language of ['zh-CN','en'])for(const theme of ['light','dark']){
      await page.locator('#settings-button').click();await page.locator('#tab-general').click();
      await page.locator('#language-select').selectOption(language);await expect(page.locator('html')).toHaveAttribute('lang',language);
      await page.locator('#theme-select').selectOption(theme);await expect(page.locator('html')).toHaveAttribute('data-theme',theme);
      await page.locator('#close-settings').click();
      for(const width of [300,320,360,720,1440]){
        await page.setViewportSize({width,height:1000});const layout=await page.evaluate(inspect);result.layouts.push(layout);
        if(width===300&&theme==='light')await page.locator('#draft-panel').screenshot({path:path.join(output,'draft-'+language+'-300.png')});
      }
    }
    // Page zoom changes the effective CSS viewport. This verifies the 720x500
    // CSS-pixel / DPR 2 layout of a 1440x1000 viewport at 200%, without claiming
    // to automate the browser's own zoom menu or Windows display scaling.
    const session=await context.newCDPSession(page);
    await session.send('Emulation.setDeviceMetricsOverride',{width:720,height:500,deviceScaleFactor:2,mobile:false,screenWidth:1440,screenHeight:1000});
    const zoom=await page.evaluate(inspect);zoom.scenario='200% equivalent CSS viewport';result.layouts.push(zoom);
    await page.locator('#draft-panel').screenshot({path:path.join(output,'draft-200-percent-equivalent.png')});
    await session.send('Emulation.clearDeviceMetricsOverride');await session.detach();
    result.problems.push(...result.layouts.flatMap(row=>row.problems.map(problem=>`${row.language}/${row.theme}/${row.width}: ${problem}`)));
    snapshot={enabled:true,state:'unavailable',revision:4,teams:{ally:blank(),enemy:blank()}};
    await expect(page.locator('#draft-enemy-4')).toBeDisabled();await page.setViewportSize({width:300,height:700});
    await page.locator('#draft-panel').screenshot({path:path.join(output,'draft-disconnected-300.png')});
    result.checks.push('Twenty long-name BP language/theme/viewport combinations and 200% equivalent reflow');
    result.passed=result.problems.length===0;
    return result;
  }finally{await context.unroute(origin+'/api/draft**',routeHandler);}
}
