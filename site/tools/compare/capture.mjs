// 使い方: node capture.mjs <baseUrl> <outDir> <old|new> [pageFilter]
import { chromium } from "playwright";
import { mkdirSync, writeFileSync } from "node:fs";
const [base, out, kind, filter] = process.argv.slice(2);
const slugs = ["overview","main-settings","reaction-events","windows","translate","game","timer","commands","pokemon","beatsaber","faq","troubleshooting","changelog"];
let pages = ["/", "/docs", ...slugs.map(s => "/docs/" + s), "/changelog", "/privacy", "/report", "/no-such-page", "/docs/no-such"];
if (filter) pages = pages.filter(p => p.includes(filter));
const viewports = { pc1280: [1280, 800], pc1920: [1920, 1080], sp390: [390, 844] };
mkdirSync(out, { recursive: true });
const browser = await chromium.launch();
const result = {};
for (const [vn, [w, h]] of Object.entries(viewports)) {
  for (const scheme of ["light", "dark"]) {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, colorScheme: scheme, deviceScaleFactor: 1 });
    await ctx.addInitScript(([k, s]) => {
      try {
        if (k === "old") localStorage.setItem("fara-bot-moderator-site.theme", s);
        else localStorage.setItem("fara-bot-moderator-site.theme", JSON.stringify({ Theme: s }));
      } catch { /* 保存できないとき（プライベート閲覧など）は、そのまま */ }
    }, [kind, scheme]);
    const page = await ctx.newPage();
    const problems = [];
    page.on("console", m => { if (m.type() === "error" || m.type() === "warning") problems.push(`${m.type()}: ${m.text()}`); });
    page.on("pageerror", e => problems.push("pageerror: " + e.message));
    for (const p of pages) {
      const key = `${vn}-${scheme}-${p === "/" ? "home" : p.slice(1).replace(/\//g, "_")}`;
      const resp = await page.goto(base + p, { waitUntil: "load" });
      // 新しい側は WASM の起動を待つ
      if (kind === "new") { await page.waitForSelector("#fbc-main .fbc-content, .fbc-maintenance", { timeout: 60000 }).catch(() => {}); await page.waitForFunction(() => !document.getElementById("boot-indicator"), null, { timeout: 30000 }).catch(() => {}); }
      await page.waitForTimeout(400);
      await page.screenshot({ path: `${out}/${key}.png` });
      const r = await page.evaluate((kind) => {
        const root = document.querySelector(kind === "old" ? "#main-content" : "#fbc-main .fbc-content") ;
        const clone = root ? root.cloneNode(true) : null;
        if (clone) clone.querySelectorAll("footer, .maintenance-notice, .fbc-notice, script, style").forEach(e => e.remove());
        const sidebar = document.querySelector(kind === "old" ? ".sidebar" : "#fbc-sidebar");
        return {
          title: document.title,
          text: clone ? clone.innerText.replace(/[ \t ]+/g, " ").replace(/\s*\n\s*/g, "\n").trim() : null,
          scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth,
          nav: sidebar ? [...sidebar.querySelectorAll("a")].map(a => a.textContent.trim().replace(/\s+/g, " ") + "|" + a.getAttribute("href")) : [],
        };
      }, kind);
      result[key] = { status: resp?.status(), ...r };
    }
    result[`_problems-${vn}-${scheme}`] = problems;
    await ctx.close();
  }
}
await browser.close();
writeFileSync(`${out}/result.json`, JSON.stringify(result, null, 1));
console.log("done", Object.keys(result).length);
