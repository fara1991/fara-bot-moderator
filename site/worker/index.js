// Cloudflare Worker。すべてのリクエストがまずここを通る（wrangler.toml の run_worker_first = true）。
// サイト本体（Blazor WebAssembly と、本文入りの静的 HTML。publish/wwwroot）は、静的アセットとしてそのまま返す。
// セキュリティ用のヘッダー（CSP など）は、静的アセットに付く _headers が唯一の情報源（fara-site-kit の csp/render-headers.mjs が書く。
// Blazor WebAssembly の 'wasm-unsafe-eval' と、問い合わせの Turnstile を全ページで許可する。README の「CSP」）。ここでは CSP を付けない。
// 共有の JS（報告の受け口・メンテナンス）は fara-site-kit（external/fara-site-kit）。
//   - /api/maintenance: Worker の変数 MAINTENANCE_MODE / MAINTENANCE_MESSAGE の状態（{state, message}）。アプリが起動時に読んで、お知らせの帯を出す（Razor の MaintenanceBanner）
//   - MAINTENANCE_MODE=true: /api/maintenance 以外は、すべて 503 のメンテナンス画面（検索エンジンには載せない）
//   - /api/report: 作者への報告（Turnstile → Discord）

import { handleReport } from "../../external/fara-site-kit/worker/report.js";
import { getMaintenance, maintenanceResponse } from "../../external/fara-site-kit/worker/maintenance.js";
import { errorSummary } from "../../external/fara-site-kit/worker/error-summary.js";

// 報告の 1 日の送信回数の制限（送信元ごと 3 回）の Durable Object。クラスを公開する（wrangler.toml の durable_objects と migrations が要る）
export { ReportRateLimiter } from "../../external/fara-site-kit/worker/report-rate-limit.js";

const SITE_NAME = "FaraBotModerator";

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
    if (maintenance.state === "on") return maintenanceResponse(request, maintenance, SITE_NAME);
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
