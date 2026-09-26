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
    public PresentationToken DisplayedToken { get; private set; }

    private void OnEnable()
    {
        player = new KtorySequencer(KtoryParser.Parse(script.text));
        player.OnTagsDispatched += tags =>
        {
            foreach (var tag in tags) Debug.Log($"[Ktory tag] {tag.Name}");
        };
        player.Start(requestedLocale: "zh");
        LogCurrent();
    }

    public void AdvanceDisplayedBeat(PresentationToken token)
    {
        player.Step(token);
        LogCurrent();
    }

    public void SubmitOption(string optionId, PresentationToken token)
    {
        player.SubmitChoice(optionId, token);
        LogCurrent();
    }

    private void OnDisable() => player?.InvalidateSession();

    private void LogCurrent()
    {
        DisplayedToken = player.CurrentPresentationToken;
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

表示時に完全な `DisplayedToken` を保存し、選択ボタンには同じメニューのトークンと項目の `Id` を渡します。コールバック作成前にローカル変数へトークンを保存し、実行時に変更後のプロパティを読み直さないでください。`SubmitChoice()` は選択先の最初の拍まで進むため、同じ選択に追加の `Step()` を送らないでください。入力側は、全文即時表示や外部の進行条件を判定してから通常の進行を送ります。

## 共通の表示時間制御

ホストは次のように `PresentationController` を使うか、同じ契約を独自に実装できます。表の `token` は表示処理や入力の予約時に保存した `presentation.CurrentPresentationToken` で、セッションと拍の両方を含みます。

| ホストの処理 | インターフェース |
| --- | --- |
| 作成済みプレイヤーを接続 | `new PresentationController(player)` |
| `Start()` または選択確定後の状態設定 | `SetupForCurrentBeat()` の後で現在の出力を読む |
| 毎フレームの時計更新 | `Update(Time.unscaledDeltaTime)` |
| 通常のクリック入力 | `HandleUserClick(token)` |
| タイプライターの自然終了 | `NotifyPrintingFinished(token)` |
| 自動またはクリック進行後の再描画 | `OnBeatChanged` を購読 |
| 現在の全文を即時表示 | `OnFastForwardRequested` で全文を表示し、完了通知は再送しない |
| 実行中の言語変更 | `player.SetLanguage(...)`、`RefreshLanguage()`、表示更新 |

最低待機時間と自動進行は別の条件です。`.wait` は終了時に入力制限を解除するだけで、クリックを保留しません。`.wait.next` は待機後に自動進行します。AUTO 領域やホストの自動再生も自動進行を指定できます。`.wait(s).next(t)` は全文表示完了時に同時に計時し、順序には依存しません。入力は `HandleUserClick(token)` に渡し、`Step()` で表示制御を迂回しないでください。互換性のための `QueuedAdvance` は常に `false` です。

既定の `AutoStepSequencer == true` ではコントローラー自身が進行します。どちらの進行イベントからも追加の `Step()` を呼ばないでください。ホストが進行を担当する場合は `false` にし、`OnAdvanceRequestedWithToken` でトークンを保存します。有効な要求を受け入れて `Step(token)` を実行した後、次の拍を設定します。古いコールバックで Setup を再実行したり、現在の計時を初期化したりしないでください。リソースと立ち絵の対応付け、世界状態、外部演出の条件はホストが管理します。

`Start()` ごとに新しいセッション識別子を生成します。終了・置換・再起動前に `InvalidateSession()` を呼び、古い入力、購読、非同期処理を取り消します。古い入力や処理済み入力の再送はバックグラウンドの `InputIgnored` 診断だけとなり、現在のメニューに対する不正な選択はエラーのままです。トークンなし、または拍番号だけの旧 API は同期互換用で、セッション間の隔離を保証しません。実際の Unity ライフサイクルは Unity 上での検証が必要です。

## Play Mode デバッグウィンドウ

**Window → Ktory → Debugging** から開きます。既存ホストに `IKtoryDebugTarget` を実装し、実際のセッションを開始する前に `KtoryDebugRegistry` へ登録します。無効化、破棄、セッション差し替え時に登録を Dispose してください。ホストの asmdef で `Ktory.Unity` を参照し、アダプターのコードは `#if UNITY_EDITOR` で囲みます。Package Manager に **Debugging host probe** の接続例があります。

複数インスタンス、ノードとソース行、本文と話者、実際のコントローラーの入力制限と AUTO タイマー、デフォルト言語への追従と言語指定、通常の全文表示・送り、有効な選択と再起動、呼び出しスタック、ループ、消費済み選択肢を表示します。言語変更はホストの `SetLanguage`、`RefreshLanguage(false)`、UI 更新を使用し、進行やタグの再発行を行いません。標準タイマーの表示には、実際の `PresentationController` の公開が必要です。

履歴は登録後から収集し、全インスタンス合計で最大 2000 件です。フィルター、消去、コピーに対応し、ウィンドウの開閉は進行に影響しません。アニメーション、追加の入力制限、独自 AUTO は `InputBlockReason` と任意の `IKtoryDebugInfoProvider` でプロジェクト側が提供します。Package は独自修飾子の意味を推測しません。強制スキップは提供せず、UI・登録表・ログは正式ビルドに含まれません。接続と Unity 検証項目は生成パッケージ内の `DEBUGGING.md` を参照してください。Core テストは Unity UI や実際の演出の検証を代替しません。
