// A fake TipCL for testing the Node plugin host: spawns host.mjs, speaks the NDJSON protocol
// (DESIGN §4) and implements the TipCL side of the host API in memory.

import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
import { request as httpRequest } from 'node:http';

export const HOST_DIR = fileURLToPath(new URL('../../src/TipCL/Resources/plugin-host/', import.meta.url));
export const SAMPLES_DIR = fileURLToPath(new URL('../', import.meta.url));

export class FakeTipcl {
  /** @param {{home: string, token?: string}} opts */
  constructor(opts) {
    this.home = opts.home;
    this.token = opts.token ?? 'test-host-token';
    this.nextId = 1;
    /** requests the host sent us: {method, params} */
    this.calls = [];
    this.pending = new Map();
    this.stderr = '';
    this.notifications = [];
    // in-memory TipCL state
    this.config = {};
    this.credentials = { 'hello-plugin': { HELLO_API_KEY: 'sk-test' } };
    this.attachments = [];
    this.subscriptions = {};
    this.aborted = new Set();
    /** chatId → events (TipCL-side history) */
    this.chats = new Map();
    this.handlers = this.#defaultHandlers();

    this.child = spawn(process.execPath, [join(HOST_DIR, 'host.mjs')], {
      env: { ...process.env, TIPCL_HOST_TOKEN: this.token, TIPCL_HOME: this.home, TIPCL_VERSION: '3.3.0', TIPCL_LANG: 'zh-CN' },
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    this.exited = new Promise((resolve) => this.child.on('exit', (code) => resolve(code)));
    this.child.stderr.setEncoding('utf8');
    this.child.stderr.on('data', (d) => { this.stderr += d; if (process.env.HOST_DEBUG) process.stderr.write(d); });
    let buf = '';
    this.child.stdout.setEncoding('utf8');
    this.child.stdout.on('data', (d) => {
      buf += d;
      let nl;
      while ((nl = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, nl);
        buf = buf.slice(nl + 1);
        if (line.trim()) this.#receive(JSON.parse(line)); // throws on any non-protocol stdout line
      }
    });
    this.hello = this.waitForCall('host.hello').then((c) => { this.port = c.params.webPort; return c.params; });
  }

  // ── protocol ──

  #send(msg) { this.child.stdin.write(JSON.stringify(msg) + '\n'); }

  /** Request to the host. */
  request(method, params = {}) {
    const id = `t${this.nextId++}`;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.#send({ id, method, params });
    });
  }
  notify(method, params = {}) { this.#send({ method, params }); }

  #receive(msg) {
    if (msg.method === undefined) {
      const p = this.pending.get(msg.id);
      if (!p) return;
      this.pending.delete(msg.id);
      if (msg.error) p.reject(Object.assign(new Error(msg.error.message), { code: msg.error.code })); else p.resolve(msg.result);
      return;
    }
    const call = { method: msg.method, params: msg.params ?? {} };
    this.calls.push(call);
    for (const w of [...this.waiters ?? []]) w();
    if (msg.id === undefined) { this.notifications.push(call); return; }
    const handler = this.handlers[msg.method];
    Promise.resolve()
      .then(() => {
        if (!handler) throw Object.assign(new Error(`fake: no handler for ${msg.method}`), { code: 'method_not_found' });
        return handler(call.params);
      })
      .then((result) => this.#send({ id: msg.id, result: result ?? null }), (err) => this.#send({ id: msg.id, error: { code: err.code ?? 'error', message: err.message } }));
  }

  /** Resolve with the first (new or past, from index `since`) call matching. */
  waitForCall(method, pred = () => true, { since = 0, timeout = 5000 } = {}) {
    return new Promise((resolve, reject) => {
      const check = () => {
        const hit = this.calls.slice(since).find((c) => c.method === method && pred(c.params));
        if (hit) { this.waiters.delete(check); clearTimeout(timer); resolve(hit); return true; }
        return false;
      };
      this.waiters ??= new Set();
      const timer = setTimeout(() => { this.waiters.delete(check); reject(new Error(`timeout waiting for ${method}`)); }, timeout);
      if (!check()) this.waiters.add(check);
    });
  }
  callsOf(method, pred = () => true) { return this.calls.filter((c) => c.method === method && pred(c.params)); }

  // ── fake chats ──

  /** Append an event to a chat and push it to the host as `ai.event`. */
  pushChatEvent(chatId, type, data, { send = true } = {}) {
    const list = this.chats.get(chatId) ?? [];
    this.chats.set(chatId, list);
    const event = { seq: (list.at(-1)?.seq ?? 0) + 1, type, data, at: Date.now() };
    list.push(event);
    if (send) this.notify('event', { topic: 'ai.event', data: { chatId, event } });
    return event;
  }

  #defaultHandlers() {
    return {
      'host.hello': () => ({ tipclVersion: '3.3.0', lang: 'zh-CN', home: this.home, theme: { mode: 'dark', colors: { bg: '#101014', fg: '#f0f0f0', card: '#1c1c22', border: '#33333a', accent: '#4f8cff', muted: '#888', danger: '#e5484d' }, font: 'Segoe UI' } }),
      log: () => null,
      // additive, like TipCL's PluginManager.Subscribe; probe-style permission check for one topic
      'events.subscribe': (p) => {
        if (p.topics.includes('forbidden.topic')) throw Object.assign(new Error('no permission'), { code: 'permission_denied' });
        const set = (this.subscriptions[p.plugin] ??= new Set());
        for (const t of p.topics) set.add(t);
      },
      'events.unsubscribe': (p) => { for (const t of p.topics) this.subscriptions[p.plugin]?.delete(t); },
      'tools.register': () => null,
      'tools.unregister': () => null,
      'presets.register': () => null,
      'presets.unregister': () => null,
      'ui.register': () => null,
      'ui.unregister': () => null,
      'ui.toast': () => null,
      'ui.overlayUpdate': () => null,
      'ui.openPage': () => null,
      'ui.confirm': () => true,
      'hooks.register': () => null,
      'hooks.unregister': () => null,
      'launcher.call': (p) => ({ echo: p }),
      'attachments.save': (p) => p.images.map((img) => {
        const att = { id: `att-${this.attachments.length + 1}`, mediaType: img.mediaType, name: img.name };
        this.attachments.push({ ...att, data: img.data });
        return att;
      }),
      'llm.providers': () => [{ id: 'fake', name: 'Fake' }],
      'llm.models': () => [{ id: 'm1', name: 'Model 1' }],
      'llm.stream': async (p) => {
        const prompt = JSON.stringify(p.messages);
        const words = prompt.includes('slow') ? Array.from({ length: 200 }, (_, i) => `w${i} `) : ['古池や', '蛙飛び込む', '水の音'];
        let text = '';
        for (const w of words) {
          if (this.aborted.has(p.requestId)) return { text, finish: { kind: 'aborted' } };
          text += w;
          this.notify('llm.chunk', { requestId: p.requestId, chunk: { type: 'text-delta', text: w } });
          await new Promise((r) => setTimeout(r, prompt.includes('slow') ? 30 : 5));
        }
        return { text, finish: { kind: 'stop' }, usage: { outputTokens: words.length } };
      },
      'llm.abort': (p) => { this.aborted.add(p.requestId); },
      'credentials.resolve': (p) => { const v = this.credentials[p.plugin]?.[p.name]; return v ? { value: v } : null; },
      'credentials.set': (p) => { (this.credentials[p.plugin] ??= {})[p.name] = p.value; },
      'credentials.status': (p) => Object.fromEntries(Object.entries(this.credentials[p.plugin] ?? {}).map(([k, v]) => [k, { configured: !!v }])),
      'config.get': (p) => this.config[p.plugin] ?? {},
      'config.set': (p) => {
        (this.config[p.plugin] ??= {})[p.key] = p.value;
        this.notify('plugin.configChanged', { id: p.plugin, key: p.key, value: p.value });
      },
      'ai.chats.events': (p) => {
        const list = this.chats.get(p.chatId) ?? [];
        return { seq: list.at(-1)?.seq ?? 0, events: list.filter((e) => e.seq > (p.from ?? 0)) };
      },
      'ai.chats.state': (p) => ({ running: false, seq: this.chats.get(p.chatId)?.at(-1)?.seq ?? 0 }),
      'ai.chats.send': (p) => { this.pushChatEvent(p.chatId, 'user/message', { text: p.text, source: p.mode === 'steer' ? 'steer' : 'plugin' }); return { ok: true, mode: p.mode }; },
      'ai.chats.cancel': () => ({ ok: true }),
      'ai.chats.create': () => ({ chatId: 'new-chat' }),
      'ai.chats.open': () => null,
    };
  }

  // ── HTTP helpers against the host's web server ──

  /** @returns {Promise<{status: number, headers: any, body: Buffer, text: string}>} */
  http(path, { method = 'GET', headers = {}, body, host } = {}) {
    return new Promise((resolve, reject) => {
      const req = httpRequest({ host: '127.0.0.1', port: this.port, path, method, headers: { host: host ?? `127.0.0.1:${this.port}`, ...headers } }, (res) => {
        const chunks = [];
        res.on('data', (c) => chunks.push(c));
        res.on('end', () => { const b = Buffer.concat(chunks); resolve({ status: res.statusCode, headers: res.headers, body: b, text: b.toString('utf8') }); });
        res.on('error', reject);
      });
      req.on('error', reject);
      if (body !== undefined) req.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body)); else req.end();
    });
  }

  /**
   * Open an SSE stream; returns {events, res, close, next(n)} where events collects parsed `data:` payloads.
   */
  sse(path) {
    return new Promise((resolve, reject) => {
      const req = httpRequest({ host: '127.0.0.1', port: this.port, path, headers: { host: `127.0.0.1:${this.port}`, accept: 'text/event-stream' } }, (res) => {
        const events = [];
        const waiters = new Set();
        let buf = '';
        let ended = false;
        res.setEncoding('utf8');
        res.on('data', (d) => {
          buf += d;
          let i;
          while ((i = buf.indexOf('\n\n')) >= 0) {
            const block = buf.slice(0, i);
            buf = buf.slice(i + 2);
            const data = block.split('\n').filter((l) => l.startsWith('data: ')).map((l) => l.slice(6)).join('\n');
            if (data) { try { events.push(JSON.parse(data)); } catch { events.push(data); } }
          }
          for (const w of [...waiters]) w();
        });
        const onEnd = () => { ended = true; for (const w of [...waiters]) w(); };
        res.on('end', onEnd);
        res.on('close', onEnd);
        resolve({
          status: res.statusCode, headers: res.headers, events, res,
          get ended() { return ended; },
          close: () => req.destroy(),
          /** wait until at least n events (or end) */
          until: (pred, timeout = 5000) => new Promise((ok, fail) => {
            const check = () => { if (pred(events) ) { waiters.delete(check); clearTimeout(t); ok(events); return true; } if (ended) { waiters.delete(check); clearTimeout(t); ok(events); return true; } return false; };
            const t = setTimeout(() => { waiters.delete(check); fail(new Error(`sse timeout; got ${JSON.stringify(events).slice(0, 300)}`)); }, timeout);
            if (!check()) waiters.add(check);
          }),
        });
      });
      req.on('error', reject);
      req.end();
    });
  }

  async stop() {
    try { await this.request('host.shutdown'); } catch { /* already gone */ }
    const code = await Promise.race([this.exited, new Promise((r) => setTimeout(() => r('timeout'), 3000))]);
    if (code === 'timeout') this.child.kill();
    return code;
  }
}

/** plugin.load params for a sample/fixture plugin directory. */
export async function loadParams(dir, { home, granted, config = {}, dev = false } = {}) {
  const { readFile } = await import('node:fs/promises');
  const manifest = JSON.parse(await readFile(join(dir, 'tipcl-plugin.json'), 'utf8'));
  const { createHmac } = await import('node:crypto');
  return {
    // TipCL computes the page token itself (same formula as the host) and passes it along
    pageToken: createHmac('sha256', 'test-host-token').update(manifest.id).digest('hex').slice(0, 32),
    id: manifest.id, dir, dataDir: join(home, 'plugin-data', manifest.id), main: manifest.main, client: manifest.client,
    manifest, config, granted: granted ?? manifest.permissions ?? [], dev,
  };
}
