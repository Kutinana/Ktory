---
title: C# スタンドアロンアプリ統合
description: 共有コア API、評価の拡張とホストの時間制御
sidebar:
  order: 2
---

> このページは [Ktory の設計原則](/ja/01-overview/03-design-principles/) に従い、現在の契約と長期目標を区別します。

# 独立 C# アプリへの接続

`Ktory.Core` は `netstandard2.1` を対象とし、外部 NuGet 依存を持ちません。ホストには対応ランタイムが必要です。リポジトリのコンソール例と Web ツールは .NET 9 を使います。他エンジンとの接続は検討できますが、移植性はアダプターの提供済みを意味しません。

手動コンソールの完全なコードは [クイックスタート](/ja/01-overview/02-quickstart/) にあります。コア API は次のとおりです。

| 操作 | API と結果 |
| --- | --- |
| 解析 | `KtoryParser.Parse(source)` が `KtoryFile` を返す |
| 作成 | `new KtorySequencer(file)` |
| 開始 | `Start(requestedLocale: "ja")` が最初の出力まで進む |
| 通常の拍を進める | `Step(token)` |
| 現在の選択を確定 | `SubmitChoice(option.Id, token)` が選択先の最初の出力まで進む |
| 言語切替 | `SetLanguage("en", token)` の後に現在の出力を読み直す |
| 出力を読む | `Status`、`CurrentPayload`、`CurrentChoice` |
| 演出意図を受け取る | `OnTagsDispatched` |

## 条件とゲーム状態

`IExpressionEvaluator` はホストの条件評価を接続するインターフェースです。ゲームの Sequencer が組み込み `DefaultExpressionEvaluator` を使用すると、未知の条件で `Warning` を出し、`false` として処理します。既知の条件は通常どおり評価します。独立試読では `IgnoreUnknownConditions = true` を明示的に指定します。ゲーム側は必要な実条件を実装し、試読の緩い結果を所持品やクエスト状態として使わないでください。コアが管理するのは選択肢の消費履歴であり、ゲーム世界の状態ではありません。

## 時間と入力

クリック、タイマー終了、コアの進行は別の操作です。コンソール例は手動で停止します。`.next`、`.wait`、`.skippable` には表示時間の処理が必要です。共通 `PresentationController` の接続は [Unity 接続ページ](/ja/03-integration/01-unity-upm/) の表を参照してください。API 自体は Unity に依存しません。

表示または処理の予約時に `var token = player.CurrentPresentationToken` を保存し、非同期コールバックにはこの完全なトークンを渡します。Start ごとに新しいセッションになり、終了・置換前には `InvalidateSession()` とホスト側のコールバック解除を行います。古い入力や処理済み送信の再送はバックグラウンドの `InputIgnored` 診断だけとなり、現在のメニューに対する不正な選択は引き続きエラーです。トークンなし、または `PresentationId` だけの旧 API は同期互換用で、セッション間の隔離を保証しません。トークンは恒久的な内容 ID やセーブデータではありません。

[実際の Web 試読器](https://ktory.vercel.app) は共有 C# コアを使います。トップページの対話表示は説明用のシミュレーションであり、実行意味やホスト演出の検証には使えません。


`SetLanguage(locale, token)` はセッション設定です。同じ有効なセッションなら、拍が進んだ後や自然終了後も言語を変更できますが、古いセッションからの要求は無視します。
