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
| 普通推进 | `Step(currentPayload.PresentationId)` |
| 提交当前菜单 | `SubmitChoice(option.Id, currentChoice.PresentationId)`，已执行到分支首个输出 |
| 语言切换 | `SetLanguage("en")`，重新读取当前载荷或菜单 |
| 输出 | `Status`、`CurrentPayload`、`CurrentChoice` |
| 演出意图通知 | `OnTagsDispatched` |

## 条件与游戏状态

`IExpressionEvaluator` 是连接宿主条件、插值和操作的扩展接口。当前默认求值器用于基础试读，未知条件默认放行；正式游戏应提供自己的实现，不能将试读的宽松结果当作背包或任务状态。核心维护会话内选择消耗，不接管游戏世界状态。

## 时序与输入

原始点击、计时结束与核心推进是不同操作。控制台例子只做手动停止；要采用 `.next`、`.wait`、`.skippable`，需接入表现时序。配套 `PresentationController` 的事件与调用顺序见 [Unity 接入中的时序表](/03-integration/01-unity-upm/#接入配套表现时序)，这些接口本身不依赖 Unity。

`PresentationId` 用于将输入关联当前输出，不是永久内容 ID 或存档格式。重启会话时宿主必须取消旧回调。格式错误、非法选择和控制流错误应捕获并显示源码位置；收到未知外部能力与剧本语法错误不是同一种情况。

[真实 Web 试读器](https://ktory.vercel.app) 使用共享 C# 核心。官网首页的互动展示是示意模拟，不能用于验证语义或宿主演出。
