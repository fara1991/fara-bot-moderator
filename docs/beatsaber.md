# Beat Saber のコマンド（!bsr・!bomb）

Beat Saber で使うチャットのコマンドです。曲のリクエスト（`!bsr`）と、爆弾を出すコマンド（`!bomb` 系）を説明します。

- 曲のリクエスト（`!bsr`）: ソングリクエスト用の外部のボットが処理します。このアプリは、返信文を読み上げ用の文に変換するだけです。
- 爆弾（`!bomb` 系）: Beat Saber 側の Mod が処理します（FaraBombRush を含め、対応する Mod が受け取ります）。`!bomb` は他の Mod でも使われているコマンドで、FaraBombRush とは限りません。このアプリのボットは、爆弾のコマンドを処理しません。
- FaraBombRush を入れていない場合は、使っている Mod の説明に従ってください。このページの FaraBombRush の項目は、FaraBombRush を使う場合の説明です。

## 目次

- [FaraBombRush の対応バージョンと導入](#farabombrush-の対応バージョンと導入)
- [曲リクエスト（!bsr）](#曲リクエストbsr)
- [爆弾（!bomb・!bombline・!bombreset）](#爆弾bombbomblinebombreset)
- [FaraBombRush の設定](#farabombrush-の設定)
- [このアプリでの扱い](#このアプリでの扱い)

## FaraBombRush の対応バージョンと導入

FaraBombRush は、Beat Saber **1.29.1** と **1.40.8** に対応しています（v1.1.0 は 2026-10-24（JST）公開）。zip は Beat Saber の版ごとに別です。自分の版のものだけを使い、混ぜないでください。

| Beat Saber | 配布ファイル（[Releases](https://github.com/fara1991/fara-bomb-rush/releases/tag/v1.1.0)） |
|---|---|
| 1.29.1 | `FaraBombRush-v1.1.0-BS1.29.1.zip` |
| 1.40.8 | `FaraBombRush-v1.1.0-BS1.40.8.zip` |

### 必要なもの

- 共通: [BSIPA](https://github.com/bsmg/BeatSaber-IPA-Reloaded) 4.2.0 以上（4.x 系）
- 1.40.8 のみ: SiraUtil 3.1.0 以上（3.x 系）と、BeatSaberMarkupLanguage 1.12.0 以上（1.x 系）。無いと Mod が読み込まれません
- Twitch チャットのコマンドを使うとき（1.29.1・1.40.8 とも）: BeatSaberPlus（ChatPlexSDK）。無い場合は、チャットのコマンドが無効になります（ゲーム内で確認済み）

### 導入の手順

1. 上の必要なものを入れます。
2. 自分の Beat Saber の版の zip を、Beat Saber のフォルダに展開します（`Plugins/` と `UserData/FaraBombRush/` が入ります）。

詳しくは [FaraBombRush の README](https://github.com/fara1991/fara-bomb-rush#readme) を見てください。

## 曲リクエスト（!bsr）

`!bsr XXX` で、曲をリクエストできます。

- リクエストは、ソングリクエスト用の外部のボットが処理します。このアプリのボットは、曲リクエストを受け付けません。
- このアプリがするのは、チャットに流れる Beat Saber の返信文などを、棒読みちゃんで読み上げやすい文に変換することだけです（FaraBombRush とは別の機能です）。
- 変換した文は、棒読みちゃん連携が有効なときに、発言者の名前に「さん」を付けて読み上げられます。
- 変換の規則は、「Auto Bot Settings」の「Game」タブで「Beat Saber」を選ぶと出る表（正規表現パターン → 変換後テキスト）で編集できます。

規則は、表の上から順に見て、最初に合った行の文に変換します。既定の規則は次のとおりです。

| 元の文（例） | 読み上げる文（既定） |
|---|---|
| `!bsr XXX` | から、ソングリクエストXXXを頂きました。 |
| `Request 曲名 /…`（リクエスト登録） | リクエスト曲 曲名 が登録されました。 |
| `… requested by … added to queue` | 読み上げません（空の文） |
| `… already in queue`（既にリクエスト済み） | 既にリクエストされた曲です。 |
| `曲名 / … requested by 名前 is next`（次の曲） | 次の曲は、名前さんがリクエストした曲名です。 |
| `Queue is closed`（Queue の終了） | ソングリクエストを終了します。皆さんありがとう！ |
| `Queue is open`（Queue の開始） | ソングリクエストを開始しました。リクエストお待ちしてます。 |
| `map 曲名 not found`（曲が見つからない） | リクエスト曲曲名は存在しません。 |
| `No results found for request XXX` | XXXはリクエストにないよ。 |
| `Request for XXX produces N results`（検索結果の件数） | XXX で検索したら N 曲あったよ。絞り込んでみてね。 |

## 爆弾（!bomb・!bombline・!bombreset）

`!bomb` 系のコマンドは、Beat Saber 側の Mod が処理します。以下の説明は、FaraBombRush を使う場合です。FaraBombRush を入れていない場合は、使っている Mod の説明に従ってください。

FaraBombRush は、BeatSaberPlus 経由で Twitch チャットを読みます。Play Mode が「Interactive」のとき、Twitch チャットのコマンドで爆弾を出せます。曲のプレイ中に使う想定です。

| コマンド | 内容 | 例 |
|---|---|---|
| `!bomb` | 爆弾を 1 個出す | `!bomb 5` |
| `!bombline` | 同じ位置に、爆弾を 5 個の列にして出す。間隔は約 1 m で、ほぼ接している | `!bombline 3` |
| `!bombreset` | 上段か下段の 4 か所に、同じタイミングで爆弾を出す（リセット用の配置）。どちらの段になるかは位置で決まる | `!bombreset 9` |

いずれのコマンドも、後ろに半角スペースと 1〜12 の数字を付けて、位置を指定できます。省くとランダムです。

### 位置の番号

位置の番号と、ゲーム中の位置の対応は次のとおりです（FaraBombRush の場合。プレイヤーから見て左から右の順）。

| 段 | 左端 | 左から 2 | 右から 2 | 右端 |
|---|---|---|---|---|
| 上段 | 1 | 2 | 3 | 4 |
| 中段 | 5 | 6 ランダム | 7 ランダム | 8 |
| 下段 | 9 | 10 | 11 | 12 |

- 6 と 7 は、爆弾の位置として用意されていない中央の 2 か所です。指定すると、位置なしと同じランダムになります。
- ランダムは、実在する 10 か所（1〜5 と 8〜12）から同じ確率で選びます。
- `!bombline` の間隔は、曲の BPM に関係なく約 1 m です。以前は BombLineCount で個数を設定できましたが、いまは常に 5 個です。

### !bombreset のリセットする段

`!bombreset` は、指定した位置で、リセットする段が決まります。

| 指定の位置 | リセットする段 |
|---|---|
| 1〜4・5（上段と中段の左） | 上段（1〜4 の 4 か所） |
| 8〜12（中段の右と下段） | 下段（9〜12 の 4 か所） |
| 6・7・省略 | 実在する 10 か所からランダムに選んだ位置の段（上段と下段は半々） |

### 反応する発言・しない発言（FaraBombRush の場合）

- 1〜12 以外の数字（0・13 など）と、数字でない指定は、爆弾を出しません。全角の数字（５）も出ません。
- 数字は 2 桁までで、先頭の 0 は可です（`05` は可、`005` は不可）。
- `!bombs` や `!bomberman` のような、よく似た別の言葉には反応しません。大文字小文字は区別しません。
- `!bomb 5 おはよう` のように、コマンドと位置のほかに言葉が付くと反応しません。
- `@名前 !bomb 5` のような Twitch の返信でも反応します。7TV が付ける不可視文字は無視されます。
- コマンドは Twitch のチャットからだけ受け付けます。

## FaraBombRush の設定

ゲームのメインメニューの「Fara Bomb Rush」ボタンから、設定を開きます。

| 設定 | 内容 |
|---|---|
| Language | 設定画面とメニューのボタンの表示言語（English / Japanese）。既定は English |
| Enabled | Mod 全体の有効・無効。メニューの爆弾のプレビューも、この設定に従う |
| Play Mode | None（何もしない。爆弾を出さず、チャットも読まない）/ Interactive（Twitch チャットのコマンドで出す）/ Auto（曲の BPM に合わせて自動で出す） |
| Player Bomb Level | Auto の難しさ（Beginner / Easy / Normal / Hard / Expert / ExpertPlus / Lawless）。ScoreSaber の PP から推奨レベルが表示される |
| Valid Bomb Collision | 爆弾の当たり判定の有無 |

> [!NOTE]
> - 1.1.0 から新しく入れた場合、Play Mode の既定は Interactive です。
> - 1.0.0 から更新した場合、保存済みの Play Mode が None（1.0.0 の既定）の人は、None のまま無効です。自動では有効になりません。使うときは Play Mode を変えてください。
> - 以前あった Battle は未実装のため、選択肢から外れました。古い設定に残っていても None と同じ扱いです。

## このアプリでの扱い

- このアプリは、小文字の `!bomb` を含む発言を、翻訳と読み上げの対象から外します（`!bombs` など、文字列に含まれるだけの発言も同様です）。爆弾を出すかどうかの判断は、このアプリはしません。
- FaraBombRush は、大文字小文字の違いや、間に不可視文字が入った発言も受け付けます。そのような発言（`!BOMB 5` など）は、このアプリが翻訳・読み上げることがあります。
- `!bsr` の発言と、Beat Saber の返信文は、[曲リクエスト（!bsr）](#曲リクエストbsr) の規則で読み上げ用に変換します。
