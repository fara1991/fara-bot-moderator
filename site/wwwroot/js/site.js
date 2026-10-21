// サイドバーの表示・非表示（PC）。選んだ状態はこのブラウザに覚えておく。
// 最初に <head> で読み込み、描画の前に状態を反映して、開いたときのちらつきを防ぐ。
// ボタンは [data-sidebar-toggle]（サイドバーの「隠す」と、隠したときの左上の「開く」）。
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
    function apply(hidden) {
        root.classList.toggle("sidebar-hidden", hidden);
        var buttons = document.querySelectorAll("[data-sidebar-toggle]");
        for (var i = 0; i < buttons.length; i++) buttons[i].setAttribute("aria-expanded", hidden ? "false" : "true");
    }

    root.classList.toggle("sidebar-hidden", read());
    document.addEventListener("DOMContentLoaded", function () { apply(root.classList.contains("sidebar-hidden")); });
    document.addEventListener("click", function (e) {
        if (!e.target.closest || !e.target.closest("[data-sidebar-toggle]")) return;
        var hidden = !root.classList.contains("sidebar-hidden");
        apply(hidden);
        write(hidden);
        // 開いたら、隠すボタンへ、隠したら、開くボタンへ、キーボードの位置を移す
        var next = document.querySelector(hidden ? ".sidebar-reveal" : ".side-hide");
        if (next) next.focus();
    });
})();
