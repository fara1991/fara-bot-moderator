# CLAUDE.md（site/ 用）

紹介サイト（.NET 10 の Blazor WebAssembly + MudBlazor。共通部品は fara-site-kit）。`npm run build:site` で、ブラウザのアプリ・本文入りの静的 HTML・`_headers` を `publish/wwwroot` に作り、Cloudflare の Worker（`worker/index.js`）が配信する。Worker は /api/maintenance・メンテナンス画面（503）・`/api/report` を担当する。詳細は README.md、移行の経緯と決定は `docs/wasm-migration-plan.md`。

## ブランチ運用（必ず守る）

- 作業ブランチは `develop` から作成する
- 作業ブランチの PR は `develop` 向けに作る
- `master` へのマージはユーザーが行う。Claude は `master` へはマージしない

## 構成

- `src/FaraBotModeratorSite.Shared`: モデル・`SiteContent`・`SiteInfo`（サイト名・URL・保存のキー・SNS）・検索（`Search/`）。ブラウザとビルドの両方が使う
- `src/FaraBotModeratorSite.Content`: `ContentBuilder`（`Manual.md` + `Content/pages.json` + `Content/changelog.json` → ページ）。ビルド時だけ（ブラウザには載せない）
- `src/FaraBotModeratorSite.Pages`: ページの部品（Razor クラスライブラリ）。ブラウザと、静的 HTML の本文（`HtmlRenderer`）で同じ部品を使う
- `src/FaraBotModeratorSite.Web`: Blazor WebAssembly（レイアウト・サイドメニュー・検索欄・`index.html`・`wwwroot/css/site.css`）
- `src/FaraBotModeratorSite.Build`: `data/site.json`・kit の build-pages 用 `pages.json`・`site.json` を書く。`build/site.json` は build-pages の設定（CSP の外部ホストもここ）
- `tools/build-site.mjs`: Build → `dotnet publish` → kit の `build-pages`。`tools/compare/`: 移行前後の比較（README あり）
- 説明ページの本文は `Manual.md`（リポジトリ直下。アプリの操作説明書と同じ）。ページの題名・グループ・説明・親子・読みは `Content/pages.json`
- Beat Saber のコマンドの説明は `/docs/beatsaber`（`Manual.md` の「5.5. Beat Saber コマンド」の節と、`pages.json` の `parent: commands` の子ページ）。ゲームのコマンドを増やすときも、先に `Manual.md` に節を書き、`parent: commands` の子ページを足す
- `external/fara-site-kit`（submodule）は、使うタグに固定する（今は v0.3.0）。kit の develop の最新を追わない。上げるときは、kit のタグのコミットに上げる PR（develop 向け）を出し、確認用で確かめる
- `external/fara-pokemon-battle-assistant`（submodule）は、ポケモン側の変更のたびに `develop` の最新へ更新する（PR を出す）。kit とは別物

## CSP の方針（必ず守る）

CSP は `build/site.json` の `csp` から kit の `csp/render-headers.mjs` が `_headers` に書く。唯一の情報源で、Worker は CSP を付けない。

1. インラインの script（`<script>…</script>`・`onclick` などのイベント属性）は使わない。script は同じオリジン（`'self'`）の外部ファイルだけ。テスト（`SiteTests`）が `.razor` と `index.html` を見る
2. `script-src` に `'unsafe-inline'` と `'unsafe-eval'` は許可しない。`'wasm-unsafe-eval'`（Blazor WebAssembly）は kit が常に入れる。`style-src 'unsafe-inline'` は MudBlazor と読み込み表示のために kit が固定で入れる（以前の静的サイトは `style-src 'self'` だけだった。ゆるめた理由と範囲は README の「CSP」）
3. 外部のホストは、使うものだけ `build/site.json` に書く。今は Cloudflare Turnstile（`challenges.cloudflare.com`）と、Cloudflare Web Analytics のビーコン（script-src `static.cloudflareinsights.com`・connect-src `cloudflareinsights.com`。ゾーンの設定で自動に差し込まれる）だけ。`/report` へは画面の移動（ドキュメントを取り直さない）で行くので、`/report` だけ別の CSP にはできず、全ページで許可している
4. 外部のスクリプト・スタイル・フォント（Google Fonts なし）・アイコンフォント・画像は使わない。アイコンは MudBlazor のインライン SVG（`Icons.Material.*`）か、インライン SVG
5. 新しい UI ライブラリ・外部ライブラリは、同じ CSP の状態で試し、違反 0 件であることを採用の条件にする
6. CSP を緩めた場合は、理由と範囲を README と `worker/index.js` のコメントに書く
7. CSP を変えたら `worker/index.test.mjs` と `tests/FaraBotModeratorSite.Web.Tests` も更新する

## 保存のキー・見た目

- localStorage: テーマ `fara-bot-moderator-site.theme`（値は kit の JSON `{"Theme":"dark"}`。以前の `"dark"` の文字だけの値は `wwwroot/js/site-init.js` が書き直す）、サイドバー `fara-bot-moderator-site.sidebar-hidden`（`"1"` で隠す）。`SiteInfo` の値と `index.html` の `theme-init.js` の `data-*-key` は同じにする（テストが見る）
- ヘッダー・サイドバー・フッター・お知らせ・404・問い合わせのフォームは kit の部品。本文の見た目は `wwwroot/css/site.css`（色は `:root` の変数。ダークは `html[data-bs-theme="dark"]`）
- `site.css` は、以前の BeerCSS の既定のうち本文の見た目に効いていたもの（前に兄弟がある要素の上の間隔 1rem・リンクの inline-flex・コード）を、本文の中（`.hero` `.page-section` `.doc-layout`）だけに置いている。見た目を変えたら `tools/compare/` で現行と比べる
- 名前の衝突に注意: kit は `fsk-` の接頭辞、MudBlazor は `mud-`。このサイトのクラス名は `site-`・本文用の短い名前（`card`・`button`・`prose` など）
- 本文は `.fsk-content` の中だけがスクロールする（kit の既定。フッターは画面の下に固定）。`MobilePageScroll` は false のまま（true にするとスマホでフッターがページの最後に出る）。見出しへのジャンプの余白（`scroll-padding-top`）と目次の高さ（`.doc-toc`）は、この枠が基準

## 確認

```bash
cd site
dotnet test FaraBotModeratorSite.sln          # Content・Shared（検索）・Web（静的な本文・ページ・index.html）
node --test worker/index.test.mjs tests/js/site-init.test.mjs
node --test ../external/fara-site-kit/worker/report.test.mjs ../external/fara-site-kit/worker/maintenance.test.mjs
npm install && npm run build:site             # publish/wwwroot を作る
npx wrangler dev --env dev                    # Cloudflare と同じ動きで確認（Worker 経由。CSP ヘッダーが 1 本で wasm-unsafe-eval を含むこと）
npx wrangler deploy --dry-run --env production && npx wrangler deploy --dry-run --env dev
```
