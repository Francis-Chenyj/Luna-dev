import inspectLayout from './layout-probe.js';

// Real-browser regression, intentionally separate from the fake DOM unit suite.
// Pass a Codex Browser Use tab, or a Playwright adapter exposing the same methods.
// Use an isolated runtime and its exact origin; this changes test preferences.
export async function runBrowserSmoke(tab, expectedOrigin) {
  const url=new URL(await tab.url());
  if(url.hostname!=='127.0.0.1'||url.origin!==expectedOrigin)throw new Error('Select the exact isolated Luna origin');
  const page=tab.playwright,checks=[];
  const observe=()=>page.domSnapshot();
  const check=(condition,label)=>{if(!condition)throw new Error(label);checks.push(label);};
  await page.locator('#home-link').click();await observe();
  await page.locator('#hero-search').fill('Lee Sin');await page.locator('#hero-search').press('Enter');await observe();
  await page.locator('#reset-button').click();await observe();
  await page.locator('.matchup-card').first().waitFor({state:'visible',timeoutMs:5000});
  const options=await page.locator('#segment').evaluate(el=>[...el.options].map(o=>o.value));
  check(options.length===8,'eight scope choices');
  for(const group of options){
    await page.locator('#segment').selectOption(group);await observe();await tab.reload();await observe();
    check(await page.locator('#segment').evaluate(el=>el.value)===group,'reload restores '+group);
  }
  await tab.back();await observe();check(await page.locator('#segment').evaluate(el=>el.value)==='mid_low_segment','back restores scope');
  await tab.forward();await observe();check(await page.locator('#segment').evaluate(el=>el.value)==='low_segment','forward restores scope');
  await page.locator('#segment').selectOption('all');await observe();
  await page.locator('#settings-button').click();await observe();
  await page.locator('#tab-general').click();await observe();
  await page.locator('#language-select').selectOption('en');await observe();
  check(await page.locator('#close-dismiss').getAttribute('aria-label')==='Cancel','translated native-close accessible label');
  await page.locator('#tab-general').press('End');await observe();
  check(await page.locator('#tab-about').getAttribute('aria-selected')==='true','keyboard reaches About tab');
  await page.locator('#about-license').click();await observe();
  check((await page.locator('#document-content').innerText()).includes('MIT License'),'offline license document');
  await page.locator('#close-document').press('Escape');await observe();
  check(await page.evaluate(()=>!document.querySelector('#document-dialog').open&&document.activeElement.id==='about-license'),'Escape closes document and restores focus');
  await page.locator('#close-settings').click();await observe();
  const counts=await page.locator('summary small').allTextContents();
  check(counts.length>0&&counts.every(s=>/^1 champion$|^(0|[2-9]|\d{2,}) champions$/.test(s)),'grammatical zero/singular/plural counts');
  const layout=await page.evaluate(inspectLayout);check(layout.passed,'real filter and result-card layout');
  return{passed:true,origin:expectedOrigin,checks,layout,scopeCount:options.length};
}
export {inspectLayout};
