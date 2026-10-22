// BeerCSS（配布の CSS だけ）を wwwroot/css/vendor/ に取り込み、CSP のためのパッチを当てる。
// 使い方（site/ で）: node tools/vendor-beercss.mjs
//
// 手順（再現用。npm の配布物をそのまま使い、版と入力の md5 を固定する）
//   1. npm pack beercss@5.0.3 で配布物を一時フォルダに取り、tar で展開する
//   2. dist/cdn/beer.min.css を読む。md5 が INPUT_MD5 と違えば止める（版が変わった、または配布物が違う）
//   3. パッチ: @font-face の 4 つを取り除く。
//      - 2 番目の src が jsdelivr の絶対 URL で、font-src 'self' の CSP に違反する（読み込み時に 4 件）
//      - このサイトはアイコンフォントを使わない（アイコンはインライン SVG）ので、フォント自体を読ませない
//   4. パッチ後に、http(s) の URL・@font-face・HTML のタグが残っていないことを確かめる
//   5. wwwroot/css/vendor/beer.min.css と LICENSE-beercss.txt を書く。出力の md5 を表示する（vendor/README.md に書く値）
//
// beer.min.js は取り込まない（DOM を書き換える処理は、サイトの JS（site.js・nav.js）と合わないため）。
import { exec, execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtemp, readFile, writeFile, copyFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const execAsync = promisify(exec);
const here = dirname(fileURLToPath(import.meta.url));
const siteRoot = resolve(here, "..");
const outDir = join(siteRoot, "wwwroot", "css", "vendor");

const VERSION = "5.0.3";
const INPUT_MD5 = "74789a58e5b1f4af51550f165b115d04";
const OUTPUT_MD5 = "1e1565b430d311c00c828f8d0b494142";   // パッチ後の md5（vendor/README.md と同じ値）
const md5 = (buf) => createHash("md5").update(buf).digest("hex");

const tmp = await mkdtemp(join(tmpdir(), "beercss-"));
try {
  // 展開は、ファイル名だけを渡し、作業フォルダの中で行う（Windows の「C:」を tar が host と誤認しないように）
  const { stdout } = await execAsync(`npm pack beercss@${VERSION} --pack-destination "${tmp}"`);
  const name = stdout.trim().split(/\r?\n/).pop();
  await execFileAsync("tar", ["-xzf", name, "-C", "."], { cwd: tmp });
  const pkg = join(tmp, "package");

  const input = await readFile(join(pkg, "dist", "cdn", "beer.min.css"));
  if (md5(input) !== INPUT_MD5) throw new Error(`入力の md5 が違います: ${md5(input)}（期待: ${INPUT_MD5}）`);

  const fontFace = /@font-face\{[^}]*\}/g;
  const found = (input.toString("utf8").match(fontFace) || []).length;
  if (found !== 4) throw new Error(`@font-face の数が想定と違います: ${found}`);
  const css = input.toString("utf8").replace(fontFace, "");

  if (/@font-face/.test(css)) throw new Error("@font-face が残っています");
  if (/https?:\/\//.test(css)) throw new Error("外部の URL が残っています");
  if (/<\/?script|<style/i.test(css)) throw new Error("HTML のタグが入っています");

  await writeFile(join(outDir, "beer.min.css"), css, "utf8");
  await copyFile(join(pkg, "LICENSE"), join(outDir, "LICENSE-beercss.txt"));
  const out = await readFile(join(outDir, "beer.min.css"));
  console.log(`beercss ${VERSION}: input md5 ${md5(input)}, output md5 ${md5(out)} (${out.length} bytes)`);
  if (OUTPUT_MD5 && md5(out) !== OUTPUT_MD5) throw new Error("出力の md5 が固定値と違います");
} finally {
  await rm(tmp, { recursive: true, force: true });
}
