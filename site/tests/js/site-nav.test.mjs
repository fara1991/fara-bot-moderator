// このサイトの、ページ移動の設定（nav-config.js）と、移動のあとの後処理（site-nav.js）のテスト。  node --test tests/js/site-nav.test.mjs（site/ で）
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";

const JS = new URL("../../wwwroot/js/", import.meta.url);
const read = (name) => readFileSync(new URL(name, JS), "utf8");

test("nav-config.js: 本文は #main-content、左のメニューの中身だけを差し替え、報告フォームと API は対象外", () => {
  const window = {};
  runInNewContext(read("nav-config.js"), { window });
  const cfg = window.faraNavConfig;
  assert.equal(cfg.main, "#main-content");
  assert.deepStrictEqual(Array.from(cfg.swap), [".nav-scrollable > nav"]);
  assert.equal(cfg.skipLink, ".skip-link");
  assert.deepStrictEqual(Array.from(cfg.excludePrefixes), ["/api/", "/report"]);
});

test("site-nav.js: 移動のたびに、スマホのメニューを閉じ、fara:navigated を出す", () => {
  let handler = null;
  const toggle = { checked: true };
  const events = [];
  const window = { faraNav: { register: (fn) => { handler = fn; } } };
  const document = {
    getElementById: (id) => (id === "nav-toggle" ? toggle : null),
    dispatchEvent: (e) => events.push(e.type),
  };
  runInNewContext(read("site-nav.js"), { window, document, Event: class { constructor(t) { this.type = t; } } });
  assert.equal(typeof handler, "function", "register に処理を渡している");
  handler();
  assert.equal(toggle.checked, false);
  assert.deepEqual(events, ["fara:navigated"]);
});

test("site-nav.js: nav.js がないときは、何もしない（エラーにならない）", () => {
  assert.doesNotThrow(() => runInNewContext(read("site-nav.js"), { window: {}, document: {}, Event: class {} }));
});
