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
