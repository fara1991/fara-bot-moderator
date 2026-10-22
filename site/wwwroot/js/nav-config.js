// ページ移動（nav.js）の、このサイトの設定。nav.js より前に読み込む（App.razor）。CSP のため、インラインではなく外部のファイルにする。
window.faraNavConfig = {
  // 本文。移動のたびに、この要素の中身を新しいページのものに替える
  main: "#main-content",
  // 左のメニューの中身（リンクと、いま開いているページの印）だけを替える。開閉のボタンとスマホの検索欄は残す
  swap: [".nav-scrollable > nav"],
  // 「本文へスキップ」のリンク（移動のあとも、本文の位置に合わせる）
  skipLink: ".skip-link",
  // ページ移動の対象外。API と、報告フォーム（/report は Cloudflare Turnstile のため CSP が違うので、通常どおり読み込む）
  excludePrefixes: ["/api/", "/report"],
};
