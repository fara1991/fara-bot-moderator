// 紹介サイトを、公開できる形（site/publish/wwwroot）にまで作る。
//   node tools/build-site.mjs [--env production|develop]      （site/ で。npm run build:site）
// 1. Build（C#）: Manual.md・Content/*.json から、ブラウザが読む data/site.json と、build-pages 用の pages.json・site.json を作る
// 2. dotnet publish: Blazor WebAssembly（Web）の出力。index.html は、本文が空のテンプレート
// 3. build-pages（external/fara-site-kit）: ページごとの本文入りの静的 HTML・404.html・robots.txt・sitemap.xml・_headers を publish/wwwroot に書く
// 書き出し前に publish と artifacts を消す（前回の古いファイルを残さない）。
import { spawnSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync, rmSync } from "node:fs";
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
const kitBuild = resolve(siteDir, "..", "external", "fara-site-kit", "build", "build-pages.mjs");

function run(command, commandArgs) {
    console.log(`\n> ${command} ${commandArgs.join(" ")}`);
    const result = spawnSync(command, commandArgs, { cwd: siteDir, stdio: "inherit" });
    if (result.error || result.status !== 0) {
        console.error(`失敗しました: ${command} ${commandArgs[0] ?? ""}`);
        process.exit(result.status || 1);
    }
}

if (!existsSync(kitBuild)) {
    console.error("external/fara-site-kit がありません（git submodule update --init external/fara-site-kit）");
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
console.log(`\n完了: ${join(publishDir, "wwwroot")}（${env}）`);
