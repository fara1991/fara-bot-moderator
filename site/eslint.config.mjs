// ESLint（flat config）。共通のルールと除外は kit（external/fara-blazor-component/tools/lint）が持つ。ここはサイトだけの対象と環境を書く。
// 対象: site/ の JS（worker・tools・tests・画面の wwwroot/js）。kit・生成物（publish・artifacts・bin・obj）は共通の除外。
// 実行: npm run lint（site/ で）。docs: external/fara-blazor-component/docs/lint.md
import shared, { createSharedConfig } from "../external/fara-blazor-component/tools/lint/eslint.config.shared.mjs";

export default [
  // Node の JS（**/*.{js,mjs,cjs}）。tools・tests・ルートの設定ファイルなど
  ...shared,
  // Worker（worker/）は Node の環境に、Cloudflare の実行環境だけのグローバル（HTMLRewriter）を足す
  ...createSharedConfig({
    files: ["worker/**/*.js"],
    environments: ["node"],
    extraGlobals: { HTMLRewriter: "readonly" },
  }),
  // 画面の JS（index.html の <script> で読む。import も module もしない）
  ...createSharedConfig({
    files: ["src/FaraBotModeratorSite.Web/wwwroot/js/**/*.js"],
    environments: ["browser"],
    sourceType: "script",
  }),
  // 見た目の比較（tools/compare）は Node で動くが、page.evaluate に渡す関数はブラウザで動く（kit の layout-check の browser.mjs と同じ扱い）
  ...createSharedConfig({
    files: ["tools/compare/**/*.mjs"],
    environments: ["node", "browser"],
  }),
];
// 注: リポジトリ直下の .github/scripts（デプロイのゲート）は、この設定の基準パス（site/）の外なので、ESLint の対象にできない
// （外のファイルは「base path の外」として無視される）。そこは、CI の node --test（.github/scripts/*.test.mjs）で確かめる。
