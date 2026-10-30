# 困ったとき

Token の期限切れ、接続が不安定なとき、ログの場所をまとめています。それ以外は [よくある質問](faq.md) を見てください。

## Token の期限切れエラー

「Your API AccessToken has expired」と表示された場合は、次の手順で再認証します。

1. 「**Authorize**」ボタンをクリックします。
2. WebView で Twitch にログインします。
3. アプリを許可します。
4. `Token expiration` が更新されたことを確認します。

## 接続が不安定な場合

1. 一度「**Disconnect**」で切断します。
2. 数秒待ってから、「**Connect**」で再接続します。
3. それでも改善しない場合は、アプリを再起動します。

## ログの確認

エラーが発生した場合、アプリのインストールフォルダ内の `Log` フォルダに、ログファイルが保存されます。

| 種類 | ファイル名 |
|---|---|
| 通常 | `YYYY.MM.DD.log` |
| イベント別 | `イベント名_YYYY.MM.DD.log` |

> [!WARNING]
> ログには、視聴者のユーザー名とチャットの本文が入ります。他の人に渡したり、配信画面に映したりしないでください。詳しくは [プライバシーポリシー](../PRIVACY.md#ログ) を見てください。

## それでも解決しないとき

[GitHub の Issues](https://github.com/fara1991/fara-bot-moderator/issues) か、Twitter（X）の [@game_fara](https://twitter.com/game_fara) に問い合わせてください。
