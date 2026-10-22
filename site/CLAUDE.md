# CLAUDE.md（site/ 用）

紹介サイト（.NET 8 の静的 SSR）。`dotnet run -- --export dist` で静的に書き出し、Cloudflare の Worker（`worker/index.js`）が `dist` を配信する。Worker は CSP などのセキュリティヘッダー、お知らせ帯、メンテナンス画面、`/api/report` を担当する。詳細は README.md。

## ブランチ運用（必ず守る）

- 作業ブランチは `develop` から作成する
- 作業ブランチの PR は `develop` 向けに作る
- `master` へのマージはユーザーが行う。Claude は `master` へはマージしない

## CSP の方針（必ず守る）

1. 通常のページは、同じオリジン（`'self'`）のスクリプトとスタイルだけを許可する。インラインのスクリプト・スタイル（`<script>…</script>`、`onclick` などのイベント属性、`<style>`、`style` 属性、JS が実行時に付ける `style` 属性）は使わない。
2. 例外は、必要なページだけに限り、ホスト名を限定して許可する（現状は `/report` の Cloudflare Turnstile=`challenges.cloudflare.com` のみ。`worker/index.js` の `REPORT_PAGE_CONTENT_SECURITY_POLICY`）。
3. `'unsafe-inline'` と `'unsafe-eval'` は許可しない。
4. WebAssembly が必要になったときだけ `'wasm-unsafe-eval'` を検討する（現状は使っていない）。
5. 外部のスクリプト・スタイル・フォント・アイコンフォント・画像は使わない。アイコンはインライン SVG（`Components/Shared/NavIcon.razor` など）にする。
6. 新しい UI ライブラリ・外部ライブラリは、bot と同じ CSP を付けた状態で試し、違反 0 件であることを採用の条件にする。
7. CSP を緩めた場合は、理由と範囲を README と `worker/index.js` のコメントに書く。
8. CSP を変えたら `worker/index.test.mjs` のテストも更新する。

## 共有ファイル（他のリポジトリと同一内容を保つ）

- `worker/maintenance.js`・`worker/report.js`：pokemon・avatar・portfolio・bot の 4 つで同一内容。
- `wwwroot/js/nav.js`・`wwwroot/js/report.js`：avatar と bot で同一内容（CR を除いて同一）。`NavigationTests` が `nav.js` の sha256 を見る。
- 変更するときは、全リポジトリに同じ変更を入れる。

## その他

- `external/fara-pokemon-battle-assistant`（submodule）は、ポケモン側の変更のたびに `develop` の最新へ更新する（PR を出す）。
- Beat Saber のコマンドの説明は、ユーザーが後で書く。`/docs/beatsaber` は `pages.json` から外してある。`Manual.md` に節を書いてから、`parent: commands` の子ページを足す。

## 見た目（BeerCSS）

- 取り込み: `node tools/vendor-beercss.mjs`（BeerCSS 5.0.3 の `beer.min.css` を取り、`@font-face` を外す。md5 を確かめる）。版・md5・パッチ内容は `wwwroot/css/vendor/README.md`。`beer.min.js` は取り込まない
- 部品は BeerCSS のクラス（`.button`・`.button.border`・`.card`・`nav` の `a.active`）を使い、色・寸法は `wwwroot/css/site.css` で合わせる。`ui()`（テーマ切り替え）と BeerCSS の JS は使わない（DOM を書き換えて、Blazor と site.js の処理と衝突するため）
- BeerCSS の既定は、ページの要素名に対しても効く（`body`・`header`・`nav`・`article`・`main`・`pre`・`blockquote`・`label`）。新しい要素を足したら、site.css で上書きする
- 名前の衝突に注意: BeerCSS は `.page`（開くとき `opacity: 0`）・`.field`・`.border`・`.button` などの名前を持つ。レイアウトの名前は `site-page`・`report-field` のように、BeerCSS と被らない名前にする
- 色トークンは `wwwroot/css/site.css` の `:root`（既定はダーク）と `@media (prefers-color-scheme: light)`・`:root[data-theme="light"]`（ライト）。BeerCSS の変数名（`--primary`・`--surface`・`--on-surface` など）に合わせている

## 確認

```bash
cd site
dotnet test tests/FaraBotModeratorSite.Tests
node --test worker/report.test.mjs worker/index.test.mjs worker/maintenance.test.mjs tests/js/nav.test.mjs tests/js/site-nav.test.mjs
```
