// 背景の飾り（見た目だけ）: Twitch のチャットのような、吹き出し（アイコン・名前・文章を表す線）が、下からゆっくり浮かび上がる。
// 画面の後ろ（z-index: -1）に置いた canvas に描く。操作には関わらない。外部のファイルは読み込まない（CSP は同じオリジンだけ）。
// - 動きを減らす設定（prefers-reduced-motion）のときは、止めた 1 枚だけを描く
// - タブが裏に回ったら止める。負荷を抑えるため、描くのは 30fps まで（スマホは 20fps・吹き出しも減らす）、解像度の倍率は最大 1.5
// - 色は、ライト・ダークのテーマ（html の data-bs-theme と、端末の設定）に合わせる
(function () {
    "use strict";
    if (!document.body || document.getElementById("site-scene")) return;

    var canvas = document.createElement("canvas");
    canvas.id = "site-scene";
    canvas.setAttribute("aria-hidden", "true");
    document.body.insertBefore(canvas, document.body.firstChild);
    var ctx = canvas.getContext("2d");
    if (!ctx) return;

    var reduce = window.matchMedia("(prefers-reduced-motion: reduce)");
    var schemeLight = window.matchMedia("(prefers-color-scheme: light)");
    var W = 0, H = 0, dpr = 1, dark = true;
    // スマホ（タッチ操作）では、吹き出しを減らし、20fps にして、発熱・電池の消費を抑える
    var touch = window.matchMedia("(pointer: coarse)").matches;
    var frameGap = touch ? 50 : 33;

    // 吹き出しの色（インディゴ〜ブルーを中心に、少しだけ水色を混ぜる）
    var HUES = ["121,134,224", "150,168,245", "88,108,214", "96,165,250", "129,140,248", "121,134,224"];

    // 同じ並びになるよう、乱数は固定の種から作る
    var seed = 20261022;
    function rnd() { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; }

    var bubbles = [];
    function make(initial) {
        var depth = 0.55 + rnd() * 0.85;                    // 奥（小さい・薄い・遅い）〜 手前（大きい・濃い・速い）
        var w = (180 + rnd() * 170) * depth;
        var lines = 1 + Math.floor(rnd() * 3);
        return {
            x: rnd() * (W + 200) - 100,
            y: initial ? rnd() * (H + 200) : H + 120 + rnd() * 200,
            w: w, h: (34 + lines * 16) * depth, depth: depth, lines: lines,
            speed: (9 + rnd() * 14) * depth,                // px/秒
            hue: HUES[Math.floor(rnd() * HUES.length)],
            nameW: (40 + rnd() * 50) * depth,
            textW: [0.9, 0.7, 0.5].map(function (f) { return w * (0.42 + rnd() * 0.3) * f + 30 * depth; }),
        };
    }

    function readTheme() {
        var t = document.documentElement.getAttribute("data-bs-theme");
        dark = t ? t === "dark" : !schemeLight.matches;
    }
    function resize() {
        dpr = Math.min(window.devicePixelRatio || 1, 1.5);
        W = window.innerWidth; H = window.innerHeight;
        canvas.width = Math.round(W * dpr); canvas.height = Math.round(H * dpr);
        canvas.style.width = W + "px"; canvas.style.height = H + "px";
        var n = Math.max(10, Math.min(22, Math.round((W * H) / 90000)));
        if (touch) n = Math.min(n, 10);
        if (bubbles.length !== n) { seed = 20261022; bubbles = []; for (var i = 0; i < n; i++) bubbles.push(make(true)); }
    }

    function rrect(x, y, w, h, r) {
        ctx.beginPath();
        ctx.moveTo(x + r, y); ctx.lineTo(x + w - r, y); ctx.arcTo(x + w, y, x + w, y + r, r);
        ctx.lineTo(x + w, y + h - r); ctx.arcTo(x + w, y + h, x + w - r, y + h, r);
        ctx.lineTo(x + r, y + h); ctx.arcTo(x, y + h, x, y + h - r, r);
        ctx.lineTo(x, y + r); ctx.arcTo(x, y, x + r, y, r);
        ctx.closePath();
    }

    function drawBubble(b) {
        // 画面の上下の端で、すっと消える
        var edge = Math.min(1, Math.max(0, Math.min(b.y + b.h, H - b.y) / 160));
        var k = (dark ? 0.6 : 0.35) * edge * (0.45 + 0.55 * (b.depth - 0.5));   // 文字の邪魔にならないよう、控えめに
        if (k <= 0.01) return;
        var pad = 10 * b.depth, r = 12 * b.depth, ink = dark ? "255,255,255" : "40,50,110";
        ctx.globalAlpha = k;
        rrect(b.x, b.y, b.w, b.h, r);
        ctx.fillStyle = "rgba(" + b.hue + "," + (dark ? 0.1 : 0.12) + ")"; ctx.fill();
        ctx.lineWidth = 1; ctx.strokeStyle = "rgba(" + b.hue + ",0.38)"; ctx.stroke();
        // アイコン（丸）
        var cr = 11 * b.depth;
        ctx.beginPath(); ctx.arc(b.x + pad + cr, b.y + pad + cr, cr, 0, Math.PI * 2);
        ctx.fillStyle = "rgba(" + b.hue + ",0.75)"; ctx.fill();
        // 名前（色付きの線）と、文章（線）
        var tx = b.x + pad + cr * 2 + 8 * b.depth, lh = 5 * b.depth;
        ctx.fillStyle = "rgba(" + b.hue + ",0.95)";
        rrect(tx, b.y + pad, b.nameW, lh, lh / 2); ctx.fill();
        ctx.fillStyle = "rgba(" + ink + ",0.2)";
        for (var i = 0; i < b.lines; i++) { rrect(tx, b.y + pad + (lh + 7 * b.depth) * (i + 1), Math.min(b.textW[i], b.w - (tx - b.x) - pad), lh, lh / 2); ctx.fill(); }
    }

    var lastT = 0, last = 0, raf = 0;
    function draw(dt) {
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, W, H);
        for (var i = 0; i < bubbles.length; i++) {
            var b = bubbles[i];
            b.y -= b.speed * dt;
            if (b.y + b.h < -20) bubbles[i] = b = make(false);
            drawBubble(b);
        }
        ctx.globalAlpha = 1;
    }

    function frame(now) {
        raf = requestAnimationFrame(frame);
        if (document.hidden || now - last < frameGap) return;
        var dt = Math.min(0.1, (now - (lastT || now)) / 1000);
        last = now; lastT = now;
        draw(dt);
    }

    function start() {
        readTheme(); resize();
        cancelAnimationFrame(raf); lastT = 0;
        if (reduce.matches) { draw(0); return; }
        raf = requestAnimationFrame(frame);
    }

    window.addEventListener("resize", function () { resize(); if (reduce.matches) draw(0); });
    new MutationObserver(function () { readTheme(); if (reduce.matches) draw(0); }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-bs-theme"] });
    if (schemeLight.addEventListener) schemeLight.addEventListener("change", function () { readTheme(); });
    if (reduce.addEventListener) reduce.addEventListener("change", start);
    start();
})();
