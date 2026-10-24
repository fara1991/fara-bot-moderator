# FaraBotModerator 紹介サイト

FaraBotModerator の機能と使い方を紹介するサイトです。

| 環境 | URL | ブランチ |
| --- | --- | --- |
| 本番 | https://twitch-bot.fara-labs.com | `master` |
| 確認用 | https://twitch-bot-dev.fara-labs.com（Cloudflare Access で自分だけが閲覧可能） | `develop` |

Cloudflare の Worker 名は本番 `fara-twitch-bot`、確認用 `fara-twitch-bot-dev` です（`wrangler.toml` の `name` はダッシュボードの Worker 名と一致させる。違うと別の Worker が作られる）。

## 作り

- .NET 10 の Blazor WebAssembly + MudBlazor。ヘッダー・サイドバー・フッター・テーマ・問い合わせフォーム・404・お知らせは、共通部品 [fara-site-kit](https://github.com/fara1991/fara-site-kit)（`external/fara-site-kit`、v0.3.0）
- 公開物（`publish/wwwroot`）は、アプリ（WebAssembly）と、ページごとの**本文入りの静的 HTML**（検索エンジンと、JavaScript が使えない環境のため。ブラウザは起動すると、同じ内容の画面に置き換える）。Cloudflare Workers（Static Assets）でホスティング
- 説明ページの本文は、リポジトリ直下の `Manual.md` を見出しで切り分けたものです。**文章を直すときは `Manual.md` を直します**（アプリに同梱する操作説明書と同じ内容になります）。ビルドが `data/site.json` に変換するので、ブラウザは Markdown を読みません
- どの見出しをどのページにするか、ページの題名・グループ・説明は `Content/pages.json` に書きます。見出しの名前を変えたときは、`pages.json` の `heading` も合わせてください（見つからないとビルドが失敗します）
- 題名に漢字があるページには `reading`（題名のひらがなの読み。長音は「ー」）を書きます（例: `"reading": "こまんどじっこう"`）。サイト内検索で、ローマ字（komando・kenri）やかなの読みでも探せるようにするためです。読みは、ひらがなと「ー」だけにします（ビルドとテストで確かめます）
- 親ページを持つページ（`parent` に親の slug を書く）は、親のグループに入り、サイドメニューでは親の下に入れ子で出ます。ゲームのコマンドは、「コマンド実行」（`commands`）の子として `pokemon` のように書きます（`parent` や `group` に `null` は使えません）
- 更新履歴は `Content/changelog.json`
- サイト内検索は C#（`src/FaraBotModeratorSite.Shared/Search`）。ひらがな・カタカナ・全角・ローマ字（nn→ん など）で探せます
- 画面は `src/FaraBotModeratorSite.Pages`（ページ）と `src/FaraBotModeratorSite.Web`（レイアウト）、本文の見た目は `src/FaraBotModeratorSite.Web/wwwroot/css/site.css`（色は `:root` の変数）。構成と注意は `CLAUDE.md`

## ローカルで見る

```bash
cd site
npm install
npm run build:site            # publish/wwwroot を作る（Build → dotnet publish → build-pages）
npx wrangler dev --env dev    # Cloudflare と同じ動き（Worker 経由）で http://localhost:8787 に
```

`Manual.md` や `Content/*.json` を変えたら、`npm run build:site` をやり直します（`dotnet watch` の開発サーバーでは `data/site.json` と静的な本文は作られません）。

## CSP

`_headers` の `Content-Security-Policy` は、`build/site.json` の `csp` から kit が作ります（Worker は付けません）。以前の静的サイトとの違い:

- `script-src` に `'wasm-unsafe-eval'`（Blazor WebAssembly に必要）。`'unsafe-inline'`・`'unsafe-eval'` は許可しません
- `style-src` に `'unsafe-inline'`（MudBlazor と、起動の前に当てる読み込み表示の CSS のため）
- `challenges.cloudflare.com`（問い合わせの Turnstile）を、`/report` だけでなく全ページで許可（`/report` へは画面の移動で行き、ドキュメントを取り直さないため、ページごとの CSP にできない）
- `static.cloudflareinsights.com`（script-src）と `cloudflareinsights.com`（connect-src）は、Cloudflare Web Analytics のビーコン用（ゾーンの設定で自動に差し込まれる）
- 外部のフォント・スクリプト・画像は、引き続き使いません

## 公開

`.github/workflows/site.yml` が、`master` / `develop` への push（`site/`・`Manual.md`・`external/fara-site-kit` などを変えたとき）で、ビルド・テストして Cloudflare にデプロイします。PR ではビルドとテストだけ行います。

- 本番（master）の公開は、Worker の変数 `MAINTENANCE_MODE` が `notice` のときだけ行い、公開と同時に `true`（メンテナンス画面）にします。作業が終わったら、Cloudflare で `false` に戻します（お知らせも一緒に消える）
- メンテナンス表示の切り替えは、Actions の「Run workflow」（確認用は `develop`、本番は `master` を選ぶ）で `maintenance_mode`（`true` / `notice` / `false`、空 = 変更しない）と `maintenance_message`（文面。空 = 変更しない）を入れて実行します。公開されるのは、選んだブランチの最新のコードです。mode を指定した本番の実行は、`notice` の確認を飛ばします。結果はジョブサマリーに出ます（手順は fara-site-kit の `docs/maintenance-cli.md`）
- お知らせ（`MAINTENANCE_MODE=notice`）の帯は、アプリが `/api/maintenance` を読んで出します（以前の、Worker が HTML に差し込む方式はやめました）

GitHub のリポジトリに、次の設定が必要です（ポケモンサイトなど、ほかのサイトと同じ値）。

- Secrets: `CLOUDFLARE_API_TOKEN`
- Variables: `CLOUDFLARE_ACCOUNT_ID`
- Environments: `production`、`develop`（初回のデプロイで自動で作られます）
