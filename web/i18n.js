(function(root,factory){
  const locales=typeof module==='object'&&module.exports?require('./locales.js'):root.LunaLocales;
  const create=()=>factory(locales);
  if(typeof module==='object'&&module.exports)module.exports={create};else root.LunaI18n=create();
})(typeof globalThis!=='undefined'?globalThis:this,function(en){
  'use strict';
  // Named messages supplement the existing Chinese source keys. Both languages
  // use the same placeholders; text interpolation never interprets markup.
  const extra={
    'settings.title':['设置','Settings'], 'settings.close':['关闭设置','Close settings'],
    'settings.help':['右上角设置可切换语言与主题，控制下次启动的数据更新，查看关于信息，打开目录或复制精简诊断。','Settings at the top right changes language and appearance, controls data updates on the next launch, and opens About, local folders or limited diagnostics.'],
    'settings.general':['常规','General'], 'settings.data':['数据','Data'], 'settings.support':['排错','Support'], 'settings.about':['关于','About'],
    'settings.language':['语言 / Language','Language / 语言'], 'settings.system':['跟随系统','Follow system'],
    'settings.languageNote':['立即切换界面语言，保留当前查询；两种语言都支持中英文搜索。','Changes immediately and preserves your query. Search supports both languages in either mode.'],
    'settings.theme':['外观','Appearance'], 'settings.dark':['深色','Dark'], 'settings.light':['浅色','Light'],
    'settings.themeNote':['保留 Luna 的绿色风格。“跟随系统”会随 Windows 外观变化。','Luna keeps its green identity. Follow system responds to Windows appearance changes.'],
    'settings.saved':['已保存','Saved'], 'settings.saving':['正在保存…','Saving…'],
    'settings.persistence':['选择保存在本机，重启后继续使用。','Your preferences are saved locally and persist after restart.'],
    'settings.auto':['启动时自动更新数据','Update data automatically at startup'],
    'settings.autoNote':['下次启动生效。不会立即开始或停止下载；已有任务可在数据管理中停止。','Applies on the next launch. This does not start or stop a download now. Stop an existing task in Data management.'],
    'settings.dataNote':['这里只更新英雄与对局数据。手动更新仍保留；不自动下载或安装 Luna 程序。','These updates cover champion and matchup data. Manual updates remain available. Luna itself is not downloaded or installed automatically.'],
    'settings.mode.normal':['普通模式','Normal mode'], 'settings.mode.manual':['手动更新模式','Manual-update mode'], 'settings.mode.offline':['离线模式','Offline mode'],
    'settings.effective.on':['本次启动：已启用自动数据更新。','This launch: automatic data updates enabled.'],
    'settings.effective.off':['本次启动：仅手动更新。','This launch: manual updates only.'],
    'settings.effective.offline':['本次启动：离线，所有联网更新均不可用。保存的开关用于下次普通启动。','This launch is offline; all network updates are disabled. The saved switch applies on the next normal launch.'],
    'settings.effective.manual':['本次启动由启动参数关闭了自动更新。保存的开关不能覆盖此限制，手动更新仍可用。','A launch option disabled automatic updates for this session. This preference cannot override it; manual updates remain available.'],
    'settings.unreadable':['设置文件无法读取，已暂用默认外观并关闭启动自动更新。重新保存会保留原文件副本。','Preferences could not be read. Default appearance is active and startup updates are disabled. Saving preserves a copy of the old file.'],
    'settings.unsupported':['设置文件来自不兼容的版本，当前版本不会覆盖它。','Preferences use an unsupported format. This version will not overwrite them.'],
    'settings.invalid':['设置值无效，请重新选择。','Invalid preference. Choose an available option.'],
    'settings.busy':['另一个窗口正在保存设置，请稍后重试。','Another window is saving preferences. Try again shortly.'],
    'settings.saveFailed':['未能保存设置，已保留原选择。请检查程序文件夹是否可写。','Preferences could not be saved. The previous choices remain active. Check that the app folder is writable.'],
    'settings.loadFailed':['未能读取设置，请检查本地服务后重试。','Could not read preferences. Check the local service and retry.'],
    'folder.logs':['打开日志目录','Open logs folder'], 'folder.data':['打开数据目录','Open data folder'],
    'folder.note':['日志目录保存本次及后续运行的错误日志。数据目录保存设置与已下载数据；删除其中内容会丢失这些记录。','The logs folder contains errors from this and later runs. The data folder contains preferences and downloads; deleting its contents removes those records.'],
    'folder.opened':['已请求在资源管理器中打开目录。','Requested the folder in File Explorer.'],
    'folder.invalid':['只能打开 Luna 的日志或数据目录。','Only Luna logs and data folders can be opened.'],
    'folder.unavailable':['目录未能打开，请确认程序文件夹可访问。','Could not open the folder. Check that the app folder is accessible.'],
    'diagnostics.title':['诊断信息','Diagnostics'], 'diagnostics.copy':['复制诊断信息','Copy diagnostics'],
    'diagnostics.note':['只包含 Luna 版本、运行模式和本次运行的错误类别统计；不包含原始日志、文件路径、账号或查询记录。不会自动发送。','Contains only the Luna version, run mode and error-category counts for this session. No raw logs, paths, accounts or query history. Nothing is sent automatically.'],
    'diagnostics.copied':['诊断信息已复制。','Diagnostics copied.'],
    'diagnostics.fallback':['浏览器未允许自动复制。下方已选中文本，请按 Ctrl+C。','The browser did not allow copying. The text below is selected; press Ctrl+C.'],
    'diagnostics.version':['Luna 版本','Luna version'], 'diagnostics.mode':['运行模式','Run mode'],
    'diagnostics.auto':['本次启动自动更新','Startup updates this session'], 'diagnostics.on':['开启','Enabled'], 'diagnostics.off':['关闭','Disabled'],
    'diagnostics.errors':['本次运行错误摘要','Errors this session'], 'diagnostics.none':['未记录错误','No errors recorded'],
    'diagnostics.network':['网络连接错误','Network errors'], 'diagnostics.permission':['文件访问权限错误','File permission errors'],
    'diagnostics.storage':['本地文件读写错误','Local file errors'], 'diagnostics.data':['数据格式或校验错误','Data format or validation errors'], 'diagnostics.internal':['程序内部错误','Internal errors'],
    'about.intro':['英雄联盟对局助手 · 本地查询，清晰选人。','A local League of Legends matchup explorer.'],
    'about.version':['版本','Version'], 'about.author':['作者','Author'], 'about.home':['Luna 项目主页 ↗','Luna project homepage ↗'],
    'about.pending':['开发中，欢迎反馈。','In development. Feedback is welcome.'],
    'about.license':['原创代码 · MIT','Original code · MIT'],
    'about.licenseNote':['MIT 适用于 Luna 原创代码。第三方数据、英雄图像及其他内容遵循各自条款，不因随程序提供而改为 MIT。','MIT covers Luna’s original code. Third-party data, champion images and other content remain subject to their own terms. Bundling them does not relicense them under MIT.'],
    'about.changelog':['更新记录','Changelog'], 'about.mit':['查看 MIT 许可证','View MIT license'], 'about.thirdParty':['第三方声明','Third-party notices'],
    'about.acknowledgments':['参考项目与致谢见双语 README；本程序的主页入口始终指向 Luna。','Reference projects and acknowledgments are in the bilingual README. The app homepage always points to Luna.'],
    'document.close':['关闭文档','Close document'], 'document.loading':['正在读取本地文档…','Loading local document…'],
    'document.invalid':['未提供此文档。','This document is not available.'], 'document.unavailable':['无法读取本地文档，请从完整程序包恢复。','Could not read the local document. Restore it from the complete app package.'],
    'app.title':['Luna · 英雄联盟对局助手','Luna · League of Legends Matchup Explorer'],
    'message.unknown':['操作未能完成。请查看数据管理或本地日志。','The operation could not finish. Check Data management or the local logs.'],
    'job.idle':['查询只读取本地数据。需要时可在这里更新。','Queries use local data. Check for updates here when needed.'],
    'job.queued':['已加入优先更新，原有结果仍可查询。','Queued for priority update. Existing results remain available.'],
    'job.running':['正在下载并校验此英雄的双向数据，原有结果仍可查询。','Downloading and verifying both directions for this champion. Existing results remain available.'],
    'job.scheduled':['即将在后台检查数据，可继续查询本地数据。','A background data check is scheduled. Local queries remain available.'],
    'job.waiting':['其他窗口正在更新或整理，等待完成后继续。','Waiting for another window to finish updating or cleaning up.'],
    'job.checking':['正在检查原站数据版本。','Checking the source data version.'],
    'job.hero':['正在优先更新单英雄数据，可继续本地查询。','Updating a priority champion request. Local queries remain available.'],
    'job.focused':['正在优先更新当前与最近查询的英雄。','Updating current and recently queried champions first.'],
    'job.downloading':['正在补齐完整离线库，中断后可继续下载。','Downloading the full offline library. Interrupted progress can resume later.'],
    'job.verifying':['正在校验数据完整性与原站版本。','Verifying data integrity and the source version.'],
    'job.complete':['更新完成，数据已保存在本地。','Update complete. Data is saved locally.'],
    'job.unchanged':['检查完成，数据版本未变化。','Check complete. The data version has not changed.'],
    'job.cancelled':['更新已停止，原有数据仍可使用。','Update stopped. Existing data remains available.'],
    'job.error':['更新未完成，原有数据仍可查询。详情见日志目录，可稍后手动重试。','Update incomplete. Existing data remains available. See the logs for details and retry manually later.'],
    'job.stopping':['正在停止，已完成的下载会保留。','Stopping. Completed downloads will be preserved.'],
    'metric.value':['关系值 {0}','Relationship value {0}'], 'metric.sourceNote':['原站补充说明','Source note'],
    'metric.original':['原站原文：{0}','Original source text: {0}']
  };
  const backend={
    '当前英雄的更新记录不可用，已使用原有完整数据。':'The champion update is unavailable. The existing complete library is being used.',
    '优先使用的数据未能提供此结果，已使用备用数据。可在数据管理中更新或恢复程序包。':'The preferred data did not contain a usable result. Fallback data is shown. Update the library or restore the complete package.',
    '这是旧版保存的单条结果；数据管理可补齐此筛选组合的完整离线库。':'This is a legacy saved result. Download the complete library for these filters in Data management.',
    '这条本地数据损坏，且没有可用备份。请更新离线库或重新解压程序包。':'This local record is damaged and has no usable backup. Update the library or extract the complete package again.',
    '本地尚未保存这个筛选组合的结果。可恢复默认条件，或在“数据管理”中下载此组合；不会用其他条件的结果代替。':'This filter profile is not saved locally. Restore default filters or download the profile in Data management. Results from other filters will not be substituted.',
    '完整离线库未能加载，请恢复 data 文件夹。':'The complete library could not be loaded. Restore the data folder from the app package.',
    '当前为离线模式。请使用“启动Luna.cmd”后再更新。':'Offline mode is active. Start Luna normally to enable network updates.',
    '当前为离线模式，不能联网更新。':'Offline mode disables network updates.',
    '更新队列已满，请稍后再试。':'The update queue is full. Try again shortly.',
    '单英雄更新队列已满，请稍后再试。':'The champion update queue is full. Try again shortly.',
    '查询条件无效':'Invalid query parameters.', '筛选档位无效':'Invalid filter levels.',
    '英雄、分路或筛选条件无效':'Invalid champion, lane or filters.',
    '请从本地程序打开':'Open this page from the local Luna app.',
    '本地数据未能读取，请查看数据管理中的状态，或从完整程序包恢复 data 文件夹。':'Could not read local data. Check Data management or restore the data folder from the complete app package.'
  };
  const translations={...en,...backend};
  const reverse=new Map(Object.entries(translations).map(([zh,value])=>[value,zh]));
  for(const [key,pair] of Object.entries(extra))for(const value of pair)reverse.set(value,key);
  let language='zh-CN';
  const t=(key,...args)=>{
    const value=extra[key]?.[language==='en'?1:0]??(language==='en'?translations[key]??key:key);
    return String(value??'').replace(/\{(\d+)\}/g,(_,i)=>String(args[Number(i)]??'{'+i+'}'));
  };
  function message(value){
    if(!value)return '';
    const key=reverse.get(value)||value;
    if(Object.hasOwn(extra,key)||Object.hasOwn(translations,key))return t(key);
    // Unknown upstream exceptions remain in local logs; do not leak unlocalized
    // file paths or credentials into the English interface.
    return language==='en'&&/[\u3400-\u9fff]/.test(value)?t('message.unknown'):String(value);
  }
  function apply(document){
    document.documentElement.lang=language;document.title=t('app.title');
    for(const node of document.querySelectorAll('[data-i18n]'))node.textContent=t(node.dataset.i18n).replaceAll('&nbsp;','\u00a0');
    for(const attr of ['title','aria-label','placeholder'])for(const node of document.querySelectorAll('[data-i18n-'+attr+']'))node.setAttribute(attr,t(node.getAttribute('data-i18n-'+attr)));
  }
  function jobMessage(job,hero=false){
    if(!job)return '';
    if(job.state==='error')return t('job.error');
    if(job.message?.startsWith('正在停止'))return t('job.stopping');
    if(language==='zh-CN'&&job.message)return job.message;
    const state=job.state==='complete'&&!hero&&!job.done&&!job.focusedUpdated?'unchanged':job.state||'idle';
    return t('job.'+(Object.hasOwn(extra,'job.'+state)?state:'running'));
  }
  const groupLabel=key=>t(({all_segment:'全分段',high_mid_segment:'高 / 中分段',high_segment:'仅高分段',high_low_segment:'高 / 低分段',mid_segment:'仅中分段',mid_low_segment:'中 / 低分段',low_segment:'仅低分段'})[key]||key);
  function valueText(item,mode,core){
    if(language!=='en')return core.valueText(item,mode);
    if(item.displayText){const original=String(item.displayText);return original.replace(/严父值|大儿值/g,'Relationship value').replace(/严父|大儿/g,'Relationship').replace(/共轭/g,'Conjugate');}
    const value=Number(item.fatherValue);return t('metric.value',Number.isFinite(value)?value.toFixed(2):'—');
  }
  function metricTooltip(item){
    if(language!=='en')return item.metricTooltipText||'';
    // Preserve external prose verbatim and label it, rather than inventing a
    // translation or deriving statistics not present in the source.
    return item.metricTooltipText?t('metric.original',item.metricTooltipText):'';
  }
  return {t,message,apply,jobMessage,groupLabel,valueText,metricTooltip,translations,extra,
    setLanguage(value){language=value==='en'?'en':'zh-CN';},get language(){return language;}};
});
