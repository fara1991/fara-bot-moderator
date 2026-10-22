// ページ移動（nav.js）のあとに、このサイトの部品を合わせる。nav.js は読み込みのときと、移動のたびに、登録された処理を呼ぶ。
// - スマホのメニューを閉じる（メニューの中身は差し替えるが、開いている印（チェックボックス）は残るため）
// - fara:navigated を出す（site.js が、スクロールの線を合わせる。読み込みのイベントは、移動では出ないため）
(function () {
    "use strict";
    if (!window.faraNav) return;

    window.faraNav.register(function () {
        var toggle = document.getElementById("nav-toggle");
        if (toggle) toggle.checked = false;
        document.dispatchEvent(new Event("fara:navigated"));
    });
})();
