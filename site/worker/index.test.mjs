// worker/index.js のテスト。  node --test worker/index.test.mjs（site/ で）
// /api/maintenance・メンテナンス画面（503）・/api/report の振り分け・CSP を Worker から付けないこと・公開の設定を確かめる。
// 報告の中身（検証・Turnstile・Discord）とメンテナンスの部品は、kit のテスト（external/fara-site-kit/worker/*.test.mjs）が確かめる。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import worker from "./index.js";

const HOST = "https://twitch-bot.example";
const read = (path) => readFileSync(new URL(path, import.meta.url), "utf8");
const page = "<html><body>本文</body></html>";
const env = {
  ASSETS: { fetch: async () => new Response(page, { status: 200, headers: { "Content-Type": "text/html; charset=utf-8", "Content-Security-Policy": "default-src 'self'" } }) },
  TURNSTILE_SITE_KEY: "site-key",
  TURNSTILE_SECRET: "secret-value",
  DISCORD_WEBHOOK_URL: "https://discord.example/hook",
};
const open = (path, e = env, accept = "text/html") => worker.fetch(new Request(HOST + path, { headers: { Accept: accept } }), e);

test("kit の共有 JS を使う（Worker の中に、報告・メンテナンスの写しを持たない）", () => {
  const source = read("./index.js");
  assert.match(source, /from "\.\.\/\.\.\/external\/fara-site-kit\/worker\/report\.js"/);
  assert.match(source, /from "\.\.\/\.\.\/external\/fara-site-kit\/worker\/maintenance\.js"/);
  assert.ok(!/withNotice\(/.test(source), "お知らせは、アプリの MaintenanceBanner が出す（HTML に差し込むと二重になる）");
});

test("CSP は Worker から付けない（静的アセットの _headers が唯一の情報源。応答に付いているものは、そのまま通す）", async () => {
  const res = await open("/");
  assert.equal(res.headers.get("Content-Security-Policy"), "default-src 'self'");
  assert.ok(!/headers\.set\(\s*"Content-Security-Policy"/.test(read("./index.js")));
});

test("全ページに nosniff と Referrer-Policy が付く", async () => {
  const res = await open("/");
  assert.equal(res.headers.get("X-Content-Type-Options"), "nosniff");
  assert.equal(res.headers.get("Referrer-Policy"), "strict-origin-when-cross-origin");
  assert.equal(await res.text(), page);
});

test("noindex は、Worker では付けない（確認用の noindex は、build-pages と公開のワークフローが _headers に付ける）。変数 NOINDEX も使わない", async () => {
  for (const host of ["https://twitch-bot-dev.fara-labs.com/", HOST + "/"]) {
    const res = await worker.fetch(new Request(host), { ...env, NOINDEX: "true" });
    assert.equal(res.headers.get("X-Robots-Tag"), null, host);
  }
  assert.ok(!read("../wrangler.toml").includes("NOINDEX"));
  const yml = read("../../.github/workflows/site.yml");
  const step = yml.indexOf("Add noindex header (develop)");
  assert.ok(step > 0);
  const next = yml.indexOf("- name:", step + 1);
  const body = yml.slice(step, next > 0 ? next : undefined);
  assert.match(body, /if: github\.ref_name != 'master'/);
  assert.match(body, /X-Robots-Tag: noindex, nofollow/);
});

test("/api/report/config は報告の受け口へ（静的ファイルに任せない）", async () => {
  const res = await open("/api/report/config");
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

test("/api/maintenance: 状態（state・message）を返す。キャッシュさせない", async () => {
  for (const [vars, state, message] of [
    [{}, "off", ""],
    [{ MAINTENANCE: "false", MAINTENANCE_MESSAGE: "x" }, "off", ""],
    [{ MAINTENANCE: "notice", MAINTENANCE_MESSAGE: "10月25日 20:00 ごろ" }, "notice", "10月25日 20:00 ごろ"],
    [{ MAINTENANCE: "true", MAINTENANCE_MESSAGE: "21:00 ごろ終了予定" }, "on", "21:00 ごろ終了予定"],
  ]) {
    const res = await open("/api/maintenance", { ...env, ...vars }, "application/json");
    assert.equal(res.status, 200, JSON.stringify(vars));
    assert.deepEqual(await res.json(), { state, message });
    assert.equal(res.headers.get("Cache-Control"), "no-store");
  }
});

test("MAINTENANCE=true: すべてのページが 503 のメンテナンス画面（文つき）。検索エンジンには載せない", async () => {
  for (const path of ["/", "/docs/overview", "/privacy", "/_framework/blazor.webassembly.js"]) {
    const res = await open(path, { ...env, MAINTENANCE: "true", MAINTENANCE_MESSAGE: "21:00 ごろ終了予定" });
    assert.equal(res.status, 503, path);
    assert.equal(res.headers.get("X-Robots-Tag"), "noindex");
    if (path.endsWith(".js")) continue;
    const html = await res.text();
    assert.match(html, /ただいまメンテナンス中です/);
    assert.match(html, /21:00 ごろ終了予定/);
    assert.match(html, /twitter\.com\/game_fara/);
    assert.ok(!html.includes("本文"));
  }
});

test("MAINTENANCE=true: 報告の受け口も止まる（API は JSON の 503）", async () => {
  const res = await open("/api/report/config", { ...env, MAINTENANCE: "true" }, "application/json");
  assert.equal(res.status, 503);
  assert.equal((await res.json()).error, "maintenance");
});

test("MAINTENANCE=notice: ページは 200 のまま、HTML は書き換えない（お知らせはアプリが出す）", async () => {
  const res = await open("/", { ...env, MAINTENANCE: "notice", MAINTENANCE_MESSAGE: "10月25日 20:00 ごろ" });
  assert.equal(res.status, 200);
  assert.equal(await res.text(), page);
});

test("MAINTENANCE=false（または未設定）: メンテナンス画面は出ない", async () => {
  for (const extra of [{ MAINTENANCE: "false", MAINTENANCE_MESSAGE: "10月25日 20:00 ごろ" }, { MAINTENANCE_MESSAGE: "x" }, {}]) {
    const res = await open("/", { ...env, ...extra });
    assert.equal(res.status, 200);
    assert.equal(await res.text(), page);
  }
});

test("wrangler.toml: ダッシュボードの変数を公開のたびに消さない（keep_vars）、全リクエストが Worker を通る、404 は 404.html、公開物は publish/wwwroot", () => {
  const toml = read("../wrangler.toml");
  const keep = [...toml.matchAll(/^keep_vars = true$/gm)];
  assert.equal(keep.length, 1);
  assert.ok(keep[0].index < toml.indexOf("[env."), "最初の [env.*] より前（トップレベル）");
  assert.equal([...toml.matchAll(/^run_worker_first = true$/gm)].length, 3, "本番・確認用・トップレベルのすべて");
  assert.equal([...toml.matchAll(/^not_found_handling = "404-page"$/gm)].length, 3);
  assert.equal([...toml.matchAll(/^directory = "\.\/publish\/wwwroot"$/gm)].length, 3);
  assert.match(toml, /name = "fara-twitch-bot"/);
  assert.match(toml, /name = "fara-twitch-bot-dev"/);
  assert.match(toml, /twitch-bot\.fara-labs\.com/);
  assert.match(toml, /twitch-bot-dev\.fara-labs\.com/);
});

test("本番の公開は MAINTENANCE=notice を確かめてから行い、公開と同時に MAINTENANCE=true にする（site.yml）", () => {
  const yml = read("../../.github/workflows/site.yml");
  assert.match(yml, /node \.github\/scripts\/require-maintenance\.mjs fara-twitch-bot/);
  const gate = yml.indexOf("Require MAINTENANCE=notice (production)");
  assert.ok(gate > 0);
  assert.match(yml.slice(gate, gate + 200), /if: github\.ref_name == 'master'/, "確認用の develop は確かめない");
  assert.ok(gate < yml.indexOf("cloudflare/wrangler-action@v3"), "確認は公開より前");
  assert.match(yml, /--var MAINTENANCE:true/);
  assert.match(yml, /!inputs\.skip_maintenance_check/);
  assert.match(yml, /SKIP_MAINTENANCE_CHECK: \$\{\{ inputs\.skip_maintenance_check \}\}/);
  assert.match(yml, /require-maintenance\.test\.mjs/, "CI で確認のスクリプトもテストする");
  assert.match(yml, /submodules: true/, "kit の submodule を取得する");
  assert.match(yml, /external\/fara-site-kit/, "kit を上げたときも公開する（paths）");
});

test("package.json は wrangler のバージョンを固定する（site.yml と同じ）", () => {
  const pkg = JSON.parse(read("../package.json"));
  assert.match(pkg.devDependencies.wrangler, /^\d+\.\d+\.\d+$/);
  assert.match(read("../../.github/workflows/site.yml"), new RegExp(`wranglerVersion: "${pkg.devDependencies.wrangler.replace(/\./g, "\\.")}"`));
});
