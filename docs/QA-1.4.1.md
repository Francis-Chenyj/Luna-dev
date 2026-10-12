# Luna 1.4.1 verification / 验证记录

日期 / Date: 2026-10-11

## 分路与界面逻辑 / Lane and interface logic

`node --test tests/core.test.cjs tests/ui.test.cjs`：62 项通过。

新增覆盖：首次常用分路、保持非默认分路、搜索/候选/最近英雄/关系卡片的一致性、缺失分路时不发送查询或更新请求、明确选择后继续、取消旧请求与重试、历史导航、返回首页、目录更新删除原分路、英雄完全没有分路。

62 tests passed. Coverage includes initial lane defaults; retention across search, suggestions, recent champions and matchup cards; no query/update requests for an unavailable lane; explicit recovery; cancellation of old requests/retries; history/home navigation; catalog changes; champions with no lanes.

## 图标 / Icons

`tests/run-icons.ps1`：Windows 原生加载 14 个实际尺寸（16、20、24、30、32、36、40、48、60、64、72、96、128、256）全部通过。逐像素检查 L 内外邻接区域，只有不透明黑色与原绿色 #bdff62，无重采样模糊过渡；背景圆角单独抗锯齿。

候选版实际 Edge 应用窗口的诊断曾返回 DPI 192（200%）、小图标 32 px、大图标 64 px，两者均为已应用状态。自动截图检查因 computer-use 工具无法可靠确定 Edge 应用窗口 URL 而停止，未完成任务栏前后截图对比。未改变其他浏览器窗口、系统缩放或全局图标缓存。

Windows decoded all 14 native icon sizes successfully. Pixel checks confirmed opaque black/lime L edges without resampling blur. The rounded background is antialiased separately. Runtime diagnostics for the candidate Edge app window reported 192 DPI, 32 px small and 64 px large icons, both applied. Computer-use stopped the screenshot check because it could not reliably identify the Edge app window URL, so a taskbar before/after screenshot comparison was not completed. Other browser windows, display scaling and the global icon cache were not changed.

## 离线数据 / Offline data

本机隔离测试实例：`tests/server.mjs http://127.0.0.1:18741 --offline`，856 条关系全部通过原始快照逐项比对。该次本地 HTTP 耗时中位数 4.75 ms，P95 7.94 ms，最大 20.70 ms。此为本机该次测试测量，不是所有机器的速度保证。

All 856 records matched the source snapshot through an isolated local server. Measured local HTTP latency: median 4.75 ms, P95 7.94 ms, maximum 20.70 ms. These are measurements from this machine/run, not a performance guarantee for every computer.

程序包中 1,036 个 data 文件与 1.4.0 一致；分段下拉菜单逐字对比一致。中英文 README 保留作者 FrancisChen。

All 1,036 data files are unchanged from 1.4.0. The rank dropdown is identical. Both README languages retain the author credit FrancisChen.

`tests/run-stability.ps1` 全部通过：离线查询不访问上游、更新失败保留旧数据、断点续传、同版跳过下载、更新期间查询、取消请求释放资源、回退与自动清理、数据时间来源区分。测试仅操作独立临时目录。

The isolated stability suite also passed: local-only queries, safe update failure, resume without duplicate records, unchanged-version download skipping, reads during updates, cancellation/resource release, fallback and pruning, and truthful timestamp provenance. Tests operated only in separate temporary directories.

## Native integration references

- [Windows icon sizes and scaling](https://learn.microsoft.com/zh-cn/windows/apps/design/iconography/app-icon-construction)
- [Windows taskbar icon resource property](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchiconresource)
- [Chromium application-window names](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/web_applications/web_app_helpers.cc)

The desktop module only accepts Edge executable paths and this installation/port's dedicated application tag. It holds and releases only its own icon handles; query threads do not wait for it. A protected localhost diagnostic endpoint, `/api/window-icon`, is available with the same `X-Luna: 1` marker as the other local APIs.
