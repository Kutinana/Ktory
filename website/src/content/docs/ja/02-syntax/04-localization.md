---
title: 多言語ローカライズ (I18N)
description: 単一ファイル内インライン多言語バリアントと副作用ゼロの即時ホットリロード
sidebar:
  order: 4
---

> このページは [Ktory の設計原則](/ja/01-overview/03-design-principles/) に従い、現在の契約と長期目標を区別します。

# 同一ファイルの多言語

`@defaultLang` でファイルの既定言語を指定し、省略時は `zh` になります。`@locale` のない本文は既定言語に属します。セリフ、話者の表示名、選択肢ラベルを一緒に管理できます。

```ktory
@defaultLang: zh
@speaker alice: zh="爱丽丝" | en="Alice" | ja="アリス"

alice:
  @zh: 你好，旅行者。
  @en: Hello, traveler.
  @ja: こんにちは、旅人さん。
  .expression(smile)

#choice
  * [@zh: "继续"]
    [@en: "Continue"]
    [@ja: "続ける"]
    alice: 我们出发吧。
```

## 出力とフォールバック

各文は要求言語を優先し、訳がなければ `defaultLang` に戻ります。`TextPayload.ActualLanguage` は実際の本文の言語、`RequestedLanguage` は要求言語です。この例で英語を要求しても、選択先の未翻訳の文は `zh` になります。その本文を英語として扱わないでください。

`@speaker` は表示名を検索する、大文字と小文字を区別した別名を定義します。ここでは `alice`、`爱丽丝`、`Alice`、`アリス` が一つの名前定義を指します。未定義の話者は記述どおりに表示されます。立ち絵や音声キャラクターの対応付けは行いません。

現在の `ChoiceOption` は翻訳済みの `Label` を持ちますが、項目ごとの `ActualLanguage` はありません。表示ラベルは項目の `Id` の代わりにはなりません。既定言語の本文も存在しない場合などは、現在のコアと仕様で確認が必要です。通常のフォールバックから追加の保証を推測しないでください。

## 再生中の切替

```csharp
player.SetLanguage("en");
```

切替は現在の本文、話者、メニューラベルを更新し、修飾子の再通知や履歴の消去は行いません。その後にホストの表示を更新します。`PresentationController` を使う場合は `RefreshLanguage()` で表示状態を更新し、言語切替のために追加の `Step()` を送らないでください。

同一ファイルの訳文は個人の執筆・翻訳に適した方式です。この段階では自動翻訳、訳文の抽出・取込、別ファイル間の同期を約束しません。
