---
title: Ktory とは
description: Ktory ノベルスクリプトシステムの立ち位置とコア設計思想
sidebar:
  order: 1
---

**Ktory は、対話駆動型ゲームのための物語制作・接続システムです。** 一編の Ktory スクリプトは劇の一幕のようなものです。テキスト、セリフの順序、選択肢、分岐をひとつにまとめ、音楽や視覚的な演出の指示を注記します。そしてゲームエンジンは現場の舞台監督として、具体的な表現、ワールドの状態、実際のイベント判定を担当します。

従来のスクリプト手法と比べて、Ktory は複雑な物語を持つゲームの開発により適しています。

## 戯曲のように物語を組み立てる

```ktory
@defaultLang: ja
@speaker alice: ja="アリス" | en="Alice"

alice:
  @ja: 今日の風は、どこかいつもと違うみたい。
  @en: The wind feels different today.
  .expression(pensive)

#.sfx("wind.ogg").wait(1).next()

#choice.loop
  * [出発する]
    alice: 行こう。
    -> break
  + [周りを見る]
    : 窓の外で落ち葉が渦を巻く。
```

セリフと地の文が「拍（ビート）」を形成し、修飾子がその拍に付随します。まるで脚本を書くように、ひとつひとつの節拍がユーザーインタラクションとなり、ひとつひとつの修飾子がセリフの演出を彩ります。

## ひとつのコア、多様な実行環境

Ktory は C# で構文解析と実行を行うコアを中心に据えていますが、大小を問わず様々な環境で利用できます。Unity や Godot のような大規模なゲームエンジンから、Web ブラウザや VS Code 拡張機能のような軽量で柔軟な環境、さらにはコマンドラインであっても Ktory スクリプトを再生可能です。

C# コアが分岐、呼び出し、ループ、およびセッション履歴を管理します。ホスト環境が表示、リソース、時計（タイマー）、プレイヤー入力、およびゲーム状態を管理します。

Ktory は現在、初期開発段階にあります。現時点では以下のプラットフォームに対応しています：
- Unity: Package Manager の「Install package from git URL...」で `https://github.com/Kutinana/Ktory.git#upm` を入力。
- VS Code: [VS Code 拡張機能](vscode:extension/ktory.ktory)。
- Web: [Web Reader](https://reader.ktory.ink/)。

> コアの移植性が高いからといって、あらゆるエンジン向けのアダプターがすでに提供されているわけではありません。さらなるプラットフォームへの対応も鋭意進行中です。
