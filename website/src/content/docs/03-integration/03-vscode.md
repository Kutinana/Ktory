---
title: VS Code 扩展安装
description: 安装 VSIX，在编辑器中离线试读当前 Ktory 剧本
sidebar:
  order: 3
---

本节将介绍如何安装 Ktory for VS Code 插件，让您能够在 VS Code 中快速编辑 Ktory 剧本，并进行便捷的离线试读。

## 安装插件

若您使用的是 VS Code，您可以直接在 <a href="https://marketplace.visualstudio.com/items?itemName=Kutinana.ktory" target="_blank">Extension Marketplace</a> 中搜索 "Ktory" 来安装插件。或直接 <a href="vscode:extension/ktory.ktory" target="_blank">点此链接以安装</a>。

若您使用的是衍生自 VS Code 的编辑器，如 Cursor, Antigravity IDE 等，您可以在其扩展市场中搜索 "Ktory" 来安装扩展。

若您的编辑器不支持 Marketplace 安装，您可以从 <a href="https://open-vsx.org/extension/ktory/ktory" target="_blank">Open VSX</a> 中下载 VSIX 文件，然后手动安装。

## 写作与试读

扩展已内置 WebAssembly 运行时；试读不需要安装 .NET、启动 Unity、运行本地服务或连接在线试读器。

- 点击阅读区或按空格／Enter 快显和推进；用按钮或数字键提交选择。
- 选择入口小节、切换语言；缺译由核心按默认语言回退。
- 编辑后，点击 **重新载入编辑器内容**，或执行 **Ktory: Reload Preview from Editor**。未保存修改同样可以试读；重载会从入口开始新会话。
- 普通输入不会自动重启试读；面板会提示仍在阅读旧版本。RESTART 重放已载入的快照。
- 核心提供行列位置的错误同时出现在面板和 Problems 列表；修改源码后清除旧诊断。

试读保留现有 Reader 的打字机与基础时序策略。未知外部选项条件会被忽略，未知演出会跳过；它不验证 Unity 的资源、世界状态或演出效果。编辑器内支持基础富文本与注音，移除可执行 HTML、资源加载元素及任意 HTML 属性。

## 开发与分发

从仓库的 `src/Ktory.VSCode` 执行 `pnpm install --frozen-lockfile`、`pnpm build`、`pnpm test`、`pnpm test:integration`、`pnpm package`。开发构建需要 .NET SDK 9、Node.js 20+ 和 pnpm 10；安装包的使用者不需要这些工具。

TextMate grammar 在扩展目录唯一维护，门户直接引用它。生成的 `reader/` 和 VSIX 不能作为另一套源码修改。完整流程见 [扩展 README](https://github.com/Kutinana/Ktory/blob/main/src/Ktory.VSCode/README.md)。
