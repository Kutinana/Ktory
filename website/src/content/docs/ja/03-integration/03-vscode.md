---
title: VS Code のハイライトと試読
description: VSIX をインストールし、編集中の Ktory 脚本をオフラインで試読する
sidebar:
  order: 3
---

このページは [Ktory の設計原則](/ja/01-overview/03-design-principles/) に従います。拡張は共通の C# コアと Reader フロントエンドを使い、別のインタープリターを持ちません。

## インストールと起動

VS Code 1.97 以降で **Extensions: Install from VSIX…** を実行し、ビルド済みの `ktory-vscode-<version>.vsix` を選びます。`.ktr` または `.ktory` ファイルを開き、エディター上部の試読アイコン、または **Ktory: Open Preview to the Side** を実行します。

VSIX は WebAssembly ランタイムを内蔵しています。利用者は .NET、Unity、ローカルサーバー、オンライン試読器を必要としません。ローカル VSIX の提供は Marketplace への公開を意味しません。

## 執筆と試読

- 読書領域のクリック、Space／Enter で全文表示と進行、選択肢ボタンまたは数字キーで分岐を選びます。
- 入口セクションと言語を選択できます。訳文がなければコアの既定言語へフォールバックします。
- 編集後は **重新载入编辑器内容** または **Ktory: Reload Preview from Editor** で再読込します。未保存の編集も使われ、入口から新しいセッションを開始します。
- 入力だけでは試読を再起動しません。古い版を試読中の場合はパネルに表示します。RESTART は読込済みの内容を再生します。
- 行と列があるエラーはパネルと Problems に表示し、編集時に古い診断を消去します。

試読では既存 Reader の文字表示と基本的な時間制御を使います。未知の外部選択条件は無視し、未知の演出はスキップします。Unity のリソース、ゲーム状態、演出効果を検証するものではありません。基本書式とルビは表示しますが、実行可能な HTML、リソースを読み込む要素、任意の HTML 属性は除去します。

## 開発と配布

`src/Ktory.VSCode` で `pnpm install --frozen-lockfile`、`pnpm build`、`pnpm test`、`pnpm test:integration`、`pnpm package` を実行します。ビルドには .NET SDK 9、Node.js 20+、pnpm 10 が必要ですが、利用者には不要です。

TextMate grammar は拡張側だけで管理し、ポータルが直接参照します。生成された `reader/` や VSIX を別のソースとして修正しません。詳細は [拡張 README](https://github.com/Kutinana/Ktory/blob/main/src/Ktory.VSCode/README.md) を参照してください。
