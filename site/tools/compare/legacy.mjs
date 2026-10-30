import { chromium } from "playwright";
const b = await chromium.launch();
for (const [pref, stored] of [["light", "dark"], ["dark", "light"], ["light", null], ["dark", null]]) {
  const ctx = await b.newContext({ colorScheme: pref }); const p = await ctx.newPage();
  await p.addInitScript(([s]) => { if (!sessionStorage.getItem("seeded")) { sessionStorage.setItem("seeded", "1"); if (s) localStorage.setItem("fara-bot-moderator-site.theme", s); localStorage.setItem("fara-bot-moderator-site.sidebar-hidden", "1"); } }, [stored]);
  const warns = []; p.on("console", m => { if (m.text().includes("[fara-blazor-component]")) warns.push(m.text()); });
  await p.goto(process.argv[2] + "/privacy"); await p.waitForFunction(() => !document.getElementById("boot-indicator")); await p.waitForTimeout(500);
  console.log(`os=${pref} saved=${stored}:`, await p.evaluate(() => ({ attr: document.documentElement.getAttribute("data-bs-theme"), stored: localStorage.getItem("fara-bot-moderator-site.theme"), sidebarHidden: document.documentElement.classList.contains("fbc-sidebar-hidden") })), warns);
  await ctx.close();
}
await b.close();
