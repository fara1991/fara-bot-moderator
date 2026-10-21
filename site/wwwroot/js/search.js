// サイト内検索（ヘッダーとスマホのメニューの欄）。/search-index.json（ページと、ページの中の見出しごとの節）を、最初に使うときに一度だけ読む。
// 入力するとすぐに、一致した場所（グループ › ページ › 見出し）と、一致した部分の前後の文章を出す。
// ↑↓ で選んで Enter、Esc で閉じる。どのページでも Ctrl+K（Mac は ⌘K）か「/」で、見えている検索欄に移る。
// 日本語は単語区切りがないので、形態素解析はせず、スペースで区切った語が「すべて含まれる」ものを一致とする。全角・半角の違いは NFKC でそろえる。
(function () {
    "use strict";
    var MAX = 10;
    var indexPromise = null;

    function load() {
        if (!indexPromise) {
            indexPromise = fetch("/search-index.json").then(function (r) {
                if (!r.ok) throw new Error("search index " + r.status);
                return r.json();
            }).then(function (list) {
                list.forEach(function (e) { e._t = norm(e.title); e._p = norm(e.page); e._x = norm(e.text); });
                return list;
            }).catch(function (err) { indexPromise = null; throw err; });
        }
        return indexPromise;
    }

    function norm(s) { return String(s).normalize("NFKC").toLowerCase(); }
    function terms(q) { return norm(q).split(/\s+/).filter(Boolean); }
    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]; });
    }
    function escapeRegExp(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); }

    /** すべての語を含むものを、見出し > ページ名 > 本文の順に点数を付けて並べる */
    function search(index, q) {
        var ts = terms(q), hits = [];
        if (!ts.length) return hits;
        index.forEach(function (e) {
            var score = 0;
            for (var i = 0; i < ts.length; i++) {
                var t = ts[i], s = 0;
                if (e._t === t) s = 30;
                else if (e._t.indexOf(t) === 0) s = 15;
                else if (e._t.indexOf(t) >= 0) s = 10;
                else if (e._p.indexOf(t) >= 0) s = 4;
                else if (e._x.indexOf(t) >= 0) s = 1;
                else { score = 0; break; }
                score += s;
            }
            if (score > 0) hits.push({ entry: e, score: score + (e.type === "page" ? 0.5 : 0) });
        });
        hits.sort(function (a, b) { return b.score - a.score; });
        return hits;
    }

    /** 一致した部分を <mark> で囲む（全角・半角が違っても一致するよう、1 文字ずつ正規化した文字列で位置を探す） */
    function highlight(text, ts) {
        text = String(text);
        if (!ts.length) return escapeHtml(text);
        var normed = "", map = [];
        for (var i = 0; i < text.length; i++) {
            var n = norm(text[i]);
            for (var k = 0; k < n.length; k++) { normed += n[k]; map.push(i); }
        }
        var pattern = new RegExp(ts.slice().sort(function (a, b) { return b.length - a.length; }).map(escapeRegExp).join("|"), "g");
        var html = "", last = 0, m;
        while ((m = pattern.exec(normed)) !== null) {
            if (!m[0].length) { pattern.lastIndex++; continue; }
            var s = map[m.index], e = map[m.index + m[0].length - 1] + 1;
            if (s < last) continue;
            html += escapeHtml(text.slice(last, s)) + "<mark>" + escapeHtml(text.slice(s, e)) + "</mark>";
            last = e;
        }
        return html + escapeHtml(text.slice(last));
    }

    /** 本文から、最初に一致した語のまわりを切り出す（一致が無ければ、先頭から） */
    function snippet(text, ts, width) {
        text = String(text || "");
        var lower = norm(text), pos = -1;
        for (var i = 0; i < ts.length && pos < 0; i++) pos = lower.indexOf(ts[i]);
        if (pos < 0) return text.slice(0, width) + (text.length > width ? "…" : "");
        // 正規化で文字数が変わっても大きくずれないよう、元の文字列の同じ位置あたりを使う
        var start = Math.max(0, pos - Math.floor(width / 3)), end = Math.min(text.length, start + width);
        return (start > 0 ? "…" : "") + text.slice(start, end) + (end < text.length ? "…" : "");
    }

    function init(root) {
        var input = root.querySelector(".site-search-input"), list = root.querySelector(".site-search-list");
        if (!input || !list) return;
        var items = [], active = -1, timer = 0, last = "";

        function open() { list.hidden = false; input.setAttribute("aria-expanded", "true"); }
        function close() {
            list.hidden = true; active = -1;
            input.setAttribute("aria-expanded", "false"); input.removeAttribute("aria-activedescendant");
        }
        function render(hits, q) {
            var ts = terms(q);
            var html = hits.slice(0, MAX).map(function (h, i) {
                var e = h.entry, where = escapeHtml(e.group) + " › " + escapeHtml(e.page) + (e.type === "section" && e.title !== e.page ? " › " + highlight(e.title, ts) : "");
                return '<a class="site-search-item" role="option" id="' + list.id + "-" + i + '" href="' + escapeHtml(e.url) + '" aria-selected="false">' +
                    '<span class="site-search-title">' + highlight(e.title, ts) + (e.type === "page" ? '<span class="site-search-badge">ページ</span>' : "") + "</span>" +
                    '<span class="site-search-where">' + where + "</span>" +
                    '<span class="site-search-snippet">' + highlight(snippet(e.text, ts, 110), ts) + "</span></a>";
            }).join("");
            if (!hits.length) html = '<div class="site-search-empty">「' + escapeHtml(q) + "」に一致するものはありません</div>";
            else if (hits.length > MAX) html += '<div class="site-search-more">ほか ' + (hits.length - MAX) + " 件。語を足すと絞り込めます</div>";
            list.innerHTML = html;
            items = Array.prototype.slice.call(list.querySelectorAll(".site-search-item"));
            active = -1;
            open();
        }
        function update() {
            var q = input.value.trim();
            last = q;
            if (!q) { close(); return; }
            load().then(function (index) {
                if (q !== last) return;
                render(search(index, q), q);
            }, function () {
                list.innerHTML = '<div class="site-search-empty">検索インデックスを読み込めませんでした</div>';
                open();
            });
        }
        function move(d) {
            if (!items.length) return;
            active += d;
            if (active >= items.length) active = -1;
            else if (active < -1) active = items.length - 1;
            items.forEach(function (el, i) { el.classList.toggle("is-active", i === active); el.setAttribute("aria-selected", i === active ? "true" : "false"); });
            if (active >= 0) { input.setAttribute("aria-activedescendant", items[active].id); items[active].scrollIntoView({ block: "nearest" }); }
            else input.removeAttribute("aria-activedescendant");
        }

        input.addEventListener("focus", function () {
            load().catch(function () { /* 失敗は、入力したときに表示する */ });
            if (input.value.trim()) update();
        });
        input.addEventListener("input", function () { clearTimeout(timer); timer = setTimeout(update, 60); });
        input.addEventListener("keydown", function (e) {
            if (e.isComposing) return;   // 日本語入力の変換中は、Enter などを奪わない
            if (e.key === "ArrowDown" || e.key === "ArrowUp") {
                if (list.hidden) { update(); return; }
                e.preventDefault();
                move(e.key === "ArrowDown" ? 1 : -1);
            } else if (e.key === "Enter") {
                var target = active >= 0 ? items[active] : items[0];
                if (!list.hidden && target) { e.preventDefault(); location.href = target.getAttribute("href"); }
            } else if (e.key === "Escape") {
                if (!list.hidden) { e.preventDefault(); close(); } else input.blur();
            }
        });
        list.addEventListener("mousedown", function (e) { e.preventDefault(); });   // クリックで入力欄のフォーカスを外さない
        document.addEventListener("click", function (e) { if (!root.contains(e.target)) close(); });
        root.faraFocus = function () { input.focus(); input.select(); };
    }

    function isTyping(el) {
        if (!el || el === document.body) return false;
        if (el.isContentEditable) return true;
        return el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.tagName === "SELECT";
    }

    function start() {
        var roots = Array.prototype.slice.call(document.querySelectorAll("[data-site-search]"));
        roots.forEach(init);
        document.addEventListener("keydown", function (e) {
            var k = e.key && e.key.toLowerCase();
            var ctrlK = (e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && k === "k";
            var slash = e.key === "/" && !e.ctrlKey && !e.metaKey && !e.altKey && !isTyping(e.target);
            if (!ctrlK && !slash) return;
            // 見えている欄（PC はヘッダー、スマホはメニューの中）へ移る
            var visible = roots.filter(function (r) { return r.offsetParent !== null; })[0];
            if (!visible || !visible.faraFocus) return;
            e.preventDefault();
            visible.faraFocus();
        });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
    else start();
})();
