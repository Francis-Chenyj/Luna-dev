# Luna 1.5.1 — Local review / 本地审阅版

2026-10-15 · FrancisChen

- Round header controls and replace the search input's inner rectangular focus outline with one rounded outline around the search form. Keyboard focus and forced-color support remain available.
- Synchronize the page theme-color with light, dark and system preferences. Add optional Windows caption/text color synchronization for the precisely identified Luna Edge app window; unsupported systems keep their native frame.
- Preserve local queries, lane selection, background updates, existing data and user settings. No additional runtime dependency is introduced.
- This local revision has not been published to GitHub. Current public downloads remain at v1.5.0-dev.1.

- 页头控件统一圆角；搜索框去掉内部直角焦点框，改为整个圆角搜索区域高亮，保留键盘及高对比度支持。
- 网页主题色随浅色、深色、跟随系统设置同步，并对精确识别的 Luna Edge 独立窗口同步 Windows 标题栏与文字颜色；系统不支持时保留原生外观。
- 保留本地查询、分路选择、后台更新、已有数据及设置，不增加运行依赖。
- 这次是本地修订，尚未发布到 GitHub；公开下载仍为 v1.5.0-dev.1。

## Files / 文件

- `Luna-v1.5.1-Windows.zip`: complete portable runtime / 完整便携运行包。
- `Luna-v1.5.1-Source.zip`: source, build/test scripts and documentation / 源码、构建测试脚本和文档。
- `SHA256SUMS.txt` and `release-files.json`: package checksums and inventory / 包校验与逐文件清单。

Fully extract the Windows package. Use **Exit** before upgrading an existing installation, and preserve its `cache` to keep downloaded data and preferences. See the [user guide](USER_GUIDE.md), [用户指南](USER_GUIDE.zh-CN.md) and [QA record](QA-1.5.1.md).

请完整解压运行包；升级已有安装前使用程序内“退出”，保留 `cache` 以延续已下载数据和设置。实机标题栏外观验收尚未完成，具体限制见 QA 记录。
