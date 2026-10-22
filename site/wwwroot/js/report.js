// 「問い合わせ」ページ（/report）のフォーム。Cloudflare Turnstile（ボット判定）を出し、POST /api/report（worker/report.js）に送る。
// 外部のスクリプトは https://challenges.cloudflare.com だけ。Avatar Modding Guide と FaraBotModerator のサイトで同じファイルを使う。
(function () {
    "use strict";
    // ページ移動（nav.js）のあとは、新しいフォームで init をもう一度呼ぶ（フォームのないページでは何もしない）
    function init() {
    var form = document.getElementById("report-form");
    if (!form) return;

    var TURNSTILE_SRC = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
    var MIN_MESSAGE = 5, MAX_MESSAGE = 2000;
    var category = document.getElementById("report-category");
    var message = document.getElementById("report-message");
    var contact = document.getElementById("report-contact");
    var trap = document.getElementById("report-website");
    var counter = document.getElementById("report-count");
    var status = document.getElementById("report-status");
    var submit = document.getElementById("report-submit");
    var box = document.getElementById("report-turnstile");
    var done = document.getElementById("report-done");
    var again = document.getElementById("report-again");
    var token = "";
    var widgetId = null;

    function show(text, kind) {
        status.textContent = text || "";
        status.className = "report-status" + (kind ? " report-status-" + kind : "");
        status.hidden = !text;
    }

    // 報告のきっかけのページ（フッターのリンクが ?from= に付ける）。同じサイトのパスだけ
    function fromPage() {
        var from = new URLSearchParams(location.search).get("from") || "";
        return /^\/[A-Za-z0-9\-_\/.]{0,299}$/.test(from) && from.indexOf("//") !== 0 ? from : "";
    }

    function updateCount() { counter.textContent = message.value.trim().length; }
    message.addEventListener("input", updateCount);
    updateCount();

    function loadTurnstile() {
        if (window.turnstile) return Promise.resolve();
        return new Promise(function (resolve, reject) {
            var s = document.createElement("script");
            s.src = TURNSTILE_SRC;
            s.async = true;
            s.onload = function () { resolve(); };
            s.onerror = function () { reject(new Error("turnstile")); };
            document.head.appendChild(s);
        });
    }

    function disable(text) {
        submit.disabled = true;
        show(text, "warn");
    }

    // 受け付けているか（設定が済んでいるか）を確かめて、確認の欄を出す
    fetch("/api/report/config", { headers: { Accept: "application/json" } })
        .then(function (r) { return r.ok ? r.json() : { enabled: false }; })
        .then(function (cfg) {
            if (!cfg.enabled || !cfg.siteKey) { disable("いまは報告を受け付けていません。しばらくしてからお試しください。"); return null; }
            return loadTurnstile().then(function () {
                widgetId = window.turnstile.render(box, {
                    sitekey: cfg.siteKey,
                    callback: function (t) { token = t; },
                    "expired-callback": function () { token = ""; },
                    "error-callback": function () { token = ""; },
                });
            });
        })
        .catch(function () { disable("確認の部品を読み込めませんでした。ページを開き直してお試しください。"); });

    again.addEventListener("click", function () {
        done.hidden = true;
        form.hidden = false;
        category.focus();
    });

    function resetCheck() {
        token = "";
        if (window.turnstile && widgetId !== null) { try { window.turnstile.reset(widgetId); } catch (e) { /* すでに無いとき */ } }
    }

    form.addEventListener("submit", function (e) {
        e.preventDefault();
        if (submit.disabled) return;
        var text = message.value.trim();
        if (!category.value) { show("種類を選んでください", "error"); category.focus(); return; }
        if (text.length < MIN_MESSAGE) { show("内容を " + MIN_MESSAGE + " 文字以上で書いてください", "error"); message.focus(); return; }
        if (text.length > MAX_MESSAGE) { show("内容は " + MAX_MESSAGE + " 文字までにしてください（いま " + text.length + " 文字）", "error"); message.focus(); return; }
        submit.disabled = true;
        // 確認（Turnstile）は、読み込み直後や、送信に失敗して作り直した直後は、数秒かかることがある。待ってから送る
        show(token ? "送信中…" : "確認しています…", "info");
        waitForToken(5000).then(function (t) {
            if (!t) {
                show("「ロボットではありません」の確認が終わっていません。表示された確認をしてから、もう一度押してください", "error");
                submit.disabled = false;
                return null;
            }
            show("送信中…", "info");
            return send(text, t);
        });
    });

    // 確認のトークンが届くまで、最大 timeoutMs 待つ（届いていれば、すぐ返す）
    function waitForToken(timeoutMs) {
        return new Promise(function (resolve) {
            var waited = 0;
            (function check() {
                if (token) { resolve(token); return; }
                if (waited >= timeoutMs) { resolve(""); return; }
                waited += 200;
                setTimeout(check, 200);
            })();
        });
    }

    function send(text, checkToken) {
        return fetch("/api/report", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                category: category.value,
                message: text,
                contact: contact.value.trim(),
                page: fromPage(),
                turnstileToken: checkToken,
                website: trap.value,
            }),
        })
            .then(function (res) {
                return res.text().then(function (body) { return { ok: res.ok, status: res.status, body: body }; });
            })
            .then(function (r) {
                if (r.ok) {
                    // 入力欄を隠して、お礼を出す（「もう 1 件送る」で戻る）。確認は 1 回で使い切りなので、作り直しておく
                    form.reset();
                    updateCount();
                    resetCheck();
                    show("");
                    form.hidden = true;
                    done.hidden = false;
                    submit.disabled = false;
                    return;
                }
                var msg = "";
                try { var j = JSON.parse(r.body); if (j && typeof j.message === "string") msg = j.message; } catch (e) { /* 読めない応答 */ }
                if (!msg) msg = r.status === 429 ? "短い間に何度も送信されました。少し待ってから、もう一度お試しください"
                    : r.status === 413 ? "内容が長すぎます。短くして、もう一度お試しください"
                    : r.status === 503 ? "いまは報告を受け付けていません。しばらくしてからお試しください"
                    : r.status >= 500 ? "送信に失敗しました。しばらくしてからもう一度お試しください"
                    : "送信できませんでした。入力を確かめて、もう一度お試しください";
                show(msg, "error");
                resetCheck();   // 確認は 1 回で使い切りなので、やり直せるように作り直す
                submit.disabled = false;
            })
            .catch(function () {
                show("通信できませんでした。通信の状態を確かめて、もう一度お試しください", "error");
                resetCheck();
                submit.disabled = false;
            });
    }
    }

    window.faraNav ? window.faraNav.register(init) : init();
})();
