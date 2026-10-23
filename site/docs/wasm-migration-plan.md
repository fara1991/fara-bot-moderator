# 紹介サイトの Blazor WebAssembly + MudBlazor への移行計画

`site/`（現状: .NET 静的 SSR + BeerCSS + nav.js + Worker）を、Blazor WebAssembly + MudBlazor（fara-site-kit v0.2.1）に移す。
手本は pokemon（kit 採用 #267・#269）、avatar の移行（#80〜#85、静的 HTML 本文入り + WASM の併用）、kit の `docs/usage.md` 8.12。

## 1. 方針（変えないもの）

見た目と動作は現行から変えない。次を守る。

- 404 は portfolio 風の大きな 404 とホームへ戻るボタン（kit の `NotFoundView`）。JavaScript なしでも同じ内容を `404.html` に入れる。
- フッターは画面の下に固定（kit の `SiteLayout` の既定。`MobilePageScroll` は false のまま）。`viewport-fit=cover` と safe-area。
- プライバシー・更新履歴は PC で広く、`/report` のフォームは今の幅。改行の `word-break: auto-phrase`。表は 390 幅に収まる。
- 検索のローマ字読み（nn→ん など #114 の仕様）、`Manual.md` 駆動のドキュメントページ。
- localStorage のキー（`fara-bot-moderator-site.theme` / `.sidebar-hidden`）、Cloudflare の設定（Worker 名・ドメイン・`run_worker_first`・MAINTENANCE 変数）。
- Beat Saber コマンド（`/docs/beatsaber`）は 1.40.8 公開まで内容を変えない。見た目の移行だけ。

変わるもの（kit の部品に替わる）は、ヘッダー・サイドバー・フッターの見た目（pokemon・avatar と同じ MudBlazor の外観）。
本文の中（ホーム・ドキュメント・更新履歴・プライバシー・問い合わせ・404）は、現行の CSS をそのまま移して揃える。

## 2. 決定とその理由

| # | 決定 | 理由 |
| --- | --- | --- |
| D1 | `site/` を .NET 10 の Blazor WebAssembly にし、kit（`external/fara-site-kit`、v0.2.1 に固定）の `FaraSiteKit.Razor` を使う | kit は net10.0 だけ。SDK は `global.json` が 10.0.100 で、すでに揃っている |
| D2 | すべての URL を「本文入りの静的 HTML」+「起動後に WASM が引き継ぐ同じ Razor ページ」にする。静的のままにするページは作らない | bot にはインタラクティブなページがほぼ無く（問い合わせだけ）、ドキュメントを静的のまま残すと、MPA と SPA でサイドバー・検索・テーマが二重になる。avatar が静的のまま残したのは、練習ページが WASM だけで動く別物だったから |
| D3 | 静的の本文は、WASM と同じ Razor コンポーネントを、ビルド時に `HtmlRenderer` で描いて作る（kit の `build-pages` の `bodyHtml`） | 本文の情報源が 1 つ。静的と起動後の文章がずれない |
| D4 | `Manual.md` → ページへの変換はビルド時に 1 回だけ行い（`ContentBuilder`、Markdig）、結果を `data/site.json` に書く。ブラウザは Markdown を読まない | WASM に `Manual.md` を読む仕組み（IWebHostEnvironment）は無い。Markdig を配らずに済む。見出しが無い・リンク切れ・pages.json の誤りは、従来どおりビルドで失敗させる |
| D5 | 検索は C# に移す（`SearchEngine`、`Romaji`）。`search.js` と同じ点数・同じ読み。`tests/js/search.test.mjs` の事例を xUnit に移し、移行前の JS との差分実行でも確かめる | Blazor が DOM を持つ画面に JS で候補を差し込むと衝突する。kit の `SearchSheet` は開くまで DOM が無い |
| D6 | テーマの保存は、同じキー `fara-bot-moderator-site.theme` のまま、値を kit の JSON（`{"Theme":"dark"}`）に変える。旧い値（`dark` / `light` の文字だけ）は、`site-init.js` が描画の前に JSON へ書き直す | kit の `theme-init.js` は JSON しか読めず、旧い値だと light に戻ってしまう。サイドバーのキー（`"1"` で隠す）は同じ形なので変えない |
| D7 | 保存が無いときの既定: 端末がダークなら、`site-init.js` が最初の 1 回だけ `{"Theme":"dark"}` を書く（現行は、保存が無いとき端末の設定にいつも追従する） | kit は保存が無いと必ず light。端末に追従する既定は kit に無い（不足 K1）。最初の 1 回だけ追従で、あとは選んだ値を覚える。端末の設定を後で変えても追従しない点だけ、現行と違う |
| D8 | CSP は kit の `csp/render-headers.mjs` が作る `_headers` を唯一の情報源にし、Worker は CSP を付けない。`presets.turnstile` を全ページに許可する | WASM は `'wasm-unsafe-eval'` が要る。`/report` へは画面の移動（ドキュメントの再取得なし）で行くので、`/report` だけ別の CSP にはできない。ゆるめた理由と範囲は README・Worker のコメント・`site/CLAUDE.md` に書く |
| D9 | Worker は `run_worker_first = true` のまま（全リクエストが通る）。`/api/maintenance` を足し、HTML へのお知らせの差し込み（`withNotice`）をやめる。お知らせは Razor の `MaintenanceBanner`、メンテナンス（503）は今までどおり Worker が全リクエストに返す | お知らせが二重に出ないようにする。503 の挙動は変えない |
| D10 | 外部のフォントは使わない（Google Fonts なし）。`CustomTheme` で現行のシステムフォントと色・サイドバー幅 270px を指定する | `site/CLAUDE.md` の CSP の方針 5 |
| D11 | ビルドは `npm run build:site`（`dotnet run` で `data/site.json` と `pages.json` を作る → `dotnet publish` → `build-pages`）。wrangler は `package.json` で固定（4.149.0） | pokemon と同じ形。ローカルと CI で同じ手順 |

## 3. PR の分割（develop 向けのスタック）

develop に入るたびに確認用（twitch-bot-dev）へ公開されるので、**最後の「切り替え」まで、公開される中身は今の静的 SSR のまま**にする。

1. **基盤**（この PR）: この計画書、kit の submodule（v0.2.1）、.NET 10 化、`Shared`（モデル・`SiteContent`・JSON）と `Content`（`ContentBuilder`）の切り出し、コンテンツのテストの移動。公開物は変わらない。
2. **検索（C# 版）**: `SearchText`・`Romaji`・`SearchIndex`（Shared）と、そのテスト。移行前の search.js と同じ結果になることを、実データの 863 件の検索語で確かめた。
3. **ビルドと Web プロジェクト**: `Build`（`data/site.json`・kit 用の `pages.json`・`site.json` の生成）、`Pages`（Razor クラスライブラリ）、`Web`（Blazor WebAssembly）、レイアウト・ページ・CSS・`index.html`、静的な本文の生成（`HtmlRenderer`）、npm スクリプト。ここまでは公開しない。
4. **切り替え**（公開が Blazor WebAssembly に変わる）: Worker（kit の共有 JS・`/api/maintenance`・CSP を `_headers` へ）、`wrangler.toml`、`site.yml`、旧い SSR・BeerCSS・nav.js 一式の削除、`site/CLAUDE.md`・README・CSP の方針の書き直し、現行との比較ツール。

## 4. 検証

- `dotnet test`（Content・Search・Pages・Web）、`node --test`（worker・tests/js）、`wrangler deploy --dry-run`（本番・確認用）。
- 現行の develop（書き出した静的サイトを `wrangler dev` で配信）と、移行後の全ページを、PC 1280/1920・スマホ 390・ライト/ダークで Playwright のスクリーンショット比較。
- 本文テキストの一致検証は、移行前の本文と、移行後の「静的 HTML（JS なし）」「起動後の DOM」の両方に対して行う。
- CSP 違反 0、コンソールエラー 0。`wrangler dev` で、CSP のヘッダーが 1 本だけで `wasm-unsafe-eval` を含むこと。
- 最新コミットの CI（`check-runs`）が success。なお、base が develop でないスタックの PR では `ci.yml` は動かず、`site.yml` だけが動く。

## 5. kit の不足（移行中に見つけたもの。kit は v0.2.1 のまま使い、このリポジトリでは直していない）

- K1: 保存が無いときのテーマが、端末の設定に追従しない（常に light）。→ サイトの `site-init.js` で、端末がダークなら最初の 1 回だけ保存する（D7）
- K2: `ReportFormView` の見出しが `<h3>`（現行の問い合わせは `<h1>`）。末尾の注意書きが「アクセスキー」固定（現行は「Twitch のトークン」）で、GitHub の案内を入れる場所が無い（フォームの下の段落に出した）。入力欄の例文・連絡先の説明はパラメーターで合わせた
- K3: `SiteNavMenu` は平らな一覧だけで、子ページの入れ子（字下げ）が無い。→ サイト側で `MudNavLink` を直接並べた（pokemon と同じやり方）
- K4: フッターの SNS アイコンが 22px の角丸の四角（現行は 32px の丸、スマホ 20px）。kit の統一の見た目として受け入れた
- K5: 保存済みテーマの形式が JSON 固定。旧い文字だけの値を読めない（サイトで移行している）
- K6: `fsk-content` が本文のスクロール枠のため、`#見出し` への移動（同じページ）で、Blazor の既定のスクロールが動かない。→ `search-keys.js` のクリック処理で補った

## 5.1 現行の見た目との差

- ヘッダー・サイドバー・フッター・お知らせの帯・スクロール量の線は kit の見た目（ヘッダーとサイドバーは、ライトでも濃い紫。サイドバーの選択中の項目は、面ではなく白い帯）。本文は同じ
- 本文のスクロールは、ページ全体ではなく `.fsk-content` の中（フッターが常に見えるのは、そのため）
- 404 のボタンの矢印はアイコン、問い合わせはフォームの部品（kit）
- 現行の見た目の癖も、そのまま移した（「含まれるページ」の見出しが大きい・リンクカードの中央寄せ・ページ送りの間隔など。BeerCSS の既定の影響）。直す場合は別の PR

## 6. リスク

- 初回表示が、静的サイトより重くなる（WASM とフレームワークの取得）。静的な本文と読み込み表示で緩和する。
- ローカル（Windows）では `NavigationTests` の nav.js の sha256 が CRLF のため失敗する（CI の Linux は通る）。移行で nav.js を削除するので、この問題も消える。
