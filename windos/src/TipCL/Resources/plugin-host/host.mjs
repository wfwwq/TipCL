#!/usr/bin/env node
// TipCL Node plugin host (DESIGN §3.1). One process runs every `runtime: "node"` plugin.
//
//   node host.mjs        env: TIPCL_HOST_TOKEN, TIPCL_HOME, TIPCL_VERSION, TIPCL_LANG  (optional TIPCL_HOST_PORT)
//
// stdin/stdout: NDJSON JSON-RPC with TipCL (host-rpc.mjs). stderr: logs, `[plugin-id] text` per line.
// Files (flat on purpose, so the exe can embed/extract them without directory mapping):
//   host.mjs           boot + protocol dispatch (this file)
//   host-rpc.mjs       NDJSON JSON-RPC
//   host-console.mjs   console → stderr with plugin attribution (AsyncLocalStorage)
//   host-loader.mjs    `tipcl:tools` / `@deepseek-ai/dsh-tools` module alias
//   host-tools.mjs     the aliased module (defineTool & co.)
//   host-plugin.mjs    loaded plugin + DSH-compatible ctx
//   host-sessions.mjs  synchronous chat event mirror
//   host-web.mjs       loopback web server (routes, view shell, static, /__tipcl/call, /__tipcl/events)
//   host-util.mjs      helpers
//   sdk.js             browser SDK served at /__tipcl/sdk.js
//   sdk-events-worker.js  SharedWorker sharing one /__tipcl/events stream per plugin across views
//   tipcl-plugin.d.ts  typings for plugin authors

import { emitLog, installConsole } from './host-console.mjs';
import { installLoader } from './host-loader.mjs';
import { Rpc } from './host-rpc.mjs';
import { WebServer } from './host-web.mjs';
import { SessionMirror } from './host-sessions.mjs';
import { BUILTIN_SERVICES, LoadedPlugin } from './host-plugin.mjs';
import { HostError, randomToken, topicMatches } from './host-util.mjs';

export const HOST_VERSION = '1.0.0';

const DEFAULT_THEME = {
  mode: 'dark',
  colors: { bg: '#1b1b1f', fg: '#ececf1', card: '#26262c', border: '#3a3a42', accent: '#4f8cff', muted: '#9a9aa6', danger: '#e5484d' },
  font: 'system-ui, "Segoe UI", "Microsoft YaHei UI", sans-serif',
};

/** Topics a plugin may receive when TipCL broadcasts an event without naming its recipients. */
function permittedTopic(plugin, topic) {
  if (topic === 'theme.changed' || topic === 'plugins.changed') return true;
  if (topic.startsWith('ai.')) return plugin.can('ai.sessions');
  if (topic === 'credentials.updated' || topic === 'config.changed') return false; // always targeted
  return plugin.can('launcher');
}

export class PluginHost {
  constructor(writeLine) {
    this.hostToken = process.env.TIPCL_HOST_TOKEN || randomToken();
    this.home = process.env.TIPCL_HOME || process.cwd();
    this.lang = process.env.TIPCL_LANG || 'zh-CN';
    this.tipclVersion = process.env.TIPCL_VERSION || '';
    this.theme = DEFAULT_THEME;
    this.rpc = new Rpc(process.stdin, writeLine);
    this.web = new WebServer(this);
    this.mirror = new SessionMirror();
    /** @type {Map<string, LoadedPlugin>} */ this.plugins = new Map();
    /** services provided by plugins. @type {Map<string, {service: any, plugin: LoadedPlugin}>} */ this.services = new Map();
    /** @type {Set<() => void>} */ this.serviceWatchers = new Set();
    /** @type {Map<string, {abort: AbortController, plugin: LoadedPlugin}>} */ this.toolCalls = new Map();
    /** @type {Map<string, import('./host-util.mjs').AsyncQueue>} */ this.llmStreams = new Map();
    /** @type {Map<string, number>} */ this.importCounts = new Map();
    /** @type {{id: string, name: string}[] | null} */ this.providerCache = null;
    this.providerFetchedAt = 0;
    /** last `events.subscribe` topic list sent per plugin */ this.sentTopics = new Map();
    this.subscriptionDirty = new Set();
    this.webPort = 0;
    /** load/unload run one at a time (env vars and import order stay per plugin) */
    this.queue = Promise.resolve();
  }

  // ── services registry (ctx.provide / ctx.get / ctx.inject) ──

  hasService(name) { return BUILTIN_SERVICES.includes(name) || this.services.has(name); }
  setService(name, entry) {
    if (entry) this.services.set(name, entry); else this.services.delete(name);
    for (const w of [...this.serviceWatchers]) w();
  }

  // ── helpers used by plugins ──

  log(pluginId, level, text) {
    this.rpc.request('log', { plugin: pluginId, level, text }).catch(() => {});
  }

  async refreshProviders(plugin) {
    if (this.providerCache && Date.now() - this.providerFetchedAt < 5000) return this.providerCache;
    this.providerFetchedAt = Date.now();
    const list = await plugin.request('llm.providers', {});
    if (Array.isArray(list)) this.providerCache = list;
    return this.providerCache;
  }

  /**
   * Sync a plugin's event subscriptions with TipCL (debounced). `events.subscribe` is ADDITIVE and
   * `events.unsubscribe` removes, so only the difference is sent. Wanted topics = node handlers + page SSE
   * topics + `ai.event` (keeps the sync session mirror current) when the plugin has `ai.sessions`.
   * If TipCL rejects a batch (e.g. one topic lacks permission) the topics are retried one by one.
   */
  updateSubscriptions(plugin) {
    if (this.subscriptionDirty.has(plugin)) return;
    this.subscriptionDirty.add(plugin);
    queueMicrotask(async () => {
      this.subscriptionDirty.delete(plugin);
      if (plugin.unloaded) return;
      const wanted = new Set(plugin.eventHandlers.map((h) => h.pattern));
      for (const t of this.web.pageTopics(plugin.id)) wanted.add(t);
      if (plugin.can('ai.sessions')) wanted.add('ai.event');
      wanted.add('theme.changed'); // keeps the host's theme (used by the view shell) current; needs no permission
      wanted.delete('config.changed'); // produced locally from plugin.configChanged
      let sent = this.sentTopics.get(plugin.id);
      if (!sent) this.sentTopics.set(plugin.id, sent = new Set());
      const add = [...wanted].filter((t) => !sent.has(t)).sort();
      const remove = [...sent].filter((t) => !wanted.has(t)).sort();
      for (const t of add) sent.add(t);
      for (const t of remove) sent.delete(t);
      if (remove.length) plugin.request('events.unsubscribe', { topics: remove }).catch(() => {});
      if (!add.length) return;
      try {
        await plugin.request('events.subscribe', { topics: add });
      } catch (err) {
        if (add.length === 1) { sent.delete(add[0]); emitLog(plugin.id, 'warn', `events.subscribe ${add[0]}: ${err.message}`); return; }
        for (const t of add) {
          try { await plugin.request('events.subscribe', { topics: [t] }); } catch (e) { sent.delete(t); emitLog(plugin.id, 'warn', `events.subscribe ${t}: ${e.message}`); }
        }
      }
    });
  }

  themeFor(modeOverride) {
    if (modeOverride !== 'light' && modeOverride !== 'dark') return this.theme;
    return { ...this.theme, mode: modeOverride };
  }

  setTheme(t) {
    if (!t || typeof t !== 'object') return;
    // accept {mode, colors, font} or the event shape {theme:'dark', colors}
    const mode = t.mode ?? t.theme ?? this.theme.mode;
    this.theme = { mode, colors: { ...this.theme.colors, ...(t.colors ?? {}) }, font: t.font ?? this.theme.font };
  }

  // ── protocol ──

  serial(fn) {
    const run = this.queue.then(fn, fn);
    this.queue = run.catch(() => {});
    return run;
  }

  install() {
    const rpc = this.rpc;

    rpc.handle('plugin.load', (p) => this.serial(async () => {
      const id = String(p?.id ?? '');
      if (!/^[a-z0-9][a-z0-9-_.]{1,63}$/.test(id)) throw new HostError('invalid_params', `bad plugin id: ${id}`);
      const old = this.plugins.get(id);
      if (old) { await old.unload(); this.plugins.delete(id); }
      this.sentTopics.delete(id); // re-send every topic (subscribe is additive and idempotent)
      const plugin = new LoadedPlugin(this, p);
      this.plugins.set(id, plugin);
      try {
        await plugin.load();
      } catch (err) {
        emitLog(id, 'error', `load failed: ${err?.stack ?? err}`);
        await plugin.unload().catch(() => {});
        this.plugins.delete(id);
        return { ok: false, error: String(err?.message ?? err) };
      }
      this.updateSubscriptions(plugin);
      return { ok: true, pageToken: plugin.pageToken, webPort: this.webPort, ...(plugin.loadErrors.length ? { warnings: plugin.loadErrors } : {}) };
    }));

    rpc.handle('plugin.unload', (p) => this.serial(async () => {
      const plugin = this.plugins.get(String(p?.id));
      if (!plugin) return { ok: true };
      await plugin.unload();
      this.plugins.delete(plugin.id);
      this.sentTopics.delete(plugin.id);
      return { ok: true };
    }));

    rpc.on('plugin.configChanged', (p) => { this.plugins.get(String(p?.id))?.setConfig(p.key, p.value); });

    rpc.handle('tool.execute', async (p) => {
      const plugin = this.plugins.get(String(p?.plugin));
      if (!plugin) return { content: [{ type: 'text', text: `plugin ${p?.plugin} is not loaded` }], isError: true };
      return plugin.executeTool(p);
    });
    rpc.on('tool.cancel', (p) => { this.toolCalls.get(String(p?.callId))?.abort.abort(new Error('cancelled')); });

    rpc.on('llm.chunk', (p) => { this.llmStreams.get(String(p?.requestId))?.push(p.chunk); });

    rpc.handle('hook.invoke', async (p) => {
      const plugin = this.plugins.get(String(p?.plugin));
      if (!plugin) return null;
      return plugin.invokeHook(String(p.hook), p.data);
    });

    rpc.on('action.invoke', (p) => {
      const plugin = this.plugins.get(String(p?.plugin));
      const fn = plugin?.actions.get(String(p?.id));
      if (fn) plugin.run(() => Promise.resolve().then(() => fn(p.context ?? {})).catch((e) => emitLog(plugin.id, 'error', `action ${p.id}: ${e?.stack ?? e}`)));
    });

    rpc.on('ui.event', (p) => {
      const plugin = this.plugins.get(String(p?.plugin));
      if (!plugin) return;
      for (const fn of plugin.uiListeners) plugin.run(() => { try { fn(p); } catch (e) { emitLog(plugin.id, 'error', `ui.event: ${e?.stack ?? e}`); } });
    });

    rpc.on('event', (p) => this.onEvent(p ?? {}));

    rpc.handle('host.ping', () => ({ ok: true, hostVersion: HOST_VERSION, webPort: this.webPort, plugins: [...this.plugins.keys()] }));

    rpc.handle('host.shutdown', async () => {
      await this.shutdown();
      setTimeout(() => process.exit(0), 50);
      return { ok: true };
    });

    rpc.onClose = () => { void this.shutdown().finally(() => process.exit(0)); };
  }

  /** `event {topic, data, plugin?, plugins?}` from TipCL. */
  onEvent({ topic, data, plugin: target, plugins: targets }) {
    if (typeof topic !== 'string') return;
    if (topic === 'ai.event' && data) this.mirror.apply(String(data.chatId), data.event);
    if (topic === 'theme.changed') {
      this.setTheme(data);
      data = this.theme;
      this.web.publish(null, 'theme.changed', data);
      for (const pl of this.plugins.values()) pl.dispatchEvent(topic, data);
      return;
    }
    let recipients;
    if (target) recipients = [this.plugins.get(String(target))].filter(Boolean);
    else if (Array.isArray(targets)) recipients = targets.map((id) => this.plugins.get(String(id))).filter(Boolean);
    else recipients = [...this.plugins.values()].filter((pl) => permittedTopic(pl, topic));
    for (const pl of recipients) {
      pl.dispatchEvent(topic, data);
      this.web.publish(pl.id, topic, data);
    }
  }

  async shutdown() {
    if (this.shuttingDown) return this.shuttingDown;
    this.shuttingDown = (async () => {
      for (const plugin of [...this.plugins.values()].reverse()) await plugin.unload().catch(() => {});
      this.plugins.clear();
      this.web.close();
    })();
    return this.shuttingDown;
  }

  async start() {
    this.install();
    this.webPort = await this.web.listen(Number(process.env.TIPCL_HOST_PORT || 0));
    emitLog(null, 'info', `TipCL plugin host ${HOST_VERSION} on node ${process.version}, web http://127.0.0.1:${this.webPort}`);
    try {
      const hello = await this.rpc.request('host.hello', { hostVersion: HOST_VERSION, webPort: this.webPort, pid: process.pid });
      if (hello?.lang) this.lang = hello.lang;
      if (hello?.home) this.home = hello.home;
      if (hello?.tipclVersion) this.tipclVersion = hello.tipclVersion;
      if (hello?.theme) this.setTheme(hello.theme);
    } catch (err) {
      emitLog(null, 'warn', `host.hello failed: ${err.message}`);
    }
  }
}

// ── boot ─────────────────────────────────────────────────────────────────

const writeLine = installConsole();
installLoader();
process.on('uncaughtException', (err) => emitLog(null, 'error', `uncaught: ${err?.stack ?? err}`));
process.on('unhandledRejection', (err) => emitLog(null, 'error', `unhandled rejection: ${err?.stack ?? err}`));
const host = new PluginHost(writeLine);
await host.start();
