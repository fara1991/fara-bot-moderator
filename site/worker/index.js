// Cloudflare Worker。すべてのリクエストがまずここを通る（wrangler.toml の run_worker_first = true）。
// 静的ファイル（dist/）をそのまま返し、セキュリティ用のヘッダーを付ける。
// 確認用（env.dev の NOINDEX、または *-dev のホスト）には、検索エンジン向けの noindex も付ける。

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

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const res = await env.ASSETS.fetch(request);
    const out = new Response(res.body, res);
    out.headers.set("Content-Security-Policy", CONTENT_SECURITY_POLICY);
    out.headers.set("X-Content-Type-Options", "nosniff");
    out.headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
    if (env.NOINDEX === "true" || url.hostname.endsWith("-dev.fara-labs.com")) {
      out.headers.set("X-Robots-Tag", "noindex, nofollow");
    }
    return out;
  },
};
