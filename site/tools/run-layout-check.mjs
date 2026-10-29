// レイアウトの自動計測（kit の tools/layout-check）を、ビルド済みの静的出力（publish/wwwroot）に対して行う。公開はしない。
//   npm run layout-check      （site/ で。先に npm run build:site で publish/wwwroot を作る）
// 1. kit の serve-static で publish/wwwroot を、設定の baseURL（8880）で配る（止まったら、この中で閉じる）
// 2. kit の cli が layout-check.config.mjs を読んで計測する。終了コードは cli のまま（0 違反なし / 1 違反あり / 2 計測できなかった）
import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { startStaticServer } from "../../external/fara-blazor-component/tools/layout-check/serve-static.mjs";

const siteDir = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const wwwroot = join(siteDir, "publish", "wwwroot");
const cli = resolve(siteDir, "..", "external", "fara-blazor-component", "tools", "layout-check", "cli.mjs");
const PORT = 8880; // layout-check.config.mjs の baseURL と同じ

if (!existsSync(join(wwwroot, "index.html"))) {
    console.error("publish/wwwroot がありません。先に npm run build:site で作る（計測は、ビルド済みの静的出力に対して行う）");
    process.exit(2);
}
if (!existsSync(cli)) {
    console.error("external/fara-blazor-component がありません（git submodule update --init external/fara-blazor-component。kit の npm ci も必要）");
    process.exit(2);
}

let server;
try {
    server = await startStaticServer(wwwroot, PORT, { tries: 1 });
} catch (error) {
    console.error(`配信を始められなかった（ポート ${PORT} が使われている？）: ${error.message}`);
    process.exit(2);
}

// 非同期に走らせる（spawnSync だと、この中の配信が止まり、計測が読み込みで失敗する）
const code = await new Promise((resolveCode) => {
    const child = spawn(process.execPath, [cli, "--config", "layout-check.config.mjs"], { cwd: siteDir, stdio: "inherit" });
    child.on("error", () => resolveCode(2));
    child.on("close", (status) => resolveCode(status ?? 2));
});
await server.close();
process.exit(code);
