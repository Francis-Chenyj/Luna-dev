# Luna 1.6.1 — 验证记录 / Verification

日期 / Date: 2026-10-15.

## 中文

本次只调整界面样式、阅读窗口结构及版本说明。没有改动查询、分路选择、网络更新或缓存清理算法。原始 1.6.0 交付保留；窗口验收使用独立副本，没有修改用户的语言、主题或数据。

- 源码构建成功，主程序与维护程序均为 1.6.1。
- 现有界面、翻译、文档及版本一致性检查共 81 项通过；未为这次简单样式修改添加重复的样式断言测试。
- 实际 Windows / WebView2 窗口：中文浅色及英文深色的五个设置标签布局正常。常规选中、关于悬停、Home 键返回常规的焦点边框均为圆角。
- 使用说明滚动到中部、再使用 Ctrl+End 到末尾，标题和关闭键位置保持不变；末尾链接和文字完整可读。点击关闭键可直接返回首页。
- 英文深色的长文档使用 Tab 进入正文、Ctrl+End 到末尾，标题及关闭键仍然可见，正文不被按钮遮挡；Esc 返回原设置页。
- 在首页通过 Shift+Tab 将焦点移到 Luna，只有名称下划线，没有包围图标或版本号的外框。

交付 ZIP 的逐文件校验、签名检查与离线库核对结果保存在交付目录的 `verification` 中。本版未重新执行 1.6.0 的六轮 30 秒会话和大量缓存基准；那些结果仍归属于 [1.6.0 验证记录](QA-1.6.0.md)。本次没有覆盖所有 Windows、缩放比例、窄窗口及高对比度组合。

## English

This revision changes presentation, reading-dialog structure and version documentation. Query, lane-selection, network-update and cache-cleanup algorithms are unchanged. The original 1.6.0 delivery is retained. Native checks used an isolated copy, leaving the user's preferences and data intact.

- Both executables build as 1.6.1; 81 existing UI, translation, documentation and version checks pass.
- Native Windows/WebView2 review covered Chinese/light and English/dark Settings layouts. Selected General, hovered About and keyboard focus after Home all have rounded outlines.
- Help was scrolled through its middle and to the end with Ctrl+End. The heading/close control stayed in place, final text remained readable, and the close button returned directly to the home page.
- A long document in English/dark was focused with Tab and scrolled to the end with Ctrl+End. Its header and close control remained available without covering text; Esc returned to Settings.
- Shift+Tab focus on the Luna home link underlined only the name, with no enclosing logo/version outline.

Delivered-archive integrity, signature and offline-library results are kept in the delivery's `verification` directory. The 1.6.0 six-session and large-cache benchmarks were not repeated for this presentation fix; their results remain attributed to [1.6.0](QA-1.6.0.md). This is not an exhaustive OS, scaling, narrow-window or high-contrast matrix.
