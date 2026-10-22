// ページ移動を、画面を読み直さずに行う（拡張ナビゲーション）。
// 同じサイトのリンクを押すと、次のページの HTML を取ってきて、本文と一部の要素（config.swap）だけを差し替え、履歴を更新する。
// ヘッダー・背景の 3D・スクロールの線・サイドバーの開閉などは、差し替えないので、そのまま残る。
//  - 検索エンジンや直接アクセスには、今までどおりページごとの完全な HTML が返る（JS がないときは何もしない）
//  - 3D（three.js）や WebSocket を使うページ（type="module" / importmap の script を持つページ）は、従来どおり読み込み直す
//  - ページ専用の JS は faraNav.register(init) で登録する。読み込み時に 1 回、移動のあとにも呼ばれる。
//    init は何度呼ばれても二重に登録しないこと（ページの要素に対して付け、window・document への登録は 1 回だけ）
//  - 戻る・進むと再読み込みでは、履歴の state に保存したスクロール位置に戻す
//  - 失敗したら、通常のページ読み込みに戻す
//
// 設定（任意）: nav.js より前に window.faraNavConfig を置くと、下の既定値を上書きできる。
//   main            本文の要素。無いページでは、ページ移動を使わない（register は使える）
//   swap            移動のたびに、同じセレクタの要素を新しいページのものに差し替える（無ければ無視）
//   skipLink        「本文へスキップ」のリンク（href を差し替える）
//   excludePrefixes ページ移動の対象外にするパスの前置き
//   head            差し替えるときに、<head> から入れ替える要素のセレクタ（description・canonical・OGP など）
(function () {
  "use strict";
  if (window.faraNav) return;

  var DEFAULTS = {
    main: "#main-content",
    swap: [".sidebar-panel"],
    skipLink: ".skip-link",
    excludePrefixes: ["/api/", "/_"],
    head: 'meta[name]:not([name="viewport"]), meta[property], link[rel="canonical"]',
  };
  var cfg = {};
  Object.keys(DEFAULTS).forEach(function (k) { cfg[k] = DEFAULTS[k]; });
  var user = window.faraNavConfig || {};
  Object.keys(user).forEach(function (k) { if (user[k] !== undefined && k in DEFAULTS) cfg[k] = user[k]; });

  var MODULE_SEL = 'script[type="module"], script[type="importmap"]';

  var handlers = [];
  var loading = 0;     // ページ専用スクリプトを読み込んでいる間は、登録された init をすぐには呼ばない（移動のあとにまとめて呼ぶ）
  var seq = 0;         // 移動の番号。追い越された古い移動は捨てる
  var current = null;  // いま進んでいる移動の AbortController
  var loaded = {};     // 読み込み済みのページ専用スクリプト（絶対 URL）
  var pending = {};    // 読み込み中のページ専用スクリプト（絶対 URL → Promise。同じものを二度読まない）
  var rendered = "";   // いま画面に出ているページ（パス + 検索）
  var saveTimer = 0;

  function safeCall(fn) {
    try { fn(); } catch (err) { console.error(err); }
  }

  /** ページ専用の初期化を登録する。読み込み中でなければ、いま 1 回呼ぶ。 */
  function register(fn) {
    handlers.push(fn);
    if (loading === 0) safeCall(fn);
  }

  function emit() {
    handlers.forEach(function (fn) { safeCall(fn); });
  }

  /** 同じサイトのページとして、ページ移動で開いてよいか（画像・JSON・API は対象外）。 */
  function isRoutable(url, origin, excludes) {
    if (url.origin !== origin) return false;
    var p = url.pathname;
    var list = excludes || cfg.excludePrefixes;
    for (var i = 0; i < list.length; i++) if (p.indexOf(list[i]) === 0) return false;
    if (/\.[A-Za-z0-9]+$/.test(p) && !/\.html$/.test(p)) return false;
    return true;
  }

  var api = {
    register: register,
    isRoutable: isRoutable,
    config: cfg,
    /** 同じサイトのリンクを、ページ移動で開く（検索の候補など、JS から開くとき）。 */
    navigate: function (href) { return navigate(href, { push: true, y: 0 }); },
  };
  window.faraNav = api;

  if (typeof document === "undefined") return; // テスト（node）では、判定の関数だけを使う
  if (!document.querySelector(cfg.main)) return; // 本文がないページでは、ページ移動を使わない

  function hasModuleScripts(root) {
    return !!root.querySelector(MODULE_SEL);
  }

  function pageKey(loc) {
    return loc.pathname + loc.search;
  }

  function saveScroll() {
    try {
      history.replaceState(Object.assign({}, history.state, { faraNav: 1, y: window.scrollY }), "");
    } catch (err) { /* 履歴の変更が制限されているとき */ }
  }

  /** ページ専用スクリプトを読む。同じ URL の読み込みが進んでいれば、それを待つ（二重に読まない）。 */
  function loadScript(src) {
    if (loaded[src]) return Promise.resolve();
    if (pending[src]) return pending[src];
    var p = new Promise(function (resolve, reject) {
      var s = document.createElement("script");
      s.src = src;
      s.async = false;
      s.onload = function () { loaded[src] = true; delete pending[src]; resolve(); };
      s.onerror = function () { delete pending[src]; reject(new Error("読み込めません: " + src)); };
      document.body.appendChild(s);
    });
    pending[src] = p;
    return p;
  }

  /** 新しいページの本文にある、まだ読んでいないページ専用スクリプトの URL。 */
  function pageScriptSources(doc) {
    var out = [];
    var base = location.origin + "/";
    Array.prototype.forEach.call(doc.querySelectorAll(cfg.main + " script[src]"), function (s) {
      var abs = new URL(s.getAttribute("src"), base).href;
      if (!loaded[abs] && out.indexOf(abs) < 0) out.push(abs);
    });
    return out;
  }

  function swap(doc) {
    var newMain = doc.querySelector(cfg.main);
    var oldMain = document.querySelector(cfg.main);
    // 本文のスクリプトは loadScript で読むので、ここで入れると二重に動く。取り除いてから入れる
    Array.prototype.forEach.call(newMain.querySelectorAll("script"), function (s) { s.parentNode.removeChild(s); });
    oldMain.parentNode.replaceChild(document.importNode(newMain, true), oldMain);

    // 左のメニュー（いま開いているページの印）など。新しいページにも同じ要素があるものだけ差し替える
    cfg.swap.forEach(function (sel) {
      var n = doc.querySelector(sel), o = document.querySelector(sel);
      if (n && o) o.innerHTML = n.innerHTML;
    });
    var newSkip = cfg.skipLink && doc.querySelector(cfg.skipLink);
    var oldSkip = cfg.skipLink && document.querySelector(cfg.skipLink);
    if (newSkip && oldSkip) oldSkip.setAttribute("href", newSkip.getAttribute("href"));

    if (cfg.head) {
      Array.prototype.forEach.call(document.head.querySelectorAll(cfg.head), function (el) {
        el.parentNode.removeChild(el);
      });
      Array.prototype.forEach.call(doc.head.querySelectorAll(cfg.head), function (el) {
        document.head.appendChild(document.importNode(el, true));
      });
    }
    document.title = doc.title;
  }

  function scrollToHash(hash, y) {
    var id = "";
    try { id = decodeURIComponent((hash || "").slice(1)); } catch (err) { id = ""; }
    var target = id ? document.getElementById(id) : null;
    if (target) target.scrollIntoView();
    else window.scrollTo(0, y || 0);
  }

  function finish(hash, y) {
    var main = document.querySelector(cfg.main);
    // 見出し（h1）がなければ、本文そのものにフォーカスを移す（読み上げの位置を合わせるため）
    var h = main.querySelector("h1") || main;
    if (!h.hasAttribute("tabindex")) h.setAttribute("tabindex", "-1");
    h.focus({ preventScroll: true });
    scrollToHash(hash, y);
  }

  /** 同じページの中だけの移動（検索の候補で、いま開いているページの見出しを選んだときなど）。 */
  function sameDocument(url, push, y) {
    if (push) {
      saveScroll();
      history.pushState({ faraNav: 1, y: 0 }, "", url.pathname + url.search + url.hash);
    }
    scrollToHash(url.hash, y || 0);
  }

  async function navigate(href, opts) {
    var push = !!opts.push;
    var url = new URL(href, location.href);
    var token = ++seq;
    if (current) current.abort(); // 進んでいる移動は中止する（追い越された）
    var ctrl = typeof AbortController === "function" ? new AbortController() : null;
    current = ctrl;

    if (!isRoutable(url, location.origin) || hasModuleScripts(document)) {
      location.assign(url.href);
      return;
    }
    // 今見えているページと同じ（検索や履歴の state で変わった分も含めて、いまの URL と比べる）
    if (push && pageKey(url) === pageKey(location)) {
      sameDocument(url, push, opts.y);
      return;
    }

    if (push) saveScroll();
    try {
      var res = await fetch(url.href, {
        headers: { Accept: "text/html" },
        credentials: "same-origin",
        signal: ctrl ? ctrl.signal : undefined,
      });
      var type = res.headers.get("content-type") || "";
      if (!res.ok || type.indexOf("text/html") < 0) throw new Error("HTML ではありません: " + res.status);
      var text = await res.text();
      if (token !== seq) return;

      var final = new URL(res.url || url.href);
      var doc = new DOMParser().parseFromString(text, "text/html");
      if (hasModuleScripts(doc) || !doc.querySelector(cfg.main)) {
        location.assign(url.href);
        return;
      }

      var srcs = pageScriptSources(doc);
      loading++;
      try {
        for (var i = 0; i < srcs.length; i++) await loadScript(srcs[i]);
      } finally {
        loading--;
      }
      if (token !== seq) return;

      rendered = pageKey(final);
      if (push) history.pushState({ faraNav: 1, y: 0 }, "", rendered + url.hash);
      swap(doc);
      emit();
      finish(url.hash, opts.y);
    } catch (err) {
      // 追い越された移動（中止を含む）は、何もしない。それ以外は、通常のページ読み込みに任せる
      if (token !== seq) return;
      location.assign(url.href);
    }
  }

  // 同じサイトのリンクのクリックを拾う（修飾キー・新しいタブ・ダウンロード・data-no-spa は、ブラウザに任せる）
  document.addEventListener("click", function (e) {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    var a = e.target && e.target.closest ? e.target.closest("a[href]") : null;
    if (!a || a.hasAttribute("download") || a.hasAttribute("data-no-spa")) return;
    var target = a.getAttribute("target");
    if (target && target !== "_self") return;
    if (/\bexternal\b/.test(a.getAttribute("rel") || "")) return;
    // SVG の <a> では a.href が文字列でないので、属性から読む
    var url;
    try { url = new URL(a.getAttribute("href"), document.baseURI); } catch (err) { return; }
    if (!isRoutable(url, location.origin) || hasModuleScripts(document)) return;
    if (pageKey(url) === pageKey(location)) {
      // いまのページ: # への移動はブラウザに任せる。# がなければ、ページの先頭へ（読み直さない）
      if (url.hash) return;
      e.preventDefault();
      window.scrollTo(0, 0);
      return;
    }
    e.preventDefault();
    navigate(url.href, { push: true, y: 0 });
  });

  // 戻る・進む。同じページの中の # だけなら、何もしない（ブラウザに任せる）
  window.addEventListener("popstate", function () {
    if (pageKey(location) === rendered) return;
    var y = history.state && typeof history.state.y === "number" ? history.state.y : 0;
    navigate(location.href, { push: false, y: y });
  });

  // 読んでいる位置を、履歴の state に覚えておく（戻ったときに同じ位置へ戻すため）。連続してスクロールしている間は、止まってから保存する
  window.addEventListener("scroll", function () {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(saveScroll, 200);
  }, { passive: true });
  // 再読み込み・ページを閉じるときは、待たずに今の位置を保存する（スクロールが続いていても、戻ったときに同じ位置へ戻すため）
  window.addEventListener("pagehide", saveScroll);

  // 読み込み済みのスクリプトを覚えておく（同じスクリプトを二度読まないため）
  Array.prototype.forEach.call(document.querySelectorAll("script[src]"), function (s) { loaded[s.src] = true; });
  rendered = pageKey(location);

  // 戻る・進むや再読み込みのとき、履歴に残した位置へ戻す（ブラウザの自動の復元は使わない）
  if ("scrollRestoration" in history) history.scrollRestoration = "manual";
  (function restoreOnLoad() {
    var st = history.state;
    if (!st || !st.faraNav || typeof st.y !== "number" || st.y <= 0 || location.hash) return;
    var go = function () { window.scrollTo(0, st.y); };
    if (document.readyState === "complete") go();
    else window.addEventListener("load", go, { once: true });
  })();
})();
