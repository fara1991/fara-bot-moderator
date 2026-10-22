// worker/index.js のテスト。  node --test worker/index.test.mjs
// ページごとの Content-Security-Policy（/report だけ Turnstile のために緩める）と、/api/report の振り分けを確かめる。
import { test } from "node:test";
import assert from "node:assert/strict";
import worker, { isReportPage } from "./index.js";

const HOST = "https://twitch-bot.example";
const env = {
  ASSETS: { fetch: async () => new Response("<html></html>", { status: 200, headers: { "Content-Type": "text/html" } }) },
  TURNSTILE_SITE_KEY: "site-key",
  TURNSTILE_SECRET: "secret-value",
  DISCORD_WEBHOOK_URL: "https://discord.example/hook",
};

const csp = async (path, e = env) => (await worker.fetch(new Request(HOST + path), e)).headers.get("Content-Security-Policy");

test("isReportPage は /report と /report/ だけ", () => {
  assert.equal(isReportPage("/report"), true);
  assert.equal(isReportPage("/report/"), true);
  for (const p of ["/", "/privacy", "/reports", "/report/x", "/docs/report", "/api/report"]) assert.equal(isReportPage(p), false, p);
});

test("/report だけ challenges.cloudflare.com を許可する", async () => {
  for (const path of ["/report", "/report/"]) {
    const policy = await csp(path);
    assert.match(policy, /script-src 'self' https:\/\/challenges\.cloudflare\.com/);
    assert.match(policy, /frame-src https:\/\/challenges\.cloudflare\.com/);
    assert.match(policy, /connect-src 'self' https:\/\/challenges\.cloudflare\.com/);
  }
});

test("ほかのページは、これまでどおり同じオリジンだけ", async () => {
  for (const path of ["/", "/privacy", "/docs/overview", "/search"]) {
    const policy = await csp(path);
    assert.ok(!policy.includes("challenges.cloudflare.com"), path);
    assert.match(policy, /script-src 'self'(;|$)/);
    assert.match(policy, /default-src 'self'/);
    assert.match(policy, /frame-ancestors 'none'/);
  }
});

test("全ページに nosniff と Referrer-Policy が付く", async () => {
  const res = await worker.fetch(new Request(HOST + "/"), env);
  assert.equal(res.headers.get("X-Content-Type-Options"), "nosniff");
  assert.equal(res.headers.get("Referrer-Policy"), "strict-origin-when-cross-origin");
});

test("確認用のホストと NOINDEX には noindex が付く", async () => {
  const dev = await worker.fetch(new Request("https://twitch-bot-dev.fara-labs.com/"), env);
  assert.match(dev.headers.get("X-Robots-Tag") ?? "", /noindex/);
  const flagged = await worker.fetch(new Request(HOST + "/"), { ...env, NOINDEX: "true" });
  assert.match(flagged.headers.get("X-Robots-Tag") ?? "", /noindex/);
  const prod = await worker.fetch(new Request(HOST + "/"), env);
  assert.equal(prod.headers.get("X-Robots-Tag"), null);
});

test("/api/report/config は報告の受け口へ（静的ファイルに任せない）", async () => {
  const res = await worker.fetch(new Request(HOST + "/api/report/config"), env);
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { enabled: true, siteKey: "site-key" });
  assert.equal(res.headers.get("X-Content-Type-Options"), "nosniff");
});

test("/api/report は、同じサイトからでないと 403", async () => {
  const res = await worker.fetch(new Request(HOST + "/api/report", {
    method: "POST",
    headers: { "Content-Type": "application/json", Origin: "https://evil.example" },
    body: JSON.stringify({ category: "bug", message: "テストです", turnstileToken: "t" }),
  }), env);
  assert.equal(res.status, 403);
});
