// Cloudflare Worker。すべてのリクエストがまずここを通る（wrangler.toml の run_worker_first = true）。
// 静的ファイル（dist/）をそのまま返し、セキュリティ用のヘッダーを付ける。
// 確認用の noindex は、公開のワークフローが dist/_headers に付ける。

import { handleReport } from "./report.js";
import { getMaintenance, maintenanceResponse, withNotice } from "./maintenance.js";

// 外部のスクリプト・スタイル・フォントは使っていないので、すべて同じオリジンだけを許可する。
const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "img-src 'self' data:",
  "style-src 'self'",
  "script-src 'self'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join("; ");

// 問い合わせのページ（/report）だけ、ボット判定（Cloudflare Turnstile）のために challenges.cloudflare.com を許可する。
const REPORT_PAGE_CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "img-src 'self' data:",
  "style-src 'self'",
  "script-src 'self' https://challenges.cloudflare.com",
  "frame-src https://challenges.cloudflare.com",
  "connect-src 'self' https://challenges.cloudflare.com",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join("; ");

/** /report と /report/（末尾のスラッシュ）。auto-trailing-slash で report.html を返すパス。 */
export const isReportPage = (pathname) => pathname === "/report" || pathname === "/report/";

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    // メンテナンス（Worker の変数 MAINTENANCE。worker/maintenance.js）。true の間は、すべて 503 のメンテナンス画面
    const maintenance = getMaintenance(env);
    if (maintenance.state === "on") return maintenanceResponse(request, maintenance, "FaraBotModerator");
    // 作者への報告の受け口（worker/report.js）
    if (url.pathname === "/api/report" || url.pathname === "/api/report/config") {
      let res;
      try {
        res = await handleReport(request, env, "FaraBotModerator");
      } catch {
        res = new Response("internal error", { status: 500 });
      }
      res.headers.set("X-Content-Type-Options", "nosniff");
      return res;
    }
    // お知らせ（MAINTENANCE=notice）のときは、HTML の先頭に「更新予定」のバナーを足す
    const res = withNotice(await env.ASSETS.fetch(request), maintenance);
    const out = new Response(res.body, res);
    out.headers.set("Content-Security-Policy", isReportPage(url.pathname) ? REPORT_PAGE_CONTENT_SECURITY_POLICY : CONTENT_SECURITY_POLICY);
    out.headers.set("X-Content-Type-Options", "nosniff");
    out.headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
    return out;
  },
};
