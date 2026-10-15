# Luna 1.6.0

Author / 作者: FrancisChen

- Fast startup reads library manifests; queried records retain integrity checks. Resumable background auditing checks other records.
- Quarantines invalid champion files and damaged settings, bounded by 20 files, 10 MiB and 7 days. Existing usable libraries and backup protections remain.
- Storage reporting includes core app files, bundled data, downloaded libraries, browser profile, app update staging/rollback, diagnostics and other cache files.
- Signed app-update discovery, resumable downloads, explicit restart/install and one rollback backup. No forced restart. A compatible signed stable release must first be published by the maintainer.
- Runtime ownership manifests protect source folders and extra files. Optional per-user installation, Windows uninstall registration, and uninstall with optional local-data deletion.
- Updated Chinese and English documentation. No GitHub publication is performed by building or packaging.

- 启动只读必要清单；实际查询仍校验记录。后台完整性检查保存进度。
- 异常英雄缓存及损坏设置进入隔离区：最多 20 份、10 MiB、7 天。保留可用库和必要备份。
- 空间统计包括程序核心与自带数据、下载库、浏览器配置、程序更新与回退、日志及其他缓存。
- 程序更新验证发布者签名，支持续传，用户明确选择重启安装，保留一份旧程序用于恢复。需要维护者先发布兼容的已签名正式版本。
- 运行包文件清单保护源码与额外文件。支持可选的当前用户安装、Windows 卸载登记、可选清除本地数据。
- 更新中英文说明。构建、打包不会上传 GitHub。

See [verification](QA-1.6.0.md) / 见[验证记录](QA-1.6.0.md)。
