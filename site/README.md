# FaraBotModerator 紹介サイト

FaraBotModerator の機能と使い方を紹介するサイトです。

| 環境 | URL | ブランチ |
| --- | --- | --- |
| 本番 | https://twitch-bot.fara-labs.com | `master` |
| 確認用 | https://twitch-bot-dev.fara-labs.com（Cloudflare Access で自分だけが閲覧可能） | `develop` |

Cloudflare の Worker 名は本番 `fara-twitch-bot`、確認用 `fara-twitch-bot-dev` です（`wrangler.toml` の `name` はダッシュボードの Worker 名と一致させる。違うと別の Worker が作られる）。

## 作り

- .NET 10 の Blazor WebAssembly + MudBlazor。ヘッダー・サイドバー・フッター・テーマ・問い合わせフォーム・404・お知らせは、共通部品 [fara-site-kit](https://github.com/fara1991/fara-site-kit)（`external/fara-site-kit`、v0.8.0）
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

## 品質チェック（レイアウト・ESLint・C#）

- **レイアウトの自動計測**: `npm run layout-check`。ビルド済みの `publish/wwwroot` を配って、トップ・ドキュメント・コマンド・ポケモン・Beat Saber・問い合わせ・404 の画面を、360〜1440px の 5 幅で開き、数値で決まる崩れ（横スクロール・h1 の数・フォーカスの見え方・画面の外へのフォーカス・押せる範囲・文字の大きさ・メニューの Esc）を見る。設定は `layout-check.config.mjs`。CI は `build.yml` の `Layout check (site)`
  - 除外は `rule`・`selector`・`reason`（必須・具体的に）・`page`（任意）で書く。直せるものは CSS・Razor で直す（今の除外は、kit の計測の制限による本文の項目と、フッターの SNS アイコン、受け付け停止中の送信ボタン、トップの一覧のリンク。それぞれの理由は設定ファイルに書いてある）
  - 使い方と制限は kit の `docs/layout-check.md`。計測の依存は kit の中の `node_modules` から読むため、`external/fara-site-kit` でも `npm ci` する
- **ESLint**: `npm run lint`（`eslint.config.mjs`。共通のルールは kit の `tools/lint/`）。対象は `worker/`・`tools/`・`tests/`・画面の `wwwroot/js/`。`.github/scripts` は対象外（基準パスの外。node --test で確かめる）
- **C#**: リポジトリ直下の `Directory.Build.props` が kit の `dotnet.editorconfig` を読む。`CS4014`・`CA1816`・`IDE0059` は error（違反 0 件）。`CA1031`・`CA2000`・`VSTHRD100` は warning のまま（件数は `CLAUDE.md` の表）。ルート `.editorconfig` で `VSTHRD100` だけ warning に下げている（理由は `CLAUDE.md`）

## CSP

`_headers` の `Content-Security-Policy` は、`build/site.json` の `csp` から kit が作ります（Worker は付けません）。以前の静的サイトとの違い:

- `script-src` に `'wasm-unsafe-eval'`（Blazor WebAssembly に必要）。`'unsafe-inline'`・`'unsafe-eval'` は許可しません
- `style-src` に `'unsafe-inline'`（MudBlazor と、起動の前に当てる読み込み表示の CSS のため）
- `challenges.cloudflare.com`（問い合わせの Turnstile）を、`/report` だけでなく全ページで許可（`/report` へは画面の移動で行き、ドキュメントを取り直さないため、ページごとの CSP にできない）
- `static.cloudflareinsights.com`（script-src）と `cloudflareinsights.com`（connect-src）は、Cloudflare Web Analytics のビーコン用（ゾーンの設定で自動に差し込まれる）
- 外部のフォント・スクリプト・画像は、引き続き使いません

## 問い合わせの回数制限

問い合わせ（`/api/report`）は、送信元（`CF-Connecting-IP`）ごとに **1 日 3 回まで**（日本時間の日付で数える）です。超えると 429 を返し、画面には「1 日の送信上限（3 回）に達しました。明日もう一度お試しください。」を出します。数えるのは、Turnstile を通って Discord へ送る直前の 1 回です（Turnstile の失敗や、本文の不正で弾いたものは数えません）。Discord への送信に失敗したときは、数えた分を戻します。

- 仕組みは kit の `worker/report-rate-limit.js`（Durable Object の `ReportRateLimiter`）。`worker/index.js` が公開し、`wrangler.toml` の `durable_objects`・`migrations` が、トップレベル・`env.production`・`env.dev` の 3 か所にあります。どれかを消すと、制限が効かなくなります（バインディングが無ければ制限しない）
- 保存するのは、送信元の IP アドレスから作った識別子（`TURNSTILE_SECRET` から導いた鍵の HMAC。IP アドレスそのものは保存しません）と回数だけで、日付ごとに分けて、日本時間の翌 0 時に消します。**`TURNSTILE_SECRET` を変えると、計数はリセットされます**
- 同じ回線（共有 IP）の人は枠を共有します。数えられないとき（Durable Object が使えないなど）は、制限せずに送ります（fail-open。Turnstile は残ります）
- 方針の詳細とプライバシーの書き方は、kit の `docs/usage.md` の 2.2。プライバシーポリシーは `/privacy`（「問い合わせで送る情報」の節）

## 公開

ワークフローは fara-pokemon-battle-assistant などと同じ 3 つです。公開の 2 つは `site/`・`Manual.md`・`external/fara-site-kit` などを変えたときだけ動き、自分でビルドして公開します（成果物（artifact）は使いません）。ほかに、アプリのインストーラーのリリース用の `release-app.yml`（タグ `v*` か手動実行）があります。

- `.github/workflows/build.yml`（Build and test (site)）: PR と、develop・master への push で、アプリ本体（Windows）と紹介サイトのビルドとテスト。サイトは本番用・確認用の両方を書き出して確かめる。公開しない。あわせて、`Layout check (site)`（レイアウトの自動計測）と `Lint (site JS)`（ESLint）も動く
- `.github/workflows/deploy-develop.yml`（Deploy (develop)）: `develop` への push で、確認用に公開
- `.github/workflows/deploy.yml`（Deploy (production)）: `master` への push で、本番に公開

- 本番（master）の公開は、Worker の変数 `MAINTENANCE_MODE` が `notice` のときだけ行い、公開と同時に `true`（メンテナンス画面）にします。作業が終わったら、Cloudflare で `false` に戻します（お知らせも一緒に消える）
- Worker がまだ無い初回の公開だけ、Run workflow で `first_publish` を `true` にします。Worker が見つからない（404）のは、初回のほかに Worker 名・アカウント ID の誤りでも起きるため、それ以外では止めます（確認を飛ばさない）
- メンテナンス表示の切り替えは、Actions の「Run workflow」（確認用は Deploy (develop) で `develop`、本番は Deploy (production) で `master` を選ぶ）で `maintenance_mode`（`true` / `notice` / `false`、空 = 変更しない）と `maintenance_message`（文面。空 = 変更しない）を入れて実行します。公開されるのは、選んだブランチの最新のコードです。mode を指定した本番の実行は、`notice` の確認を飛ばします。結果はジョブサマリーに出ます（手順は fara-site-kit の `docs/maintenance-cli.md`）
- お知らせ（`MAINTENANCE_MODE=notice`）の帯は、アプリが `/api/maintenance` を読んで出します（以前の、Worker が HTML に差し込む方式はやめました）

GitHub のリポジトリに、次の設定が必要です（ポケモンサイトなど、ほかのサイトと同じ値）。

- Secrets: `CLOUDFLARE_API_TOKEN`
- Variables: `CLOUDFLARE_ACCOUNT_ID`
- Environments: `production`、`develop`（初回のデプロイで自動で作られます）
