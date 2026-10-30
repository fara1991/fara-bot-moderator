// js/site-init.js（旧い形のテーマの保存値を kit の JSON に直す・保存が無いとき端末がダークなら 1 回だけダークにする）のテスト。  node --test tests/js/site-init.test.mjs（site/ で）
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";

const SRC = readFileSync(new URL("../../src/FaraBotModeratorSite.Web/wwwroot/js/site-init.js", import.meta.url), "utf8");
const KEY = "fara-bot-moderator-site.theme";

function run(stored, prefersLight) {
  const store = new Map(stored === undefined ? [] : [[KEY, stored]]);
  const localStorage = { getItem: (k) => (store.has(k) ? store.get(k) : null), setItem: (k, v) => store.set(k, v) };
  runInNewContext(SRC, { localStorage, window: { matchMedia: () => ({ matches: prefersLight }) }, JSON });
  return store.get(KEY) ?? null;
}

test("以前の静的サイトの値（dark / light の文字だけ）は、同じキーのまま JSON に直す", () => {
  assert.equal(run("dark", true), "{\"Theme\":\"dark\"}");
  assert.equal(run("light", false), "{\"Theme\":\"light\"}");
});

test("すでに JSON の値は変えない", () => {
  assert.equal(run("{\"Theme\":\"light\",\"Other\":1}", false), "{\"Theme\":\"light\",\"Other\":1}");
});

test("保存が無いとき: 端末がダーク（ライトを明示していない）なら、最初の 1 回だけダークにする", () => {
  assert.equal(run(undefined, false), "{\"Theme\":\"dark\"}");
});

test("保存が無く、端末がライトなら、何も書かない（kit の既定のライト）", () => {
  assert.equal(run(undefined, true), null);
});

test("localStorage が使えなくても、例外にしない", () => {
  runInNewContext(SRC, { localStorage: { getItem() { throw new Error("blocked"); } }, window: {}, JSON });
});
