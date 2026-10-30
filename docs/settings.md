# 各設定

アプリの各タブの設定項目をまとめています。「Main Settings」の導入部分は [導入の手順](getting-started.md) を見てください。

## 目次

- [棒読みちゃん連携](#棒読みちゃん連携)
- [リアクションイベント（Reaction Event Settings）](#リアクションイベントreaction-event-settings)
- [翻訳（Translate）](#翻訳translate)
- [ゲーム設定（Game）](#ゲーム設定game)
- [タイマー（Timer）](#タイマーtimer)
- [ウィンドウ設定（Windows Settings）](#ウィンドウ設定windows-settings)

## 棒読みちゃん連携

チャットを読み上げる場合の設定です。

1. 棒読みちゃんを起動します。
2. 棒読みちゃんの設定で「HTTP連携」を有効にします（ポート: 5008）。
3. アプリの「Main Settings」で、`BouyomiChanConnect` にチェックを入れます。

> [!NOTE]
> Twitch に接続する前に、棒読みちゃんを起動しておいてください。

## リアクションイベント（Reaction Event Settings）

「Reaction Event Settings」タブでは、Twitch のイベントが起きたときに自動で送るメッセージを設定します。

### 対応イベント

| イベント | 説明 |
|---|---|
| `Follow` | 誰かがチャンネルをフォローしたとき |
| `Raid` | 他の配信者からレイドされたとき |
| `Subscription` | 誰かがサブスクライブしたとき |
| `Bits` | 誰かがビッツ（チアー）を投げたとき |
| `Gift` | 誰かがサブスクギフトを贈ったとき |
| `ChannelPoint` | 誰かがチャンネルポイントを使用したとき |

### 設定方法

各イベントごとに、次の設定ができます。

1. チェックボックス: イベント通知の ON / OFF
2. メッセージ欄: 送信するメッセージのテンプレート
3. `Test Send`: テストメッセージを送信（接続中のみ有効）

### プレースホルダー（置換文字）

メッセージ内に書くと、イベントが起きたときに実際の値に置き換わります。

| イベント | プレースホルダー | 置き換わる内容 |
|---|---|---|
| Follow | `{followerName}` | フォローしたユーザーの名前 |
| Follow | `{followerChannelUrl}` | フォローしたユーザーのチャンネル URL |
| Raid | `{raiderName}` | レイドしてきたユーザーの名前 |
| Raid | `{raiderChannelUrl}` | レイドしてきたユーザーのチャンネル URL |
| Subscription | `{subscriberName}` | サブスクしたユーザーの名前 |
| Subscription | `{totalSubscriptionMonth}` | 累計サブスク月数 |
| Bits | `{bitsSendUserName}` | ビッツを投げたユーザーの名前 |
| Bits | `{bitsAmount}` | 投げられたビッツの量 |
| Gift | `{giftedUserName}` | ギフトを贈ったユーザーの名前 |
| ChannelPoint | `{channelPointUserName}` | チャンネルポイントを使用したユーザーの名前 |
| ChannelPoint | `{channelPointTitle}` | 使用されたチャンネルポイント報酬のタイトル |
| ChannelPoint | `{channelPointCost}` | 消費されたポイント数 |

### 設定例

```
Follow: {followerName}さん、フォローありがとうございます！
Raid: {raiderName}さん、レイドありがとうございます！チャンネル: {raiderChannelUrl}
Subscription: {subscriberName}さん、{totalSubscriptionMonth}ヶ月目のサブスクありがとうございます！
Bits: {bitsSendUserName}さん、{bitsAmount}ビッツありがとうございます！
```

## 翻訳（Translate）

「Auto Bot Settings」タブの「Translate」タブで、DeepL API を使った自動翻訳を設定します。

### DeepL API キーを取得する

1. [DeepL](https://www.deepl.com/) にアクセスします。
2. アカウントを作成します（無料プランあり）。
3. マイページから「APIキー」を取得します。

### 設定する

1. 「**DeepL API Key**」欄に API キーを入力します。
2. 「**DeepL Site**」ボタンで、DeepL のサイトを WebView に表示できます。

### 翻訳の動作

- チャットメッセージを自動で検出します。
- 日本語は英語に、英語は日本語に翻訳します。
- 翻訳結果をチャットに送信します。
- `!poke` 系コマンドは、翻訳・読み上げの対象外です。

## ゲーム設定（Game）

「Auto Bot Settings」タブの「Game」タブで、ゲームごとの設定をします。上の選択欄でゲームを選ぶと、そのゲームの設定が表示されます。「初期化」ボタンで、選んでいるゲームの設定を初期値に戻せます。

| ゲーム | 設定 |
|---|---|
| Beat Saber | ソングリクエストのボットのメッセージを日本語に変換するパターン（正規表現パターン → 変換後テキスト） |
| ポケモンシリーズ | 「対戦形式」「データの取得元」「アクセスキー」 |

- Beat Saber の変換パターンは、[Beat Saber のコマンド](beatsaber.md#曲リクエストbsr) を見てください。
- ポケモンシリーズの設定は、[ポケモンのコマンド](pokemon.md#設定画面) を見てください。

## タイマー（Timer）

「Auto Bot Settings」タブの「Timer」タブで、定期的または指定した時刻にメッセージを自動で送ります。

### Cycle Notifications（定期メッセージ）

指定した間隔で、メッセージを繰り返し送ります。`CycleTimer1` から `CycleTimer4` までの 4 つを設定できます。

| 設定項目 | 説明 |
|---|---|
| チェックボックス | タイマーの ON / OFF |
| スライダー | 送信間隔（15 分〜120 分） |
| メッセージ欄 | 送信するメッセージ |

使用例は次のとおりです。
- 配信ルールの定期告知
- SNS アカウントの宣伝
- Discord サーバーへの招待

### Fixed Notifications（日時指定メッセージ）

指定した日時に、メッセージを 1 回送ります。`FixedTimer1` から `FixedTimer4` までの 4 つを設定できます。

| 設定項目 | 説明 |
|---|---|
| チェックボックス | タイマーの ON / OFF |
| 日付 | 送信する日付 |
| 時刻 | 送信する時刻（秒単位まで指定可能） |
| メッセージ欄 | 送信するメッセージ |

使用例は次のとおりです。
- 配信終了予告
- イベント開始告知
- 特定時刻のお知らせ

## ウィンドウ設定（Windows Settings）

「Windows Settings」タブで、補助ウィンドウを開きます。

### Chat Window（チャットウィンドウ）

Twitch チャットをリアルタイムで表示・送信するウィンドウです。「**Open Chat Window**」ボタンで開きます。

- チャットメッセージの一覧表示
- ユーザーアイコンの表示
- メッセージの送信

### Raid List Window（レイドリストウィンドウ）

配信終了時のレイド先の候補を表示するウィンドウです。「**Open Raid List Window**」ボタンで開きます。

- 現在配信中のフォロー中ユーザーを表示
- 同じゲームをプレイ中の配信者を表示
- レイドコマンドをクリップボードにコピー
