// wwwroot/js/router.js（サイト内の移動）の判定。ブラウザの代わりに、最小限の window・document を用意して読み込む。
// 実行: node --test tests/js/router.test.mjs（site/ で）
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, "..", "..", "wwwroot", "js", "router.js"), "utf8");

// 読み込みだけで動く（イベントの登録・履歴の初期化）ので、何もしない部品を用意する
function load(url = "https://example.test/") {
  const noop = () => {};
  const location = new URL(url);
  const window = {
    location,
    history: { state: null, replaceState: noop, pushState: noop, scrollRestoration: "auto" },
    addEventListener: noop,
    scrollTo: noop,
    requestAnimationFrame: noop,
    pageYOffset: 0,
  };
  const document = {
    addEventListener: noop,
    querySelector: () => null,
    getElementById: () => null,
    head: null,
  };
  vm.runInNewContext(source, { window, document, location, URL, Object, fetch: noop, DOMParser: class {}, Event: class {} });
  return window.FaraRouter;
}

const R = load();
const docsPage = new URL("https://example.test/docs/overview");

test("/report は、ページの差し替えの対象にしない（CSP が違うので、読み直す）", () => {
  assert.equal(R.isReportPath("/report"), true);
  assert.equal(R.isReportPath("/report/"), true);
  assert.equal(R.isReportPath("/reports"), false);
  assert.equal(R.isNavigablePath("/report"), false);
  assert.equal(R.sameSiteTarget("/report?from=/docs", docsPage), null);
});

test("API・ファイル（.json・.png など）は、差し替えの対象にしない", () => {
  assert.equal(R.isNavigablePath("/api/report"), false);
  assert.equal(R.isNavigablePath("/search-index.json"), false);
  assert.equal(R.isNavigablePath("/images/site-icon.svg"), false);
  assert.equal(R.isNavigablePath("/docs/overview"), true);
  assert.equal(R.isNavigablePath("/"), true);
});

test("同じサイトの、ページへのリンクは移動する（クエリ・ハッシュは残す）", () => {
  const url = R.sameSiteTarget("/docs/faq#troubleshooting", docsPage);
  assert.equal(url.href, "https://example.test/docs/faq#troubleshooting");
  assert.equal(R.sameSiteTarget("/changelog", docsPage).pathname, "/changelog");
});

test("別のサイト・ページ内の #見出し（目次・スキップリンク）は、ブラウザに任せる", () => {
  assert.equal(R.sameSiteTarget("https://other.example/docs", docsPage), null);
  assert.equal(R.sameSiteTarget("#main-content", docsPage), null);
  assert.equal(R.sameSiteTarget("/docs/overview#main-settings", docsPage), null);
  // 同じページでも、ハッシュ無しの同じ URL は、移動として扱う（読み直し）
  assert.notEqual(R.sameSiteTarget("/docs/overview", docsPage), null);
});

test("報告フォームのページから出るリンクは、移動しない（CSP が違うので、通常の読み直し）", () => {
  const report = new URL("https://example.test/report");
  assert.equal(R.sameSiteTarget("/docs", report), null);
});

test("本文に script があるページは、差し替えない（script は差し替えでは動かない）", () => {
  const withScript = { querySelector: (sel) => (sel === "main#main-content" ? { querySelector: () => ({}) } : null) };
  const plain = { querySelector: (sel) => (sel === "main#main-content" ? { querySelector: () => null } : null) };
  assert.equal(R.swappable(withScript), false);
  assert.equal(R.swappable(plain), true);
  assert.equal(R.swappable({ querySelector: () => null }), false);
});

test("二重に読み込んでも、2 回目は何もしない（リスナーを二重に付けない）", () => {
  const window = { FaraRouter: { marker: true } };
  const document = { addEventListener: () => { throw new Error("should not run"); } };
  vm.runInNewContext(source, { window, document, location: new URL("https://example.test/"), URL, Object });
  assert.deepEqual(window.FaraRouter, { marker: true });
});
