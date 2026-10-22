import assert from 'node:assert/strict';
import {expect} from 'playwright/test';

// Exercise the real page against sanitized synthetic BP snapshots. No client
// credentials, account data or writable League endpoints are involved.
export async function runDraftBrowser(page, context, origin, report) {
  const blank = () => Array.from({length:5}, (_,slot) => ({slot,championId:0,selection:'empty',pickOrder:0,lane:''}));
  let snapshot = {enabled:false,state:'disabled',revision:1,teams:{ally:blank(),enemy:blank()}};
  const calls=[];
  await context.route(origin+'/api/draft**', async route => {
    const request=route.request(), path=new URL(request.url()).pathname;
    calls.push({path,method:request.method()});
    if(path.endsWith('/connect')) snapshot={...snapshot,enabled:true,state:'ready',revision:snapshot.revision+1};
    if(path.endsWith('/disconnect')) snapshot={enabled:false,state:'disabled',revision:snapshot.revision+1,teams:{ally:blank(),enemy:blank()}};
    await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(snapshot)});
  });
  await page.reload();
  await expect(page.locator('#draft-connect')).toBeVisible();
  await expect(page.locator('[data-draft-slot]')).toHaveCount(10);
  assert.equal(calls.filter(x=>x.path.endsWith('/connect')).length,0,'The initial page must not enable client access');
  await page.locator('#draft-connect').click();
  await expect(page.locator('#draft-disconnect')).toBeVisible();
  const catalog=await page.evaluate(async()=> (await (await fetch('/api/catalog',{headers:{'X-Luna':'1'}})).json()).data.heroes);
  const heroes=catalog.slice(0,10);
  const queryRequests=[];
  const observe=request=>{const u=new URL(request.url());if(u.pathname==='/api/detail'||u.pathname==='/api/hero/update'||u.pathname==='/api/library/update')queryRequests.push(u);};
  page.on('request',observe);
  snapshot={enabled:true,state:'champ-select',revision:3,teams:{ally:blank(),enemy:blank()}};
  snapshot.teams.ally[0]={slot:0,championId:+heroes[0].id,selection:'hovered',pickOrder:1,lane:''};
  await expect(page.locator('#draft-ally-0')).toBeEnabled();
  await expect(page.locator('#draft-enemy-0')).toBeDisabled();
  await page.locator('#draft-ally-0').focus();
  snapshot={...snapshot,revision:4,teams:{ally:heroes.slice(0,5).map((h,slot)=>({slot,championId:+h.id,selection:'locked',pickOrder:Math.floor(slot/2)+1,lane:''})),enemy:heroes.slice(5).map((h,slot)=>({slot,championId:+h.id,selection:'locked',pickOrder:Math.floor(slot/2)+1,lane:''}))}};
  await expect(page.locator('#draft-enemy-4')).toBeEnabled();
  await expect(page.locator('#draft-ally-0')).toBeFocused();
  assert.equal(queryRequests.length,0,'Synchronizing ten champions must not query or download ten results');
  await page.locator('#draft-ally-0').press('Enter');
  await expect(page.locator('.selected-hero')).toBeVisible();
  await expect.poll(()=>queryRequests.filter(u=>u.pathname==='/api/detail').length).toBeGreaterThan(0);
  assert.ok(queryRequests.filter(u=>u.pathname==='/api/detail').every(u=>u.searchParams.get('championId')===String(heroes[0].id)));
  assert.equal(queryRequests.filter(u=>u.pathname.endsWith('/update')).length,0);
  // A swapped champion updates the current slot; it must not keep the old avatar.
  const first=snapshot.teams.ally[0].championId;
  snapshot.teams.ally[0].championId=snapshot.teams.ally[1].championId;
  snapshot.teams.ally[1].championId=first;snapshot.revision++;
  await expect(page.locator('#draft-ally-0-icon')).toHaveAttribute('src',new RegExp('/icons/'+snapshot.teams.ally[0].championId+'\\.png$'));
  snapshot.teams.enemy[4].championId=9999;snapshot.revision++;
  await expect(page.locator('#draft-enemy-4')).toBeDisabled();
  snapshot={enabled:true,state:'unavailable',revision:6,teams:{ally:blank(),enemy:blank()}};
  await expect(page.locator('#draft-ally-0')).toBeDisabled();
  await expect(page.locator('#draft-enemy-0')).toBeDisabled();
  // The already selected local query remains useful after a client disconnect.
  await expect(page.locator('.matchup-card').first()).toBeVisible();
  await page.locator('#draft-disconnect').click();
  await expect(page.locator('#draft-connect')).toBeVisible();
  assert.ok(calls.filter(x=>/connect$/.test(x.path)).every(x=>x.method==='POST'));
  page.off('request',observe);
  report.checks.push('BP opt-in, ten stable slots, hover/lock, focus preservation, local click-only query, swaps, unknown hero and disconnect');
}
