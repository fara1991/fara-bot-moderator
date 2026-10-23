// このサイト固有の起動時の処理（head で、描画の前に読む。kit の theme-init.js より前に読むこと）。
// テーマ（ライト／ダーク）と PC のサイドバーを隠す設定の反映は、kit の theme-init.js が行う。
(function () {
    "use strict";
    var KEY = "fara-bot-moderator-site.theme";   // index.html の theme-init.js の data-storage-key、SiteInfo.StorageKey と同じ
    try {
        var saved = localStorage.getItem(KEY);
        if (saved === "light" || saved === "dark") {
            // 以前の静的サイトは、"dark" / "light" の文字だけを保存していた。kit は JSON（{"Theme":"dark"}）しか読めないので、同じキーのまま書き直す
            localStorage.setItem(KEY, JSON.stringify({ Theme: saved }));
        } else if (saved === null) {
            // まだ選んでいないとき: 端末がダークなら、最初の 1 回だけダークにする（以前は、選ぶまで端末の設定にいつも合わせていた。kit の既定は常にライト）。
            // 端末の設定は、ライトを明示したとき以外はダーク（以前の判定と同じ）。あとは、選んだ値を覚える
            if (!window.matchMedia || !window.matchMedia("(prefers-color-scheme: light)").matches) {
                localStorage.setItem(KEY, JSON.stringify({ Theme: "dark" }));
            }
        }
    } catch (e) { /* localStorage が使えないときは、kit の既定（ライト）のまま */ }
})();
