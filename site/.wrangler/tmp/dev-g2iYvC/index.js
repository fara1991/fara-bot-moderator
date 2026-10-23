var __defProp = Object.defineProperty;
var __name = (target, value) => __defProp(target, "name", { value, configurable: true });

// ../external/fara-site-kit/worker/report.js
var MAX_BODY_CHARS = 8 * 1024;
var MAX_BODY_BYTES = 16 * 1024;
var FETCH_TIMEOUT_MS = 5e3;
var MAX_MESSAGE = 2e3;
var MAX_CONTACT = 200;
var MIN_MESSAGE = 5;
var TURNSTILE_URL = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
var CATEGORIES = { bug: "\u4E0D\u5177\u5408", request: "\u8981\u671B\u30FB\u63D0\u6848", other: "\u305D\u306E\u4ED6" };
var json = /* @__PURE__ */ __name((status, body, headers = {}) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", ...headers } }), "json");
function clean(value, max) {
  if (typeof value !== "string") return "";
  const text = value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, "").replace(/[\ud800-\udbff](?![\udc00-\udfff])|(?<![\ud800-\udbff])[\udc00-\udfff]/g, "").trim();
  const chars = Array.from(text);
  return chars.length > max ? chars.slice(0, max).join("").trim() : text;
}
__name(clean, "clean");
function cleanPage(value) {
  return typeof value === "string" && /^\/[A-Za-z0-9\-_/.]{0,299}$/.test(value) && !value.startsWith("//") ? value : "";
}
__name(cleanPage, "cleanPage");
function originHost(origin) {
  try {
    return new URL(origin).host;
  } catch {
    return null;
  }
}
__name(originHost, "originHost");
function isConfigured(env) {
  return Boolean(env.TURNSTILE_SITE_KEY && env.TURNSTILE_SECRET && env.DISCORD_WEBHOOK_URL);
}
__name(isConfigured, "isConfigured");
async function handleReport(request, env, siteName, deps = {}) {
  const doFetch = deps.fetch ?? fetch;
  const url = new URL(request.url);
  if (url.pathname === "/api/report/config") {
    if (request.method !== "GET") return json(405, { error: "method_not_allowed" }, { Allow: "GET" });
    return json(200, { enabled: isConfigured(env), siteKey: env.TURNSTILE_SITE_KEY || null });
  }
  if (url.pathname !== "/api/report") return json(404, { error: "not_found" });
  if (request.method !== "POST") return json(405, { error: "method_not_allowed" }, { Allow: "POST" });
  const origin = request.headers.get("Origin");
  if (!origin || originHost(origin) !== url.host) return json(403, { error: "forbidden" });
  if (!(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) return json(415, { error: "unsupported_media_type" });
  const declared = Number(request.headers.get("Content-Length"));
  if (Number.isFinite(declared) && declared > MAX_BODY_BYTES) return json(413, { error: "too_large" });
  const text = await request.text();
  if (text.length > MAX_BODY_CHARS) return json(413, { error: "too_large" });
  let body;
  try {
    body = JSON.parse(text);
  } catch {
    return json(400, { error: "invalid_json", message: "\u9001\u4FE1\u5185\u5BB9\u3092\u8AAD\u3081\u307E\u305B\u3093\u3067\u3057\u305F" });
  }
  if (body === null || typeof body !== "object" || Array.isArray(body)) return json(400, { error: "invalid_body", message: "\u9001\u4FE1\u5185\u5BB9\u3092\u8AAD\u3081\u307E\u305B\u3093\u3067\u3057\u305F" });
  if (typeof body.website === "string" && body.website.length > 0) return json(200, { ok: true });
  const category = typeof body.category === "string" && Object.hasOwn(CATEGORIES, body.category) ? body.category : null;
  if (!category) return json(400, { error: "invalid_category", message: "\u7A2E\u985E\u3092\u9078\u3093\u3067\u304F\u3060\u3055\u3044" });
  const message = clean(body.message, MAX_MESSAGE);
  if (message.length < MIN_MESSAGE) return json(400, { error: "invalid_message", message: `\u5185\u5BB9\u3092 ${MIN_MESSAGE} \u6587\u5B57\u4EE5\u4E0A\u3067\u66F8\u3044\u3066\u304F\u3060\u3055\u3044` });
  const contact = clean(body.contact, MAX_CONTACT);
  const page = cleanPage(body.page);
  const token = typeof body.turnstileToken === "string" ? body.turnstileToken.slice(0, 2048) : "";
  if (!token) return json(400, { error: "missing_token", message: "\u300C\u30ED\u30DC\u30C3\u30C8\u3067\u306F\u3042\u308A\u307E\u305B\u3093\u300D\u306E\u78BA\u8A8D\u3092\u3057\u3066\u304F\u3060\u3055\u3044" });
  if (!isConfigured(env)) return json(503, { error: "not_configured", message: "\u3044\u307E\u306F\u5831\u544A\u3092\u53D7\u3051\u4ED8\u3051\u3066\u3044\u307E\u305B\u3093" });
  const form = new URLSearchParams({ secret: env.TURNSTILE_SECRET, response: token });
  const ip = request.headers.get("CF-Connecting-IP");
  if (ip) form.set("remoteip", ip);
  let result;
  try {
    const res = await doFetch(TURNSTILE_URL, { method: "POST", body: form, signal: AbortSignal.timeout(FETCH_TIMEOUT_MS) });
    if (res.status >= 500) return json(502, { error: "verify_failed", message: "\u78BA\u8A8D\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\u3082\u3046\u4E00\u5EA6\u304A\u8A66\u3057\u304F\u3060\u3055\u3044" });
    result = res.ok ? await res.json() : null;
  } catch {
    return json(502, { error: "verify_failed", message: "\u78BA\u8A8D\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\u3082\u3046\u4E00\u5EA6\u304A\u8A66\u3057\u304F\u3060\u3055\u3044" });
  }
  const hostOk = typeof result?.hostname !== "string" || result.hostname === url.hostname;
  if (result?.success !== true || !hostOk) return json(400, { error: "turnstile_failed", message: "\u300C\u30ED\u30DC\u30C3\u30C8\u3067\u306F\u3042\u308A\u307E\u305B\u3093\u300D\u306E\u78BA\u8A8D\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\u3082\u3046\u4E00\u5EA6\u304A\u8A66\u3057\u304F\u3060\u3055\u3044" });
  const fields = [{ name: "\u7A2E\u985E", value: CATEGORIES[category], inline: true }];
  if (contact) fields.push({ name: "\u9023\u7D61\u5148", value: contact, inline: true });
  if (page) fields.push({ name: "\u30DA\u30FC\u30B8", value: page });
  const country = request.cf?.country;
  if (country) fields.push({ name: "\u56FD", value: String(country), inline: true });
  const payload = {
    username: "\u30B5\u30A4\u30C8\u304B\u3089\u306E\u5831\u544A",
    allowed_mentions: { parse: [] },
    embeds: [{
      title: `${siteName} \u3078\u306E\u5831\u544A`,
      description: message,
      color: category === "bug" ? 14431557 : category === "request" ? 8141549 : 7036810,
      fields,
      timestamp: (deps.now?.() ?? /* @__PURE__ */ new Date()).toISOString()
    }]
  };
  try {
    const res = await doFetch(env.DISCORD_WEBHOOK_URL, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload), signal: AbortSignal.timeout(FETCH_TIMEOUT_MS) });
    if (!res.ok) return json(502, { error: "notify_failed", message: "\u9001\u4FE1\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\u3057\u3070\u3089\u304F\u3057\u3066\u304B\u3089\u3082\u3046\u4E00\u5EA6\u304A\u8A66\u3057\u304F\u3060\u3055\u3044" });
  } catch {
    return json(502, { error: "notify_failed", message: "\u9001\u4FE1\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\u3057\u3070\u3089\u304F\u3057\u3066\u304B\u3089\u3082\u3046\u4E00\u5EA6\u304A\u8A66\u3057\u304F\u3060\u3055\u3044" });
  }
  return json(200, { ok: true });
}
__name(handleReport, "handleReport");

// ../external/fara-site-kit/worker/maintenance.js
var MAX_MESSAGE2 = 200;
var TWITTER_URL = "https://twitter.com/game_fara";
var escapeHtml = /* @__PURE__ */ __name((text) => String(text).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]), "escapeHtml");
function cleanMessage(value) {
  if (typeof value !== "string") return "";
  const text = value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, "").trim();
  const chars = Array.from(text);
  return chars.length > MAX_MESSAGE2 ? chars.slice(0, MAX_MESSAGE2).join("").trim() : text;
}
__name(cleanMessage, "cleanMessage");
function getMaintenance(env) {
  const raw = String(env?.MAINTENANCE ?? "").trim().toLowerCase();
  const state = raw === "true" || raw === "on" || raw === "1" ? "on" : raw === "notice" ? "notice" : "off";
  return { state, message: state === "off" ? "" : cleanMessage(env?.MAINTENANCE_MESSAGE) };
}
__name(getMaintenance, "getMaintenance");
function maintenancePageHtml(siteName, message) {
  const schedule = message ? `<p class="s">${escapeHtml(message)}</p>` : "";
  return `<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex"><title>\u30E1\u30F3\u30C6\u30CA\u30F3\u30B9\u4E2D | ${escapeHtml(siteName)}</title>
<style>body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px;background:#0c0616;color:#f3edff;font:1rem/1.8 system-ui,sans-serif;text-align:center}h1{font-size:1.5rem;color:#b39ddb}p{color:#b4a7d1}.s{color:#f3edff;font-weight:700}a{display:inline-block;margin-top:8px;padding:8px 20px;border-radius:99px;background:#7c3aed;color:#fff;text-decoration:none;font-weight:700}</style></head>
<body><main><h1>\u305F\u3060\u3044\u307E\u30E1\u30F3\u30C6\u30CA\u30F3\u30B9\u4E2D\u3067\u3059</h1><p>\u30B5\u30A4\u30C8\u306E\u8ABF\u6574\u3092\u884C\u3063\u3066\u3044\u307E\u3059\u3002<br>\u3057\u3070\u3089\u304F\u3057\u3066\u304B\u3089\u3001\u3082\u3046\u4E00\u5EA6\u304A\u8A66\u3057\u304F\u3060\u3055\u3044\u3002</p>${schedule}<p>\u304A\u77E5\u3089\u305B\u306F Twitter \u3067</p><a href="${TWITTER_URL}" rel="noopener">@game_fara\uFF08Twitter\uFF09</a></main></body></html>
`;
}
__name(maintenancePageHtml, "maintenancePageHtml");
function maintenanceResponse(request, info, siteName) {
  const url = new URL(request.url);
  const accept = request.headers.get("Accept") ?? "";
  const headers = { "Cache-Control": "no-store", "Retry-After": "3600", "X-Robots-Tag": "noindex" };
  if (url.pathname.startsWith("/api/") || accept.includes("application/json") && !accept.includes("text/html")) {
    return new Response(JSON.stringify({ error: "maintenance", message: info.message || "\u30E1\u30F3\u30C6\u30CA\u30F3\u30B9\u4E2D\u3067\u3059" }), {
      status: 503,
      headers: { ...headers, "Content-Type": "application/json; charset=utf-8" }
    });
  }
  if (accept.includes("text/html")) {
    return new Response(request.method === "HEAD" ? null : maintenancePageHtml(siteName, info.message), {
      status: 503,
      headers: {
        ...headers,
        "Content-Type": "text/html; charset=utf-8",
        // この画面は、スタイルを中に持つので、サイトの CSP とは別に、必要なものだけ許可する
        "Content-Security-Policy": "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'"
      }
    });
  }
  return new Response("\u30E1\u30F3\u30C6\u30CA\u30F3\u30B9\u4E2D\u3067\u3059", { status: 503, headers: { ...headers, "Content-Type": "text/plain; charset=utf-8" } });
}
__name(maintenanceResponse, "maintenanceResponse");

// worker/index.js
var SITE_NAME = "FaraBotModerator";
var worker_default = {
  async fetch(request, env) {
    const url = new URL(request.url);
    const maintenance = getMaintenance(env);
    if (url.pathname === "/api/maintenance") {
      return new Response(JSON.stringify({ state: maintenance.state, message: maintenance.message }), {
        headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" }
      });
    }
    if (maintenance.state === "on") return maintenanceResponse(request, maintenance, SITE_NAME);
    if (url.pathname === "/api/report" || url.pathname === "/api/report/config") {
      let res2;
      try {
        res2 = await handleReport(request, env, SITE_NAME);
      } catch {
        res2 = new Response("internal error", { status: 500 });
      }
      res2.headers.set("X-Content-Type-Options", "nosniff");
      return res2;
    }
    const res = await env.ASSETS.fetch(request);
    const out = new Response(res.body, res);
    out.headers.set("X-Content-Type-Options", "nosniff");
    out.headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
    return out;
  }
};

// node_modules/wrangler/templates/middleware/middleware-ensure-req-body-drained.ts
var drainBody = /* @__PURE__ */ __name(async (request, env, _ctx, middlewareCtx) => {
  try {
    return await middlewareCtx.next(request, env);
  } finally {
    try {
      if (request.body !== null && !request.bodyUsed) {
        const reader = request.body.getReader();
        while (!(await reader.read()).done) {
        }
      }
    } catch (e) {
      console.error("Failed to drain the unused request body.", e);
    }
  }
}, "drainBody");
var middleware_ensure_req_body_drained_default = drainBody;

// node_modules/wrangler/templates/middleware/middleware-miniflare3-json-error.ts
function reduceError(e) {
  return {
    name: e?.name,
    message: e?.message ?? String(e),
    stack: e?.stack,
    cause: e?.cause === void 0 ? void 0 : reduceError(e.cause)
  };
}
__name(reduceError, "reduceError");
var jsonError = /* @__PURE__ */ __name(async (request, env, _ctx, middlewareCtx) => {
  try {
    return await middlewareCtx.next(request, env);
  } catch (e) {
    const error = reduceError(e);
    const body = JSON.stringify(error);
    const headers = {
      "Content-Type": "application/json",
      "MF-Experimental-Error-Stack": "true"
    };
    const encoded = encodeURIComponent(body);
    if (encoded.length <= 8192) {
      headers["MF-Experimental-Error-Stack-Payload"] = encoded;
    }
    return new Response(body, { status: 500, headers });
  }
}, "jsonError");
var middleware_miniflare3_json_error_default = jsonError;

// .wrangler/tmp/bundle-rKVzAB/middleware-insertion-facade.js
var __INTERNAL_WRANGLER_MIDDLEWARE__ = [
  middleware_ensure_req_body_drained_default,
  middleware_miniflare3_json_error_default
];
var middleware_insertion_facade_default = worker_default;

// node_modules/wrangler/templates/middleware/common.ts
var __facade_middleware__ = [];
function __facade_register__(...args) {
  __facade_middleware__.push(...args.flat());
}
__name(__facade_register__, "__facade_register__");
function __facade_invokeChain__(request, env, ctx, dispatch, middlewareChain) {
  const [head, ...tail] = middlewareChain;
  const middlewareCtx = {
    dispatch,
    next(newRequest, newEnv) {
      return __facade_invokeChain__(newRequest, newEnv, ctx, dispatch, tail);
    }
  };
  return head(request, env, ctx, middlewareCtx);
}
__name(__facade_invokeChain__, "__facade_invokeChain__");
function __facade_invoke__(request, env, ctx, dispatch, finalMiddleware) {
  return __facade_invokeChain__(request, env, ctx, dispatch, [
    ...__facade_middleware__,
    finalMiddleware
  ]);
}
__name(__facade_invoke__, "__facade_invoke__");

// .wrangler/tmp/bundle-rKVzAB/middleware-loader.entry.ts
var __Facade_ScheduledController__ = class ___Facade_ScheduledController__ {
  constructor(scheduledTime, cron, noRetry) {
    this.scheduledTime = scheduledTime;
    this.cron = cron;
    this.#noRetry = noRetry;
  }
  scheduledTime;
  cron;
  static {
    __name(this, "__Facade_ScheduledController__");
  }
  #noRetry;
  noRetry() {
    if (!(this instanceof ___Facade_ScheduledController__)) {
      throw new TypeError("Illegal invocation");
    }
    this.#noRetry();
  }
};
function wrapExportedHandler(worker) {
  if (__INTERNAL_WRANGLER_MIDDLEWARE__ === void 0 || __INTERNAL_WRANGLER_MIDDLEWARE__.length === 0) {
    return worker;
  }
  for (const middleware of __INTERNAL_WRANGLER_MIDDLEWARE__) {
    __facade_register__(middleware);
  }
  const fetchDispatcher = /* @__PURE__ */ __name(function(request, env, ctx) {
    if (worker.fetch === void 0) {
      throw new Error("Handler does not export a fetch() function.");
    }
    return worker.fetch(request, env, ctx);
  }, "fetchDispatcher");
  return {
    ...worker,
    fetch(request, env, ctx) {
      const dispatcher = /* @__PURE__ */ __name(function(type, init) {
        if (type === "scheduled" && worker.scheduled !== void 0) {
          const controller = new __Facade_ScheduledController__(
            Date.now(),
            init.cron ?? "",
            () => {
            }
          );
          return worker.scheduled(controller, env, ctx);
        }
      }, "dispatcher");
      return __facade_invoke__(request, env, ctx, dispatcher, fetchDispatcher);
    }
  };
}
__name(wrapExportedHandler, "wrapExportedHandler");
function wrapWorkerEntrypoint(klass) {
  if (__INTERNAL_WRANGLER_MIDDLEWARE__ === void 0 || __INTERNAL_WRANGLER_MIDDLEWARE__.length === 0) {
    return klass;
  }
  for (const middleware of __INTERNAL_WRANGLER_MIDDLEWARE__) {
    __facade_register__(middleware);
  }
  return class extends klass {
    #fetchDispatcher = /* @__PURE__ */ __name((request, env, ctx) => {
      this.env = env;
      this.ctx = ctx;
      if (super.fetch === void 0) {
        throw new Error("Entrypoint class does not define a fetch() function.");
      }
      return super.fetch(request);
    }, "#fetchDispatcher");
    #dispatcher = /* @__PURE__ */ __name((type, init) => {
      if (type === "scheduled" && super.scheduled !== void 0) {
        const controller = new __Facade_ScheduledController__(
          Date.now(),
          init.cron ?? "",
          () => {
          }
        );
        return super.scheduled(controller);
      }
    }, "#dispatcher");
    fetch(request) {
      return __facade_invoke__(
        request,
        this.env,
        this.ctx,
        this.#dispatcher,
        this.#fetchDispatcher
      );
    }
  };
}
__name(wrapWorkerEntrypoint, "wrapWorkerEntrypoint");
var WRAPPED_ENTRY;
if (typeof middleware_insertion_facade_default === "object") {
  WRAPPED_ENTRY = wrapExportedHandler(middleware_insertion_facade_default);
} else if (typeof middleware_insertion_facade_default === "function") {
  WRAPPED_ENTRY = wrapWorkerEntrypoint(middleware_insertion_facade_default);
}
var middleware_loader_entry_default = WRAPPED_ENTRY;
export {
  __INTERNAL_WRANGLER_MIDDLEWARE__,
  middleware_loader_entry_default as default
};
//# sourceMappingURL=index.js.map
