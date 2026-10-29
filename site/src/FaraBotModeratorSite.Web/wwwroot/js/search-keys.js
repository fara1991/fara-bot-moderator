// サイト内検索の欄（SiteSearch.razor）のキー操作。Blazor が受ける前に、ブラウザの動きを調整する。
// - 日本語入力の変換中（確定の Enter 含む）のキーは、候補の選択・移動に使わない（Safari などは、確定の Enter を key="Enter"・keyCode=229 で送る）
// - 結果を開いているあいだは、↑↓ Enter で、欄の中の文字のカーソルを動かさない
// - Ctrl+K（Mac は ⌘K）か「/」（入力中でないとき）で、見えている検索欄に移る
// - 結果をクリックしても、検索欄からフォーカスを外さない（外れると、クリックが届く前に結果の一覧が閉じてしまう）
document.addEventListener("keydown", function (e) {
    var t = e.target;
    if ((e.isComposing || e.keyCode === 229) && t && t.closest && t.closest(".site-search")) {
        e.stopImmediatePropagation();
        return;
    }
    if (t && t.classList && t.classList.contains("site-search-input") && t.getAttribute("aria-expanded") === "true") {
        if (e.key === "ArrowDown" || e.key === "ArrowUp" || e.key === "Enter") e.preventDefault();
    }
}, true);

document.addEventListener("keydown", function (e) {
    var k = e.key && e.key.toLowerCase();
    var ctrlK = (e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && k === "k";
    var t = e.target;
    var typing = !!t && t !== document.body && (t.isContentEditable || t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT");
    var slash = e.key === "/" && !e.ctrlKey && !e.metaKey && !e.altKey && !typing;
    if (!ctrlK && !slash) return;
    if (e.isComposing) return;
    // 見えている欄（PC はヘッダー）へ移る。スマホの欄は、ヘッダーの検索ボタンで開くので、ここでは何もしない
    var inputs = document.querySelectorAll(".site-search-input");
    for (var i = 0; i < inputs.length; i++) {
        if (inputs[i].offsetParent !== null) {
            e.preventDefault();
            inputs[i].focus();
            inputs[i].select();
            return;
        }
    }
});

document.addEventListener("mousedown", function (e) {
    if (e.target && e.target.closest && e.target.closest(".site-search-list")) e.preventDefault();
}, true);

// 候補を ↑↓ で選んだとき、選んだ行が一覧の見える範囲に入るようにする（SiteSearch.razor が呼ぶ）
window.faraScrollIntoView = function (id) {
    var el = document.getElementById(id);
    if (el) el.scrollIntoView({ block: "nearest" });
};

// URL の #見出し の位置までスクロールする（下のクリックの処理が呼ぶ）
window.faraScrollToHash = function () {
    var id = decodeURIComponent((location.hash || "").slice(1));
    var el = id ? document.getElementById(id) : null;
    if (el) el.scrollIntoView({ block: "start" });
};

// 同じページの #見出し へのリンク（目次・本文の中のリンク）をクリックしたとき、その見出しまでスクロールする。
// Blazor はこのリンクを自分で処理する（履歴に足すだけ）が、本文は .fbc-content の中だけがスクロールする作りで、既定のスクロールが動かないため
document.addEventListener("click", function (e) {
    var a = e.target && e.target.closest && e.target.closest("a[href]");
    if (!a || e.defaultPrevented === undefined || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    var url;
    try { url = new URL(a.href, location.href); } catch { return; }
    if (url.origin === location.origin && url.pathname === location.pathname && url.hash.length > 1) {
        setTimeout(window.faraScrollToHash, 50);
    }
}, false);
