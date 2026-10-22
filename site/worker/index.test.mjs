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

test("noindex は、Worker では付けない（確認用の noindex は、公開のワークフローが dist/_headers に付ける）。変数 NOINDEX も使わない", async () => {
  const { readFileSync } = await import("node:fs");
  for (const host of ["https://twitch-bot-dev.fara-labs.com/", HOST + "/"]) {
    const res = await worker.fetch(new Request(host), { ...env, NOINDEX: "true" });
    assert.equal(res.headers.get("X-Robots-Tag"), null, host);
  }
  assert.ok(!readFileSync(new URL("../wrangler.toml", import.meta.url), "utf8").includes("NOINDEX"));
  const yml = readFileSync(new URL("../../.github/workflows/site.yml", import.meta.url), "utf8");
  const step = yml.indexOf("Add noindex header (develop)");
  assert.ok(step > 0);
  const next = yml.indexOf("- name:", step + 1);
  const body = yml.slice(step, next > 0 ? next : undefined);
  assert.match(body, /if: github\.ref_name != 'master'/);
  assert.match(body, /X-Robots-Tag: noindex, nofollow/);
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

// ---- メンテナンス（MAINTENANCE / MAINTENANCE_MESSAGE。worker/maintenance.js） ----
// HTMLRewriter は Workers にだけあるので、<main> の先頭に足すだけの代わりを用意する
globalThis.HTMLRewriter = class {
  on(_selector, handlers) { this.handlers = handlers; return this; }
  transform(response) {
    const handlers = this.handlers;
    const text = response.text();
    return new Response(new ReadableStream({
      async start(controller) {
        let body = await text;
        handlers.element({ prepend: (html) => { body = body.replace("<main>", `<main>${html}`); } });
        controller.enqueue(new TextEncoder().encode(body));
        controller.close();
      },
    }), response);
  }
};
const pageEnv = (extra) => ({
  ...env,
  ASSETS: { fetch: async () => new Response("<html><main>本文</main></html>", { headers: { "Content-Type": "text/html; charset=utf-8" } }) },
  ...extra,
});
const open = (path, e, accept = "text/html") => worker.fetch(new Request(HOST + path, { headers: { Accept: accept } }), e);

test("MAINTENANCE=true: すべてのページが 503 のメンテナンス画面（文つき）", async () => {
  for (const path of ["/", "/docs/overview", "/privacy"]) {
    const res = await open(path, pageEnv({ MAINTENANCE: "true", MAINTENANCE_MESSAGE: "21:00 ごろ終了予定" }));
    assert.equal(res.status, 503, path);
    const html = await res.text();
    assert.match(html, /ただいまメンテナンス中です/);
    assert.match(html, /21:00 ごろ終了予定/);
    assert.match(html, /twitter\.com\/game_fara/);
    assert.ok(!html.includes("本文"));
  }
});

test("MAINTENANCE=true: 報告の受け口も止まる（API は JSON の 503）", async () => {
  const res = await open("/api/report/config", pageEnv({ MAINTENANCE: "true" }), "application/json");
  assert.equal(res.status, 503);
  assert.equal((await res.json()).error, "maintenance");
});

test("MAINTENANCE=notice: ページの先頭に、更新予定のお知らせ。ページ自体は 200", async () => {
  const res = await open("/", pageEnv({ MAINTENANCE: "notice", MAINTENANCE_MESSAGE: "10月25日 20:00 ごろ" }));
  assert.equal(res.status, 200);
  const html = await res.text();
  assert.match(html, /<main><div class="maintenance-notice"[^>]*><strong>お知らせ<\/strong> 10月25日 20:00 ごろ<\/div>本文/);
  assert.equal(res.headers.get("Cache-Control"), "no-store");
});

test("MAINTENANCE=false（または未設定）: お知らせもメンテナンス画面も出ない。文が残っていても出ない", async () => {
  for (const extra of [{ MAINTENANCE: "false", MAINTENANCE_MESSAGE: "10月25日 20:00 ごろ" }, { MAINTENANCE_MESSAGE: "x" }, {}]) {
    const res = await open("/", pageEnv(extra));
    assert.equal(res.status, 200);
    const html = await res.text();
    assert.ok(!html.includes("maintenance-notice"), JSON.stringify(extra));
    assert.equal(html, "<html><main>本文</main></html>");
  }
});

test("MAINTENANCE=notice でも、HTML でない応答（CSS など）には足さない", async () => {
  const e = pageEnv({ MAINTENANCE: "notice", MAINTENANCE_MESSAGE: "x" });
  e.ASSETS = { fetch: async () => new Response("body{}", { headers: { "Content-Type": "text/css" } }) };
  assert.equal(await (await open("/css/site.css", e, "text/css")).text(), "body{}");
});

test("wrangler.toml は、ダッシュボードの変数を公開のたびに消さない（keep_vars。トップレベルだけが有効で、本番・確認用の両方に効く）", async () => {
  const { readFileSync } = await import("node:fs");
  const toml = readFileSync(new URL("../wrangler.toml", import.meta.url), "utf8");
  const lines = [...toml.matchAll(/^keep_vars = true$/gm)];
  assert.equal(lines.length, 1);
  assert.ok(lines[0].index < toml.indexOf("[env."), "最初の [env.*] より前（トップレベル）");
});

test("本番の公開は MAINTENANCE=notice を確かめてから行い、公開と同時に MAINTENANCE=true にする（site.yml）", async () => {
  const { readFileSync } = await import("node:fs");
  const yml = readFileSync(new URL("../../.github/workflows/site.yml", import.meta.url), "utf8");
  assert.match(yml, /node \.github\/scripts\/require-maintenance\.mjs fara-twitch-bot/);
  const gate = yml.indexOf("Require MAINTENANCE=notice (production)");
  assert.ok(gate > 0);
  assert.match(yml.slice(gate, gate + 200), /if: github\.ref_name == 'master'/, "確認用の develop は確かめない");
  assert.ok(gate < yml.indexOf("cloudflare/wrangler-action@v3"), "確認は公開より前");
  assert.match(yml, /--var MAINTENANCE:true/);
  assert.match(yml, /!inputs\.skip_maintenance_check/);
  assert.match(yml, /SKIP_MAINTENANCE_CHECK: \$\{\{ inputs\.skip_maintenance_check \}\}/);
  assert.match(yml, /require-maintenance\.test\.mjs/, "CI で確認のスクリプトもテストする");
});

test("メンテナンスのお知らせの帯は sticky で上に残る。PC は固定ヘッダーの真下（top: 3.5rem）、重なりは固定ヘッダー（70）より下・隠したサイドバー（75）より下", async () => {
  const { readFileSync } = await import("node:fs");
  const css = readFileSync(new URL("../wwwroot/css/site.css", import.meta.url), "utf8");
  const rule = css.match(/^\.maintenance-notice \{([^}]*)\}/m);
  assert.ok(rule, "帯の基本の規則");
  assert.match(rule[1], /position: sticky;/);
  assert.match(rule[1], /(^|\s)top: 0;/);
  const z = Number(rule[1].match(/z-index: (\d+);/)[1]);
  assert.ok(z > 0 && z < 70, `z-index ${z} は 70（固定ヘッダー）より下`);
  assert.ok(z < 75, "隠したサイドバーが広がったとき（75）の下");
  const pc = css.match(/@media \(min-width: 900px\) \{[\s\S]*?\n\}/g).filter((b) => b.includes(".maintenance-notice"));
  assert.equal(pc.length, 1, "PC の指定は 900px の 1 か所");
  assert.match(pc[0], /\.maintenance-notice \{ top: 3\.5rem; \}/);
  assert.match(css, /html:has\(\.maintenance-notice\) \{ scroll-padding-top: calc\(var\(--notice-h\) \+ 16px\); \}/, "スマホの見出しへのジャンプ");
  assert.match(pc[0], /html:has\(\.maintenance-notice\) main \.doc-toc \{\s*top: calc\(3\.5rem \+ var\(--notice-h\) \+ 20px\);/, "PC の目次は帯の下");
});
