# FaraBotModerator 紹介サイト

FaraBotModerator の機能と使い方を紹介するサイトです。

| 環境 | URL | ブランチ |
| --- | --- | --- |
| 本番 | https://twitch-bot.fara-labs.com | `master` |
| 確認用 | https://twitch-bot-dev.fara-labs.com（Cloudflare Access で自分だけが閲覧可能） | `develop` |

Cloudflare の Worker 名は本番 `fara-twitch-bot`、確認用 `fara-twitch-bot-dev` です（`wrangler.toml` の `name` はダッシュボードの Worker 名と一致させる。違うと別の Worker が作られる）。

## 作り

- .NET 8 の Blazor Web App（静的サーバーサイドレンダリング。SignalR 接続なし）
- 公開時は静的サイトとして書き出し、Cloudflare Workers（Static Assets）でホスティング
- 外部のフォント・スタイル・スクリプトは使いません（Worker が付ける CSP も同じオリジンだけを許可）
- 説明ページの本文は、リポジトリ直下の `Manual.md` を見出しで切り分けたものです。**文章を直すときは `Manual.md` を直します**（アプリに同梱する操作説明書と同じ内容になります）
- どの見出しをどのページにするか、ページの題名・グループ・説明は `Content/pages.json` に書きます。
- 親ページを持つページ（`parent` に親の slug）は、親のグループに入り、サイドメニューでは親の下に入れ子で出ます。ゲームのコマンドは、「コマンド実行」（`commands`）の子として `pokemon` や `beatsaber` のように書きます見出しの名前を変えたときは、`pages.json` の `heading` も合わせてください（見つからないとビルドが失敗します）
- 画面は `Components/`、見た目は `wwwroot/css/site.css`（色は `:root` の変数）

## ローカルで見る

```bash
cd site
dotnet run
```

開発中は、`Manual.md` と `Content/pages.json` を保存してブラウザをリロードするだけで反映されます。

## 静的サイトとして書き出す

```bash
cd site
dotnet run -c Release --no-launch-profile -- --export dist
npx wrangler@4.149.0 dev --env dev   # Cloudflare と同じ動きで確認
```

## 公開

`.github/workflows/site.yml` が、`master` / `develop` への push（`site/` か `Manual.md` を変えたとき）で、書き出して Cloudflare にデプロイします。PR ではビルドだけ行います。

GitHub のリポジトリに、次の設定が必要です（ポケモンサイトなど、ほかのサイトと同じ値）。

- Secrets: `CLOUDFLARE_API_TOKEN`
- Variables: `CLOUDFLARE_ACCOUNT_ID`
- Environments: `production`、`develop`（初回のデプロイで自動で作られます）
