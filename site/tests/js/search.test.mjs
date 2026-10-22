// サイト内検索（wwwroot/js/search.js）のテスト: ローマ字・かなの読み・長音・nn・2 文字の一致・本文検索の順位。  node --test tests/js/search.test.mjs（site/ で）
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";

const SRC = readFileSync(new URL("../../wwwroot/js/search.js", import.meta.url), "utf8");

// ブラウザの document の代わり（search.js は読み込み時に、検索欄を探すだけ。検索欄は無い）
function load() {
  const sandbox = {
    module: { exports: {} },
    document: { readyState: "complete", querySelectorAll: () => [], addEventListener: () => {} },
    window: {},
    console,
  };
  runInNewContext(SRC, sandbox);
  return sandbox.module.exports;
}
const S = load();

test("looseKana: 長音「ー」を除き、おう・おお の書き方をそろえる。え段＋い は、そろえない", () => {
  assert.equal(S.looseKana("げーむ"), "げむ");
  assert.equal(S.looseKana("ほうき"), "ほき");
  assert.equal(S.looseKana("ひょうき"), S.looseKana("ひょき"));
  assert.equal(S.looseKana("おおきい"), "おきい");
  assert.equal(S.looseKana("ていき"), "ていき");
  assert.equal(S.looseKana("せいじん"), "せいじん");
  assert.equal(S.looseKana("うお"), "うお");
});

test("romajiToHiragana: nn は「ん」1 つ（後ろが母音・y なら、2 つ目の n は次の音）", () => {
  assert.equal(S.romajiToHiragana("kennri"), "けんり");
  assert.equal(S.romajiToHiragana("kann"), "かん");
  assert.equal(S.romajiToHiragana("kenn"), "けん");
  assert.equal(S.romajiToHiragana("kanni"), "かんに");
  assert.equal(S.romajiToHiragana("konnichiha"), "こんにちは");
  assert.equal(S.romajiToHiragana("konnnichiha"), "こんにちは");
  assert.equal(S.romajiToHiragana("konnyaku"), "こんにゃく");
  assert.equal(S.romajiToHiragana("komando"), "こまんど");
  assert.equal(S.romajiToHiragana("kenri"), "けんり");
  assert.equal(S.romajiToHiragana("xyz"), null);
});

test("alternatives: 3 文字以上のローマ字は、ひらがなにした形も探す", () => {
  assert.deepEqual(Array.from(S.alternatives("kennri")), ["kennri", "けんり"]);
  assert.deepEqual(Array.from(S.alternatives("ab")), ["ab"]);
});

test("readingScore: 完全一致 30、前方一致は 3 文字で 15・2 文字で 5、途中の一致は 3 文字以上だけ 10", () => {
  assert.equal(S.readingScore("こまんどじっこう", "こまんどじっこう"), 30);
  assert.equal(S.readingScore("こまんどじっこう", "こまんど"), 15);
  assert.equal(S.readingScore("あいてちーむとりれき", "あい"), 5);
  assert.equal(S.readingScore("ちあいてむ", "あい"), 0, "2 文字は途中の一致を見ない");
  assert.equal(S.readingScore("ちあいてむ", "てむ"), 0);
  assert.equal(S.readingScore("こまんどじっこう", "じっこ"), 10, "3 文字以上は途中の一致も見る");
  assert.equal(S.readingScore("げーむせってい", "げむ"), 5, "長音を除いて比べる（gemu）");
  assert.equal(S.readingScore("", "あい"), 0);
  assert.equal(S.readingScore("こまんど", "あ"), 0, "1 文字は読みでは見ない");
});

function index(entries) {
  return entries.map((e) => S.prepare({ type: "section", title: "", page: "", group: "", url: "/x", text: "", ...e }));
}

test("検索: 読みだけで当たるページが、ローマ字・かなの両方で出る", () => {
  const idx = index([
    { type: "page", title: "コマンド実行", page: "コマンド実行", group: "コマンド実行", url: "/docs/commands", text: "", reading: "こまんどじっこう" },
    { type: "page", title: "よくある質問（FAQ）", page: "よくある質問（FAQ）", group: "困ったときは", url: "/docs/faq", text: "", reading: "よくあるしつもん" },
  ]);
  assert.equal(S.search(idx, "こまんどじっこう")[0].entry.url, "/docs/commands");
  assert.equal(S.search(idx, "komandojikko")[0].entry.url, "/docs/commands");
  assert.equal(S.search(idx, "yokuarushitsumon")[0].entry.url, "/docs/faq");
  assert.equal(S.search(idx, "kenri").length, 0, "当たらない語は出ない");
});

test("検索: 2 文字のかなは、読みの先頭からだけ当たる（「あい」が途中に当たらない）", () => {
  const idx = index([
    { type: "page", title: "相手チームと履歴", page: "相手チームと履歴", url: "/docs/rival", reading: "あいてちーむとりれき" },
    { type: "page", title: "途中に含むページ", page: "途中に含むページ", url: "/docs/mid", reading: "ちあいてむ" },
  ]);
  const urls = Array.from(S.search(idx, "あい"), (h) => h.entry.url);
  assert.deepEqual(urls, ["/docs/rival"]);
});

test("回帰: 本文の一致の順位は変わらない（題名の一致 > ページ名 > 本文）", () => {
  const idx = index([
    { type: "section", title: "説明", page: "A", url: "/docs/a#x", text: "dmg の使い方" },
    { type: "section", title: "dmg の計算", page: "B", url: "/docs/b#y", text: "" },
    { type: "page", title: "ダメージ", page: "ダメージ", url: "/docs/c", text: "dmg", reading: "だめーじ" },
  ]);
  const urls = Array.from(S.search(idx, "dmg"), (h) => h.entry.url);
  assert.deepEqual(urls, ["/docs/b#y", "/docs/c", "/docs/a#x"]);
});
