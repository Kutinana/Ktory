---
title: 什么是 Ktory
description: Ktory 叙事脚本系统定位与核心理念
sidebar:
  order: 1
---

**Ktory 是面向对白驱动游戏的叙事创作与接入系统。** 一纸 Ktory 就好比一幕戏，它统一组织文本、对白顺序、选择与分支，注解音乐与视觉演出。而游戏引擎是现场导演，负责具体表现、世界状态和真实事件判定。

相较于传统的脚本方案，Ktory 更适合复杂叙事游戏的开发。

## 像剧本一样组织内容

```ktory
@defaultLang: zh
@speaker alice: zh="爱丽丝" | en="Alice"

alice:
  @zh: 今天的风，似乎有点不同寻常。
  @en: The wind feels different today.
  .expression(pensive)

#.sfx("wind.ogg").wait(1).next()

#choice.loop
  * [出发]
    alice: 我们走吧。
    -> break
  + [再看看周围]
    : 窗外落叶卷起旋涡。
```

对白与旁白形成拍，修饰符附着在拍上。就好比在创作一篇剧本，每一个节拍都是一次用户互动，每一个修饰符都是一句台词的修饰。

## 一个核心，无数载体

Ktory 以 C# 为解析与执行核心，但它能被用在大大小小的各类地方。庞大繁杂如 Unity 与 Godot，轻便灵活如网页与 VS Code 扩展，甚至命令行也能阅读 Ktory 脚本。

C# 核心管理分支、调用、循环和会话记录。宿主管理呈现、资源、时钟、玩家输入和游戏状态。

Ktory 仍处于早期开发阶段。目前，Ktory 已经对下列平台进行了支持：
- Unity：使用 "Install Package from git URL" 并填入 `https://github.com/Kutinana/Ktory.git#upm`。
- VS Code: [VS Code 扩展](vscode:extension/ktory.ktory)。
- Web: [Web Reader](https://reader.ktory.ink/)。

> 可移植的核心不等于已为每种引擎交付适配器，更多平台的支持还在锐意开发中。
