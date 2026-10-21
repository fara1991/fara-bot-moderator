// サイドバーの表示・非表示（PC）。選んだ状態はこのブラウザに覚えておく。
// 最初に <head> で読み込み、描画の前に状態を反映して、開いたときのちらつきを防ぐ。
// ボタンは [data-sidebar-toggle]（サイドバーの「隠す」と、隠したときの左上の「開く」）。
// 「隠す」ボタンは開いているときだけ、「開く」ボタンは隠しているときだけ見えるので、aria-expanded は Razor に固定で書く。
(function () {
    "use strict";
    var KEY = "fara-bot-moderator-site.sidebar-hidden";
    var root = document.documentElement;

    function read() {
        try { return localStorage.getItem(KEY) === "1"; } catch (e) { return false; }
    }
    function write(hidden) {
        try { localStorage.setItem(KEY, hidden ? "1" : "0"); } catch (e) { }
    }

    root.classList.toggle("sidebar-hidden", read());
    document.addEventListener("click", function (e) {
        if (!e.target.closest || !e.target.closest("[data-sidebar-toggle]")) return;
        var hidden = !root.classList.contains("sidebar-hidden");
        root.classList.toggle("sidebar-hidden", hidden);
        write(hidden);
        // 開いたら、隠すボタンへ、隠したら、開くボタンへ、キーボードの位置を移す
        var next = document.querySelector(hidden ? ".sidebar-reveal" : ".side-hide");
        if (next) next.focus();
    });
})();
