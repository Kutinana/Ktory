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
| 通常の拍を進める | `Step(currentPayload.PresentationId)` |
| 現在の選択を確定 | `SubmitChoice(option.Id, currentChoice.PresentationId)` が選択先の最初の出力まで進む |
| 言語切替 | `SetLanguage("en")` の後に現在の出力を読み直す |
| 出力を読む | `Status`、`CurrentPayload`、`CurrentChoice` |
| 演出意図を受け取る | `OnTagsDispatched` |

## 条件とゲーム状態

`IExpressionEvaluator` はホストの条件、補間、操作を接続するインターフェースです。現在の既定評価器は基本試読向けで、未知の条件を既定で許可します。実際のゲームは独自の評価器を提供し、試読の緩い結果を所持品やクエスト状態として使わないでください。コアが管理するのは選択肢の消費履歴であり、ゲーム世界の状態ではありません。

## 時間と入力

クリック、タイマー終了、コアの進行は別の操作です。コンソール例は手動で停止します。`.next`、`.wait`、`.skippable` には表示時間の処理が必要です。共通 `PresentationController` の接続は [Unity 接続ページ](/ja/03-integration/01-unity-upm/) の表を参照してください。API 自体は Unity に依存しません。

`PresentationId` は入力と現在の出力を結び付けるもので、恒久的な内容 ID やセーブ形式ではありません。再開時には古いコールバックを取り消します。構文、無効な選択、制御フローのエラーは捕捉してソース位置を表示してください。ホスト機能の不足と構文の誤りは異なる問題です。

[実際の Web 試読器](https://ktory.vercel.app) は共有 C# コアを使います。トップページの対話表示は説明用のシミュレーションであり、実行意味やホスト演出の検証には使えません。
