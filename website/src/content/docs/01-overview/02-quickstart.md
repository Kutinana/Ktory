---
title: 快速上手 (Quickstart)
description: 5 分钟上手 Ktory 核心调用与首个剧本
sidebar:
  order: 2
---

无需安装任何软件，您可以直接访问 <a href="https://reader.ktory.ink/" target="_blank" rel="noopener noreferrer">Ktory 在线试读器</a>，来体验 Ktory 的示例剧本。您也可以使用这一平台，直接开始编写属于您的第一个 Ktory 剧本。

而接下来，本文将快速介绍如何将 Ktory 用于您自己的项目中。

正如我们在上一篇介绍中所说：**一纸 Ktory 是剧本，而你的程序就是现场导演。** 现在，让我们来写下第一幕故事，并用最简洁的 C# 代码驱动它。

## 编写第一幕剧本

在您的电脑上创建一个名为 `prologue.ktr` 的文件，并写入以下内容：

```ktory
爱丽丝: 你醒了？感觉怎么样？
  .expression(smile)

你眨了眨眼，看着眼前陌生的女孩。

#choice
  * [你是……？]
    女孩摇了摇头，并未作答。
  * [先看看周围]
    你环顾四周，这里似乎是一座古老的石塔。

爱丽丝: 我们先离开这里吧。
```

对于 Ktory 来说，它致力于让写叙事脚本如写戏剧台词一样直观：
- `爱丽丝:`：指示说话人，后文为台词。
- `.expression(smile)`：这是一个**修饰符**，附着在台词上。你可以任意定义你喜欢的修饰符，也可以不定义修饰符。
- 没有说话人的台词即为**旁白**。
- `#choice`：开启一个选择支。以 `*` 列出选项，选项以 `[]` 包裹。
- 无论玩家选择哪一项，剧情都会自然汇合到爱丽丝的最后一句台词。

## 准备提词器

Ktory 的核心（Ktory.Core）就像一位严谨冷静的**提词器**——它管理着分支走向、循环和剧情记录；而你的游戏或终端则是**现场导演**——每一次玩家按回车或点击对话框，导演给提词器一个前进信号，提词器就把下一句台词或选项交给你去展现。

打开终端，创建一个最简单的控制台应用，并引用 Ktory 核心：

```bash
dotnet new console -n KtoryDemo
cd KtoryDemo
dotnet add reference ../Ktory/src/Ktory.Core/Ktory.Core.csproj
```

> **提示**：如果您正在使用 Unity 开发，无需手动引用源码，直接通过 Unity Package Manager（UPM）填入 git 地址即可安装，详情请参见 [Unity 集成指南](/03-integration/01-unity-upm/)。

将刚才写好的 `prologue.ktr` 文件放进 `KtoryDemo` 项目目录中。

## 让剧本开演吧

将项目中的 `Program.cs` 替换为以下内容：

```csharp
using System;
using System.IO;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

// 1. 读取并解析剧本文件
var file = KtoryParser.Parse(File.ReadAllText("prologue.ktr"));

// 2. 创建调度器（提词器）
var player = new KtorySequencer(file);

// 监听修饰符派发：当剧本出现表情、音效或动作提示时通知我们
player.OnTagsDispatched += tags =>
{
    foreach (var tag in tags)
    {
        Console.WriteLine($"  [演出标记] {tag.Name}");
    }
};

// 以中文启动剧本（启动后提词器会自动准备好第一拍）
player.Start(requestedLocale: "zh");

// 3. 循环推进：玩家敲回车就走一步，遇到选项就输入数字
while (player.Status != ExecutionStatus.Completed)
{
    var token = player.CurrentPresentationToken;
    // 遇到选项支：等待玩家输入选择
    if (player.Status == ExecutionStatus.AwaitingChoice)
    {
        var menu = player.CurrentChoice!;
        Console.WriteLine("\n=== 请做出选择 ===");
        for (int i = 0; i < menu.Options.Count; i++)
        {
            var opt = menu.Options[i];
            Console.WriteLine($"{i + 1}. {opt.Label} {(opt.CanSelect ? "" : "(已选过)")}");
        }

        Console.Write("> ");
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int choice) &&
            choice >= 1 && choice <= menu.Options.Count &&
            menu.Options[choice - 1].CanSelect)
        {
            // 提交选择：核心会自动推进到所选分支的第一拍
            player.SubmitChoice(menu.Options[choice - 1].Id, token);
        }
        continue;
    }

    // 普通的一拍（角色台词或旁白）
    var beat = player.CurrentPayload;
    if (beat is null) break;

    string speaker = string.IsNullOrEmpty(beat.Speaker) ? "旁白" : beat.Speaker;
    Console.WriteLine($"[{speaker}] {beat.Content}");

    // 等待玩家敲击回车，迈出下一步
    Console.ReadLine();
    player.Step(token);
}

Console.WriteLine("\n剧本播放完毕！");
```

现在，在终端中输入：

```bash
dotnet run
```

按下回车键。现在，您就能亲手推动爱丽丝与您的这场相遇。

## 几个小细节

- **拍（Beat）**：对白或旁白的逻辑停顿点。每当你按一次回车或点击一次对话框，核心就走过一“拍”。
- **提词器不管打字机**：核心只负责告诉游戏引擎“现在该说这句话了”，至于这句话是用打字机逐字蹦出、还是淡入淡出、还是配着语音播放，完全由游戏引擎自行决定。
- **选择即推进**：当调用 `SubmitChoice()` 提交选项时，核心已经帮你翻到了所选分支的第一句话并停下，不需要也不应该再额外调用一次 `Step()`。
- **会话与拍令牌（`PresentationToken`）**：显示内容时保存 `player.CurrentPresentationToken`，之后提交输入时携带这份令牌。它同时标识会话与拍；旧回调或已消费提交的重复投递会被忽略，仅记后台诊断。异步回调不能在执行时重新读取新令牌。

## 下一步

现在，你已经掌握了 Ktory 最核心的交互流程！接下来你可以：

- 前往 [剧本语法规范](/02-syntax/01-anchor-decorator/)，解锁循环、条件判断与自定义修饰符。
- 探索 [多语言与国际化](/02-syntax/04-localization/)，感受无需复制多套剧本即可无缝热切语言的便利。
- 查阅 [Unity 集成指南](/03-integration/01-unity-upm/)，将提词器接入真正的游戏画面与演出世界。

