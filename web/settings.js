(function(root){
  'use strict';
  root.LunaSettings=function({api,toast,openLibrary,rerender,isExited}){
    const I=root.LunaI18n,t=I.t,$=id=>document.getElementById(id);
    const tabs=['general','data','support','about'];
    const media=root.matchMedia('(prefers-color-scheme: dark)');
    let info=null,saving=false,reading=false,currentTab='general',activeDocument='',documentEpoch=0,preferenceEpoch=0;
    const preferenceControls=['language-select','theme-select','auto-update-toggle'];
    let saveState='',diagnosticData=null,diagnosticState='',actionMessage='';
    const valid=p=>p&&['system','zh-CN','en'].includes(p.language)&&['system','dark','light'].includes(p.theme)&&typeof p.autoUpdate==='boolean';
    function applyTheme(){
      const choice=info?.preferences.theme||'system';
      document.documentElement.dataset.theme=choice==='system'?(media.matches?'dark':'light'):choice;
    }
    function apply(data){
      if(!valid(data?.preferences)||!['zh-CN','en'].includes(data.systemLanguage))throw new Error(t('settings.loadFailed'));
      info=data;
      I.setLanguage(info.preferences.language==='system'?info.systemLanguage:info.preferences.language);
      applyTheme();I.apply(document);render();rerender();
    }
    function render(){
      const p=info?.preferences;
      if(p){$('language-select').value=p.language;$('theme-select').value=p.theme;$('auto-update-toggle').checked=p.autoUpdate;}
      for(const id of preferenceControls)$(id).disabled=saving||!info||info.warning==='settings.unsupported';
      $('settings-warning').hidden=!info?.warning;
      $('settings-warning').textContent=info?.warning?t(info.warning):'';
      $('settings-run-mode').textContent=info?t(info.mode==='offline'?'settings.effective.offline':info.mode==='manual'?'settings.effective.manual':info.automaticUpdatesThisRun?'settings.effective.on':'settings.effective.off'):'';
      $('settings-save-status').textContent=saveState?t(saveState):'';
      $('settings-save-status').classList.toggle('warning',saveState.startsWith('settings.')&&!['settings.saved','settings.saving'].includes(saveState));
      $('settings-action-status').hidden=!actionMessage;
      $('settings-action-status').textContent=I.message(actionMessage);
      $('settings-action-status').classList.toggle('warning',!!actionMessage&&actionMessage!=='folder.opened');
      $('about-version').textContent=info?.version||'1.5.0';
      if(activeDocument)$('document-title').textContent=t(documentTitle(activeDocument));
      if(diagnosticData)$('diagnostics-preview').value=diagnosticText(diagnosticData);
      $('diagnostics-status').textContent=I.message(diagnosticState);
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
        if(!isExited()){saveState='settings.saved';apply(data);}
      }catch(error){saveState='settings.saveFailed';toast(I.message(error.message));}
      finally{saving=false;render();}
    }
    function selectTab(tab,focus=false){
      currentTab=tab;
      for(const name of tabs){const active=name===tab;$('tab-'+name).setAttribute('aria-selected',String(active));$('tab-'+name).tabIndex=active?0:-1;$('settings-'+name).hidden=!active;}
      if(focus)$('tab-'+tab).focus();
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
    $('settings-button').addEventListener('click',()=>{if(isExited())return;$('settings-dialog').showModal();read();});
    $('close-settings').addEventListener('click',()=>$('settings-dialog').close());
    $('language-select').addEventListener('change',()=>save('language',$('language-select').value));
    $('theme-select').addEventListener('change',()=>save('theme',$('theme-select').value));
    $('auto-update-toggle').addEventListener('change',()=>save('autoUpdate',$('auto-update-toggle').checked));
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
    media.addEventListener('change',applyTheme);
    applyTheme();I.apply(document);render();
    return {read,render,diagnosticText};
  };
})(typeof window!=='undefined'?window:this);
