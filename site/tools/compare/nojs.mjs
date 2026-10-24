import { chromium } from "playwright";
const [oldBase, newBase] = process.argv.slice(2);
const slugs = ["overview","main-settings","reaction-events","windows","translate","game","timer","commands","pokemon","pokemon-my","pokemon-team","pokemon-sync","pokemon-battle","pokemon-rival","pokemon-notes","beatsaber","faq","troubleshooting","changelog"];
const pages = ["/", "/docs", ...slugs.map(s => "/docs/" + s), "/changelog", "/privacy", "/report", "/no-such-page"];
const b = await chromium.launch(); const ctx = await b.newContext({ javaScriptEnabled: false, viewport: { width: 1280, height: 800 } }); const p = await ctx.newPage();
const norm = t => t.replace(/\s+/g, "");
let same = 0, diff = 0;
for (const path of pages) {
  await p.goto(oldBase + path); const o = await p.evaluate(() => { const r = document.querySelector("#main-content").cloneNode(true); r.querySelectorAll("footer,script,style,noscript").forEach(e => e.remove()); return r.innerText || r.textContent; });
  const r = await p.goto(newBase + path); const n = await p.evaluate(() => { const r = document.querySelector(".entry-static"); return r ? (r.innerText || r.textContent) : null; });
  const meta = await p.evaluate(() => ({ canonical: document.querySelector("link[rel=canonical]")?.href, desc: !!document.querySelector("meta[name=description]"), robots: document.querySelector("meta[name=robots]")?.content }));
  const eq = norm(o) === norm(n ?? "");
  if (eq) same++; else { diff++; console.log("DIFF", path, r.status(), JSON.stringify(norm(o).slice(0, 80)), "|", JSON.stringify(norm(n ?? "").slice(0, 80))); }
  if (path === "/docs/overview" || path === "/report") console.log(path, meta, r.status());
}
console.log({ same, diff });
await b.close();
