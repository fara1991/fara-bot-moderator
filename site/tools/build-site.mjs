// 紹介サイトを、公開できる形（site/publish/wwwroot）にまで作る。
//   node tools/build-site.mjs [--env production|develop]      （site/ で。npm run build:site）
// 1. Build（C#）: Manual.md・Content/*.json から、ブラウザが読む data/site.json と、build-pages 用の pages.json・site.json を作る
// 2. dotnet publish: Blazor WebAssembly（Web）の出力。index.html は、本文が空のテンプレート
// 3. build-pages（external/fara-blazor-component）: ページごとの本文入りの静的 HTML・404.html・robots.txt・sitemap.xml・_headers を publish/wwwroot に書く
// 書き出し前に publish と artifacts を消す（前回の古いファイルを残さない）。
import { spawnSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const siteDir = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2);
const envIndex = args.indexOf("--env");
const env = envIndex >= 0 ? args[envIndex + 1] : "production";
if (!["production", "develop"].includes(env)) {
    console.error(`--env は production か develop です: ${env}`);
    process.exit(2);
}

const publishDir = join(siteDir, "publish");
const artifactsDir = join(siteDir, "artifacts");
const pagesDir = join(artifactsDir, "pages");
const templateDir = join(artifactsDir, "template");   // テンプレートは、出力先（publish/wwwroot）とは別のフォルダに置く（build-pages の決まり）
const kitBuild = resolve(siteDir, "..", "external", "fara-blazor-component", "build", "build-pages.mjs");

function run(command, commandArgs) {
    console.log(`\n> ${command} ${commandArgs.join(" ")}`);
    const result = spawnSync(command, commandArgs, { cwd: siteDir, stdio: "inherit" });
    if (result.error || result.status !== 0) {
        console.error(`失敗しました: ${command} ${commandArgs[0] ?? ""}`);
        process.exit(result.status || 1);
    }
}

if (!existsSync(kitBuild)) {
    console.error("external/fara-blazor-component がありません（git submodule update --init external/fara-blazor-component）");
    process.exit(2);
}

rmSync(publishDir, { recursive: true, force: true });
rmSync(artifactsDir, { recursive: true, force: true });
mkdirSync(pagesDir, { recursive: true });
mkdirSync(templateDir, { recursive: true });

run("dotnet", ["run", "--project", "src/FaraBotModeratorSite.Build", "-c", "Release", "--", "--out", pagesDir]);
run("dotnet", ["publish", "src/FaraBotModeratorSite.Web", "-c", "Release", "-o", publishDir]);
cpSync(join(publishDir, "wwwroot", "index.html"), join(templateDir, "index.html"));
run("node", [kitBuild,
    "--site", join(pagesDir, "site.json"),
    "--pages", join(pagesDir, "pages.json"),
    "--template", join(templateDir, "index.html"),
    "--out", join(publishDir, "wwwroot"),
    "--env", env]);
// dotnet publish が作る事前圧縮のファイル（.br・.gz）は、公開物に含めない（Cloudflare が配信時に自分で圧縮する。含めると、アップロードが倍以上になる）
function walk(dir) {
    return readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(join(dir, e.name)) : [join(dir, e.name)]));
}
const wwwroot = join(publishDir, "wwwroot");
const all = walk(wwwroot);
const bytes = (files) => files.reduce((n, f) => n + statSync(f).size, 0);
const beforeBytes = bytes(all);
for (const f of all.filter((f) => /\.(br|gz)$/.test(f))) rmSync(f);
const after = walk(wwwroot);
console.log(`\n事前圧縮（.br・.gz）を除外: ${all.length} 件 ${(beforeBytes / 1048576).toFixed(1)} MB → ${after.length} 件 ${(bytes(after) / 1048576).toFixed(1)} MB`);
console.log(`\n完了:${join(publishDir, "wwwroot")}（${env}）`);
