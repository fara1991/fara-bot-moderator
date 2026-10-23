// worker/index.js のテスト。  node --test worker/index.test.mjs（site/ で）
// /api/maintenance・メンテナンス画面（503）・/api/report の振り分け・CSP を Worker から付けないこと・公開の設定を確かめる。
// 報告の中身（検証・Turnstile・Discord）とメンテナンスの部品は、kit のテスト（external/fara-site-kit/worker/*.test.mjs）が確かめる。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
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
    [{ MAINTENANCE_MODE: "false", MAINTENANCE_MESSAGE: "x" }, "off", ""],
    [{ MAINTENANCE_MODE: "notice", MAINTENANCE_MESSAGE: "10月25日 20:00 ごろ" }, "notice", "10月25日 20:00 ごろ"],
    [{ MAINTENANCE_MODE: "true", MAINTENANCE_MESSAGE: "21:00 ごろ終了予定" }, "on", "21:00 ごろ終了予定"],
  ]) {
    const res = await open("/api/maintenance", { ...env, ...vars }, "application/json");
    assert.equal(res.status, 200, JSON.stringify(vars));
    assert.deepEqual(await res.json(), { state, message });
    assert.equal(res.headers.get("Cache-Control"), "no-store");
  }
});

test("MAINTENANCE_MODE=true: すべてのページが 503 のメンテナンス画面（文つき）。検索エンジンには載せない", async () => {
  for (const path of ["/", "/docs/overview", "/privacy", "/_framework/blazor.webassembly.js"]) {
    const res = await open(path, { ...env, MAINTENANCE_MODE: "true", MAINTENANCE_MESSAGE: "21:00 ごろ終了予定" });
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

test("MAINTENANCE_MODE=true: 報告の受け口も止まる（API は JSON の 503）", async () => {
  const res = await open("/api/report/config", { ...env, MAINTENANCE_MODE: "true" }, "application/json");
  assert.equal(res.status, 503);
  assert.equal((await res.json()).error, "maintenance");
});

test("MAINTENANCE_MODE=notice: ページは 200 のまま、HTML は書き換えない（お知らせはアプリが出す）", async () => {
  const res = await open("/", { ...env, MAINTENANCE_MODE: "notice", MAINTENANCE_MESSAGE: "10月25日 20:00 ごろ" });
  assert.equal(res.status, 200);
  assert.equal(await res.text(), page);
});

test("MAINTENANCE_MODE=false（または未設定）: メンテナンス画面は出ない", async () => {
  for (const extra of [{ MAINTENANCE_MODE: "false", MAINTENANCE_MESSAGE: "10月25日 20:00 ごろ" }, { MAINTENANCE_MESSAGE: "x" }, {}]) {
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

/** site.yml を読む（Windows の CRLF は LF にそろえる）。 */
const readYml = () => read("../../.github/workflows/site.yml").replace(/\r\n/g, "\n");
/** site.yml の「- name: <name>」のステップ全体（次のステップの手前まで）。 */
const stepOf = (yml, name) => {
  const start = yml.indexOf(`- name: ${name}`);
  assert.ok(start > 0, `${name} のステップがありません`);
  const next = yml.indexOf("\n      - name:", start + 1);
  const nextUses = yml.indexOf("\n      - uses:", start + 1);
  const ends = [next, nextUses].filter((n) => n > 0);
  return yml.slice(start, ends.length ? Math.min(...ends) : undefined);
};

test("本番の公開は MAINTENANCE_MODE=notice を確かめてから行う。mode を指定した手動実行は確認を飛ばす（site.yml）", () => {
  const yml = readYml();
  assert.match(yml, /node \.github\/scripts\/require-maintenance\.mjs fara-twitch-bot/);
  const gate = stepOf(yml, "Require MAINTENANCE_MODE=notice (production)");
  assert.ok(gate.includes(`if: \${{ github.ref_name == 'master' && !contains(fromJSON('["true","notice","false"]'), inputs.maintenance_mode) }}`), "確認用の develop は確かめない。mode 指定のときも確かめない");
  assert.match(gate, /SKIP_MAINTENANCE_CHECK: \$\{\{ inputs\.skip_maintenance_check \}\}/);
  assert.ok(yml.indexOf("- name: Require MAINTENANCE_MODE=notice") < yml.indexOf("- name: Deploy to Cloudflare Workers"), "確認は公開より前");
  assert.match(yml, /require-maintenance\.test\.mjs/, "CI で確認のスクリプトもテストする");
  assert.match(yml, /submodules: true/, "kit の submodule を取得する");
  assert.match(yml, /external\/fara-site-kit/, "kit を上げたときも公開する（paths）");
  assert.ok(!/MAINTENANCE(?![_A-Z])/.test(yml.replace(/SKIP_MAINTENANCE_CHECK/g, "")), "旧名 MAINTENANCE は使わない");
});

test("手動実行の入力: maintenance_mode（choice）・maintenance_message（string）・skip_maintenance_check", () => {
  const yml = readYml();
  const inputs = yml.slice(yml.indexOf("  workflow_dispatch:"), yml.indexOf("permissions:"));
  assert.match(inputs, /skip_maintenance_check:\n(?:.*\n)*?\s+type: boolean/);
  assert.match(inputs, /maintenance_mode:\n(?:.*\n)*?\s+type: choice\n\s+options: \["", "true", "notice", "false"\]\n\s+default: ""/);
  assert.match(inputs, /maintenance_message:\n(?:.*\n)*?\s+type: string\n\s+default: ""/);
});

// 公開のステップの run を、実際に bash で動かして、wrangler に渡る引数を確かめる（wrangler の呼び出しを、引数を 1 行ずつ出すものに置き換える）
const deployArgs = (envVars) => {
  const step = stepOf(readYml(), "Deploy to Cloudflare Workers");
  const body = step.slice(step.indexOf("run: |\n") + "run: |\n".length, step.indexOf("\n        env:"));
  const script = body.split("\n").map((l) => l.replace(/^ {10}/, "")).join("\n");
  assert.ok(script.includes('npx wrangler@4.149.0 deploy --env "$DEPLOY_ENV" "${vars[@]}"'));
  const fake = script.replace("npx wrangler@4.149.0 deploy", 'printf "%s\\n" deploy');
  const r = spawnSync("bash", ["-c", fake], {
    env: { PATH: process.env.PATH, MAINTENANCE_MODE_INPUT: "", MAINTENANCE_MESSAGE_INPUT: "", SKIP_MAINTENANCE_CHECK: "", ...envVars },
    encoding: "utf8",
  });
  assert.equal(r.status, 0, r.stderr);
  return r.stdout.split("\n").filter((l) => l !== "");
};
const MODE = (v) => ["--var", `MAINTENANCE_MODE:${v}`];
const MSG = (v) => ["--var", `MAINTENANCE_MESSAGE:${v}`];

test("公開のステップ（本番）: push と何も指定しない手動実行だけ、公開と同時に MAINTENANCE_MODE:true", () => {
  const prod = { DEPLOY_ENV: "production" };
  assert.deepEqual(deployArgs({ ...prod }), ["deploy", "--env", "production", ...MODE("true")], "push（inputs は空）");
  assert.deepEqual(deployArgs({ ...prod, SKIP_MAINTENANCE_CHECK: "false" }), ["deploy", "--env", "production", ...MODE("true")]);
  assert.deepEqual(deployArgs({ ...prod, SKIP_MAINTENANCE_CHECK: "true" }), ["deploy", "--env", "production"], "確認を飛ばしたときは、メンテナンス画面にしない（変数も作らない）");
});

test("公開のステップ（本番）: mode を指定したら、その値（skip_maintenance_check にかかわらず）。message だけなら MAINTENANCE_MODE は渡さない", () => {
  const prod = { DEPLOY_ENV: "production" };
  for (const mode of ["true", "notice", "false"]) {
    assert.deepEqual(deployArgs({ ...prod, MAINTENANCE_MODE_INPUT: mode }), ["deploy", "--env", "production", ...MODE(mode)], mode);
  }
  assert.deepEqual(deployArgs({ ...prod, MAINTENANCE_MODE_INPUT: "true", SKIP_MAINTENANCE_CHECK: "true" }), ["deploy", "--env", "production", ...MODE("true")]);
  assert.deepEqual(deployArgs({ ...prod, MAINTENANCE_MODE_INPUT: "notice", MAINTENANCE_MESSAGE_INPUT: "20:00 ごろ" }), ["deploy", "--env", "production", ...MODE("notice"), ...MSG("20:00 ごろ")]);
  assert.deepEqual(deployArgs({ ...prod, MAINTENANCE_MESSAGE_INPUT: "文面だけ" }), ["deploy", "--env", "production", ...MSG("文面だけ")], "本番を勝手にメンテナンス画面にしない");
  assert.deepEqual(deployArgs({ ...prod, MAINTENANCE_MESSAGE_INPUT: "文面だけ", SKIP_MAINTENANCE_CHECK: "true" }), ["deploy", "--env", "production", ...MSG("文面だけ")]);
});

test("公開のステップ（確認用 dev）: 入力したものだけを渡す。push では何も渡さず、MAINTENANCE_MODE:true も付けない", () => {
  const dev = { DEPLOY_ENV: "dev" };
  assert.deepEqual(deployArgs({ ...dev }), ["deploy", "--env", "dev"], "push");
  assert.deepEqual(deployArgs({ ...dev, SKIP_MAINTENANCE_CHECK: "true" }), ["deploy", "--env", "dev"]);
  assert.deepEqual(deployArgs({ ...dev, MAINTENANCE_MODE_INPUT: "notice" }), ["deploy", "--env", "dev", ...MODE("notice")]);
  assert.deepEqual(deployArgs({ ...dev, MAINTENANCE_MODE_INPUT: "false", MAINTENANCE_MESSAGE_INPUT: "x" }), ["deploy", "--env", "dev", ...MODE("false"), ...MSG("x")]);
  assert.deepEqual(deployArgs({ ...dev, MAINTENANCE_MESSAGE_INPUT: "x" }), ["deploy", "--env", "dev", ...MSG("x")]);
});

test("公開のステップ: 文面に記号・空白・引用符・コマンド置換があっても、1 つの引数のまま（シェルに解釈させない）", () => {
  const text = "10:00 \"a\" 'b' $(touch pwned) `id` ; rm -rf / $HOME";
  assert.deepEqual(deployArgs({ DEPLOY_ENV: "dev", MAINTENANCE_MESSAGE_INPUT: text }), ["deploy", "--env", "dev", ...MSG(text)]);
});

test("公開のステップ・サマリー・環境名の式（site.yml）", () => {
  const yml = readYml();
  const deploy = stepOf(yml, "Deploy to Cloudflare Workers");
  assert.ok(deploy.includes("DEPLOY_ENV: ${{ github.ref_name == 'master' && 'production' || 'dev' }}"), "--env の式は従来どおり");
  assert.match(deploy, /MAINTENANCE_MODE_INPUT: \$\{\{ inputs\.maintenance_mode \}\}/);
  assert.match(deploy, /MAINTENANCE_MESSAGE_INPUT: \$\{\{ inputs\.maintenance_message \}\}/);
  assert.match(deploy, /SKIP_MAINTENANCE_CHECK: \$\{\{ inputs\.skip_maintenance_check \}\}/);
  assert.match(deploy, /working-directory: site/);
  assert.ok(!deploy.slice(deploy.indexOf("run: |"), deploy.indexOf("\n        env:")).includes("${{"), "入力を run に直接埋め込まない");
  const summary = stepOf(yml, "Summary (maintenance)");
  assert.match(summary, /if: \$\{\{ always\(\) \}\}/);
  assert.ok(summary.includes("WORKER_NAME: ${{ github.ref_name == 'master' && 'fara-twitch-bot' || 'fara-twitch-bot-dev' }}"));
  assert.ok(yml.indexOf("- name: Summary (maintenance)") > yml.indexOf("- name: Deploy to Cloudflare Workers"), "サマリーは公開の後");
  assert.match(read("../wrangler.toml"), /^keep_vars = true$/m, "公開で既存の変数を消さない");
  assert.match(read("../wrangler.toml"), /^run_worker_first = true$/m);
});

test("package.json は wrangler のバージョンを固定する（site.yml と同じ）", () => {
  const pkg = JSON.parse(read("../package.json"));
  assert.match(pkg.devDependencies.wrangler, /^\d+\.\d+\.\d+$/);
  assert.ok(readYml().includes(`npx wrangler@${pkg.devDependencies.wrangler} deploy`));
});
