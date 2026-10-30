// Cloudflare Worker。すべてのリクエストがまずここを通る（wrangler.toml の run_worker_first = true）。
// サイト本体（Blazor WebAssembly と、本文入りの静的 HTML。publish/wwwroot）は、静的アセットとしてそのまま返す。
// セキュリティ用のヘッダー（CSP など）は、静的アセットに付く _headers が唯一の情報源（fara-blazor-component の csp/render-headers.mjs が書く。
// Blazor WebAssembly の 'wasm-unsafe-eval' と、問い合わせの Turnstile を全ページで許可する。README の「CSP」）。ここでは CSP を付けない。
// 共有の JS（報告の受け口・メンテナンス）は fara-blazor-component（external/fara-blazor-component）。
//   - /api/maintenance: Worker の変数 MAINTENANCE_MODE / MAINTENANCE_MESSAGE の状態（{state, message}）。アプリが起動時に読んで、お知らせの帯を出す（Razor の MaintenanceBanner）
//   - MAINTENANCE_MODE=true: /api/maintenance 以外は、すべて 503 のメンテナンス画面（検索エンジンには載せない）
//   - /api/report: 作者への報告（Turnstile → Discord）

import { handleReport } from "../../external/fara-blazor-component/worker/report.js";
import { getMaintenance, maintenanceResponse } from "../../external/fara-blazor-component/worker/maintenance.js";
import { errorSummary } from "../../external/fara-blazor-component/worker/error-summary.js";

// 報告の 1 日の送信回数の制限（送信元ごと 3 回）の Durable Object。クラスを公開する（wrangler.toml の durable_objects と migrations が要る）
export { ReportRateLimiter } from "../../external/fara-blazor-component/worker/report-rate-limit.js";

const SITE_NAME = "FaraBotModerator";

/**
 * メンテナンス画面（kit の HTML。色が紫で決め打ち）を、このサイトの色（site.css の「テーマ」の節。インディゴ〜ブルー）に合わせる。
 * HTML のときだけ、色の文字列を置き換える（JSON・短い文字の応答はそのまま）。kit の HTML の色が変わると、置き換えは何もしないだけ（画面は kit の色のまま）。
 */
const MAINTENANCE_COLORS = [["#0c0616", "#090d1c"], ["#f3edff", "#eaeefc"], ["#b39ddb", "#9fb0ff"], ["#b4a7d1", "#aab4d6"], ["#7c3aed", "#4a5ee0"]];
export async function themedMaintenanceResponse(res) {
  if (!(res.headers.get("Content-Type") ?? "").includes("text/html")) return res;
  let html = await res.text();
  for (const [from, to] of MAINTENANCE_COLORS) html = html.split(from).join(to);
  return new Response(html, { status: res.status, headers: res.headers });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const maintenance = getMaintenance(env);
    // メンテナンスの状態。メンテナンス中でも返す（アプリが画面を切り替えられるように）
    if (url.pathname === "/api/maintenance") {
      return new Response(JSON.stringify({ state: maintenance.state, message: maintenance.message }), {
        headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" },
      });
    }
    // メンテナンス中は、すべて 503 のメンテナンス画面
    if (maintenance.state === "on") return themedMaintenanceResponse(maintenanceResponse(request, maintenance, SITE_NAME));
    // 作者への報告の受け口（kit の worker/report.js）
    if (url.pathname === "/api/report" || url.pathname === "/api/report/config") {
      let res;
      try {
        res = await handleReport(request, env, SITE_NAME);
      } catch (e) {
        // 利用者には中身を出さず、原因は Worker のログ（wrangler tail）に残す
        console.error(`報告の受け口で例外: ${errorSummary(e)}`);
        res = new Response("internal error", { status: 500 });
      }
      res.headers.set("X-Content-Type-Options", "nosniff");
      return res;
    }
    // お知らせ（MAINTENANCE_MODE=notice）は、ここでは HTML に差し込まない（アプリの MaintenanceBanner が /api/maintenance を読んで出す。二重に出さない）
    const res = await env.ASSETS.fetch(request);
    const out = new Response(res.body, res);
    out.headers.set("X-Content-Type-Options", "nosniff");
    out.headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
    return out;
  },
};
