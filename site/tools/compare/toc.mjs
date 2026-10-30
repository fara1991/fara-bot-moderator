import { chromium } from "playwright";
const b = await chromium.launch(); const p = await (await b.newContext({ viewport: { width: 1280, height: 800 } })).newPage();
await p.goto(process.argv[2] + "/docs/main-settings"); await p.waitForFunction(() => !document.getElementById("boot-indicator")); await p.waitForTimeout(500);
await p.waitForSelector(".doc-toc li a"); const n = await p.$$eval(".doc-toc li a", a => a.length);
await p.click(`.doc-toc li:nth-child(${n}) a`); await p.waitForTimeout(1200);
console.log(await p.evaluate(() => { const id = decodeURIComponent(location.hash.slice(1)); const el = document.getElementById(id); const c = document.querySelector(".fbc-content"); return { hash: location.hash, top: el?.getBoundingClientRect().top, containerTop: c.getBoundingClientRect().top, scrollTop: c.scrollTop }; }));
// direct load with hash
await p.goto(process.argv[2] + "/docs/main-settings" + (await p.evaluate(() => location.hash))); await p.waitForFunction(() => !document.getElementById("boot-indicator")); await p.waitForTimeout(1500);
console.log("direct:", await p.evaluate(() => { const el = document.getElementById(decodeURIComponent(location.hash.slice(1))); const c = document.querySelector(".fbc-content"); return { top: el?.getBoundingClientRect().top, containerTop: c.getBoundingClientRect().top, scrollTop: c.scrollTop }; }));
await b.close();
