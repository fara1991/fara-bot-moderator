// 使い方: node stylediff.mjs old.json new.json
import { readFileSync } from "node:fs";
const [a, b] = process.argv.slice(2).map(f => JSON.parse(readFileSync(f, "utf8")));
const skip = new Set(["_w", "fontFamily", "position", "borderTopStyle", "borderRightStyle", "borderBottomStyle", "borderLeftStyle", "verticalAlign", "height"]);
let n = 0;
for (const k of Object.keys(a)) {
  const x = a[k], y = b[k];
  if (!x) continue;
  if (!y) { console.log("MISSING in new:", k); continue; }
  const diffs = [];
  for (const p of Object.keys(x)) { if (skip.has(p)) continue; if (x[p] !== y[p]) diffs.push(`${p}: ${x[p]} -> ${y[p]}`); }
  if (diffs.length) { n++; console.log(k + "\n   " + diffs.join("\n   ")); }
}
console.log("elements with diffs:", n);
