// サイドバーの表示・非表示（PC）と、ライト・ダークの切り替え。選んだ状態はこのブラウザに覚えておく。
// 最初に <head> で読み込み、描画の前に状態を反映して、開いたときのちらつきを防ぐ。
// - 左上のハンバーガー（[data-sidebar-toggle]）を押すたびに、サイドバーを表示・非表示（Google の管理画面と同じ）
// - [data-theme-toggle] のスイッチ: ON = ダークモード、OFF = ライトモード。何も選んでいないときは、端末の設定に合わせる
(function () {
    "use strict";
    var SIDEBAR_KEY = "fara-bot-moderator-site.sidebar-hidden";
    var THEME_KEY = "fara-bot-moderator-site.theme";
    var root = document.documentElement;

    function read(key) {
        try { return localStorage.getItem(key); } catch (e) { return null; }
    }
    function write(key, value) {
        try { localStorage.setItem(key, value); } catch (e) { }
    }

    // ---- テーマ（描画の前に反映）
    function systemDark() {
        return !window.matchMedia || !window.matchMedia("(prefers-color-scheme: light)").matches;
    }
    function isDark() {
        var t = root.getAttribute("data-theme");
        return t ? t === "dark" : systemDark();
    }
    var savedTheme = read(THEME_KEY);
    if (savedTheme === "light" || savedTheme === "dark") root.setAttribute("data-theme", savedTheme);

    // ---- サイドバー（描画の前に反映）
    root.classList.toggle("sidebar-hidden", read(SIDEBAR_KEY) === "1");

    // aria-expanded（サイドバーが開いているとき true）と、スイッチの aria-checked（ダークモードのとき true）
    function sync() {
        var open = !root.classList.contains("sidebar-hidden");
        var buttons = document.querySelectorAll("[data-sidebar-toggle]");
        for (var i = 0; i < buttons.length; i++) buttons[i].setAttribute("aria-expanded", open ? "true" : "false");
        var dark = isDark();
        var switches = document.querySelectorAll("[data-theme-toggle]");
        for (var j = 0; j < switches.length; j++) switches[j].setAttribute("aria-checked", dark ? "true" : "false");
    }

    document.addEventListener("DOMContentLoaded", sync);
    document.addEventListener("click", function (e) {
        if (!e.target.closest) return;
        if (e.target.closest("[data-sidebar-toggle]")) {
            var hidden = !root.classList.contains("sidebar-hidden");
            root.classList.toggle("sidebar-hidden", hidden);
            write(SIDEBAR_KEY, hidden ? "1" : "0");
            sync();
        } else if (e.target.closest("[data-theme-toggle]")) {
            var next = isDark() ? "light" : "dark";
            root.setAttribute("data-theme", next);
            write(THEME_KEY, next);
            sync();
        }
    });
})();
