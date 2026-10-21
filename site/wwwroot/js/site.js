// サイドバーの表示・非表示（PC）。左上のハンバーガー（[data-sidebar-toggle]）を押すたびに切り替える（Google の管理画面と同じ）。
// 選んだ状態はこのブラウザに覚えておく。最初に <head> で読み込み、描画の前に状態を反映して、開いたときのちらつきを防ぐ。
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
    // aria-expanded は、サイドバーが開いているとき true、隠しているとき false
    function sync() {
        var open = !root.classList.contains("sidebar-hidden");
        var buttons = document.querySelectorAll("[data-sidebar-toggle]");
        for (var i = 0; i < buttons.length; i++) buttons[i].setAttribute("aria-expanded", open ? "true" : "false");
    }

    root.classList.toggle("sidebar-hidden", read());
    document.addEventListener("DOMContentLoaded", sync);
    document.addEventListener("click", function (e) {
        if (!e.target.closest || !e.target.closest("[data-sidebar-toggle]")) return;
        var hidden = !root.classList.contains("sidebar-hidden");
        root.classList.toggle("sidebar-hidden", hidden);
        write(hidden);
        sync();
    });
})();
