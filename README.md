# FaraBotModerator

Twitch配信者向けの多機能BOTツールです。WPF (C# / .NET 8) で構築されており、チャット管理、イベント通知、翻訳、棒読みちゃん連携などの機能を備えています。

## 主な機能

- **チャット連携**: Twitchチャットの取得、送信、表示。
- **イベント通知**: Follow, Raid, Subscription, Bits, チャンネルポイント等のイベントを検知し、自動応答チャットを送信。
- **翻訳機能**: DeepL APIと連携し、受信したチャットをリアルタイムで翻訳。
- **棒読みちゃん連携**: 受信したチャットを棒読みちゃんで読み上げ。
- **API連携**: Twitch API (Helix) を使用したユーザー情報の取得や各種アクション。
- **EventSub対応**: WebSocketを使用した低遅延なイベント受信。
- **タイマー機能**: 定期的なメッセージ送信や指定日時での自動チャット。

## セットアップと使用方法

### 1. 動作環境
- Windows 10/11 (x64)
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

### 2. インストール
- [Releases](https://github.com/fara1991/FaraBotModerator/releases) から最新の `FaraBotModerator_Setup.exe` をダウンロードして実行してください。
- または、ZIP版を解凍して `FaraBotModerator.exe` を直接実行することも可能です。

### 3. 初期設定
1. **Client設定**: `Get Token` ボタンからTwitch連携を行い、AccessTokenを取得して設定します。
2. **API設定**: [Twitch Developers](https://dev.twitch.tv/console) でアプリを登録し、`ClientID` と `ClientSecret` を設定します。
3. **DeepL連携 (任意)**: 翻訳機能を使用する場合は、DeepLの認証キーを設定します。
4. **棒読みちゃん連携 (任意)**: 棒読みちゃんのHTTP連携設定（ポート 5008）を有効にします。

## 開発者向け情報

### ビルド方法
リポジトリをクローンし、PowerShellスクリプトを使用してビルドおよびパッケージングが可能です。

```powershell
./build_release.ps1
```

このスクリプトは以下の処理を行います：
1. `dotnet publish` による実行ファイルの生成（シングルファイル形式）
2. 配布用ZIPの作成
3. Inno Setupを使用したインストーラー (`.exe`) の作成（Inno Setup 6 がインストールされている場合）

### プロジェクト構成
- `FaraBotModerator`: アプリ本体 (WPF)
- `Installer`: Inno Setup用スクリプトおよび関連ファイル
- `Resources`: アイコンや画像リソース

## ライセンス
本プロジェクトは開発中のプロトタイプです。

## 使用ライブラリ
- [TwitchLib](https://github.com/TwitchLib/TwitchLib)
- [DeepL.net](https://github.com/DeepLcom/deepl-dotnet)
- [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
- [obs-websocket-dotnet](https://github.com/BarRaider/obs-websocket-dotnet)
