# Contributing to Luna / 参与开发

Luna's original code is MIT licensed. Bundled data, portraits, screenshots and other third-party material retain their own terms; see [third-party review](THIRD_PARTY_REVIEW.md) and [evidence status](THIRD_PARTY_EVIDENCE.json).

Luna 原创代码使用 MIT；提交数据、头像、截图前请提供来源和使用范围，不要把第三方内容当作原创代码重新授权。

1. Start from a branch of the source repository. Read the [developer guide](DEVELOPER_GUIDE.md) / [中文开发指南](DEVELOPER_GUIDE.zh-CN.md).
2. Keep fixes scoped to a reproducible problem. Add a regression that fails before the fix and passes afterwards. UI mocks do not establish browser layout, keyboard focus or native-window behavior: use the [manual acceptance scenarios](UI_ACCEPTANCE.md) too.
3. Run `scripts/check.ps1` with a fresh output folder under `dist`. Native WebView2 checks additionally require an interactive Windows desktop and the installed Runtime; run `tests/run-runtime-smoke.ps1 -Desktop` against a fresh packaged runtime.
4. Include what changed, reproduction steps, tests and remaining limits in the pull request. Update both user guides for user-visible changes. Do not edit historical QA claims to imply tests were repeated.
5. Never commit `cache`, local download folders, test outputs, credentials, signing keys or private logs. Packages use explicit file lists and contain a source identity manifest.

维护者在合并和发布前核对实际提交、源码包身份、验证记录及第三方证据。请先提交复现与最小修复；不要为了测试改动用户的安装目录。安全问题参见 [SECURITY.md](SECURITY.md)。
