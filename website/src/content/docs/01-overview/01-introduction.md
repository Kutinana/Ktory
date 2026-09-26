---
title: 什么是 Ktory
description: Ktory 叙事脚本系统定位与核心理念
sidebar:
  order: 1
---

> 本页遵循 [Ktory 设计原则](/01-overview/03-design-principles/)，实现边界与长期目标以该原则及其权威文档为准。

# 什么是 Ktory

**Ktory 是对白优先、宿主驱动的按拍叙事语言与运行时。** 它面向独立作者，将对白、选择、演出触发意图和同文件多语言放进同一份剧本。表现层决定这些意图怎样呈现，核心负责下一处剧情输出。

## 像剧本一样组织内容

```ktory
@defaultLang: zh
@speaker alice: zh="爱丽丝" | en="Alice"

alice:
  @zh: 今天的风，似乎有点不同寻常。
  @en: The wind feels different today.
  .expression(pensive)

#choice
  * [出发]
    alice: 我们走吧。
  + [再看看周围]
    : 窗外落叶卷起旋涡。
```

对白与旁白形成拍，修饰符附着在拍上。`.expression(pensive)` 是交给宿主的意图；它本身不创建立绘、不查找图片，也不证明某个资源绑定已经存在。

## 同一核心，明确接入边界

`src/Ktory.Core` 是共享的 C# 解析与执行实现，目标框架为 `netstandard2.1`，无外部 NuGet 依赖。仓库的独立试读与 WebAssembly 工具使用 `net9.0`；Unity 发布流程将共享源码打包为 `com.ktory.unity`。

核心管理分支、调用、循环和会话记录。宿主管理呈现、资源、时钟、玩家输入和游戏状态。文本、无文本指令和选择都是可观察的逻辑停止位置；宿主发送 `Step()` 或 `SubmitChoice()` 后，读取对应输出。

第一阶段面向视觉小说式对白和轻量交互分支。独立试读用于检查基础剧情与语言；真实资源、立绘及其他演出由宿主接入验证。可移植的核心不等于已为每种引擎交付适配器。
