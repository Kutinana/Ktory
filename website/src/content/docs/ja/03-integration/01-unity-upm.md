---
title: Unity UPM 統合ガイド
description: 生成済み Unity パッケージ、実際のコア API と既存ホストの接続境界
sidebar:
  order: 1
---

> このページは [Ktory の設計原則](/ja/01-overview/03-design-principles/) に従い、現在の契約と長期目標を区別します。

# Unity UPM 統合

共有コアと Unity パッケージのソースは分かれています。`scripts/publish-upm.ps1` は `src/Ktory.Core`、Unity インポーター、アセンブリ定義を `com.ktory.unity` にまとめ、`upm` ブランチへ公開します。

## 生成済みパッケージを導入する

既存の依存を残し、`Packages/manifest.json` に次の依存を追加します。

```json
{
  "dependencies": {
    "com.ktory.unity": "https://github.com/Kutinana/Ktory.git#upm"
  }
}
```

`#upm` は更新を追うための移動するブランチです。複数端末で同じ版を再現する場合は、`#<upm-package-commit>` のように**生成された UPM パッケージのコミットまたはそのパッケージのタグ**へ固定します。ソース側 `main` のコミットを生成済みパッケージとして指定しないでください。`?path=/src/Ktory.Core` は現在のパッケージの入口ではありません。

## コアの呼び出し順

以下は、実際の API と選択の確定順を示す最小のログ確認用コンポーネントです。会話 UI や立ち絵を実装せず、既存の Unity 接続を置き換えません。Inspector で `.ktr` からインポートされた `TextAsset` を指定してください。

```csharp
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using UnityEngine;

public sealed class KtoryCoreProbe : MonoBehaviour
{
    [SerializeField] private TextAsset script;
    private KtorySequencer player;

    private void Start()
    {
        player = new KtorySequencer(KtoryParser.Parse(script.text));
        player.OnTagsDispatched += tags =>
        {
            foreach (var tag in tags) Debug.Log($"[Ktory tag] {tag.Name}");
        };
        player.Start(requestedLocale: "zh");
        LogCurrent();
    }

    public void AdvanceDisplayedBeat(long presentationId)
    {
        player.Step(presentationId);
        LogCurrent();
    }

    public void SubmitOption(string optionId, long presentationId)
    {
        player.SubmitChoice(optionId, presentationId);
        LogCurrent();
    }

    private void LogCurrent()
    {
        if (player.Status == ExecutionStatus.AwaitingChoice)
        {
            foreach (var option in player.CurrentChoice.Options)
                Debug.Log($"{option.Id}: {option.Label} / {option.CanSelect}");
        }
        else if (player.CurrentPayload != null)
            Debug.Log(player.CurrentPayload.Content);
    }
}
```

通常の進行には表示中の拍の `PresentationId` を渡します。選択ボタンにはメニューの `PresentationId` と項目の `Id` を保存します。`SubmitChoice()` は選択先の最初の拍まで進むため、同じ選択に追加の `Step()` を送らないでください。入力側は、全文即時表示や外部の進行条件を判定してから通常の進行を送ります。

## 共通の表示時間制御

ホストは次のように `PresentationController` を使うか、同じ契約を独自に実装できます。

| ホストの処理 | インターフェース |
| --- | --- |
| 作成済みプレイヤーを接続 | `new PresentationController(player)` |
| `Start()` または選択確定後の状態設定 | `SetupForCurrentBeat()` の後で現在の出力を読む |
| 毎フレームの時計更新 | `Update(Time.unscaledDeltaTime)` |
| 通常のクリック入力 | `HandleUserClick()` |
| タイプライターの自然終了 | `NotifyPrintingFinished()` |
| 自動またはクリック進行後の再描画 | `OnBeatChanged` を購読 |
| 現在の全文を即時表示 | `OnFastForwardRequested` で全文を表示し、完了通知は再送しない |
| 実行中の言語変更 | `player.SetLanguage(...)`、`RefreshLanguage()`、表示更新 |

最低待機時間と自動進行は別の条件です。`.wait` は終了時に入力制限を解除するだけで、クリックを保留しません。`.wait.next` は待機後に自動進行します。AUTO 領域やホストの自動再生も自動進行を指定できます。`.wait(s).next(t)` は全文表示完了時に同時に計時し、順序には依存しません。入力は `HandleUserClick()` に渡し、`Step()` で表示制御を迂回しないでください。互換性のための `QueuedAdvance` は常に `false` です。

既定の `AutoStepSequencer == true` ではコントローラー自身が進行します。`OnAdvanceRequested` から追加の `Step()` を呼ばないでください。リソースと立ち絵の対応付け、世界状態、外部演出の条件はホストが管理します。セッションの交換・再開や所有オブジェクトの破棄時は古い入力とコールバックを取り消し、実際の Unity プロジェクトのフィードバックでコアの境界を検証します。

## Play Mode デバッグウィンドウ

**Window → Ktory → Debugging** から開きます。既存ホストに `IKtoryDebugTarget` を実装し、実際のセッションを開始する前に `KtoryDebugRegistry` へ登録します。無効化、破棄、セッション差し替え時に登録を Dispose してください。ホストの asmdef で `Ktory.Unity` を参照し、アダプターのコードは `#if UNITY_EDITOR` で囲みます。Package Manager に **Debugging host probe** の接続例があります。

複数インスタンス、ノードとソース行、本文と話者、実際のコントローラーの入力制限と AUTO タイマー、デフォルト言語への追従と言語指定、通常の全文表示・送り、有効な選択と再起動、呼び出しスタック、ループ、消費済み選択肢を表示します。言語変更はホストの `SetLanguage`、`RefreshLanguage(false)`、UI 更新を使用し、進行やタグの再発行を行いません。標準タイマーの表示には、実際の `PresentationController` の公開が必要です。

履歴は登録後から収集し、全インスタンス合計で最大 2000 件です。フィルター、消去、コピーに対応し、ウィンドウの開閉は進行に影響しません。アニメーション、追加の入力制限、独自 AUTO は `InputBlockReason` と任意の `IKtoryDebugInfoProvider` でプロジェクト側が提供します。Package は独自修飾子の意味を推測しません。強制スキップは提供せず、UI・登録表・ログは正式ビルドに含まれません。接続と Unity 検証項目は生成パッケージ内の `DEBUGGING.md` を参照してください。Core テストは Unity UI や実際の演出の検証を代替しません。
