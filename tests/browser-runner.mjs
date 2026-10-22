// Runs only against a new, manifest-verified copy of the supplied Luna package.
// No personal browser profile, installed cache, upstream service or external page.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import net from 'node:net';
import {spawn} from 'node:child_process';
import {parseArgs} from 'node:util';
import {chromium} from 'playwright';
import {expect} from 'playwright/test';
import {runBrowserSmoke, inspectLayout} from './browser-smoke.mjs';
import {runDraftBrowser} from './browser-draft.mjs';
import {runDraftAccessibility} from './browser-draft-accessibility.mjs';

const {values} = parseArgs({options:{runtime:{type:'string'},output:{type:'string'},channel:{type:'string'}}});
assert.ok(values.runtime && values.output, 'Usage: node tests/browser-runner.mjs --runtime <unpacked Luna> --output <new directory> [--channel msedge]');
const input = await fs.realpath(values.runtime), output = path.resolve(values.output), runtime = path.join(output,'runtime');
assert.ok(!output.toLowerCase().startsWith(input.toLowerCase()+path.sep) && !input.toLowerCase().startsWith(output.toLowerCase()+path.sep) && output.toLowerCase() !== input.toLowerCase(), 'Keep browser output outside the supplied runtime');
await fs.mkdir(output); await fs.mkdir(runtime);
const report = {passed:false,channel:values.channel||'chromium',startedAt:new Date().toISOString(),checks:[],layouts:[],errors:[],externalRequests:[]};
let child, browser, page, context;
const delay = ms => new Promise(resolve=>setTimeout(resolve,ms));
const timeout = setTimeout(()=>{report.errors.push('Browser suite exceeded 180 seconds'); void browser?.close(); child?.kill();},180000);
timeout.unref();
try {
  const manifest = JSON.parse(await fs.readFile(path.join(input,'installed-files.json'),'utf8'));
  assert.equal(manifest.owner,'Luna.Runtime.1'); report.version = manifest.version;
  for (const row of manifest.files) {
    assert.ok(row.path && !row.path.includes('\\') && !row.path.includes(':') && row.path.split('/').every(x=>x && x!=='.' && x!=='..'),'Invalid runtime manifest path');
    const source = await fs.realpath(path.join(input,row.path));
    assert.ok(source.toLowerCase().startsWith(input.toLowerCase()+path.sep),'Runtime path escaped its package');
    const bytes = await fs.readFile(source);
    assert.equal(bytes.length,row.bytes,row.path+' size');
    assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'),row.sha256,row.path+' hash');
    const destination = path.join(runtime,row.path);
    await fs.mkdir(path.dirname(destination),{recursive:true}); await fs.writeFile(destination,bytes);
  }
  await fs.copyFile(path.join(input,'installed-files.json'),path.join(runtime,'installed-files.json'));
  let port;
  do {
    const probe=net.createServer(); await new Promise(resolve=>probe.listen(0,'127.0.0.1',resolve));
    port=probe.address().port; await new Promise(resolve=>probe.close(resolve));
  } while(port>65000);
  child=spawn(path.join(runtime,'Luna.exe'),['--offline','--no-browser','--port',String(port)],{cwd:runtime,windowsHide:true,stdio:'ignore'});
  let launchError; child.on('error',error=>{launchError=error;});
  let origin;
  for(let attempt=0;attempt<150;attempt++) {
    if(launchError)throw launchError;
    assert.equal(child.exitCode,null,'The isolated Luna process exited during startup');
    try {
      origin=(await fs.readFile(path.join(runtime,'cache/running-url.txt'),'utf8')).trim();
      const parsed=new URL(origin);
      assert.equal(parsed.hostname,'127.0.0.1');assert.equal(parsed.protocol,'http:');
      assert.ok(+parsed.port>=port && +parsed.port<=port+29);
      const health=await (await fetch(origin+'/api/health',{signal:AbortSignal.timeout(1000)})).json();
      if(health.offline && health.version===manifest.version)break;
      origin=null;
    }catch{origin=null;}
    await delay(100);
  }
  assert.ok(origin,'The isolated runtime did not become ready');report.origin=origin;
  browser=await chromium.launch({headless:true,...(values.channel?{channel:values.channel}:{})});
  report.browser=browser.version();
  context=await browser.newContext({viewport:{width:1440,height:1000}});
  await context.tracing.start({screenshots:true,snapshots:true,sources:true});
  await context.route('**/*',async route=>{
    const requestUrl=new URL(route.request().url());
    if(requestUrl.origin===origin){
      // Exercise the document's loading state even on a fast local server.
      if(requestUrl.pathname==='/api/about/document'&&requestUrl.searchParams.get('name')==='license')await delay(250);
      return route.continue();
    }
    report.externalRequests.push(route.request().url());return route.abort();
  });
  page=await context.newPage();page.setDefaultTimeout(5000);
  page.on('pageerror',error=>report.errors.push(error.message));
  await page.goto(origin);
  const ready=async()=>{await expect(page.locator('#hero-search')).toBeEnabled();};
  const wrap=locator=>new Proxy(locator,{get(target,key){
    if(key==='first')return()=>wrap(target.first());
    if(key==='waitFor')return options=>target.waitFor({...options,timeout:options.timeoutMs||5000});
    const value=Reflect.get(target,key);return typeof value==='function'?value.bind(target):value;
  }});
  const adapter={
    url:()=>page.url(),reload:async()=>{await page.reload();await ready();},
    back:async()=>{await page.goBack();await ready();},forward:async()=>{await page.goForward();await ready();},
    playwright:{locator:selector=>wrap(page.locator(selector)),evaluate:(fn,arg)=>page.evaluate(fn,arg),
      domSnapshot:async()=>{await ready();await page.waitForLoadState('networkidle');return page.locator('body').ariaSnapshot();}}
  };
  await ready();report.smoke=await runBrowserSmoke(adapter,origin);
  report.checks.push('fresh launch, search, all scopes, refresh/history, keyboard tabs, local license, Escape/focus and English counts');
  report.checks.push('local license loads with a delayed response');
  await runDraftBrowser(page,context,origin,report);
  report.draftAccessibility=await runDraftAccessibility(page,context,origin,output);
  assert.ok(report.draftAccessibility.passed,JSON.stringify(report.draftAccessibility.problems));
  report.checks.push(...report.draftAccessibility.checks);
  for(const language of ['zh-CN','en'])for(const theme of ['light','dark']) {
    await page.locator('#settings-button').click();await page.locator('#tab-general').click();
    await page.locator('#language-select').selectOption(language);await expect(page.locator('html')).toHaveAttribute('lang',language);
    await page.locator('#theme-select').selectOption(theme);await expect(page.locator('html')).toHaveAttribute('data-theme',theme);
    await page.locator('#close-settings').click();
    for(const width of [360,640,720,721,800,900,1000,1001,1440]) {
      await page.setViewportSize({width,height:1000});
      const layout=await page.evaluate(inspectLayout);report.layouts.push(layout);
      assert.ok(layout.passed,JSON.stringify(layout));
    }
  }
  report.checks.push('36 language/theme/viewport layout combinations');
  await page.reload();await ready();await expect(page.locator('html')).toHaveAttribute('lang','en');await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
  report.checks.push('language and theme survive reload');
  await page.locator('#threshold').focus();await page.locator('#threshold').press('End');
  await expect(page.locator('#profile-availability')).toBeVisible();
  await expect(page.locator('#profile-availability')).toContainText(/offline/i);
  await expect(page.locator('#profile-availability button')).toHaveCount(0);
  await page.locator('#reset-button').click();await expect(page.locator('.matchup-card').first()).toBeVisible();
  report.checks.push('missing offline profile explains recovery and reset restores results');
  await page.screenshot({path:path.join(output,'browser-final.png'),fullPage:true});
  assert.deepEqual(report.errors,[],'Browser JavaScript errors');assert.deepEqual(report.externalRequests,[],'Offline UI attempted an external request');
  report.passed=true;
} catch(error) {
  report.errors.push(error.stack||String(error));
  if(page)try{await page.screenshot({path:path.join(output,'browser-failure.png'),fullPage:true});}catch{}
  process.exitCode=1;
} finally {
  clearTimeout(timeout);
  if(context)try{await context.tracing.stop({path:path.join(output,'trace.zip')});}catch(error){report.errors.push('Trace: '+error.message);}
  if(browser)await browser.close();
  if(child && child.exitCode===null){child.kill();await Promise.race([new Promise(resolve=>child.once('exit',resolve)),delay(3000)]);}
  report.finishedAt=new Date().toISOString();
  if(report.errors.length){report.passed=false;process.exitCode=1;}
  await fs.writeFile(path.join(output,'result.json'),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify({passed:report.passed,version:report.version,checks:report.checks,layouts:report.layouts.length,errors:report.errors,output},null,2));
}
