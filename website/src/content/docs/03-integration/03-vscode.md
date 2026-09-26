---
title: VS Code 高亮与试读
description: 安装 VSIX，在编辑器中离线试读当前 Ktory 剧本
sidebar:
  order: 3
---

本页遵循 [Ktory 设计原则](/01-overview/03-design-principles/)。扩展使用同一 C# 核心和 Reader 前端，不另外解释剧情。

## 安装与打开

使用 VS Code 1.97 或更新版本。在命令面板执行 **Extensions: Install from VSIX…**，选择构建得到的 `ktory-vscode-<version>.vsix`。打开 `.ktr` 或 `.ktory` 文件，点击编辑器右上角的试读图标，或执行 **Ktory: Open Preview to the Side**。

VSIX 已内置 WebAssembly 运行时；试读不需要安装 .NET、启动 Unity、运行本地服务或连接在线试读器。当前提供本地 VSIX 构建，未因此宣称已发布 Marketplace。

## 写作与试读

- 点击阅读区或按空格／Enter 快显和推进；用按钮或数字键提交选择。
- 选择入口小节、切换语言；缺译由核心按默认语言回退。
- 编辑后，点击 **重新载入编辑器内容**，或执行 **Ktory: Reload Preview from Editor**。未保存修改同样可以试读；重载会从入口开始新会话。
- 普通输入不会自动重启试读；面板会提示仍在阅读旧版本。RESTART 重放已载入的快照。
- 核心提供行列位置的错误同时出现在面板和 Problems 列表；修改源码后清除旧诊断。

试读保留现有 Reader 的打字机与基础时序策略。未知外部选项条件会被忽略，未知演出会跳过；它不验证 Unity 的资源、世界状态或演出效果。编辑器内支持基础富文本与注音，移除可执行 HTML、资源加载元素及任意 HTML 属性。

## 开发与分发

从仓库的 `src/Ktory.VSCode` 执行 `pnpm install --frozen-lockfile`、`pnpm build`、`pnpm test`、`pnpm test:integration`、`pnpm package`。开发构建需要 .NET SDK 9、Node.js 20+ 和 pnpm 10；安装包的使用者不需要这些工具。

TextMate grammar 在扩展目录唯一维护，门户直接引用它。生成的 `reader/` 和 VSIX 不能作为另一套源码修改。完整流程见 [扩展 README](https://github.com/Kutinana/Ktory/blob/main/src/Ktory.VSCode/README.md)。
