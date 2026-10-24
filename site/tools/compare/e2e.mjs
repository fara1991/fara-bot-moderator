import { chromium } from "playwright";
const base = process.argv[2];
const b = await chromium.launch(); const ctx = await b.newContext({ viewport: { width: 1280, height: 800 }, colorScheme: "dark" }); const p = await ctx.newPage();
const bad = []; p.on("console", m => { if (["error", "warning"].includes(m.type())) bad.push(m.text()); }); p.on("pageerror", e => bad.push("pageerror " + e.message));
await p.goto(base + "/docs/main-settings"); await p.waitForFunction(() => !document.getElementById("boot-indicator")); await p.waitForTimeout(500);
// TOC click
const tocHref = await p.getAttribute(".doc-toc li:nth-child(2) a", "href"); await p.click(".doc-toc li:nth-child(2) a"); await p.waitForTimeout(800);
const id = decodeURIComponent(tocHref.split("#")[1]);
const pos = await p.evaluate((id) => { const el = document.getElementById(id); const r = el.getBoundingClientRect(); const c = document.querySelector(".fsk-content").getBoundingClientRect(); return { top: r.top, containerTop: c.top, url: location.href }; }, id);
console.log("toc jump", pos);
// search
await p.keyboard.press("Control+k"); await p.keyboard.type("komando"); await p.waitForTimeout(400);
console.log("search items:", await p.$$eval(".site-search-item", e => e.slice(0, 3).map(x => x.textContent.trim().slice(0, 60))));
await p.keyboard.press("ArrowDown"); await p.keyboard.press("Enter"); await p.waitForTimeout(800); console.log("after enter:", p.url());
// slash shortcut
await p.click("h1"); await p.keyboard.press("/"); console.log("focused search by slash:", await p.evaluate(() => document.activeElement?.className));
// theme toggle persists JSON
await p.click(".fsk-theme-switch"); await p.waitForTimeout(300); console.log("stored:", await p.evaluate(() => localStorage.getItem("fara-bot-moderator-site.theme")), "attr", await p.evaluate(() => document.documentElement.getAttribute("data-bs-theme")));
// footer report link carries from
await p.click(".fsk-footer a[href*='report']"); await p.waitForTimeout(1500); console.log("report url:", p.url(), "| h3:", await p.textContent(".fsk-page-header h3"));
// sidebar toggle persists key
await p.click(".fsk-menu-toggle"); await p.waitForTimeout(300); console.log("sidebar key:", await p.evaluate(() => localStorage.getItem("fara-bot-moderator-site.sidebar-hidden")));
console.log("problems:", bad);
await b.close();
