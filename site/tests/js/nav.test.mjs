// ページ移動（wwwroot/js/nav.js）と、ページ専用 JS の登録の仕方のテスト。  node --test tests/js/nav.test.mjs
// 画面の動き（クリック・戻る・進む・読み直しの有無・リスナーの数）は tools/nav-check/check.mjs（Edge）で確かめる。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";

const JS = new URL("../../wwwroot/js/", import.meta.url);
const ORIGIN = "https://avatar.fara-labs.com";
const NAV_SRC = readFileSync(new URL("nav.js", JS), "utf8");

/** document がない環境（node）で nav.js を読む。判定の関数・register・設定だけが使える。 */
function loadNav(config) {
  const sandbox = { window: {}, console };
  if (config) sandbox.window.faraNavConfig = config;
  runInNewContext(NAV_SRC, sandbox);
  return sandbox.window.faraNav;
}

const nav = loadNav();
const routable = (href, n = nav) => n.isRoutable(new URL(href), ORIGIN);

test("同じサイトのページ（拡張子なし・.html）は、ページ移動の対象になる", () => {
  assert.equal(routable(`${ORIGIN}/`), true);
  assert.equal(routable(`${ORIGIN}/learn/basics/01-select`), true);
  assert.equal(routable(`${ORIGIN}/learn/basics`), true);
  assert.equal(routable(`${ORIGIN}/report.html?from=/speed`), true);
  assert.equal(routable(`${ORIGIN}/search?q=abc`), true);
});

test("画像・JSON・API・別のサイトは、ブラウザに任せる（対象外）", () => {
  assert.equal(routable(`${ORIGIN}/learn/basics/images/modifiers-panel.png`), false);
  assert.equal(routable(`${ORIGIN}/search-index.json`), false);
  assert.equal(routable(`${ORIGIN}/api/rooms/abcdef/ws`), false);
  assert.equal(routable(`${ORIGIN}/_framework/blazor.web.js`), false);
  assert.equal(routable("https://example.com/learn/basics"), false);
});

test("既定の設定は、今の値（本文・メニュー・スキップリンク・除外パス）", () => {
  assert.equal(nav.config.main, "#main-content");
  assert.deepEqual([...nav.config.swap], [".sidebar-panel"]);
  assert.equal(nav.config.skipLink, ".skip-link");
  assert.deepEqual([...nav.config.excludePrefixes], ["/api/", "/_"]);
});

test("window.faraNavConfig で、除外パス・差し替え対象を上書きできる（ほかのサイトで使うため）", () => {
  const custom = loadNav({ excludePrefixes: ["/hooks/"], swap: [".side", ".banner"] });
  assert.deepEqual([...custom.config.excludePrefixes], ["/hooks/"]);
  assert.deepEqual([...custom.config.swap], [".side", ".banner"]);
  // 上書きしていない値は、既定のまま
  assert.equal(custom.config.main, "#main-content");
  assert.equal(routable(`${ORIGIN}/api/rooms/x`, custom), true);
  assert.equal(routable(`${ORIGIN}/hooks/x`, custom), false);
});

test("設定に知らない項目があっても、無視する", () => {
  const n = loadNav({ nonsense: 1 });
  assert.equal("nonsense" in n.config, false);
});

test("register は、読み込み中でなければ、その場で 1 回呼ぶ", () => {
  let calls = 0;
  nav.register(() => { calls++; });
  assert.equal(calls, 1);
});

test("register で例外が出ても、ほかの登録は動く（ページ移動の途中で止まらない）", () => {
  const original = console.error;
  console.error = () => {};
  try {
    let after = 0;
    nav.register(() => { throw new Error("boom"); });
    nav.register(() => { after++; });
    assert.equal(after, 1);
  } finally {
    console.error = original;
  }
});

// ページ専用の JS は、nav.js がなくても動く（共有ファイルの report.js は、ほかのサイトでは nav.js がない）。
// そのため、登録は「nav.js があれば register、なければその場で呼ぶ」の形にする
// bot では、ページ専用の JS は report.js だけ（avatar の一覧のうち、このサイトにあるものだけを見る）
const PAGE_SCRIPTS = ["report.js"];

for (const file of PAGE_SCRIPTS) {
  test(`${file} は faraNav が無くても動く形で、初期化を登録する`, () => {
    const src = readFileSync(new URL(file, JS), "utf8");
    assert.match(src, /window\.faraNav \? window\.faraNav\.register\(\w+\) : \w+\(\);/, `${file} の登録の形`);
    assert.doesNotMatch(src, /document\.readyState === "loading"/, `${file} に readyState の分岐が残っている`);
  });
}

test("report.js は、nav.js がなくても（ほかのサイトでも）初期化できる", () => {
  // 本文のフォームがないページでは、何もせずに終わる（例外にならない）
  const src = readFileSync(new URL("report.js", JS), "utf8");
  const sandbox = { window: {}, document: { getElementById: () => null, readyState: "complete" }, console };
  assert.doesNotThrow(() => runInNewContext(src, sandbox));
});


test("nav.js は、ページ移動の対象外（3D・WebSocket のページ）と、再読み込みのスクロール復元を持つ", () => {
  assert.match(NAV_SRC, /script\[type="module"\], script\[type="importmap"\]/);
  assert.match(NAV_SRC, /history\.scrollRestoration = "manual"/);
  assert.match(NAV_SRC, /restoreOnLoad/);
});

test("nav.js は、本文のスクリプトを二重に読まないよう、読み込み中の Promise を共有する", () => {
  assert.match(NAV_SRC, /pending\[src\]/);
});

// bot の検索欄は search.js（ページが替わっても残るヘッダーの欄）。二重に付けないことは、ページ単位の登録ではなく、起動時の 1 回だけで行う
test("ヘッダーの検索欄（ページが替わっても残る）は、起動時に 1 回だけ付ける", () => {
  const src = readFileSync(new URL("search.js", JS), "utf8");
  assert.match(src, /data-site-search/);
  assert.match(src, /document\.addEventListener\("keydown"/);
});
