---
title: クイックスタート
description: 5分でマスターする Ktory の基本呼び出しと最初のスクリプト作成
sidebar:
  order: 2
---

> このページは [Ktory の設計原則](/ja/01-overview/03-design-principles/) に従い、現在の契約と長期目標を区別します。

# クイックスタート

実際の C# API でセリフ、訳文、選択肢を動かします。このコンソール例は手動で拍を進め、Enter で次の拍、数字で選択肢を決定します。タイプライターや `.next` / `.wait` の時間制御は実装しません。

## 1. コアを参照する

.NET 9 SDK を用意し、リポジトリを複製した `Ktory` ディレクトリの隣にコンソールプロジェクトを作ります。

```bash
dotnet new console -n KtoryDemo
cd KtoryDemo
dotnet add reference ../Ktory/src/Ktory.Core/Ktory.Core.csproj
```

Unity では生成済みの `com.ktory.unity` パッケージを使います。導入とバージョン固定は [Unity UPM 統合](/ja/03-integration/01-unity-upm/) を参照してください。

## 2. `prologue.ktr` を作る

次の脚本をコンソールプロジェクトのディレクトリに保存します。どちらの分岐も最後の一文に合流します。

```ktory
@defaultLang: ja
@speaker alice: ja="アリス" | en="Alice" | zh="爱丽丝"

alice:
  @ja: 目が覚めた？ 気分はどう？
  @en: You're awake. How do you feel?
  @zh: 你醒了？感觉怎么样？
  .expression(smile)

#choice
  * [@ja: "お礼を言う"]
    [@en: "Thank her"]
    [@zh: "向她道谢"]
    : 助けてくれてありがとう。
  + [@ja: "周りを見る"]
    [@en: "Look around first"]
    [@zh: "先看看周围"]
    : 古い塔のようだ。

alice: まずは外に出よう。
```

## 3. `Program.cs` に保存して実行する

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
player.Start(requestedLocale: "ja");

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

`Start()` は最初の拍まで実行します。選択肢は `Id` で決定します。`SubmitChoice()` も選択先の最初の拍まで進むので、その出力を読むため直ちにループに戻り、同じ選択に追加の `Step()` を送らないでください。`PresentationId` は入力を現在の出力に対応させ、実行中の古い入力の検出に使えます。セッションを再開する際は、ホストが前のコールバックを取り消す必要があります。

この例は演出修飾子を表示するだけです。ゲーム状態、立ち絵の対応付け、時間制御は [設計原則](/ja/01-overview/03-design-principles/) に従ってホストが担当します。

次は [構文](/ja/02-syntax/01-anchor-decorator/)、[言語切替](/ja/02-syntax/04-localization/)、[独立 C# 接続](/ja/03-integration/02-standalone-csharp/) を参照してください。
