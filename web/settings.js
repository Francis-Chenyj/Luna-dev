(function(root){
  'use strict';
  root.LunaSettings=function({api,toast,openLibrary,rerender,isExited}){
    const I=root.LunaI18n,t=I.t,$=id=>document.getElementById(id);
    const tabs=['general','data','updates','support','about'];
    const media=root.matchMedia('(prefers-color-scheme: dark)');
    let info=null,saving=false,reading=false,currentTab='general',activeDocument='',documentEpoch=0,preferenceEpoch=0;
    const preferenceControls=['language-select','theme-select','auto-update-toggle','close-action-select','app-check-toggle','app-download-toggle'];
    let saveState='',diagnosticData=null,diagnosticState='',actionMessage='';
    const valid=p=>p&&['system','zh-CN','en'].includes(p.language)&&['system','dark','light'].includes(p.theme)&&typeof p.autoUpdate==='boolean'&&(p.closeAction===undefined||['ask','tray','exit'].includes(p.closeAction));
    function applyTheme(){
      const choice=info?.preferences.theme||'system';
      const theme=choice==='system'?(media.matches?'dark':'light'):choice;
      document.documentElement.dataset.theme=theme;
      // Edge app windows and supporting browsers use this for their frame color.
      $('theme-color').setAttribute('content',theme==='dark'?'#111715':'#f4f7f1');
    }
    function apply(data){
      if(!valid(data?.preferences)||!['zh-CN','en'].includes(data.systemLanguage))throw new Error(t('settings.loadFailed'));
      info=data;
      I.setLanguage(info.preferences.language==='system'?info.systemLanguage:info.preferences.language);
      applyTheme();I.apply(document);render();rerender();
    }
    function render(){
      const p=info?.preferences;
      if(p){$('language-select').value=p.language;$('theme-select').value=p.theme;$('auto-update-toggle').checked=p.autoUpdate;$('close-action-select').value=p.closeAction||'ask';}
      if(p){$('app-check-toggle').checked=p.appAutoCheck!==false;$('app-download-toggle').checked=p.appAutoDownload===true;}
      $('app-update-policy-note').hidden=!info||info.mode==='normal';
      $('app-update-policy-note').textContent=info?.mode==='offline'?t('appUpdate.savedOffline'):info?.mode==='manual'?t('appUpdate.savedManual'):'';
      for(const id of preferenceControls)$(id).disabled=saving||!info||info.warning==='settings.unsupported';
      const nativeWindow=!!info?.desktopWindowAvailable&&!!root.chrome?.webview;
      $('close-action-select').disabled||=!nativeWindow;
      $('close-unavailable').hidden=nativeWindow;
      $('settings-warning').hidden=!info?.warning;
      $('settings-warning').textContent=info?.warning?t(info.warning):'';
      $('settings-run-mode').textContent=info?t(info.mode==='offline'?'settings.effective.offline':info.mode==='manual'?'settings.effective.manual':info.automaticUpdatesThisRun?'settings.effective.on':'settings.effective.off'):'';
      $('settings-save-status').textContent=saveState?t(saveState):'';
      $('settings-save-status').classList.toggle('warning',saveState.startsWith('settings.')&&!['settings.saved','settings.saving'].includes(saveState));
      $('settings-action-status').hidden=!actionMessage;
      $('settings-action-status').textContent=I.message(actionMessage);
      $('settings-action-status').classList.toggle('warning',!!actionMessage&&actionMessage!=='folder.opened');
      $('about-version').textContent=info?.version||'1.6.2';
      if(activeDocument)$('document-title').textContent=t(documentTitle(activeDocument));
      if(diagnosticData)$('diagnostics-preview').value=diagnosticText(diagnosticData);
      $('diagnostics-status').textContent=I.message(diagnosticState);
      renderMaintenance();
    }
    async function read(){
      if(reading||saving||isExited())return;
      reading=true;const epoch=preferenceEpoch;
      try {const data=await api('/api/settings',{timeout:3000,trackConnection:false});if(!isExited()&&epoch===preferenceEpoch&&JSON.stringify(data)!==JSON.stringify(info))apply(data);}
      catch {if(!info){saveState='settings.loadFailed';render();}}
      finally{reading=false;}
    }
    async function save(key,value){
      if(saving||!info||isExited())return;
      saving=true;preferenceEpoch++;saveState='settings.saving';render();
      try {
        const data=await api('/api/settings/save?'+new URLSearchParams({key,value:String(value)}),{method:'POST',timeout:5000});
        if(!isExited()){
          const appPreference=key==='appAutoCheck'||key==='appAutoDownload';
          saveState=appPreference?(value?(data.mode==='offline'?'appUpdate.savedOffline':data.mode==='manual'?'appUpdate.savedManual':key==='appAutoDownload'?'appUpdate.downloadEnabled':'appUpdate.checkEnabled'):key==='appAutoCheck'?'appUpdate.checkDisabled':'appUpdate.downloadDisabled'):'settings.saved';
          apply(data);
          if(appPreference)readMaintenance();
        }
      }catch(error){saveState='settings.saveFailed';toast(I.message(error.message));}
      finally{saving=false;render();}
    }
    function selectTab(tab,focus=false){
      currentTab=tab;
      for(const name of tabs){const active=name===tab;$('tab-'+name).setAttribute('aria-selected',String(active));$('tab-'+name).tabIndex=active?0:-1;$('settings-'+name).hidden=!active;}
      if(focus)$('tab-'+tab).focus();
      if(tab==='data'||tab==='updates')readMaintenance();
    }
    for(const name of tabs){
      $('tab-'+name).addEventListener('click',()=>selectTab(name));
      $('tab-'+name).addEventListener('keydown',event=>{
        let index=tabs.indexOf(currentTab);
        if(event.key==='ArrowRight')index=(index+1)%tabs.length;
        else if(event.key==='ArrowLeft')index=(index+tabs.length-1)%tabs.length;
        else if(event.key==='Home')index=0;else if(event.key==='End')index=tabs.length-1;else return;
        event.preventDefault();selectTab(tabs[index],true);
      });
    }
    $('settings-button').addEventListener('click',()=>{if(isExited())return;$('settings-dialog').showModal();if(appUpdate?.newVersion)selectTab('updates');read();});
    $('close-settings').addEventListener('click',()=>$('settings-dialog').close());
    $('language-select').addEventListener('change',()=>save('language',$('language-select').value));
    $('theme-select').addEventListener('change',()=>save('theme',$('theme-select').value));
    $('close-action-select').addEventListener('change',()=>save('closeAction',$('close-action-select').value));
    $('auto-update-toggle').addEventListener('change',()=>save('autoUpdate',$('auto-update-toggle').checked));
    $('app-check-toggle').addEventListener('change',()=>save('appAutoCheck',$('app-check-toggle').checked));
    $('app-download-toggle').addEventListener('change',()=>save('appAutoDownload',$('app-download-toggle').checked));
    $('settings-manage-data').addEventListener('click',()=>{$('settings-dialog').close();openLibrary();});
    for(const kind of ['logs','data'])$('open-'+kind).addEventListener('click',async()=>{
      const button=$('open-'+kind);button.disabled=true;
      try{await api('/api/folders/open?'+new URLSearchParams({kind}),{method:'POST',timeout:4000});actionMessage='folder.opened';}
      catch(error){actionMessage=error.message;}finally{button.disabled=false;render();}
    });
    function diagnosticText(data){
      const categories=['network','permission','storage','data','internal'];
      const errors=Array.isArray(data.errors)?data.errors.filter(x=>categories.includes(x.category)&&Number.isInteger(x.count)&&x.count>0):[];
      const version=/^\d+\.\d+\.\d+(?:[-+][\w.-]+)?$/.test(data.version)?data.version:'—';
      const mode=['normal','manual','offline'].includes(data.mode)?data.mode:'normal';
      return [t('diagnostics.version')+': '+version,t('diagnostics.mode')+': '+t('settings.mode.'+mode),
        t('diagnostics.auto')+': '+t(data.automaticUpdatesThisRun===true?'diagnostics.on':'diagnostics.off'),
        t('diagnostics.errors')+':',...(errors.length?errors.map(x=>t('diagnostics.'+x.category)+': '+Math.min(x.count,9999)):[t('diagnostics.none')])].join('\n');
    }
    $('copy-diagnostics').addEventListener('click',async()=>{
      const button=$('copy-diagnostics');button.disabled=true;
      try{
        diagnosticData=await api('/api/diagnostics',{timeout:3000});
        const text=diagnosticText(diagnosticData),preview=$('diagnostics-preview');preview.value=text;preview.hidden=false;
        try{await root.navigator.clipboard.writeText(text);diagnosticState='diagnostics.copied';}
        catch{preview.focus();preview.select();diagnosticState='diagnostics.fallback';}
        render();
      }catch(error){diagnosticState=error.message;render();}finally{button.disabled=false;}
    });
    const documentTitle=name=>({'changelog':'about.changelog','license':'about.mit','third-party':'about.thirdParty'})[name];
    for(const name of ['changelog','license','third-party'])$('about-'+name).addEventListener('click',async()=>{
      activeDocument=name;const epoch=++documentEpoch;$('document-title').textContent=t(documentTitle(name));$('document-content').textContent=t('document.loading');$('document-dialog').showModal();
      try{const data=await api('/api/about/document?'+new URLSearchParams({name}),{timeout:4000});if(epoch===documentEpoch)$('document-content').textContent=data.text;}
      catch(error){if(epoch===documentEpoch)$('document-content').textContent=I.message(error.message);}
    });
    $('close-document').addEventListener('click',()=>$('document-dialog').close());
    let closing=false;
    const closeDialog=$('close-dialog');
    function dismissClose(){if(!closing)closeDialog.close();}
    for(const id of ['close-dismiss','close-cancel'])$(id).addEventListener('click',dismissClose);
    closeDialog.addEventListener('cancel',event=>{if(closing)event.preventDefault();});
    root.chrome?.webview?.addEventListener('message',event=>{
      if(event.data!=='luna-close-request'||isExited()||closeDialog.open)return;
      $('close-tray').checked=true;$('close-exit').checked=false;$('close-remember').checked=false;
      $('close-remember').disabled=info?.warning==='settings.unsupported';$('close-status').textContent='';
      closeDialog.showModal();$('close-tray').focus();
    });
    $('close-confirm').addEventListener('click',async()=>{
      if(closing||isExited()||!closeDialog.open)return;
      closing=true;
      const controls=['close-confirm','close-cancel','close-dismiss','close-tray','close-exit','close-remember'];
      const choice=$('close-exit').checked?'exit':'tray',remember=$('close-remember').checked;
      for(const id of controls)$(id).disabled=true;
      $('close-status').textContent=t('close.working');
      try{
        await api('/api/window/close?'+new URLSearchParams({choice,remember:String(remember)}),{method:'POST',timeout:5000});
        closeDialog.close();if(choice==='tray')await read();
      }catch(error){$('close-status').textContent=error.message?.startsWith('settings.')?t(error.message):t('close.failed');}
      finally{closing=false;for(const id of controls)$(id).disabled=false;$('close-remember').disabled=info?.warning==='settings.unsupported';}
    });
    media.addEventListener('change',applyTheme);
    let appUpdate=null,space=null,maintenance=null,maintenanceAction='',maintenanceBusy=false,maintenanceTimer=null,maintenanceEpoch=0;
    const size=bytes=>(Math.max(0,Number(bytes)||0)/1048576).toFixed(2)+' MiB';
    function renderMaintenance(){
      if(appUpdate){
        $('app-update-badge').hidden=!appUpdate.newVersion;
        const known=['idle','checking','available','downloading','verifying','ready','paused','error','current','noRelease','installed'];
        $('app-update-status').textContent=t('appUpdate.'+(appUpdate.offline?'offline':known.includes(appUpdate.state)?appUpdate.state:'idle'))+(appUpdate.newVersion?' · '+appUpdate.newVersion:'');
        $('app-update-size').textContent=appUpdate.totalBytes?t('appUpdate.size',size(appUpdate.downloadedBytes),size(appUpdate.totalBytes)):'';
        $('app-update-progress').hidden=!appUpdate.totalBytes;$('app-update-progress').max=appUpdate.totalBytes||1;$('app-update-progress').value=appUpdate.downloadedBytes||0;
        $('app-update-notes').hidden=!appUpdate.notes;$('app-update-notes').textContent=appUpdate.notes||'';
        $('app-check').disabled=appUpdate.busy||appUpdate.offline;
        $('app-download').hidden=!appUpdate.newVersion||appUpdate.state==='ready';$('app-download').disabled=appUpdate.busy||appUpdate.offline;
        $('app-cancel').hidden=!appUpdate.busy;$('app-apply').hidden=appUpdate.state!=='ready';$('app-apply').disabled=!appUpdate.managed;
        $('app-maintenance-note').textContent=(appUpdate.result?.state==='recovered'?t('appUpdate.recovered')+' ':'')+t(appUpdate.managed?'appUpdate.installNote':'maintenance.sourceFolder');
      }
      if(maintenance)$('app-uninstall').disabled=!maintenance.available;
      if(space){
        const report=space.report;
        $('space-summary').textContent=report?t('space.summary',size(report.totalBytes),new Date(report.checkedAt).toLocaleString(I.language))+(space.waiting?' · '+t('space.waiting'):space.busy?' · '+t('space.busy'):'')+(report.partial?' · '+t('space.partial'):''):t(space.waiting?'space.waiting':'space.pending');
        $('space-breakdown').textContent='';
        if(report?.bytes)for(const kind of ['program','library','heroes','browser','updates','diagnostics','settings','other']){
          const row=document.createElement('div'),label=document.createElement('dt'),value=document.createElement('dd');
          label.textContent=t('space.'+kind);value.textContent=size(report.bytes[kind]);row.append(label,value);$('space-breakdown').append(row);
        }
        $('space-clean').disabled=space.busy;
      }
    }
    async function readMaintenance(){
      clearTimeout(maintenanceTimer);if(isExited())return;
      const epoch=++maintenanceEpoch,tab=currentTab;
      try{
        if(tab==='data'){const next=await api('/api/space',{timeout:3000,trackConnection:false});if(epoch!==maintenanceEpoch||isExited())return;space=next;}
        if(tab==='updates'){const next=await Promise.all([api('/api/app-update',{timeout:3000,trackConnection:false}),api('/api/maintenance',{timeout:3000,trackConnection:false})]);if(epoch!==maintenanceEpoch||isExited())return;[appUpdate,maintenance]=next;}
        renderMaintenance();
      }catch{if(epoch===maintenanceEpoch&&currentTab==='updates')$('app-update-status').textContent=t('appUpdate.error');}
      if(epoch!==maintenanceEpoch)return;
      if($('settings-dialog').open&&(currentTab==='data'||currentTab==='updates'))maintenanceTimer=setTimeout(readMaintenance,2000);
    }
    $('settings-dialog').addEventListener('close',()=>{maintenanceEpoch++;clearTimeout(maintenanceTimer);});
    for(const action of ['check','download','cancel'])$('app-'+action).addEventListener('click',async()=>{
      $('app-'+action).disabled=true;
      try{appUpdate=await api('/api/app-update/'+action,{method:'POST',timeout:4000});renderMaintenance();readMaintenance();}
      catch(error){toast(I.message(error.message));$('app-'+action).disabled=false;}
    });
    $('space-clean').addEventListener('click',async()=>{try{space=await api('/api/space/clean',{method:'POST',timeout:3000});renderMaintenance();readMaintenance();}catch(error){toast(I.message(error.message));}});
    $('open-updates').addEventListener('click',async()=>{try{await api('/api/folders/open?kind=updates',{method:'POST',timeout:3000});}catch(error){toast(I.message(error.message));}});
    for(const action of ['update','uninstall'])$(action==='update'?'app-apply':'app-uninstall').addEventListener('click',async()=>{
      try{maintenance=await api('/api/maintenance',{timeout:3000});if(!maintenance.available){toast(t('maintenance.sourceFolder'));return;}}
      catch{toast(t('appUpdate.error'));return;}
      maintenanceAction=action;$('maintenance-title').textContent=t(action==='update'?'appUpdate.apply':'maintenance.uninstall');
      $('maintenance-description').textContent=t(action==='update'?'maintenance.updateConfirm':'maintenance.uninstallConfirm');
      $('maintenance-path').textContent=maintenance.path;$('maintenance-data-choice').hidden=action!=='uninstall';$('maintenance-remove-data').checked=false;$('maintenance-status').textContent='';$('maintenance-dialog').showModal();
    });
    for(const id of ['maintenance-dismiss','maintenance-cancel'])$(id).addEventListener('click',()=>{if(!maintenanceBusy)$('maintenance-dialog').close();});
    $('maintenance-dialog').addEventListener('cancel',event=>{if(maintenanceBusy)event.preventDefault();});
    $('maintenance-confirm').addEventListener('click',async()=>{
      if(maintenanceBusy||!$('maintenance-dialog').open)return;maintenanceBusy=true;$('maintenance-confirm').disabled=true;
      try{await api('/api/maintenance/run?'+new URLSearchParams({action:maintenanceAction,removeData:String(maintenanceAction==='uninstall'&&$('maintenance-remove-data').checked)}),{method:'POST',timeout:15000});$('maintenance-status').textContent=t('maintenance.handoff');}
      catch(error){$('maintenance-status').textContent=I.message(error.message);maintenanceBusy=false;$('maintenance-confirm').disabled=false;}
    });
    let badgeTimer=setTimeout(async function refreshBadge(){
      if(isExited())return;
      if(info?.mode!=='offline')try{appUpdate=await api('/api/app-update',{timeout:2000,trackConnection:false});renderMaintenance();}catch{}
      badgeTimer=setTimeout(refreshBadge,60000);
    },15000);
    root.addEventListener('pagehide',()=>{clearTimeout(badgeTimer);clearTimeout(maintenanceTimer);});
    applyTheme();I.apply(document);render();
    return {read,render,diagnosticText};
  };
})(typeof window!=='undefined'?window:this);
