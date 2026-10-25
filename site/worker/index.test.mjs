// worker/index.js のテスト。  node --test worker/index.test.mjs（site/ で）
// /api/maintenance・メンテナンス画面（503）・/api/report の振り分け・CSP を Worker から付けないこと・公開の設定を確かめる。
// 報告の中身（検証・Turnstile・Discord）とメンテナンスの部品は、kit のテスト（external/fara-site-kit/worker/*.test.mjs）が確かめる。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
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
  const dev = read("../../.github/workflows/deploy-develop.yml");
  const step = dev.indexOf("Add noindex header (develop)");
  assert.ok(step > 0);
  const next = dev.indexOf("- name:", step + 1);
  assert.match(dev.slice(step, next > 0 ? next : undefined), /X-Robots-Tag: noindex, nofollow/);
  // 本番のワークフローには、noindex を付けるステップが無い
  assert.ok(!read("../../.github/workflows/deploy.yml").includes("X-Robots-Tag"));
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

/** ワークフローを読む（Windows の CRLF は LF にそろえる）。 */
const readWf = (name) => read(`../../.github/workflows/${name}`).replace(/\r\n/g, "\n");
const readBuild = () => readWf("build.yml");
const readDev = () => readWf("deploy-develop.yml");
const readProd = () => readWf("deploy.yml");
/** 「- name: <name>」のステップ全体（次のステップの手前まで）。 */
const stepOf = (yml, name) => {
  const start = yml.indexOf(`- name: ${name}\n`);
  assert.ok(start > 0, `${name} のステップがありません`);
  const next = yml.indexOf("\n      - name:", start + 1);
  const nextUses = yml.indexOf("\n      - uses:", start + 1);
  const ends = [next, nextUses].filter((n) => n > 0);
  return yml.slice(start, ends.length ? Math.min(...ends) : undefined);
};

test("ワークフローは Pokémon と同じ形（build.yml / deploy-develop.yml / deploy.yml）。アプリの ci.yml・release.yml は別。成果物は使わない", () => {
  const files = readdirSync(new URL("../../.github/workflows/", import.meta.url)).sort();
  assert.deepEqual(files, ["build.yml", "ci.yml", "deploy-develop.yml", "deploy.yml", "release.yml"]);
  for (const yml of [readBuild(), readDev(), readProd()]) {
    assert.ok(!yml.includes("upload-artifact") && !yml.includes("download-artifact"), "成果物は保存も取り出しもしない");
    assert.match(yml, /permissions:\n {2}contents: read/);
    assert.match(yml, /submodules: true/, "kit の submodule を取得する");
    assert.match(yml, /external\/fara-site-kit/, "kit を上げたときも動く（paths）");
  }
});

test("動くとき: build は push と PR、確認用は develop、本番は master（どれも site/ などを変えたとき）。公開のジョブはブランチで絞る", () => {
  const build = readBuild(), dev = readDev(), prod = readProd();
  assert.match(build, /on:\n {2}push:\n {4}branches-ignore: \[gh-pages\]\n {4}paths:\n(?: {6}- .*\n)+ {2}pull_request:\n {4}paths:\n(?: {6}- .*\n)+ {2}workflow_dispatch:\n/);
  assert.match(dev, /on:\n {2}push:\n {4}branches: \[develop\]\n {4}paths:\n/);
  assert.match(prod, /on:\n {2}push:\n {4}branches: \[master\]\n {4}paths:\n/);
  for (const [name, yml] of [["build.yml", build], ["deploy-develop.yml", dev], ["deploy.yml", prod]]) assert.ok(yml.includes(`- ".github/workflows/${name}"`), `${name} 自身を paths に入れる`);
  assert.ok(!dev.includes("pull_request") && !prod.includes("pull_request"));
  assert.ok(dev.includes("\n    if: github.ref == 'refs/heads/develop'\n"));
  assert.ok(prod.includes("\n    if: github.ref == 'refs/heads/master'\n"));
  assert.ok(dev.includes("environment:\n      name: develop\n      url: https://twitch-bot-dev.fara-labs.com\n"));
  assert.ok(prod.includes("environment:\n      name: production\n      url: https://twitch-bot.fara-labs.com\n"));
  // build は公開しない（Cloudflare の鍵も environment も使わない）
  assert.ok(!build.includes("secrets.") && !build.includes("environment:") && !/wrangler@[\d.]+ deploy/.test(build));
});

test("build: テストと、本番用・確認用の両方の書き出し", () => {
  const build = readBuild();
  assert.match(build, /require-maintenance\.test\.mjs/, "CI で確認のスクリプトもテストする");
  assert.match(build, /node --test worker\/index\.test\.mjs/);
  assert.ok(build.indexOf("- name: Test\n") < build.indexOf("- name: Build site (production)\n"));
  assert.match(stepOf(build, "Build site (production)"), /build-site\.mjs --env production\n/);
  assert.match(stepOf(build, "Build site (develop)"), /build-site\.mjs --env develop\n/);
});

test("公開の 2 つは、その環境だけを書き出す。確認用は noindex → 公開 → サマリー、本番は確認（ゲート）→ 公開 → サマリー", () => {
  const dev = readDev(), prod = readProd();
  assert.match(stepOf(dev, "Build site (develop)"), /build-site\.mjs --env develop\n/);
  assert.ok(!dev.includes("--env production"));
  assert.match(stepOf(prod, "Build site (production)"), /build-site\.mjs --env production\n/);
  assert.ok(!prod.includes("--env develop") && !prod.includes("--env dev"));
  const order = (yml, names) => {
    const at = names.map((n) => yml.indexOf(`- name: ${n}\n`));
    assert.ok(at.every((i) => i > 0), names.join(", "));
    assert.deepEqual([...at].sort((x, y) => x - y), at);
  };
  order(dev, ["Build site (develop)", "Add noindex header (develop)", "Deploy to Cloudflare Workers (develop)", "Summary (maintenance)"]);
  order(prod, ["Build site (production)", "Require MAINTENANCE_MODE=notice (production)", "Deploy to Cloudflare Workers", "Summary (maintenance)"]);
});

test("本番の公開は MAINTENANCE_MODE=notice を確かめてから行う。mode を指定した手動実行は確認を飛ばす。確認用は確かめない", () => {
  const prod = readProd();
  const gate = stepOf(prod, "Require MAINTENANCE_MODE=notice (production)");
  assert.match(gate, /node \.github\/scripts\/require-maintenance\.mjs fara-twitch-bot/);
  assert.ok(gate.includes(`if: \${{ !contains(fromJSON('["true","notice","false"]'), inputs.maintenance_mode) }}`), "mode 指定のときは確かめない");
  assert.match(gate, /SKIP_MAINTENANCE_CHECK: \$\{\{ inputs\.skip_maintenance_check \}\}/);
  assert.ok(!readDev().includes("require-maintenance.mjs"));
  for (const yml of [readDev(), prod]) assert.ok(!/MAINTENANCE(?![_A-Z])/.test(yml.replace(/SKIP_MAINTENANCE_CHECK/g, "")), "旧名 MAINTENANCE は使わない");
});

test("手動実行の入力: maintenance_mode（choice）・maintenance_message（string）。本番は skip_maintenance_check も", () => {
  for (const yml of [readDev(), readProd()]) {
    const inputs = yml.slice(yml.indexOf("  workflow_dispatch:"), yml.indexOf("permissions:"));
    assert.match(inputs, /maintenance_mode:\n(?:.*\n)*?\s+type: choice\n\s+options: \["", "true", "notice", "false"\]\n\s+default: ""/);
    assert.match(inputs, /maintenance_message:\n(?:.*\n)*?\s+type: string\n\s+default: ""/);
  }
  assert.match(readProd(), /skip_maintenance_check:\n(?:.*\n)*?\s+type: boolean/);
});

// 公開のステップの run を、実際に bash で動かして、wrangler に渡る引数を確かめる（wrangler の呼び出しを、引数を 1 行ずつ出すものに置き換える）
const runDeploy = (yml, stepName, envVars) => {
  const step = stepOf(yml, stepName);
  const body = step.slice(step.indexOf("run: |\n") + "run: |\n".length, step.indexOf("\n        env:"));
  const script = body.split("\n").map((l) => l.replace(/^ {10}/, "")).join("\n");
  const fake = script.replace(/npx wrangler@4\.149\.0 deploy/, 'printf "%s\\n" deploy');
  assert.notEqual(fake, script, "wrangler の呼び出しがありません");
  const r = spawnSync("bash", ["-c", fake], {
    env: { PATH: process.env.PATH, MAINTENANCE_MODE_INPUT: "", MAINTENANCE_MESSAGE_INPUT: "", SKIP_MAINTENANCE_CHECK: "", ...envVars },
    encoding: "utf8",
  });
  return { status: r.status, stderr: r.stderr, args: r.stdout.split("\n").filter((l) => l !== "" && !l.startsWith("::")) };
};
const prodArgs = (envVars) => {
  const r = runDeploy(readProd(), "Deploy to Cloudflare Workers", { GITHUB_REF: "refs/heads/master", ...envVars });
  assert.equal(r.status, 0, r.stderr);
  return r.args;
};
const devArgs = (envVars) => {
  const r = runDeploy(readDev(), "Deploy to Cloudflare Workers (develop)", { GITHUB_REF: "refs/heads/develop", ...envVars });
  assert.equal(r.status, 0, r.stderr);
  return r.args;
};
const MODE = (v) => ["--var", `MAINTENANCE_MODE:${v}`];
const MSG = (v) => ["--var", `MAINTENANCE_MESSAGE:${v}`];
const PROD = ["deploy", "--env", "production"];
const DEV = ["deploy", "--env", "dev"];

test("公開のステップ（本番）: push と何も指定しない手動実行だけ、公開と同時に MAINTENANCE_MODE:true", () => {
  assert.deepEqual(prodArgs({}), [...PROD, ...MODE("true")], "push（inputs は空）");
  assert.deepEqual(prodArgs({ SKIP_MAINTENANCE_CHECK: "false" }), [...PROD, ...MODE("true")]);
  assert.deepEqual(prodArgs({ SKIP_MAINTENANCE_CHECK: "true" }), PROD, "確認を飛ばしたときは、メンテナンス画面にしない（変数も作らない）");
});

test("公開のステップ（本番）: mode を指定したら、その値（skip_maintenance_check にかかわらず）。message だけなら MAINTENANCE_MODE は渡さない", () => {
  for (const mode of ["true", "notice", "false"]) assert.deepEqual(prodArgs({ MAINTENANCE_MODE_INPUT: mode }), [...PROD, ...MODE(mode)], mode);
  assert.deepEqual(prodArgs({ MAINTENANCE_MODE_INPUT: "true", SKIP_MAINTENANCE_CHECK: "true" }), [...PROD, ...MODE("true")]);
  assert.deepEqual(prodArgs({ MAINTENANCE_MODE_INPUT: "notice", MAINTENANCE_MESSAGE_INPUT: "20:00 ごろ" }), [...PROD, ...MODE("notice"), ...MSG("20:00 ごろ")]);
  assert.deepEqual(prodArgs({ MAINTENANCE_MESSAGE_INPUT: "文面だけ" }), [...PROD, ...MSG("文面だけ")], "本番を勝手にメンテナンス画面にしない");
  assert.deepEqual(prodArgs({ MAINTENANCE_MESSAGE_INPUT: "文面だけ", SKIP_MAINTENANCE_CHECK: "true" }), [...PROD, ...MSG("文面だけ")]);
});

test("公開のステップ（確認用 dev）: 入力したものだけを渡す。push では何も渡さず、MAINTENANCE_MODE:true も付けない", () => {
  assert.deepEqual(devArgs({}), DEV, "push");
  assert.deepEqual(devArgs({ MAINTENANCE_MODE_INPUT: "notice" }), [...DEV, ...MODE("notice")]);
  assert.deepEqual(devArgs({ MAINTENANCE_MODE_INPUT: "false", MAINTENANCE_MESSAGE_INPUT: "x" }), [...DEV, ...MODE("false"), ...MSG("x")]);
  assert.deepEqual(devArgs({ MAINTENANCE_MESSAGE_INPUT: "x" }), [...DEV, ...MSG("x")]);
});

test("公開のステップ: 文面に記号・空白・引用符・コマンド置換があっても、1 つの引数のまま（シェルに解釈させない）", () => {
  const text = "10:00 \"a\" 'b' $(touch pwned) `id` ; rm -rf / $HOME";
  assert.deepEqual(devArgs({ MAINTENANCE_MESSAGE_INPUT: text }), [...DEV, ...MSG(text)]);
  assert.deepEqual(prodArgs({ MAINTENANCE_MESSAGE_INPUT: text }), [...PROD, ...MSG(text)]);
});

test("公開のステップ: ブランチの取り違えは、wrangler を呼ぶ前に止まる（本番は master だけ、確認用は develop だけ）", () => {
  for (const ref of ["refs/heads/develop", "refs/heads/feature/x", ""]) {
    const r = runDeploy(readProd(), "Deploy to Cloudflare Workers", { GITHUB_REF: ref });
    assert.notEqual(r.status, 0, ref);
    assert.deepEqual(r.args, []);
  }
  for (const ref of ["refs/heads/master", "refs/heads/feature/x", ""]) {
    const r = runDeploy(readDev(), "Deploy to Cloudflare Workers (develop)", { GITHUB_REF: ref });
    assert.notEqual(r.status, 0, ref);
    assert.deepEqual(r.args, []);
  }
});

test("concurrency: 本番は実行を取り消さず待たせる。確認用は新しい実行が古いものを取り消す", () => {
  assert.match(readProd(), /\nconcurrency:\n {2}group: deploy\n(?: {2}#.*\n)? {2}cancel-in-progress: false\n/);
  assert.match(readDev(), /\nconcurrency:\n {2}group: deploy-develop\n {2}cancel-in-progress: true\n/);
});

test("公開のステップ・サマリー（入力は環境変数で受ける・サマリーは always()・Worker 名）", () => {
  for (const [yml, step, worker] of [[readDev(), "Deploy to Cloudflare Workers (develop)", "fara-twitch-bot-dev"], [readProd(), "Deploy to Cloudflare Workers", "fara-twitch-bot"]]) {
    const deploy = stepOf(yml, step);
    assert.match(deploy, /MAINTENANCE_MODE_INPUT: \$\{\{ inputs\.maintenance_mode \}\}/);
    assert.match(deploy, /MAINTENANCE_MESSAGE_INPUT: \$\{\{ inputs\.maintenance_message \}\}/);
    assert.match(deploy, /working-directory: site/);
    assert.ok(!deploy.slice(deploy.indexOf("run: |"), deploy.indexOf("\n        env:")).includes("${{"), "入力を run に直接埋め込まない");
    const summary = stepOf(yml, "Summary (maintenance)");
    assert.match(summary, /if: \$\{\{ always\(\) \}\}/);
    assert.ok(summary.includes(`WORKER_NAME: ${worker}\n`), worker);
  }
  assert.match(stepOf(readProd(), "Deploy to Cloudflare Workers"), /SKIP_MAINTENANCE_CHECK: \$\{\{ inputs\.skip_maintenance_check \}\}/);
  assert.match(read("../wrangler.toml"), /^keep_vars = true$/m, "公開で既存の変数を消さない");
  assert.match(read("../wrangler.toml"), /^run_worker_first = true$/m);
});

test("package.json は wrangler のバージョンを固定する（公開のワークフローと同じ）", () => {
  const pkg = JSON.parse(read("../package.json"));
  assert.match(pkg.devDependencies.wrangler, /^\d+\.\d+\.\d+$/);
  for (const yml of [readDev(), readProd()]) assert.ok(yml.includes(`npx wrangler@${pkg.devDependencies.wrangler} deploy`));
});
