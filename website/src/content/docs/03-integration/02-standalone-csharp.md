---
title: C# 独立应用集成
description: 共享核心 API、求值扩展和宿主时序
sidebar:
  order: 2
---

> 本页遵循 [Ktory 设计原则](/01-overview/03-design-principles/)，实现边界与长期目标以该原则及其权威文档为准。

# C# 独立应用接入

`Ktory.Core` 目标框架为 `netstandard2.1`，无外部 NuGet 依赖。宿主需提供兼容运行时；仓库的控制台式示例与 Web 工具使用 .NET 9。其他引擎可以探索接入，但核心可移植不代表对应适配已经交付。

完整的手动控制台程序见 [快速上手](/01-overview/02-quickstart/)。核心调用契约如下：

| 操作 | API 与结果 |
| --- | --- |
| 解析 | `KtoryParser.Parse(source)` 返回 `KtoryFile` |
| 创建 | `new KtorySequencer(file)` |
| 启动 | `Start(requestedLocale: "zh")` 已执行到首个输出 |
| 普通推进 | `Step(token)` |
| 提交当前菜单 | `SubmitChoice(option.Id, token)`，已执行到分支首个输出 |
| 语言切换 | `SetLanguage("en", token)`，重新读取当前载荷或菜单 |
| 输出 | `Status`、`CurrentPayload`、`CurrentChoice` |
| 演出意图通知 | `OnTagsDispatched` |

## 条件与游戏状态

`IExpressionEvaluator` 连接宿主的条件求值。游戏 Sequencer 使用内置 `DefaultExpressionEvaluator` 时，未知条件产生 `Warning` 并按 `false` 处理；已知条件正常求值。独立试读显式设置 `IgnoreUnknownConditions = true`，不能将试读的宽松结果当作背包或任务状态。正式游戏应提供所需的真实条件实现。核心维护会话内选择消耗，不接管游戏世界状态。

## 时序与输入

原始点击、计时结束与核心推进是不同操作。控制台例子只做手动停止；要采用 `.next`、`.wait`、`.skippable`，需接入表现时序。配套 `PresentationController` 的事件与调用顺序见 [Unity 接入中的时序表](/03-integration/01-unity-upm/#接入配套表现时序)，这些接口本身不依赖 Unity。

显示内容或安排任务时保存 `var token = player.CurrentPresentationToken`；异步回调携带这份完整令牌。每次 Start 更换会话，关闭／替换前调用 `InvalidateSession()` 并清理宿主回调。过期或重复提交仅产生后台 `InputIgnored`，不进入剧情错误；当前有效菜单的非法选项仍报错。无令牌或仅 `PresentationId` 的旧接口只用于同步兼容，不能保证跨会话隔离。令牌不是永久内容 ID 或存档格式。

[真实 Web 试读器](https://ktory.vercel.app) 使用共享 C# 核心。官网首页的互动展示是示意模拟，不能用于验证语义或宿主演出。


`SetLanguage(locale, token)` 是会话设置：只要捕获的会话仍有效，同会话已换拍或自然结束后仍可修改语言；旧会话的请求会被忽略。
