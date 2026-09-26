---
title: クイックスタート
description: 5分でマスターする Ktory コアの呼び出しと最初のスクリプト
sidebar:
  order: 2
---

ソフトウェアをインストールすることなく、ブラウザから直接 <a href="https://reader.ktory.ink/" target="_blank" rel="noopener noreferrer">Ktory オンラインリーダー</a> にアクセスしてサンプルのスクリプトを体験できます。このプラットフォーム上で、自分だけの最初の Ktory スクリプトをその場で書き始めることも可能です。

ここでは、Ktory をご自身のプロジェクトに組み込んで動かす方法を素早く紹介します。

前章の紹介でも触れたように、**「一編の Ktory スクリプトは戯曲であり、あなたのプログラムは現場の舞台監督である」** と言えます。それでは、最初の物語を書き、もっともシンプルな C# コードで動かしてみましょう。

## 最初のスクリプトを書く

PC 上で `prologue.ktr` という名前のファイルを作成し、以下の内容を記述します：

```ktory
アリス: 目が覚めた？ 気分はどう？
  .expression(smile)

あなたはまばたきをして、目の前の見知らぬ少女を見つめた。

#choice
  * [君は……？]
    少女は首を横に振るだけで、何も答えなかった。
  * [周りを見てみる]
    あたりを見渡すと、どうやらここは古い石塔のようだ。

アリス: まずはここから出よう。
```

Ktory は、演劇の台本を書くかのように直感的に物語スクリプトを作成できることを目指しています：
- `アリス:`: 発話者（話者）を指定し、コロンの後にセリフが続きます。
- `.expression(smile)`: セリフに付随する**修飾子（Decorator）**です。好みの修飾子を自由に定義でき、修飾子を付けないことも可能です。
- 話者が指定されていないセリフは**地の文（ナレーション）**として扱われます。
- `#choice`: 選択肢ブロックを開始します。`*` で選択肢を並べ、内容は `[]` で囲みます。
- プレイヤーがどちらを選んでも、物語は自然にアリスの最後のセリフへと合流します。

## プロンプター（提詞器）の準備

Ktory のコア（`Ktory.Core`）は、冷静かつ厳密な**プロンプター（舞台の提詞器）**のような存在です。分岐の流れやループ、セッションの進行履歴を管理します。一方、あなたのゲームや端末は**現場の舞台監督**です。プレイヤーが Enter キーを押したりダイアログをクリックするたびに、監督がプロンプターへ前進シグナルを送り、プロンプターが次のセリフや選択肢を渡して画面上に表現させます。

ターミナルを開き、もっともシンプルなコンソールアプリを作成して Ktory コアを参照に追加します：

```bash
dotnet new console -n KtoryDemo
cd KtoryDemo
dotnet add reference ../Ktory/src/Ktory.Core/Ktory.Core.csproj
```

> **ヒント**: Unity で開発している場合は、ソースコードを手動で参照する必要はありません。Unity Package Manager（UPM）に git URL を入力して直接インストールできます。詳細は [Unity 統合ガイド](/ja/03-integration/01-unity-upm/) をご覧ください。

先ほど作成した `prologue.ktr` ファイルを `KtoryDemo` プロジェクトのディレクトリに配置します。

## スクリプトを動かす

プロジェクト内の `Program.cs` を以下の内容に置き換えます：

```csharp
using System;
using System.IO;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

// 1. スクリプトファイルを読み込んで構文解析
var file = KtoryParser.Parse(File.ReadAllText("prologue.ktr"));

// 2. シーケンサー（プロンプター）を作成
var player = new KtorySequencer(file);

// 修飾子（タグ）の通知を購読：表情や効果音、演出指示がスクリプトに現れた際に通知
player.OnTagsDispatched += tags =>
{
    foreach (var tag in tags)
    {
        Console.WriteLine($"  [演出タグ] {tag.Name}");
    }
};

// 日本語でスクリプトを開始（開始時にプロンプターが自動的に最初の拍を準備します）
player.Start(requestedLocale: "ja");

// 3. ループ進行：Enter キーで一歩進み、選択肢では番号を入力
while (player.Status != ExecutionStatus.Completed)
{
    var token = player.CurrentPresentationToken;
    // 選択肢ブロック：プレイヤーの入力を待つ
    if (player.Status == ExecutionStatus.AwaitingChoice)
    {
        var menu = player.CurrentChoice!;
        Console.WriteLine("\n=== 選択してください ===");
        for (int i = 0; i < menu.Options.Count; i++)
        {
            var opt = menu.Options[i];
            Console.WriteLine($"{i + 1}. {opt.Label} {(opt.CanSelect ? "" : "(選択済み)")}");
        }

        Console.Write("> ");
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int choice) &&
            choice >= 1 && choice <= menu.Options.Count &&
            menu.Options[choice - 1].CanSelect)
        {
            // 選択を送信：コアが自動的に選択した分岐の最初の拍まで進めます
            player.SubmitChoice(menu.Options[choice - 1].Id, token);
        }
        continue;
    }

    // 通常の一拍（キャラクターのセリフまたは地の文）
    var beat = player.CurrentPayload;
    if (beat is null) break;

    string speaker = string.IsNullOrEmpty(beat.Speaker) ? "地の文" : beat.Speaker;
    Console.WriteLine($"[{speaker}] {beat.Content}");

    // プレイヤーの Enter キー入力を待ち、次の一歩を踏み出す
    Console.ReadLine();
    player.Step(token);
}

Console.WriteLine("\nスクリプトの再生が終了しました！");
```

ターミナルで次のコマンドを実行します：

```bash
dotnet run
```

Enter キーを押せば、アリスとの出会いの物語をご自身の手で進めることができます。

## 知っておくべきポイント

- **拍（Beat）**: セリフや地の文における論理的な停止点です。Enter キーを押したりダイアログを 1 回クリックするたびに、コアは 1「拍」進みます。
- **プロンプターはタイプライター効果に関与しない**: コアの責務はゲームエンジンに「いまこのセリフを喋る順番です」と伝えることだけです。そのセリフをタイプライターのように 1 文字ずつ表示するか、フェードインさせるか、あるいはボイス付きで再生するかは、すべてゲームエンジン側が決定します。
- **選択即推進（選択するとそのまま進む）**: `SubmitChoice()` で選択肢を送信した時点で、コアは選択された分岐の最初のセリフまで進めて一時停止します。そのため、追加で `Step()` を呼び出す必要はなく、呼び出すべきでもありません。
- **セッションと拍のトークン（`PresentationToken`）**：内容を表示する時点で `player.CurrentPresentationToken` を保存し、入力とともに渡します。セッションと拍の両方を識別し、古いコールバックや処理済み送信の再送は無視して診断ログだけに記録します。非同期コールバックの実行時に新しいトークンを読み直してはいけません。

## 次のステップ

これで、Ktory のもっとも基本的な対話フローをマスターできました！ 次は以下のガイドをご覧ください：

- [スクリプト構文仕様](/ja/02-syntax/01-anchor-decorator/) へ進み、ループ、条件分岐、カスタム修飾子の使い方を学ぶ。
- [多言語化とローカライゼーション](/ja/02-syntax/04-localization/) を探索し、複数のスクリプトを用意することなくシームレスに言語をホットリロードできる利便性を体験する。
- [Unity 統合ガイド](/ja/03-integration/01-unity-upm/) を確認し、プロンプターを実際のゲーム画面や演出の世界に接続する。
