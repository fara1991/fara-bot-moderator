# CLAUDE.md（site/ 用）

紹介サイト（.NET 10 の Blazor WebAssembly + MudBlazor。共通部品は fara-blazor-component）。`npm run build:site` で、ブラウザのアプリ・本文入りの静的 HTML・`_headers` を `publish/wwwroot` に作り、Cloudflare の Worker（`worker/index.js`）が配信する。Worker は /api/maintenance・メンテナンス画面（503）・`/api/report` を担当する。詳細は README.md、移行の経緯と決定は `docs/wasm-migration-plan.md`。

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
- ダウンロードのページ（`/download`。`Pages/Download.razor`）は、アプリの入手先（GitHub Releases の最新版へのリンク・動作環境・導入の手順）。動作環境は `Manual.md`・README・`release-app.yml` の事実だけを書く。サイドメニューに他のサイトへのリンクは置かない（入手先はこのページから案内する。フッターの SNS は残す）
- 説明ページの本文は `Manual.md`（リポジトリ直下。アプリの操作説明書と同じ）。ページの題名・グループ・説明・親子・読みは `Content/pages.json`
- Beat Saber のコマンドの説明は `/docs/beatsaber`（`Manual.md` の「5.5. Beat Saber コマンド」の節と、`pages.json` の `parent: commands` の子ページ）。ポケモンのコマンドの説明は `/docs/pokemon` の 1 ページ（`Manual.md` の「5.4」の節の下の見出しを `sections` に並べ、`inlineToc: true` で先頭に目次を出す。以前の子ページは廃止）。サイドメニューの名前は `menuTitle`（ポケモン・Beat Saber・各種コマンド）。ゲームのコマンドを増やすときも、先に `Manual.md` に節を書き、`parent: commands` の子ページを足す
- 問い合わせの回数制限（kit v0.8.0・1 日 3 回・送信元ごと）: `worker/index.js` の `export { ReportRateLimiter }` と `wrangler.toml` の `durable_objects`・`migrations`（トップレベル・`env.production`・`env.dev` の 3 か所）が要る。どれかを消すと制限が止まる。`TURNSTILE_SECRET` を変えると計数はリセットされる。プライバシーの記載は `Privacy.razor`（テストが見る）。詳細は README.md
- `external/fara-blazor-component`（submodule）は、使うタグに固定する（今は v1.0.1）。kit の develop の最新を追わない。上げるときは、kit のタグのコミットに上げる PR（develop 向け）を出し、確認用で確かめる。kit を上げたら、kit の docs/changelog.md の「CI に影響する変更」を読む（v0.7.0 からは、`layout-check.config.mjs` の `menuEsc.closeSelector` が要る）
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
- 名前の衝突に注意: kit は `fbc-` の接頭辞、MudBlazor は `mud-`。このサイトのクラス名は `site-`・本文用の短い名前（`card`・`button`・`prose` など）
- 本文は `.fbc-content` の中だけがスクロールする（kit の既定。フッターは画面の下に固定）。`MobilePageScroll` は false のまま（true にするとスマホでフッターがページの最後に出る）。見出しへのジャンプの余白（`scroll-padding-top`）と目次の高さ（`.doc-toc`）は、この枠が基準

## 品質チェック（レイアウト・ESLint・C#）

- **レイアウトの自動計測**（`npm run layout-check`）: `publish/wwwroot`（`npm run build:site` の出力）を kit の `serve-static` で配り、kit の計測（`external/fara-blazor-component/docs/layout-check.md`）で 8 ページ × 5 幅（360〜1440px）を見る。設定は `layout-check.config.mjs`、起動は `tools/run-layout-check.mjs`。違反は CSS・Razor で直す。直せないものだけ除外に書く
  - 除外は `rule`・`selector`・`reason` を必ず書く（`reason` が空だと設定エラー）。`page` を書くと、そのページだけ。**理由は、その項目が何で、なぜ直せないかを具体的に書く**（「誤検出」だけでは書かない）。除外を増やすときは、まず CSS で直せないかを確かめる
  - kit の計測には制限がある（除外の理由の元）: 2xx 以外のページは開けない（404 は `/404.html` の画面で測る）。`/docs` のように同名のフォルダがあると `docs.html` を指定する（`serve-static` がフォルダの `index.html` を探すため）。本文の下の方の項目は、Tab で「画面の外」と数えられる（`no-offscreen-focus`）
  - 計測の前に kit の依存を入れる: `cd external/fara-blazor-component && npm ci`（kit の `tools/` は、`playwright-core` や `globals` を kit の中の `node_modules` から読むため）
- **ESLint**（`npm run lint`）: 設定は `eslint.config.mjs`。共通のルールは kit の `tools/lint/eslint.config.shared.mjs`。対象は site/ の JS（`worker/`・`tools/`・`tests/`・画面の `wwwroot/js/`）。`tools/compare/` は `page.evaluate` の中がブラウザで動くので、ブラウザの環境も使う。**`.github/scripts` は ESLint の基準パス（site/）の外なので対象外**（node --test で確かめる）
- **C# のラチェット**（リポジトリ直下の `Directory.Build.props` と、ルート `.editorconfig` の `[*.cs]`。アプリ・テスト・site の両方に効く。pokemon・kit の submodule は自分の `.editorconfig`（`root = true`）を持つので、ここは効かない）: kit の `dotnet.editorconfig`（v1.0.1 時点で `CS4014`・`VSTHRD100`・`CA1816`・`IDE0059`・`CA1031`・`CA2000`・`VSTHRD002`・`VSTHRD003`・`VSTHRD103` は error）を読み、その上に、このリポジトリで違反が 0 件になったもの（`CA2000`・`VSTHRD001`・`VSTHRD002`・`VSTHRD101`・`VSTHRD110`・`VSTHRD200`・`CS1573`・`xUnit2029`）を、ルート `.editorconfig` の `[*.cs]` で error に足している。**kit の写しではなく、kit の上書き**（kit の `dotnet.editorconfig` はそのまま）。新しく違反を作らないこと（error のルールは、ビルドが落ちる）。どうしても直せないもの（WPF・TwitchLib のイベントハンドラーの `async void`、WPF の `Dispatcher` など）だけ、`[SuppressMessage(..., Justification = "理由")]` を、そのメンバー 1 つにだけ付ける（`#pragma` と `.editorconfig` での一括の抑止はしない）

| ルール | 重大度 | 違反（2026-10 時点） | 中身・方針 |
| --- | --- | --- | --- |
| `CS4014` | error | 0 | await していない非同期呼び出し |
| `CA1816` | error | 0 | Dispose で `GC.SuppressFinalize` を呼んでいない（この PR で 4 箇所直した） |
| `IDE0059` | error | 0 | 不要な代入（`EnforceCodeStyleInBuild` を `-p:` で付けて確認。アプリの csproj は `False` のまま） |
| `CA1031` | error（kit。ルート `.editorconfig` での引き下げは無し） | 0（アプリ） | 一般の例外の catch。**想定できる型は絞り**（`TwitchApiController` の 8 件は `IsExpectedApiFailure`、`BouyomiChanController.IsRunningAsync` は `IsConnectionFailure`。想定外の例外は上位の最上位の catch へ出る）、配信中にボットを落とさないために広く握る箇所（画面・TwitchLib のイベントの最上位、fire-and-forget、`RefreshAccessTokenAsync`、`RotateAsync`、`GetChatIconAsync`）は、理由つきの `SuppressMessage` を 1 メンバーずつ付けている。新しい `catch (Exception)` は、型を絞るか、理由つきで抑止する（ビルドが落ちる）。|
| `CA2000` | error（ルート `.editorconfig`） | 0 | Dispose 漏れ（static の `HttpClient` 1 件は理由つきで抑止、site の `Program.cs` は `using var` に） |
| `VSTHRD100` | error（kit） | 0 | `async void`。WPF・TwitchLib のイベントハンドラー 14 件は型上必要なので、理由つきで抑止（本文は try/catch で受ける） |
| `VSTHRD001`・`VSTHRD002`・`VSTHRD101`・`VSTHRD110`・`VSTHRD200`・`CS1573`・`xUnit2029` | error（ルート `.editorconfig`） | 0 | `Dispatcher` の使用（JoinableTaskFactory 無し）・完了後の `Result`・`async` ラムダのイベント・結果の読み捨て・`Async` の接尾辞・XML コメントの `param` 漏れ・テストの `Assert.Empty` の誤用 |

- **pokemon（`external/fara-pokemon-battle-assistant`）と kit の警告は対象外**（submodule の中は変えない）。アプリのビルドには pokemon の `CS1573` が 30 件（15 箇所 × net8.0・net10.0 の 2 回）。site のビルドの警告は 0 件（kit v1.0.1 で kit 側の警告は 0 件になった）

## 確認

```bash
cd site
dotnet test FaraBotModeratorSite.sln          # Content・Shared（検索）・Web（静的な本文・ページ・index.html）
node --test worker/index.test.mjs tests/js/site-init.test.mjs
node --test ../external/fara-blazor-component/worker/report.test.mjs ../external/fara-blazor-component/worker/report-rate-limit.test.mjs ../external/fara-blazor-component/worker/maintenance.test.mjs
npm install && npm run build:site             # publish/wwwroot を作る
(cd ../external/fara-blazor-component && npm ci)      # 計測と lint の依存（kit の node_modules）
npm run layout-check                          # レイアウトの自動計測（publish/wwwroot に対して）
npm run lint                                  # ESLint（site/ の JS）
npx wrangler dev --env dev                    # Cloudflare と同じ動きで確認（Worker 経由。CSP ヘッダーが 1 本で wasm-unsafe-eval を含むこと）
npx wrangler deploy --dry-run --env production && npx wrangler deploy --dry-run --env dev
```
