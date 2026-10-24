// 使い方: node styles.mjs <baseUrl> <out.json> [scheme] [vw]
import { chromium } from "playwright";
import { writeFileSync } from "node:fs";
const [base, out, scheme = "dark", vw = "1280"] = process.argv.slice(2);
const props = ["display","position","boxSizing","maxWidth","marginTop","marginRight","marginBottom","marginLeft","paddingTop","paddingRight","paddingBottom","paddingLeft","fontSize","fontWeight","fontStyle","fontFamily","lineHeight","letterSpacing","color","backgroundColor","backgroundImage","borderTopWidth","borderTopStyle","borderTopColor","borderRightWidth","borderRightStyle","borderRightColor","borderBottomWidth","borderBottomStyle","borderBottomColor","borderLeftWidth","borderLeftStyle","borderLeftColor","borderTopLeftRadius","textDecorationLine","textAlign","whiteSpace","overflowWrap","wordBreak","verticalAlign","listStyleType","minHeight","height","flexDirection","alignItems","justifyContent","gap","gridTemplateColumns","textTransform","boxShadow","cursor","overflowX","opacity","textUnderlineOffset"];
const sets = {
  "/": [".hero",".hero-inner",".eyebrow",".hero h1",".hero .lead",".button-row",".hero .button",".hero .button.border",".hero-note",".page-section",".page-section h2",".card-grid",".card",".card h3",".card p",".card > a",".card.highlight",".card.highlight h3",".card.highlight code","pre.code-sample","pre.code-sample code","p code",".link-list",".link-list li",".link-list a",".steps",".steps li",".steps strong",".steps span",".changelog-recent",".changelog-recent li",".changelog-date","section.page-section > p"],
  "/docs": [".page-section h1",".page-section .lead",".page-section h2",".link-card",".link-card h3",".link-card p"],
  "/docs/pokemon-battle": [".doc-layout",".doc-main",".breadcrumb",".breadcrumb a",".doc-main h1",".doc-description",".prose",".prose h2",".prose h3",".prose h4",".prose p",".prose ul",".prose ol",".prose li",".prose li > ul",".prose code",".prose pre",".prose pre code",".prose a",".prose strong",".prose hr",".prose blockquote",".doc-toc",".doc-toc ul",".doc-toc li",".doc-toc a",".doc-toc .sub a",".doc-nav-title",".doc-pager",".doc-pager a",".doc-pager small",".pager-prev",".pager-next"],
  "/docs/beatsaber": [".table-wrap",".prose table",".prose th",".prose td",".prose tr:last-child td",".prose em",".prose img"],
  "/docs/commands": [".child-pages",".child-pages h2",".child-pages .card-grid"],
  "/changelog": [".page-section.narrow",".page-section h1",".lead",".changelog-entry",".changelog-entry .changelog-date",".changelog-entry h2",".changelog-entry ul",".changelog-entry li"],
  "/privacy": [".policy",".policy h1",".policy .lead",".policy .note",".policy-grid",".policy-item",".policy-item h2",".policy-item p",".policy-item ul",".policy-item li",".nowrap"],
};
const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: +vw, height: 900 }, colorScheme: scheme });
await ctx.addInitScript(([s]) => { try { localStorage.setItem("fara-bot-moderator-site.theme", s); } catch {} }, [scheme]);
const page = await ctx.newPage();
const result = {};
for (const [path, sels] of Object.entries(sets)) {
  await page.goto(base + path, { waitUntil: "load" });
  await page.waitForTimeout(1500);
  for (const sel of sels) {
    result[path + " :: " + sel] = await page.evaluate(([sel, props]) => {
      const el = document.querySelector(sel);
      if (!el) return null;
      const cs = getComputedStyle(el), o = {};
      for (const p of props) o[p] = cs[p];
      const r = el.getBoundingClientRect(); o._h = Math.round(r.height * 10) / 10; o._w = Math.round(r.width * 10) / 10;
      if (sel === ".steps li") { const b = getComputedStyle(el, "::before"); o["::before.bg"] = b.backgroundColor; o["::before.w"] = b.width; o["::before.color"] = b.color; }
      return o;
    }, [sel, props]);
  }
}
await browser.close();
writeFileSync(out, JSON.stringify(result, null, 1));
console.log("ok", Object.keys(result).length);
