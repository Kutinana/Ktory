---
title: Quickstart
description: Get started with Ktory core calling and your first script in 5 minutes
sidebar:
  order: 2
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# Quickstart

Use the real C# API to run dialogue, translations and choices. This console example advances manually: press Enter for a beat and enter a number for a choice. It does not implement typewriter effects or `.next` / `.wait` timing.

## 1. Reference the core

Install the .NET 9 SDK, clone the repository and create a console project beside the `Ktory` directory:

```bash
dotnet new console -n KtoryDemo
cd KtoryDemo
dotnet add reference ../Ktory/src/Ktory.Core/Ktory.Core.csproj
```

Unity uses the generated `com.ktory.unity` package. See [Unity UPM integration](/en/03-integration/01-unity-upm/) for installation and version pinning.

## 2. Write `prologue.ktr`

Save this script in the console project directory. Both branches converge at the final line.

```ktory
@defaultLang: en
@speaker alice: en="Alice" | zh="爱丽丝" | ja="アリス"

alice:
  @en: You're awake. How do you feel?
  @zh: 你醒了？感觉怎么样？
  @ja: 目が覚めた？ 気分はどう？
  .expression(smile)

#choice
  * [@en: "Thank her"]
    [@zh: "向她道谢"]
    [@ja: "お礼を言う"]
    : Thank you for helping me.
  + [@en: "Look around first"]
    [@zh: "先看看周围"]
    [@ja: "周りを見る"]
    : This looks like an old tower.

alice: Let's go outside.
```

## 3. Save as `Program.cs` and run

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
player.Start(requestedLocale: "en");

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

`Start()` already produces the first beat. Submit a choice by `Id`. `SubmitChoice()` advances into the selected branch, so immediately return to reading its output; do not add a `Step()` for the same selection. `PresentationId` associates input with the current output and helps reject stale input during playback. A host must still cancel callbacks from a previous session when restarting.

This example only prints presentation decorators. The host supplies game state, portrait bindings and timing according to the [design principles](/en/01-overview/03-design-principles/).

Continue with [syntax](/en/02-syntax/01-anchor-decorator/), [language switching](/en/02-syntax/04-localization/) and [standalone C# integration](/en/03-integration/02-standalone-csharp/).
