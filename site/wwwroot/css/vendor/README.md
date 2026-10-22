# vendor（外部のライブラリを、同じオリジンから配るもの）

## BeerCSS（CSS のみ）

| 項目 | 値 |
|---|---|
| ライブラリ | BeerCSS（https://www.beercss.com/ ・ https://github.com/beercss/beercss） |
| 版 | 5.0.3（npm 公開日 2026-08-04） |
| ライセンス | MIT（`LICENSE-beercss.txt`） |
| 入力 | `npm pack beercss@5.0.3` の `dist/cdn/beer.min.css`（md5 `74789a58e5b1f4af51550f165b115d04`） |
| 出力 | `beer.min.css`（md5 `1e1565b430d311c00c828f8d0b494142`、86,910 bytes） |

### 作り方（再現）

`site/` で次を実行する。入力の md5 と、出力の md5 を確かめて、違えば止まる。

```bash
node tools/vendor-beercss.mjs
```

### パッチの内容

配布の `beer.min.css` には、`@font-face` が 4 つあり（Material Symbols の Outlined・Rounded・Sharp・Subset）、
それぞれの 2 番目の `src` が `https://cdn.jsdelivr.net/npm/beercss@5.0.3/...` の絶対 URL になっている。
サイトの CSP は `font-src` を `'self'` に限るので、読み込み時に 4 件の違反が出る（5.0.3 で確認）。

このサイトはアイコンフォントを使わない（アイコンはインライン SVG。`Components/Shared/NavIcon.razor` など）ので、
4 つの `@font-face` ごと取り除く。パッチはこれだけ。`beer.min.js` は取り込まない（DOM を書き換えるため、サイトの JS と合わない）。

### 注意

- 版を上げるときは、`tools/vendor-beercss.mjs` の `VERSION`・`INPUT_MD5`・`OUTPUT_MD5` を一緒に直す。
- BeerCSS の既定の色は `:root` の変数（`--primary` など）。色はこのサイトの `wwwroot/css/site.css` が上書きする（ライト・ダークのトークン）。
- このフォルダの README は公開しない（`wwwroot/.assetsignore` で除外）。
