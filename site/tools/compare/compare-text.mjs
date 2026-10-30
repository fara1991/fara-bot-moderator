// 使い方: node compare-text.mjs old/result.json new/result.json [viewport-scheme-prefix]
import { readFileSync } from "node:fs";
const [a, b, prefix = "pc1280-dark"] = process.argv.slice(2);
const A = JSON.parse(readFileSync(a, "utf8")), B = JSON.parse(readFileSync(b, "utf8"));
const norm = t => (t ?? "").replace(/\s+/g, " ").trim();
let same = 0, diff = 0;
for (const k of Object.keys(A)) {
  if (!k.startsWith(prefix + "-")) continue;
  const x = A[k], y = B[k];
  if (!y) { console.log("MISSING", k); continue; }
  const eq = norm(x.text) === norm(y.text);
  const titleEq = x.title === y.title;
  if (eq && titleEq && x.status === y.status) { same++; continue; }
  diff++;
  console.log(`DIFF ${k}: status ${x.status}->${y.status} title ${titleEq ? "same" : JSON.stringify(x.title) + " -> " + JSON.stringify(y.title)} text ${eq ? "same" : "differs"}`);
  if (!eq) {
    const xs = norm(x.text), ys = norm(y.text); let i = 0; while (i < xs.length && xs[i] === ys[i]) i++;
    console.log("   old:", JSON.stringify(xs.slice(Math.max(0, i - 40), i + 80)));
    console.log("   new:", JSON.stringify(ys.slice(Math.max(0, i - 40), i + 80)));
  }
}
console.log({ same, diff });
