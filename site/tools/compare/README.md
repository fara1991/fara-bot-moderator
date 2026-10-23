# 見た目・本文の比較ツール（移行前後の確認用）

静的 SSR だった頃の紹介サイトと、Blazor WebAssembly 版を、Playwright で同じ条件（PC 1280/1920・スマホ 390・ライト/ダーク・全ページ）で撮って比べる。
CI では動かさない（ブラウザが要る）。サイトの見た目を大きく変えるとき（kit の更新・CSS の整理）に使う。

準備（リポジトリの外の作業フォルダで）: `npm i playwright` と `npx playwright install chromium`

```bash
# 比べる 2 つのサイトを、それぞれ http://127.0.0.1:8801 と :8802 で配信する（wrangler dev。new は site/ で npm run build:site のあと）
node capture.mjs http://127.0.0.1:8801 shots-old old       # 旧い側（old: 保存は "dark" の文字・本文は #main-content）
node capture.mjs http://127.0.0.1:8802 shots-new new       # 新しい側（new: 保存は JSON・本文は #fsk-main .fsk-content）
node compare-text.mjs shots-old/result.json shots-new/result.json pc1280-dark   # 本文テキスト・題名・ステータスの一致
python3 side.py pc1280-dark-home                           # 2 枚を横に並べた画像（shots-old / shots-new の下）

# 計算済みスタイルの比較（間隔・文字・色）
node styles.mjs http://127.0.0.1:8801 styles-old.json dark 1280
node styles.mjs http://127.0.0.1:8802 styles-new.json dark 1280
node stylediff.mjs styles-old.json styles-new.json
```

`capture.mjs` は、コンソールのエラーと警告、横はみ出し（scrollWidth > clientWidth）も `result.json` に記録する。
