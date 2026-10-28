// レイアウトの自動計測（kit の tools/layout-check）の設定。site/ で `npm run layout-check`（配信・計測は package.json の script）。
// 計測するのは、ビルド済みの静的出力（publish/wwwroot）を配った画面。Blazor の描画が済むまで `.mud-layout` を待つ。
// 除外は、既知の例外だけを rule・selector・reason（必須）で書く（docs/layout-check.md の「3. 除外」）。
export default {
  baseURL: "http://127.0.0.1:8880",
  pages: [
    { path: "/", name: "トップ" },
    // 一覧は本当の URL が /docs。serve-static は同名のフォルダ（docs/）があると index.html を探すため、ファイルの docs.html を指定する（同じ画面）
    { path: "/docs.html", name: "ドキュメント一覧" },
    { path: "/docs/commands", name: "コマンド" },
    { path: "/docs/pokemon-battle", name: "ポケモン（バトル）" },
    { path: "/docs/beatsaber", name: "Beat Saber コマンド" },
    { path: "/report", name: "問い合わせ" },
    // 存在しないパスの 404 は、kit の計測が 2xx 以外を失敗にするため、404 の画面のファイルを直接開く（中身は同じ 404 の画面）
    { path: "/404.html", name: "404（存在しないパスの画面）" },
  ],
  viewports: [360, 390, 768, 1024, 1440],
  viewportHeight: 800,
  readySelector: ".mud-layout",
  menuEsc: { buttonSelector: ".fsk-menu-toggle" },
  // 除外は、既知の例外だけ（rule・selector・reason が必須。page は省くとすべてのページ）。増やすときは理由を具体的に書く
  exclusions: [
    {
      rule: "no-offscreen-focus",
      selector: ".fsk-content a, .fsk-content button, .fsk-content input, .fsk-content textarea, .fsk-content [tabindex]",
      reason: "本文の項目は、Tab で移ると画面の外（下の方の段落）にあるだけ。kit の計測は scroll せずに位置を見るため、下にある本文の項目を「画面の外」と数える（kit の計測の課題）。閉じたドロワーの項目は本文の外にあり、inert で Tab に入らないので、この除外の対象外（そのまま計測する）",
    },
    {
      // パンくず（ドキュメント › コマンド実行 など）。小さな文字リンクが上下に並ぶ。折り返しは、フォントの幅で変わる（CI の Linux と Windows で違った）ため、判定が揺れる
      rule: "tap-target",
      selector: ".breadcrumb a",
      reason: "パンくずの文字リンク（上下に並ぶ小さな文字の列）。44px の判定が、折り返しのわずかな差（フォントの幅）で隣に当たったり外れたりするため。大きさは変えない（見た目の判断）",
    },
    {
      rule: "tap-target",
      selector: "a.fsk-footer-social",
      reason: "フッターの SNS アイコン（22px・間隔約 6px）。44px の当たり判定は隣のアイコンと重なり、横の判定が隣に当たる。大きさと間隔を変えないための除外（大きくするかは見た目の判断）",
    },
    {
      // 受け付けを止めている間（ビルドの既定は止めている）は、送信ボタンが disabled（MudBlazor が pointer-events を止める）。押せない状態は、押せる範囲の判定の対象外
      rule: "tap-target",
      selector: ".fsk-report-submit",
      page: "/report",
      reason: "問い合わせの受け付けを止めている状態の送信ボタン（disabled。MudBlazor が pointer-events を止めるため、判定の点が隣の欄に当たる）。押せない状態なので、押せる範囲の判定の対象外。受け付け中の大きさは、CSS で 48px にしている",
    },
    {
      rule: "tap-target",
      selector: ".fsk-content a",
      page: "/",
      reason: "トップの一覧のリンク（高さ 44px ちょうど）。縦の判定（中心から 21.5px）が、すぐ下の段落に 0.5px 当たる。行間を変えないため。文中のリンク（Releases）も含む",
    },
  ],
};
