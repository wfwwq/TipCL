// TipCL plugin host: the loopback web server shared by all Node plugins (DESIGN §3.1).
//
//   <plugin routes>                              ctx.connection.fetch.register (Fetch API Request/Response)
//   GET  /__tipcl/sdk.js                         browser SDK (+ /__tipcl/events-worker.js, its SharedWorker)
//   GET  /__tipcl/view/<plugin>/<view>?k=..      view shell page (kind, id, chatId, props, theme, transparent)
//   GET  /__tipcl/plugin/<plugin>/<path>         static files from the plugin dir (read-only, Range support)
//   POST /__tipcl/call                           page → TipCL host API (header X-TipCL-Page: <pageToken>)
//   GET  /__tipcl/events?topics=a,b&k=..         SSE fan-out of host events to pages
//   GET  /__tipcl/health                         "ok"
// Every request must carry Host: 127.0.0.1:<port> or localhost:<port> (DNS rebinding), and cross-site
// browser requests (Origin / Sec-Fetch-Site) are refused, so other websites cannot drive plugin routes.

import { createServer } from 'node:http';
import { once } from 'node:events';
import { Readable } from 'node:stream';
import { createReadStream } from 'node:fs';
import { stat, realpath, readFile } from 'node:fs/promises';
import { join, resolve as resolvePath, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { pluginScope, emitLog } from './host-console.mjs';
import { globToRegExp, mimeOf, topicMatches, toWireError } from './host-util.mjs';

const HERE = fileURLToPath(new URL('.', import.meta.url));
const MAX_BODY = 64 * 1024 * 1024;

/** Methods a page may not call through /__tipcl/call (they need a Node-side executor or are host-only). */
const PAGE_DENIED = [/^host\./, /^log$/, /^tools\./, /^hooks\./, /^presets\./, /^events\.subscribe$/, /^plugin\.(load|unload)$/, /^llm\.stream$/, /^llm\.abort$/];

/**
 * @typedef {{plugin: any, path: string, re: RegExp | null, methods: Set<string> | null, fetch: Function, requestBody: string, order: number}} Route
 */

export class WebServer {
  /** @param {import('./host.mjs').PluginHost} host */
  constructor(host) {
    this.host = host;
    /** @type {Route[]} */ this.routes = [];
    this.order = 0;
    this.port = 0;
    /** live responses per plugin (route streams + page SSE), closed on unload. @type {Map<string, Set<import('node:http').ServerResponse>>} */
    this.active = new Map();
    /** page SSE subscribers. @type {Set<{plugin: string, patterns: string[], res: import('node:http').ServerResponse}>} */
    this.pages = new Set();
    this.server = createServer((req, res) => {
      this.handle(req, res).catch((err) => {
        emitLog(null, 'error', `web ${req.method} ${req.url}: ${err?.stack ?? err}`);
        if (!res.headersSent) { res.statusCode = 500; res.end('internal error'); } else res.destroy();
      });
    });
    this.server.keepAliveTimeout = 5000;
  }

  async listen(port = 0) {
    this.server.listen(port, '127.0.0.1');
    await once(this.server, 'listening');
    this.port = /** @type {any} */ (this.server.address()).port;
    return this.port;
  }

  close() {
    for (const set of this.active.values()) for (const res of set) res.destroy();
    this.server.closeAllConnections?.();
    this.server.close();
  }

  // ── routes ──

  /** @param {any} plugin @param {{path: string, methods?: string[], fetch: Function, requestBody?: string}} spec */
  addRoute(plugin, spec) {
    const path = String(spec?.path ?? '');
    if (!path.startsWith('/')) throw new TypeError('route path must start with /');
    if (path.startsWith('/__tipcl')) throw new TypeError('/__tipcl is reserved');
    if (typeof spec.fetch !== 'function') throw new TypeError('route needs fetch()');
    if (!path.startsWith(`/api/${plugin.id}/`) && path !== `/api/${plugin.id}`) {
      emitLog(plugin.id, 'warn', `route ${path} is outside /api/${plugin.id}/ — may collide with other plugins`);
    }
    /** @type {Route} */
    const route = {
      plugin, path,
      re: path.includes('*') ? globToRegExp(path) : null,
      methods: Array.isArray(spec.methods) && spec.methods.length ? new Set(spec.methods.map((m) => m.toUpperCase())) : null,
      fetch: spec.fetch,
      requestBody: spec.requestBody ?? 'buffered',
      order: this.order++,
    };
    this.routes.push(route);
    return () => { const i = this.routes.indexOf(route); if (i >= 0) this.routes.splice(i, 1); };
  }

  /** Exact paths beat wildcards; longer wildcard patterns beat shorter ones; then newest first. */
  matchRoute(pathname, method) {
    let best = null, bestScore = -1;
    for (const r of this.routes) {
      const hit = r.re ? r.re.test(pathname) : r.path === pathname;
      if (!hit) continue;
      const okMethod = !r.methods || r.methods.has(method) || (method === 'HEAD' && r.methods.has('GET'));
      if (!okMethod) { if (!best) best = { methodMismatch: true }; continue; }
      const score = (r.re ? 0 : 1e6) + r.path.length * 1000 + r.order;
      if (score > bestScore) { bestScore = score; best = r; }
    }
    return best;
  }

  /** Close every live response of a plugin (routes + page SSE); their ReadableStreams get cancel(). */
  closePlugin(pluginId) {
    const set = this.active.get(pluginId);
    if (set) for (const res of set) res.destroy();
    this.active.delete(pluginId);
    for (const p of this.pages) if (p.plugin === pluginId) { p.res.destroy(); this.pages.delete(p); }
  }

  #track(pluginId, res) {
    let set = this.active.get(pluginId);
    if (!set) this.active.set(pluginId, set = new Set());
    set.add(res);
    res.on('close', () => set.delete(res));
  }

  // ── page events ──

  /** Topic patterns pages of a plugin currently listen to. */
  pageTopics(pluginId) {
    const out = new Set();
    for (const p of this.pages) if (p.plugin === pluginId) for (const t of p.patterns) out.add(t);
    return out;
  }

  /** Send an event to pages. `pluginId` null = every page. */
  publish(pluginId, topic, data) {
    const line = `data: ${JSON.stringify({ topic, data })}\n\n`;
    for (const p of this.pages) {
      if (pluginId && p.plugin !== pluginId) continue;
      if (!p.patterns.some((pat) => topicMatches(pat, topic))) continue;
      p.res.write(line);
    }
  }

  // ── request handling ──

  async handle(req, res) {
    const method = (req.method ?? 'GET').toUpperCase();
    // DNS rebinding: only our own loopback authority
    const hostHeader = String(req.headers.host ?? '').toLowerCase();
    if (hostHeader !== `127.0.0.1:${this.port}` && hostHeader !== `localhost:${this.port}`) return send(res, 421, 'bad host');
    // cross-site browser requests (another website poking the loopback port)
    const origin = req.headers.origin;
    if (origin && origin !== 'null' && origin !== `http://127.0.0.1:${this.port}` && origin !== `http://localhost:${this.port}`) return send(res, 403, 'cross-origin');
    const site = req.headers['sec-fetch-site'];
    if (site === 'cross-site' || site === 'same-site') return send(res, 403, 'cross-site');

    const url = new URL(req.url ?? '/', `http://${hostHeader}`);
    const path = url.pathname;
    if (path.startsWith('/__tipcl/')) return this.#internal(req, res, method, url);

    const route = this.matchRoute(path, method);
    if (!route) return send(res, 404, 'not found');
    if (route.methodMismatch) return send(res, 405, 'method not allowed');
    return this.#serveRoute(route, req, res, method, url);
  }

  async #serveRoute(route, req, res, method, url) {
    const plugin = route.plugin;
    this.#track(plugin.id, res);
    const abort = new AbortController();
    // close callbacks run outside any async context: re-enter the plugin's scope so its logs stay attributed
    res.on('close', () => { if (!res.writableFinished) pluginScope.run(plugin.id, () => abort.abort(new Error('client disconnected'))); });

    /** @type {any} */
    let body;
    if (method !== 'GET' && method !== 'HEAD') {
      if (route.requestBody === 'stream') body = Readable.toWeb(req);
      else body = await readBody(req);
    }
    const headers = new Headers();
    for (let i = 0; i < req.rawHeaders.length; i += 2) {
      try { headers.append(req.rawHeaders[i], req.rawHeaders[i + 1]); } catch { /* invalid header value */ }
    }
    const request = new Request(url.href, {
      method, headers, signal: abort.signal,
      ...(body !== undefined ? { body, ...(route.requestBody === 'stream' ? { duplex: 'half' } : {}) } : {}),
    });

    let response;
    try {
      response = await pluginScope.run(plugin.id, () => route.fetch(request, { plugin: plugin.id, signal: abort.signal }));
    } catch (err) {
      emitLog(plugin.id, 'error', `route ${route.path} failed: ${err?.stack ?? err}`);
      return send(res, 500, String(err?.message ?? err));
    }
    if (res.destroyed) { response?.body?.cancel?.().catch(() => {}); return; }
    if (response === undefined || response === null) return send(res, 204, '');
    if (!(response instanceof Response)) {
      // convenience: plain values become JSON / text
      response = typeof response === 'string' ? new Response(response) : Response.json(response);
    }
    await writeResponse(res, response, method === 'HEAD', plugin.id);
  }

  async #internal(req, res, method, url) {
    const path = url.pathname;
    if (path === '/__tipcl/health') return send(res, 200, 'ok');
    if (path === '/__tipcl/sdk.js') {
      const js = await readFile(join(HERE, 'sdk.js'));
      res.writeHead(200, { 'content-type': 'text/javascript; charset=utf-8', 'cache-control': 'no-cache' });
      return res.end(js);
    }
    if (path === '/__tipcl/events-worker.js') {
      const js = await readFile(join(HERE, 'sdk-events-worker.js'));
      res.writeHead(200, { 'content-type': 'text/javascript; charset=utf-8', 'cache-control': 'no-cache' });
      return res.end(js);
    }
    if (path === '/__tipcl/call') return this.#call(req, res, method);
    if (path === '/__tipcl/events') return this.#events(req, res, url);
    let m = /^\/__tipcl\/view\/([^/]+)\/([^/]+)$/.exec(path);
    if (m) return this.#view(res, decodeURIComponent(m[1]), decodeURIComponent(m[2]), url);
    m = /^\/__tipcl\/plugin\/([^/]+)\/(.+)$/.exec(path);
    if (m && (method === 'GET' || method === 'HEAD')) return this.#static(req, res, decodeURIComponent(m[1]), m[2], method === 'HEAD');
    return send(res, 404, 'not found');
  }

  /** Plugin for a page token, or null. */
  #pluginForToken(token) {
    if (!token) return null;
    for (const p of this.host.plugins.values()) if (p.pageToken === token) return p;
    return null;
  }

  async #call(req, res, method) {
    if (method !== 'POST') return send(res, 405, 'POST only');
    const plugin = this.#pluginForToken(String(req.headers['x-tipcl-page'] ?? ''));
    if (!plugin) return sendJson(res, 403, { error: { code: 'bad_token', message: 'missing or invalid X-TipCL-Page' } });
    let msg;
    try { msg = JSON.parse((await readBody(req, 16 * 1024 * 1024)).toString('utf8') || '{}'); } catch { return sendJson(res, 400, { error: { code: 'bad_json', message: 'body must be JSON {method, params}' } }); }
    const callMethod = String(msg?.method ?? '');
    if (!/^[A-Za-z][\w.]*$/.test(callMethod)) return sendJson(res, 400, { error: { code: 'bad_method', message: 'bad method' } });
    if (PAGE_DENIED.some((re) => re.test(callMethod))) return sendJson(res, 403, { error: { code: 'permission_denied', message: `${callMethod} is not available to pages` } });
    try {
      // identity is filled by the host from the token; whatever the page sent as `plugin` is overwritten
      const result = await this.host.rpc.request(callMethod, { ...(msg.params ?? {}), plugin: plugin.id });
      return sendJson(res, 200, { result: result ?? null });
    } catch (err) {
      const e = toWireError(err);
      return sendJson(res, e.code === 'permission_denied' ? 403 : e.code === 'method_not_found' ? 404 : 400, { error: e });
    }
  }

  #events(req, res, url) {
    const plugin = this.#pluginForToken(url.searchParams.get('k') ?? '');
    if (!plugin) return send(res, 403, 'bad token');
    const patterns = (url.searchParams.get('topics') ?? '').split(',').map((s) => s.trim()).filter(Boolean);
    res.writeHead(200, { 'content-type': 'text/event-stream; charset=utf-8', 'cache-control': 'no-cache', 'x-accel-buffering': 'no' });
    req.socket.setNoDelay(true);
    res.write('retry: 2000\n: connected\n\n');
    const sub = { plugin: plugin.id, patterns, res };
    this.pages.add(sub);
    this.#track(plugin.id, res);
    this.host.updateSubscriptions(plugin);
    const ping = setInterval(() => res.write(': ping\n\n'), 15_000);
    res.on('close', () => { clearInterval(ping); this.pages.delete(sub); if (!plugin.unloaded) this.host.updateSubscriptions(plugin); });
  }

  #view(res, pluginId, view, url) {
    const plugin = this.host.plugins.get(pluginId);
    if (!plugin || url.searchParams.get('k') !== plugin.pageToken) return send(res, 403, 'bad token');
    let props = {};
    try { props = JSON.parse(url.searchParams.get('props') ?? '{}') ?? {}; } catch { /* ignore */ }
    if (url.searchParams.get('chatId')) props.chatId = url.searchParams.get('chatId');
    // TipCL should pass kind & id; if it does not, infer them from what the plugin registered for this view
    let kind = url.searchParams.get('kind');
    let surfaceId = url.searchParams.get('id');
    if (!kind) {
      const reg = [...plugin.uiSpecs.values()].find((u) => u.view === view && (!surfaceId || u.id === surfaceId));
      kind = reg?.kind ?? 'page';
      surfaceId ??= reg?.id;
    }
    const theme = this.host.themeFor(url.searchParams.get('theme'));
    const boot = {
      plugin: { id: plugin.id, version: plugin.version, dir: plugin.dir, name: plugin.manifest.name ?? plugin.id },
      token: plugin.pageToken,
      view, kind, id: surfaceId ?? view, props,
      client: plugin.client ? `/__tipcl/plugin/${encodeURIComponent(plugin.id)}/${String(plugin.client).replace(/^\.?\//, '')}` : null,
      theme,
      transparent: kind === 'overlay' || url.searchParams.get('transparent') === '1',
      lang: this.host.lang,
    };
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store', 'referrer-policy': 'no-referrer' });
    res.end(shellHtml(boot));
  }

  async #static(req, res, pluginId, rawRest, head) {
    const plugin = this.host.plugins.get(pluginId);
    if (!plugin) return send(res, 404, 'no such plugin');
    let rel;
    try { rel = decodeURIComponent(rawRest); } catch { return send(res, 400, 'bad path'); }
    const segs = rel.split(/[\\/]+/).filter(Boolean);
    if (rel.includes('\0') || segs.some((s) => s === '..' || s === '.' || s.includes(':'))) return send(res, 403, 'forbidden');
    const root = plugin.dir;
    const file = resolvePath(root, ...segs);
    if (!file.startsWith(root + sep)) return send(res, 403, 'forbidden');
    let real, info;
    try {
      real = await realpath(file);
      const realRoot = await realpath(root);
      if (!real.startsWith(realRoot + sep)) return send(res, 403, 'forbidden'); // symlink escaping the plugin dir
      info = await stat(real);
    } catch { return send(res, 404, 'not found'); }
    if (!info.isFile()) return send(res, 404, 'not found');

    const lastModified = info.mtime.toUTCString();
    const headers = { 'content-type': mimeOf(real), 'cache-control': 'no-cache', 'last-modified': lastModified, 'accept-ranges': 'bytes', 'x-content-type-options': 'nosniff' };
    if (req.headers['if-modified-since'] === lastModified) { res.writeHead(304, headers); return res.end(); }
    let start = 0, end = info.size - 1, status = 200;
    const range = /^bytes=(\d*)-(\d*)$/.exec(String(req.headers.range ?? ''));
    if (range && info.size > 0) {
      if (range[1] === '' && range[2] !== '') { start = Math.max(0, info.size - Number(range[2])); }
      else { start = Number(range[1] || 0); if (range[2] !== '') end = Math.min(end, Number(range[2])); }
      if (start > end || start >= info.size) { res.writeHead(416, { 'content-range': `bytes */${info.size}` }); return res.end(); }
      status = 206;
      headers['content-range'] = `bytes ${start}-${end}/${info.size}`;
    }
    headers['content-length'] = String(info.size === 0 ? 0 : end - start + 1);
    res.writeHead(status, headers);
    if (head || info.size === 0) return res.end();
    const stream = createReadStream(real, { start, end });
    stream.on('error', () => res.destroy());
    res.on('close', () => stream.destroy());
    stream.pipe(res);
  }
}

// ── helpers ───────────────────────────────────────────────────────────────

function send(res, status, text) {
  res.writeHead(status, { 'content-type': 'text/plain; charset=utf-8' });
  res.end(text);
}
function sendJson(res, status, body) {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
  res.end(JSON.stringify(body));
}

async function readBody(req, limit = MAX_BODY) {
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > limit) throw new Error('request body too large');
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}

/** Write a Fetch API Response to a node response, streaming the body with backpressure. */
async function writeResponse(res, response, head, pluginId) {
  res.statusCode = response.status;
  if (response.statusText) res.statusMessage = response.statusText;
  const cookies = response.headers.getSetCookie?.() ?? [];
  for (const [k, v] of response.headers) if (k !== 'set-cookie') res.setHeader(k, v);
  if (cookies.length) res.setHeader('set-cookie', cookies);
  const type = response.headers.get('content-type') ?? '';
  const streaming = type.startsWith('text/event-stream');
  if (streaming) { res.setHeader('x-accel-buffering', 'no'); res.socket?.setNoDelay(true); }
  if (!response.body || head) { response.body?.cancel().catch(() => {}); res.end(); return; }
  res.flushHeaders();
  const reader = response.body.getReader();
  let closed = false;
  const onClose = () => { closed = true; pluginScope.run(pluginId, () => reader.cancel(new Error('client disconnected')).catch(() => {})); };
  res.on('close', onClose);
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done || closed) break;
      const chunk = typeof value === 'string' ? Buffer.from(value) : value;
      if (!res.write(chunk)) {
        // backpressure: wait until drained (or the client went away)
        await new Promise((r) => { const ok = () => { res.off('close', ok); res.off('drain', ok); r(); }; res.once('drain', ok); res.once('close', ok); });
      }
    }
    if (!closed) res.end();
  } catch (err) {
    if (!closed) { emitLog(pluginId, 'warn', `response stream failed: ${err?.message ?? err}`); res.destroy(); }
  } finally {
    res.off('close', onClose);
  }
}

/** Theme → CSS custom properties. */
export function themeCss(theme) {
  const vars = Object.entries(theme?.colors ?? {}).map(([k, v]) => `--tipcl-${k.replace(/[A-Z]/g, (c) => '-' + c.toLowerCase())}:${v};`).join('');
  return `:root{${vars}--tipcl-font:${theme?.font ?? 'system-ui, "Segoe UI", "Microsoft YaHei UI", sans-serif'};color-scheme:${theme?.mode === 'light' ? 'light' : 'dark'};}`;
}

function shellHtml(boot) {
  const json = JSON.stringify(boot).replace(/</g, '\\u003c').replace(/\u2028/g, '\\u2028').replace(/\u2029/g, '\\u2029');
  const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  return `<!doctype html>
<html lang="${esc(boot.lang ?? 'zh-CN')}" data-theme="${boot.theme?.mode === 'light' ? 'light' : 'dark'}" data-tipcl-kind="${esc(boot.kind)}"${boot.transparent ? ' data-transparent' : ''}>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(boot.plugin.name)} · ${esc(boot.view)}</title>
<style id="tipcl-theme">${themeCss(boot.theme)}</style>
<style>
html,body{margin:0;height:100%;background:var(--tipcl-bg);color:var(--tipcl-fg);font-family:var(--tipcl-font);}
html[data-transparent],html[data-transparent] body{background:transparent;}
#tipcl-root{height:100%;}
.tipcl-error{margin:16px;padding:12px 14px;border:1px solid var(--tipcl-danger,#e5484d);border-radius:8px;color:var(--tipcl-danger,#e5484d);white-space:pre-wrap;font:12px/1.5 ui-monospace,Consolas,monospace;}
</style>
<script>window.__TIPCL_BOOT__=${json};</script>
<script type="module">
import { boot } from '/__tipcl/sdk.js';
boot(window.__TIPCL_BOOT__);
</script>
</head>
<body><div id="tipcl-root"></div></body>
</html>`;
}
