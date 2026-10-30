# 開発者向け情報

FaraBotModerator をビルドする方法と、リポジトリの構成です。アプリの使い方は [README](../README.md) から読めます。

## 紹介サイトのソース

`site/` に、紹介サイトのソースがあります。サイトの中身は [Manual.md](../Manual.md) をそのまま表示しています。作りと公開の手順は [site/README.md](../site/README.md) を参照してください。

## ビルド方法

リポジトリをサブモジュールごとクローンし、PowerShell スクリプトを使用してビルドおよびパッケージングが可能です。

ビルドには .NET 10 SDK が必要です（`global.json` で指定）。アプリ本体の対象は `net10.0-windows` で、.NET のランタイムは同梱されます（自己完結）。

```powershell
git clone --recursive https://github.com/fara1991/fara-bot-moderator.git
# クローン済みの場合
git submodule update --init
./build_release.ps1
```

このスクリプトは以下の処理を行います。
1. `dotnet publish` による実行ファイルの生成（シングルファイル形式）
2. Inno Setup を使用したインストーラー（`.exe`）の作成（Inno Setup 6 がインストールされている場合）

## 署名用の自己署名証明書を作る（任意）

リリースの署名（GitHub Actions の `release-app.yml`）に使う自己署名証明書は、`create_certificate.ps1` で作れます。パスワードは自分で決めて、実行時に入力してください（スクリプトに既定のパスワードはありません。画面には表示されません）。

```powershell
./create_certificate.ps1
# Password の入力を求められるので、自分で決めたパスワードを入力する
```

できた `cert.pfx` の中身を base64 にした文字列を、GitHub のリポジトリの Settings > Secrets and variables > Actions に次の名前で登録します。
- `CERTIFICATE_PFX`: スクリプトが表示する base64 文字列
- `CERTIFICATE_PASSWORD`: 入力したパスワード

`cert.pfx` は秘密鍵を含みます。リポジトリにコミットせず、安全な場所に保管してください。

## リリースの手順

1. `Installer/installer.iss` の `MyAppVersion` と、`Manual.md` の更新履歴を上げます。
2. `docs/release-notes/vX.Y.Z.md` に変更点を書きます（利用者向けのやさしい言葉で）。見出しと導入文、「新機能」などの節を書き、インストール方法・必要条件は書きません（`release-app.yml` が共通の文として後ろに足します）。ファイルが無いときは、見出しと共通の文だけの本文になります。
3. ここまでの PR を develop にマージします。
4. develop の該当コミットに `vX.Y.Z` のタグを push します（`release-app.yml` は master に無いので、手動実行は使えません）。
5. Actions の成功と、Release の本文・署名（インストーラーのプロパティの「デジタル署名」）を確認します。本文の確認が済んだら、README・サイトの `.NET` の記述など、ほかの資料に影響が無いことも確かめます。

## プロジェクト構成

- `FaraBotModerator`: アプリ本体（WPF）
- `Installer`: Inno Setup 用スクリプトおよび関連ファイル
- `Resources`: アイコンや画像リソース
- `external/fara-pokemon-battle-assistant`: `!poke` 系コマンドの計算ライブラリ（git submodule。`FaraPokemonBattleAssistant.Core` を参照）

## 使用ライブラリ

- [TwitchLib](https://github.com/TwitchLib/TwitchLib)
- [DeepL.net](https://github.com/DeepLcom/deepl-dotnet)
- [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
- [obs-websocket-dotnet](https://github.com/BarRaider/obs-websocket-dotnet)
