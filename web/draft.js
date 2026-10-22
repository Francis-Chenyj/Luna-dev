(function(root){
  'use strict';
  // Only this small, allowlisted view model is retained by the page. The local
  // service owns client discovery and credentials; the browser never receives them.
  root.LunaDraft=function({api,chooseHero,heroById,heroName,laneLabel,isExited}){
    const I=root.LunaI18n,t=I.t,$=id=>document.getElementById(id);
    const teams=['ally','enemy'],states=new Set(['disabled','searching','ready','champ-select','in-game','unavailable','multiple']);
    const lanes=new Set(['top','jungle','middle','bottom','support']);
    const empty=(enabled=false,state='disabled')=>({enabled,state,teams:{ally:[],enemy:[]}});
    let snapshot=empty(),timer=null,controller=null,generation=0,pending=false,disposed=false,reading=false,stopRequested=false;
    let selectedFromDraft=false,lastSelectedSlot=null;
    const stopped=()=>disposed||isExited();
    function normalize(data){
      if(!data||typeof data.enabled!=='boolean'||!states.has(data.state))throw new Error('Invalid draft status');
      const next=empty(data.enabled,data.enabled?data.state:'disabled');
      if(next.state!=='champ-select')return next;
      for(const team of teams){
        const rows=data.teams?.[team];
        if(!Array.isArray(rows)||rows.length!==5)throw new Error('Invalid draft team');
        const seen=new Set();
        for(const row of rows){
          if(!row||!Number.isInteger(row.slot)||row.slot<0||row.slot>4||seen.has(row.slot))throw new Error('Invalid draft slot');
          seen.add(row.slot);
          const visible=Number.isInteger(row.championId)&&row.championId>0&&['hovered','locked'].includes(row.selection)&&!(team==='enemy'&&row.selection!=='locked');
          next.teams[team][row.slot]={slot:row.slot,championId:visible?row.championId:0,selection:visible?row.selection:'empty',
            pickOrder:visible&&Number.isInteger(row.pickOrder)&&row.pickOrder>0&&row.pickOrder<=10?row.pickOrder:0,
            lane:visible&&lanes.has(row.lane)?row.lane:''};
        }
      }
      return next;
    }
    function render(){
      const active=snapshot.enabled;
      $('draft-return').hidden=!selectedFromDraft;
      $('draft-connect').hidden=active;$('draft-disconnect').hidden=!active;
      $('draft-connect').disabled=pending;$('draft-disconnect').disabled=pending;
      $('draft-retry').hidden=!stopRequested&&!['unavailable','multiple'].includes(snapshot.state);$('draft-retry').disabled=pending||reading;
      $('draft-status').textContent=t(pending?'draft.working':stopRequested?'draft.stopFailed':'draft.state.'+snapshot.state);
      $('draft-panel').dataset.state=snapshot.state;
      let count=0;
      for(const team of teams)for(let slot=0;slot<5;slot++){
        const id='draft-'+team+'-'+slot,button=$(id),row=snapshot.teams[team][slot];
        const championId=row?.championId||0,hero=championId?heroById(championId):null;
        const name=hero?heroName(hero):championId?t('draft.unknownHero',championId):t('draft.empty');
        const phase=championId?t('draft.'+row.selection):t('draft.waiting');
        const order=row?.pickOrder?t('draft.order',row.pickOrder):'';
        const lane=championId?(row.lane?laneLabel(row.lane):t('draft.unknownLane')):'';
        button.disabled=!hero||snapshot.state!=='champ-select';
        button.dataset.selection=row?.selection||'empty';
        button.setAttribute('aria-label',[t('draft.'+team),t('draft.slot',slot+1),name,phase,order,lane,hero?t('draft.open'):championId?t('draft.notInCatalog'):''].filter(Boolean).join(' · '));
        $(id+'-name').textContent=name;
        $(id+'-meta').textContent=[phase,order,lane].filter(Boolean).join(' · ');
        const icon=$(id+'-icon');icon.hidden=!hero;
        if(hero){const src='/icons/'+hero.id+'.png';if(icon.getAttribute('src')!==src){icon.setAttribute('src',src);icon.dataset.failed='';}if(icon.dataset.failed==='true')icon.hidden=true;}
        else icon.removeAttribute('src');
        $(id+'-placeholder').hidden=!!hero&&icon.dataset.failed!=='true';
        if(championId)count++;
        if(button.disabled&&document.activeElement===button)(active?$('draft-disconnect'):$('draft-connect')).focus();
      }
      $('draft-count').textContent=snapshot.state==='champ-select'?t('draft.count',count):t('draft.hint');
    }
    function schedule(){
      clearTimeout(timer);
      if(!stopped()&&!stopRequested&&snapshot.enabled)timer=setTimeout(read,1000);
    }
    async function read(){
      if(stopped()||pending||reading||stopRequested)return;
      clearTimeout(timer);reading=true;
      const epoch=generation,request=new AbortController();controller=request;
      try{
        const data=await api('/api/draft',{signal:request.signal,timeout:2500,trackConnection:false});
        if(!stopped()&&epoch===generation)snapshot=normalize(data);
      }catch{
        if(!stopped()&&epoch===generation)snapshot=empty(snapshot.enabled,'unavailable');
      }finally{
        if(epoch===generation&&!stopped()){reading=false;controller=null;render();schedule();}
      }
    }
    async function change(enabled){
      if(stopped()||pending)return;
      generation++;controller?.abort();clearTimeout(timer);reading=false;pending=true;
      stopRequested=!enabled;
      // Clear immediately on a disconnect request; a late read cannot restore it.
      if(!enabled)snapshot=empty(snapshot.enabled,'unavailable');
      render();
      const epoch=generation,request=new AbortController();controller=request;
      try{
        const data=await api('/api/draft/'+(enabled?'connect':'disconnect'),{method:'POST',signal:request.signal,timeout:4000,trackConnection:false});
        if(!stopped()&&epoch===generation){const next=normalize(data);stopRequested=!enabled&&next.enabled;snapshot=stopRequested?empty(true,'unavailable'):next;}
      }catch{
        if(!stopped()&&epoch===generation)snapshot=empty(snapshot.enabled||enabled,'unavailable');
      }finally{
        if(!stopped()&&epoch===generation){pending=false;controller=null;render();schedule();(snapshot.enabled?$('draft-disconnect'):$('draft-connect')).focus();}
      }
    }
    for(const team of teams)for(let slot=0;slot<5;slot++){
      const id='draft-'+team+'-'+slot;
      $(id).addEventListener('click',()=>{
        if(stopped()||snapshot.state!=='champ-select')return;
        const row=snapshot.teams[team][slot],hero=row?.championId?heroById(row.championId):null;
        if(!hero)return;
        lastSelectedSlot=id;
        chooseHero(hero,{source:'draft',lane:row.lane});
        $('selected-name').focus();$('selection-panel').scrollIntoView({block:'start',behavior:'auto'});
      });
      $(id+'-icon').addEventListener('error',()=>{$(id+'-icon').dataset.failed='true';$(id+'-icon').hidden=true;$(id+'-placeholder').hidden=false;});
    }
    $('draft-connect').addEventListener('click',()=>change(true));
    $('draft-disconnect').addEventListener('click',()=>change(false));
    $('draft-retry').addEventListener('click',()=>stopRequested?change(false):read());
    $('draft-return').addEventListener('click',()=>{
      if(stopped()||!selectedFromDraft)return;
      const previous=lastSelectedSlot?$(lastSelectedSlot):null,control=$(snapshot.enabled?'draft-disconnect':'draft-connect');
      const target=previous&&!previous.disabled?previous:!control.disabled?control:$('draft-title');
      target.focus();target.scrollIntoView({block:'center',behavior:'auto'});
    });
    function selectionChanged(source){selectedFromDraft=source==='draft';$('draft-return').hidden=!selectedFromDraft;}
    function dispose(){disposed=true;generation++;controller?.abort();clearTimeout(timer);}
    render();
    return {read,render,dispose,selectionChanged};
  };
})(typeof window!=='undefined'?window:this);
