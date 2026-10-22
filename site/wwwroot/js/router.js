// サイト内の移動を速くする。同じサイトのリンクを押したとき、画面全体を読み直さず、本文（<main id="main-content">）だけを差し替える。
// 検索エンジンや直接開いたとき、JS が使えないときは、従来どおりの HTML（通常のリンク）で動く。サーバーが返す HTML は変えていない。
//
// - 差し替えるのは <main> の中身だけ。ヘッダー・サイドバー・検索・テーマ・背景（#site-scene）・スクロールの線は、同じ DOM のまま動き続ける（イベントを付け直さない）
// - <title> と description・og・robots・canonical は、取得したページの <head> に合わせる。サイドバーの現在地（class と aria-current）も合わせる
// - /report は、ページごとに CSP（Turnstile のための許可）が違い、報告フォームの script は差し替えでは動かないので、移動せず通常どおり読み直す
// - 取得に失敗したとき・HTML でないとき・本文に script があるときは、通常の移動に戻す（location.assign。読み直し）
// - 同じページの中の #見出し（目次・スキップリンク）は、ブラウザの通常の動きに任せる
// - 戻る・進むは、スクロールの位置を覚えていて戻す。履歴の状態（history.state.y）に、この画面のスクロール位置を保存する
// - 移動したら document に fara:navigated を出す（site.js が、スクロールの線を合わせる）
// 外部ファイルだけを使う（CSP は変えない）。書き方は他の js（site.js など）にそろえた ES5 風。
(function () {
    "use strict";
    if (window.FaraRouter) return;   // 二重に読み込まれても、リスナーを二重に付けない

    var MAIN_SELECTOR = "main#main-content";
    // 取得したページの <head> に合わせる要素（CSS と script は、ページが変わっても同じなので触らない）
    var HEAD_SELECTORS = [
        "title",
        'meta[name="description"]',
        'meta[name="robots"]',
        'meta[property^="og:"]',
        'link[rel="canonical"]',
    ];

    /** /report と /report/（Worker の CSP が、このパスだけ違う） */
    function isReportPath(path) {
        return path === "/report" || path === "/report/";
    }

    /** ページとして差し替えてよいパスか（API・ファイル（.json など）・報告フォームは対象外） */
    function isNavigablePath(path) {
        if (path.charAt(0) !== "/" || path === "/api" || path.indexOf("/api/") === 0) return false;
        if (/\.[A-Za-z0-9]+$/.test(path)) return false;
        return !isReportPath(path);
    }

    /**
     * href へ、この画面の中で移動してよいかを返す（移動しないなら null）。
     * 別のサイト・別の形式のファイル・報告フォームへは null。同じページの #見出しも null（ブラウザに任せる）。
     */
    function sameSiteTarget(href, here) {
        var url;
        try { url = new URL(href, here.href); } catch (e) { return null; }
        if (url.origin !== here.origin) return null;
        if (!isNavigablePath(url.pathname) || !isNavigablePath(here.pathname)) return null;
        if (url.pathname === here.pathname && url.search === here.search && url.hash) return null;
        return url;
    }

    /** 取得した HTML を差し替えてよいか（本文の <main> があり、本文に script が無い） */
    function swappable(doc) {
        var main = doc.querySelector(MAIN_SELECTOR);
        return !!main && !main.querySelector("script");
    }

    function safeDecode(text) {
        try { return decodeURIComponent(text); } catch (e) { return text; }
    }

    // ---- 表示中のページ ----
    var current = 0;                                              // 最後に始めた移動の番号（古い応答は捨てる）
    var busy = false;                                             // 差し替えの最中は、スクロールの位置を保存しない
    var renderedKey = window.location.pathname + window.location.search;

    /** いまのスクロール位置を、この履歴の状態に保存する（戻る・進むで戻すため） */
    function saveScroll() {
        if (busy) return;
        try {
            window.history.replaceState(Object.assign({}, window.history.state, { y: window.pageYOffset }), "");
        } catch (e) { /* 履歴に書けない環境では、戻ったときの位置は覚えない */ }
    }

    /** #見出し のある場所へ。無ければ先頭へ */
    function scrollToHashOrTop(hash) {
        var el = hash ? document.getElementById(safeDecode(hash.slice(1))) : null;
        if (el) el.scrollIntoView();
        else window.scrollTo(0, 0);
    }

    /** 取得した <head> に合わせる */
    function syncHead(doc) {
        var head = document.head;
        HEAD_SELECTORS.forEach(function (sel) {
            Array.prototype.forEach.call(head.querySelectorAll(sel), function (el) { el.parentNode.removeChild(el); });
            Array.prototype.forEach.call(doc.head.querySelectorAll(sel), function (el) { head.appendChild(document.importNode(el, true)); });
        });
    }

    /** サイドバーの現在地（class と aria-current）を、取得したページに合わせる。リンクの並びは同じなので、href で対応づける */
    function syncNav(doc) {
        var fresh = {};
        Array.prototype.forEach.call(doc.querySelectorAll(".sidebar a.nav-link"), function (a) { fresh[a.getAttribute("href")] = a; });
        Array.prototype.forEach.call(document.querySelectorAll(".sidebar a.nav-link"), function (a) {
            var src = fresh[a.getAttribute("href")];
            if (!src) return;
            a.className = src.className;
            if (src.hasAttribute("aria-current")) a.setAttribute("aria-current", src.getAttribute("aria-current"));
            else a.removeAttribute("aria-current");
        });
        // スマホのメニューは、移動したら閉じる（チェックボックスの状態）
        var toggle = document.getElementById("nav-toggle");
        if (toggle) toggle.checked = false;
    }

    /** 本文・見出し・サイドバーを、取得したページに差し替える（スクロールは呼び出し側） */
    function render(doc, key) {
        var main = document.querySelector(MAIN_SELECTOR);
        var next = doc.querySelector(MAIN_SELECTOR);
        var frag = document.createDocumentFragment();
        Array.prototype.forEach.call(next.childNodes, function (node) { frag.appendChild(document.importNode(node, true)); });
        main.replaceChildren(frag);
        syncHead(doc);
        syncNav(doc);
        renderedKey = key;
        // 見出しへフォーカスを移す（スクロールはしない）。main は tabindex=-1 なので、どのページでも移せる
        main.focus({ preventScroll: true });
        document.dispatchEvent(new Event("fara:navigated"));
    }

    /**
     * 移動する。mode は "push"（新しい履歴を積む）・"replace"（同じ場所）・"pop"（戻る・進む）。
     * 失敗したら、通常の読み直しに任せる（pop のときは、いまの URL を読み直す）。
     */
    function navigate(href, mode) {
        var seq = ++current;
        var url = new URL(href, window.location.href);
        var popY = null;
        if (mode === "pop") {
            popY = window.history.state && typeof window.history.state.y === "number" ? window.history.state.y : null;
        } else {
            saveScroll();   // 出ていく前に、いまの位置を覚える
        }

        fetch(url.href, { headers: { Accept: "text/html" }, credentials: "same-origin" })
            .then(function (res) {
                if (!res.ok || (res.headers.get("Content-Type") || "").indexOf("text/html") < 0) throw new Error("not html");
                return res.text().then(function (text) { return { res: res, text: text }; });
            })
            .then(function (got) {
                if (seq !== current) return;   // もっと新しい移動が始まっていたら、この応答は捨てる
                var dest = new URL(got.res.url || url.href);
                dest.hash = url.hash;
                var doc = new DOMParser().parseFromString(got.text, "text/html");
                if (dest.origin !== window.location.origin || !isNavigablePath(dest.pathname) || !swappable(doc)) {
                    throw new Error("cannot swap");
                }
                var key = dest.pathname + dest.search;
                var target = dest.pathname + dest.search + dest.hash;
                if (mode === "push" && dest.href !== window.location.href) window.history.pushState({ y: 0 }, "", target);
                else if (mode !== "pop") window.history.replaceState({ y: 0 }, "", target);

                busy = true;
                try {
                    render(doc, key);
                    if (popY !== null) window.scrollTo(0, popY);
                    else scrollToHashOrTop(dest.hash);
                } finally {
                    busy = false;
                }
                saveScroll();
            })
            .catch(function () {
                if (seq !== current) return;
                if (mode === "pop") window.location.reload();
                else window.location.assign(url.href);
            });
    }

    // ---- リンクを押したとき（左クリックだけ。修飾キーや、ほかのハンドラーが処理したものは除く） ----
    document.addEventListener("click", function (e) {
        if (e.defaultPrevented || e.button !== 0 || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return;
        var a = e.target && e.target.closest ? e.target.closest("a[href]") : null;
        if (!a || a.hasAttribute("download") || (a.target && a.target !== "_self")) return;
        var url = sameSiteTarget(a.href, window.location);
        if (!url) return;
        e.preventDefault();
        navigate(url.href, "push");
    });

    // 戻る・進む。同じ画面の #見出し への移動（履歴の状態が無い）は、位置を戻すだけ
    window.addEventListener("popstate", function () {
        var key = window.location.pathname + window.location.search;
        var y = window.history.state && typeof window.history.state.y === "number" ? window.history.state.y : null;
        if (key === renderedKey) {
            if (y !== null) window.scrollTo(0, y);
            else scrollToHashOrTop(window.location.hash);
            return;
        }
        navigate(window.location.href, "pop");
    });

    // 位置の保存は、スクロールのたびに（1 フレームに 1 回）。戻る・進むで、同じ場所に戻れるように
    var savePending = false;
    window.addEventListener("scroll", function () {
        if (savePending) return;
        savePending = true;
        window.requestAnimationFrame(function () { savePending = false; saveScroll(); });
    }, { passive: true });

    // 検索の結果など、ほかの部品からの移動（search.js の go）。移動できないリンクは通常どおり開く
    window.faraNavigate = function (href) {
        var url = sameSiteTarget(href, window.location);
        if (url) navigate(url.href, "push");
        else window.location.assign(href);
    };

    // ブラウザが戻ったときの位置は、ここで戻す（自動の復元は、差し替えと合わないので止める）
    if ("scrollRestoration" in window.history) window.history.scrollRestoration = "manual";
    if (window.history.state && typeof window.history.state.y === "number" && !window.location.hash) {
        window.scrollTo(0, window.history.state.y);
    }

    window.FaraRouter = {
        isReportPath: isReportPath,
        isNavigablePath: isNavigablePath,
        sameSiteTarget: sameSiteTarget,
        swappable: swappable,
    };
})();
