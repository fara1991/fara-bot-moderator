// サイト内検索（ヘッダーとスマホのメニューの欄）。/search-index.json（ページと、ページの中の見出しごとの節）を、最初に使うときに一度だけ読む。
// 入力するとすぐに、一致した場所（グループ › ページ › 見出し）と、一致した部分の前後の文章を出す。
// ↑↓ で選んで Enter、Esc で閉じる。どのページでも Ctrl+K（Mac は ⌘K）か「/」で、見えている検索欄に移る。
// 日本語は単語区切りがないので、形態素解析はせず、スペースで区切った語が「すべて含まれる」ものを一致とする。全角・半角の違いは NFKC で、カタカナはひらがなにそろえる（あどおん・アドオンのどちらでも探せる）。
// ローマ字（adoon）も、ひらがな（あどおん）にした形で探す。
// ページの題名に漢字があるものは、Content/pages.json の reading（ひらがなの読み）でも探す（例: komando・こまんどじっこう → コマンド実行）。
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
                list.forEach(prepare);
                return list;
            }).catch(function (err) { indexPromise = null; throw err; });
        }
        return indexPromise;
    }

    // 全角・半角、大文字・小文字をそろえる。全角の ～（Windows の日本語入力）は NFKC で ~ になるので、データの 〜 にそろえる
    function norm(s) {
        return String(s).normalize("NFKC").toLowerCase().replace(/~/g, "〜").replace(/[ァ-ヶ]/g, function (c) { return String.fromCharCode(c.charCodeAt(0) - 0x60); });
    }

    // ---- ローマ字 → ひらがな（検索語用。変換できない文字が残るときは null） ----
    var ROMAJI = (function () {
      var t = {};
      function add(keys, kana) { keys.split(" ").forEach(function (k, i) { t[k] = kana.split(" ")[i]; }); }
      add("a i u e o", "あ い う え お");
      add("ka ki ku ke ko", "か き く け こ");
      add("sa si shi su se so", "さ し し す せ そ");
      add("ta ti chi tu tsu te to", "た ち ち つ つ て と");
      add("na ni nu ne no", "な に ぬ ね の");
      add("ha hi hu fu he ho", "は ひ ふ ふ へ ほ");
      add("ma mi mu me mo", "ま み む め も");
      add("ya yu yo", "や ゆ よ");
      add("ra ri ru re ro", "ら り る れ ろ");
      add("wa wo", "わ を");
      add("ga gi gu ge go", "が ぎ ぐ げ ご");
      add("za zi ji zu ze zo", "ざ じ じ ず ぜ ぞ");
      add("da di du de do", "だ ぢ づ で ど");
      add("ba bi bu be bo", "ば び ぶ べ ぼ");
      add("pa pi pu pe po", "ぱ ぴ ぷ ぺ ぽ");
      add("kya kyu kyo", "きゃ きゅ きょ");
      add("sha shu sho sya syu syo", "しゃ しゅ しょ しゃ しゅ しょ");
      add("cha chu cho tya tyu tyo cya cyu cyo", "ちゃ ちゅ ちょ ちゃ ちゅ ちょ ちゃ ちゅ ちょ");
      add("nya nyu nyo hya hyu hyo mya myu myo rya ryu ryo", "にゃ にゅ にょ ひゃ ひゅ ひょ みゃ みゅ みょ りゃ りゅ りょ");
      add("gya gyu gyo bya byu byo pya pyu pyo dya dyu dyo", "ぎゃ ぎゅ ぎょ びゃ びゅ びょ ぴゃ ぴゅ ぴょ ぢゃ ぢゅ ぢょ");
      add("ja ju jo jya jyu jyo zya zyu zyo", "じゃ じゅ じょ じゃ じゅ じょ じゃ じゅ じょ");
      add("fa fi fe fo va vi vu ve vo", "ふぁ ふぃ ふぇ ふぉ ゔぁ ゔぃ ゔ ゔぇ ゔぉ");
      add("wi we thi dhi", "うぃ うぇ てぃ でぃ");
      add("xa xi xu xe xo la li lu le lo", "ぁ ぃ ぅ ぇ ぉ ぁ ぃ ぅ ぇ ぉ");
      add("xya xyu xyo lya lyu lyo xtu xtsu ltu ltsu", "ゃ ゅ ょ ゃ ゅ ょ っ っ っ っ");
      return t;
    })();

    function romajiToHiragana(src) {
      var s = String(src).toLowerCase();
      if (!/^[a-z'-]+$/.test(s)) return null;
      var out = "", i = 0, n = s.length;
      while (i < n) {
        var c = s[i];
        if (c === "-") { out += "ー"; i++; continue; }
        if (c === "'") { i++; continue; }
        // 「nn」は「ん」1 つ（kennri → けんり、kann → かん）。後ろが母音・y なら、最初の n だけが「ん」で、2 つ目は次の音（konnichiha → こんにちは、kanni → かんに）
        if (c === "n" && i + 1 < n && s[i + 1] === "n") {
            var after = i + 2 < n ? s[i + 2] : "";
            out += "ん";
            i += after !== "" && "aiueoy".indexOf(after) >= 0 ? 1 : 2;
            continue;
        }
        // 促音: 同じ子音が続く（n と母音は除く）
        if (i + 1 < n && s[i + 1] === c && "aiueon".indexOf(c) < 0) { out += "っ"; i++; continue; }
        var hit = false;
        for (var len = 4; len >= 1; len--) {
          var k = s.substr(i, len);
          if (k.length === len && Object.prototype.hasOwnProperty.call(ROMAJI, k)) { out += ROMAJI[k]; i += len; hit = true; break; }
        }
        if (hit) continue;
        if (c === "n") { out += "ん"; i++; if (s[i] === "'") i++; continue; }   // 母音・y の前以外の n
        return null;
      }
      return out;
    }

    /** 検索語の別の形。ローマ字に見える語（3 文字以上）は、ひらがなにした形も探す */
    function alternatives(term) {
      var alts = [term];
      if (term.length >= 3) {
        var k = romajiToHiragana(term);
        if (k && k !== term && k.length >= 2) alts.push(k);
      }
      return alts;
    }
    /** 1 文字ずつ正規化した文字列と、正規化後の位置から元の位置への対応（… が ... のように文字数が変わっても、位置がずれないように） */
    function foldMap(text) {
        var normed = "", map = [];
        for (var i = 0; i < text.length; i++) {
            var n = norm(text[i]);
            for (var k = 0; k < n.length; k++) { normed += n[k]; map.push(i); }
        }
        return { normed: normed, map: map };
    }
    /** 検索語ごとの、探す形の一覧（[[入力どおり, ひらがな], ...]）。語ごとに、どれか 1 つが含まれればよい */
    function termGroups(q) { return norm(q).split(/\s+/).filter(Boolean).map(alternatives); }
    /** 強調・抜き出し用に、すべての形を 1 列にしたもの */
    function terms(q) {
        var all = [];
        termGroups(q).forEach(function (g) { g.forEach(function (t) { if (all.indexOf(t) < 0) all.push(t); }); });
        return all;
    }
    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]; });
    }
    function escapeRegExp(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); }

    // 読みの比べ方（pokemon の LooseKana と同じ規則）: 長音の「ー」を除き、おう・おお の書き方を 1 つにそろえる（ほうき・ほき、おおきい・おきい）。
    // え段＋い（ていき・けいさん）は、長音と区別できないので、そろえない。
    var LONG_O = "おこそとのほもよろごぞどぼぽょぉ";
    function looseKana(s) {
        var t = String(s).replace(/ー/g, ""), out = "";
        for (var i = 0; i < t.length; i++) {
            var c = t[i], prev = i > 0 ? t[i - 1] : "";
            if (c === "う" && prev && LONG_O.indexOf(prev) >= 0) continue;
            if (c === "お" && prev === "お") continue;
            out += c;
        }
        return out;
    }

    /** 読みでの点数。完全一致 30・前方一致は 3 文字以上で 15（2 文字だけなら 5）・途中の一致は 3 文字以上だけ 10。2 文字のかなは、読みの途中には当てない（「あい」が途中に偶然当たらないように） */
    function readingScore(reading, t) {
        if (!reading) return 0;
        var key = looseKana(t), r = looseKana(reading);
        if (key.length < 2) return 0;
        if (r === key) return 30;
        if (r.indexOf(key) === 0) return key.length >= 3 ? 15 : 5;
        return key.length >= 3 && r.indexOf(key) > 0 ? 10 : 0;
    }

    /** 索引の 1 件に、検索用の形（正規化した題名・ページ名・本文・読み）を付ける */
    function prepare(e) {
        e._t = norm(e.title); e._p = norm(e.page); e._x = norm(e.text); e._r = looseKana(norm(e.reading || ""));
        return e;
    }

    /** 1 つの語の点数（含まれなければ 0） */
    function scoreOf(e, t) {
        if (e._t === t) return 30;
        if (e._t.indexOf(t) === 0) return 15;
        if (e._t.indexOf(t) >= 0) return 10;
        var reading = readingScore(e._r, t);
        if (reading) return reading;
        if (e._p.indexOf(t) >= 0) return 4;
        if (e._x.indexOf(t) >= 0) return 1;
        return 0;
    }

    /** すべての語を含むものを、見出し > ページ名 > 本文の順に点数を付けて並べる */
    function search(index, q) {
        var gs = termGroups(q), hits = [];
        if (!gs.length) return hits;
        index.forEach(function (e) {
            var score = 0;
            for (var i = 0; i < gs.length; i++) {
                var s = 0;
                for (var j = 0; j < gs[i].length; j++) s = Math.max(s, scoreOf(e, gs[i][j]));
                if (!s) { score = 0; break; }
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
        var folded = foldMap(text), normed = folded.normed, map = folded.map;
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
        var folded = foldMap(text), pos = -1;
        for (var i = 0; i < ts.length && pos < 0; i++) pos = folded.normed.indexOf(ts[i]);
        if (pos < 0) return text.slice(0, width) + (text.length > width ? "…" : "");
        pos = folded.map[pos];   // 元の文字列の位置に戻す
        var start = Math.max(0, pos - Math.floor(width / 3)), end = Math.min(text.length, start + width);
        return (start > 0 ? "…" : "") + text.slice(start, end) + (end < text.length ? "…" : "");
    }

    function init(root) {
        var input = root.querySelector(".site-search-input"), list = root.querySelector(".site-search-list");
        if (!input || !list) return;
        var items = [], active = -1, timer = 0, last = "";

        function open() { list.hidden = false; input.setAttribute("aria-expanded", "true"); }
        // 結果を開く。同じページの見出しへ移るだけのときは、ページが読み直されないので、一覧を閉じて、欄から離れる（スマホは、開いているメニューも閉じる）
        function go(href) {
            close();
            input.blur();
            var toggle = document.getElementById("nav-toggle");
            if (toggle) toggle.checked = false;
            // 同じサイトのページは、読み直さずに移る（nav.js）。使えないときは通常の移動
            if (window.faraNav) window.faraNav.navigate(href);
            else location.href = href;
        }
        function close() {
            list.hidden = true; active = -1;
            input.setAttribute("aria-expanded", "false"); input.removeAttribute("aria-activedescendant");
        }
        function render(hits, q) {
            var ts = terms(q);
            var html = hits.slice(0, MAX).map(function (h, i) {
                var e = h.entry, where = escapeHtml(e.group) + " › " + escapeHtml(e.page) + (e.type === "section" && e.title !== e.page ? " › " + highlight(e.title, ts) : "");
                // 題名に一致しないで、読みだけで当たったページには、その読みを小さく添える（どの語で当たったかが分かるように）
                var byReading = e.type === "page" && e._r && !ts.some(function (t) { return e._t.indexOf(t) >= 0; }) &&
                    termGroups(q).every(function (g) { return g.some(function (t) { return readingScore(e._r, t) > 0; }); });
                if (byReading) where += '<span class="site-search-reading">読み: ' + escapeHtml(e.reading) + "</span>";
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
            if (e.isComposing || e.keyCode === 229) return;   // 日本語入力の変換中は、Enter などを奪わない（Safari は、確定の Enter で isComposing が false になるので keyCode でも見る）
            if (e.key === "ArrowDown" || e.key === "ArrowUp") {
                if (list.hidden) { update(); return; }
                e.preventDefault();
                move(e.key === "ArrowDown" ? 1 : -1);
            } else if (e.key === "Enter") {
                // 入力してすぐ（検索の待ち時間の間）に Enter を押したら、古い入力の結果を開かず、先に今の入力で探す
                if (input.value.trim() !== last) { e.preventDefault(); clearTimeout(timer); update(); return; }
                var target = active >= 0 ? items[active] : items[0];
                if (!list.hidden && target) { e.preventDefault(); go(target.getAttribute("href")); }
            } else if (e.key === "Escape") {
                if (!list.hidden) { e.preventDefault(); close(); } else input.blur();
            }
        });
        list.addEventListener("mousedown", function (e) { e.preventDefault(); });   // クリックで入力欄のフォーカスを外さない
        list.addEventListener("click", function (e) {
            var item = e.target.closest && e.target.closest(".site-search-item");
            if (!item) return;
            e.preventDefault();
            go(item.getAttribute("href"));
        });
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

    // テスト用（ブラウザには module が無いので、何もしない）。tests/js/search.test.mjs が読み込む
    if (typeof module !== "undefined" && module.exports) {
        module.exports = { norm: norm, looseKana: looseKana, romajiToHiragana: romajiToHiragana, alternatives: alternatives, readingScore: readingScore, scoreOf: scoreOf, search: search, prepare: prepare };
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
    else start();
})();
