---
title: 快速上手 (Quickstart)
description: 5 分钟上手 Ktory 核心调用与首个剧本
sidebar:
  order: 2
---

> 本页遵循 [Ktory 设计原则](/01-overview/03-design-principles/)，实现边界与长期目标以该原则及其权威文档为准。

# 快速上手

下面用真实 C# API 跑通对白、译文与选择。示例采用控制台手动逐拍：按回车推进，输入数字选择，不执行打字机、`.next` 或 `.wait` 的表现时序。

## 1. 引用核心

安装 .NET 9 SDK，克隆仓库，并在 `Ktory` 目录旁建立控制台项目：

```bash
dotnet new console -n KtoryDemo
cd KtoryDemo
dotnet add reference ../Ktory/src/Ktory.Core/Ktory.Core.csproj
```

Unity 使用生成的 `com.ktory.unity` 包，安装路径与版本固定方式见 [Unity UPM 接入](/03-integration/01-unity-upm/)。

## 2. 编写 `prologue.ktr`

在控制台项目目录保存下面的脚本。两个分支之后会汇流至最后一句。

```ktory
@defaultLang: zh
@speaker alice: zh="爱丽丝" | en="Alice" | ja="アリス"

alice:
  @zh: 你醒了？感觉怎么样？
  @en: You're awake. How do you feel?
  @ja: 目が覚めた？ 気分はどう？
  .expression(smile)

#choice
  * [@zh: "向她道谢"]
    [@en: "Thank her"]
    [@ja: "お礼を言う"]
    : 谢谢你救了我。
  + [@zh: "先看看周围"]
    [@en: "Look around first"]
    [@ja: "周りを見る"]
    : 这里像是一座古老的塔。

alice: 我们先出去吧。
```

## 3. 保存为 `Program.cs` 并运行

```csharp
using System;
using System.IO;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

var file = KtoryParser.Parse(File.ReadAllText("prologue.ktr"));
var player = new KtorySequencer(file);
player.OnTagsDispatched += tags =>
{
    foreach (var tag in tags)
        Console.WriteLine($"[tag] {tag.Name}");
};
player.Start(requestedLocale: "zh");

while (player.Status != ExecutionStatus.Completed)
{
    if (player.Status == ExecutionStatus.AwaitingChoice)
    {
        var menu = player.CurrentChoice!;
        for (int i = 0; i < menu.Options.Count; i++)
        {
            var option = menu.Options[i];
            Console.WriteLine($"{i + 1}. {option.Label} (enabled: {option.CanSelect})");
        }
        string? input = Console.ReadLine();
        if (input is null) break;
        if (!int.TryParse(input, out int number) ||
            number < 1 || number > menu.Options.Count ||
            !menu.Options[number - 1].CanSelect)
            continue;

        player.SubmitChoice(menu.Options[number - 1].Id, menu.PresentationId);
        continue;
    }

    var beat = player.CurrentPayload;
    if (beat is null)
        throw new InvalidOperationException($"Unexpected status: {player.Status}");

    Console.WriteLine(beat.StepType == StepType.Text
        ? $"[{beat.ActualLanguage}] {beat.Speaker}: {beat.Content}"
        : $"[directive] #{beat.Content}");
    if (Console.ReadLine() is null) break;
    player.Step(beat.PresentationId);
}
```

```bash
dotnet run
```

`Start()` 已经输出第一拍。选择使用 `Id` 提交；`SubmitChoice()` 已推进到所选分支的第一拍，因此之后直接返回循环读取，不能为同一次选择补一次 `Step()`。`PresentationId` 关联当前输出，可帮助拒绝当前播放中的过期输入；重启会话时宿主仍须撤销旧回调。

演出修饰符在这个例子里只被打印。游戏状态、立绘映射与计时由宿主提供，实际应用应依据 [设计原则](/01-overview/03-design-principles/) 处理这些边界。

继续阅读 [语法](/02-syntax/01-anchor-decorator/)、[语言切换](/02-syntax/04-localization/) 与 [独立 C# 接入](/03-integration/02-standalone-csharp/)。
