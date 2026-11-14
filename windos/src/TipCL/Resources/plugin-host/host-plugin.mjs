// TipCL plugin host: one loaded Node plugin and the DSH-compatible `ctx` it receives (DESIGN §8).

import { mkdir } from 'node:fs/promises';
import { existsSync, readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { createRequire } from 'node:module';
import { dirname, join, resolve as resolvePath, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { formatWithOptions } from 'node:util';
import { pluginScope, emitLog } from './host-console.mjs';
import { AsyncQueue, HostError, pageTokenFor, toBase64, toJsonSchema, topicMatches } from './host-util.mjs';

/** Service names the host itself provides to every plugin (cannot be overridden by `ctx.provide`). */
export const BUILTIN_SERVICES = ['llm', 'credentials', 'sessions', 'attachments', 'tools', 'connection', 'presets',
  'launcher', 'events', 'hooks', 'config', 'ui', 'logger', 'paths'];

// ── Scope: a stack of disposers, disposed in reverse order ───────────────

export class Scope {
  /** @param {LoadedPlugin} plugin @param {Scope | null} parent */
  constructor(plugin, parent = null) {
    this.plugin = plugin;
    /** @type {{fn: () => any, label?: string}[]} */
    this.stack = [];
    this.disposed = false;
    if (parent) parent.add(() => this.dispose(), 'inject scope');
  }

  /**
   * Track a disposer. Returns an idempotent dispose function that also untracks it.
   * @param {() => any} fn @param {string} [label]
   */
  add(fn, label) {
    const entry = { fn, label };
    let done = false;
    const dispose = () => {
      if (done) return;
      done = true;
      const i = this.stack.indexOf(entry);
      if (i >= 0) this.stack.splice(i, 1);
      return runDisposer(this.plugin, entry);
    };
    entry.fn = () => { done = true; return fn(); };
    if (this.disposed) { dispose(); return () => {}; }
    this.stack.push(entry);
    return dispose;
  }

  /** Dispose everything, newest first. Awaits async disposers one by one. */
  async dispose() {
    if (this.disposed) return;
    this.disposed = true;
    while (this.stack.length) {
      const entry = this.stack.pop();
      await runDisposer(this.plugin, entry);
    }
  }
}

async function runDisposer(plugin, entry) {
  try {
    await pluginScope.run(plugin.id, () => entry.fn());
  } catch (err) {
    emitLog(plugin.id, 'error', `dispose ${entry.label ?? ''} failed: ${err?.stack ?? err}`);
  }
  if (entry.label) plugin.host.log(plugin.id, 'debug', `effect disposed: ${entry.label}`);
}

// ── LoadedPlugin ──────────────────────────────────────────────────────────

export class LoadedPlugin {
  /**
   * @param {import('./host.mjs').PluginHost} host
   * @param {any} p  plugin.load params {id, dir, dataDir, main, client, manifest, config, granted, dev}
   */
  constructor(host, p) {
    this.host = host;
    this.id = String(p.id);
    this.dir = resolvePath(String(p.dir));
    this.dataDir = resolvePath(String(p.dataDir ?? join(host.home, 'plugin-data', this.id)));
    this.cacheDir = p.cacheDir ? resolvePath(String(p.cacheDir)) : join(this.dataDir, '.cache');
    this.logFile = p.logFile ? String(p.logFile) : join(host.home, 'plugin-logs', `${this.id}.log`);
    this.manifest = p.manifest ?? {};
    this.mainSpec = p.main ?? this.manifest.main;
    this.client = p.client ?? this.manifest.client ?? null;
    this.granted = new Set(Array.isArray(p.granted) ? p.granted : []);
    this.dev = !!p.dev;
    this.version = this.manifest.version ?? '0.0.0';
    this.pageToken = typeof p.pageToken === 'string' && p.pageToken ? p.pageToken : pageTokenFor(host.hostToken, this.id);
    /** current config values (manifest defaults applied) */
    this.config = applyDefaults(this.manifest.config, p.config);
    this.scope = new Scope(this);
    /** @type {Map<string, {tool: any, scope: Scope}>} */ this.tools = new Map();
    /** @type {Map<string, {fn: Function, priority: number}[]>} */ this.hooks = new Map();
    /** @type {Map<string, Function>} */ this.actions = new Map();
    /** registered UI contributions, `${kind}:${id}` → {kind, id, view, ...} (view shell falls back to these) */
    /** @type {Map<string, any>} */ this.uiSpecs = new Map();
    /** @type {{pattern: string, fn: Function}[]} */ this.eventHandlers = [];
    /** @type {Set<Function>} */ this.configListeners = new Set();
    /** @type {Set<Function>} */ this.uiListeners = new Set();
    /** pending protocol registrations made during apply (awaited before plugin.load replies) */
    /** @type {Promise<any>[]} */ this.pending = [];
    this.loadErrors = [];
    this.unloaded = false;
  }

  can(permission) { return this.granted.has(permission); }

  /** Send a request to TipCL as this plugin. */
  request(method, params = {}) { return this.host.rpc.request(method, { ...params, plugin: this.id }); }

  /** Fire a registration request; remember it so load can surface failures. */
  register(method, params, what) {
    const p = this.request(method, params).catch((err) => {
      const msg = `${what}: ${err.code ?? ''} ${err.message}`;
      this.loadErrors.push(msg);
      emitLog(this.id, 'warn', msg);
    });
    this.pending.push(p);
    return p;
  }

  /** Run plugin code attributed to this plugin (console prefix, ALS). */
  run(fn) { return pluginScope.run(this.id, fn); }

  // ── loading ──

  async load() {
    await mkdir(this.dataDir, { recursive: true }).catch(() => {});
    // DSH compat: DSH_HOME = plugin-data root, so `$DSH_HOME/<id>` is exactly ctx.paths.data.
    process.env.DSH_HOME = dirname(this.dataDir);
    // Process-wide! Only reliable while this plugin's module is evaluated and apply() runs.
    process.env.TIPCL_PLUGIN_DATA = this.dataDir;
    if (this.can('ai.llm')) await this.host.refreshProviders(this).catch(() => {});

    const entries = (Array.isArray(this.mainSpec) ? this.mainSpec : [this.mainSpec]).filter(Boolean);
    if (!entries.length) throw new HostError('bad_manifest', 'manifest has no "main"');
    const modules = [];
    for (const entry of entries) modules.push(await this.run(() => this.importEntry(String(entry))));

    for (const mod of modules) {
      const plugin = pickPluginExport(mod);
      if (!plugin) throw new HostError('bad_module', 'entry exports no apply()');
      // module-level `inject`: names of services the entry needs (string[] or {required, optional})
      const needs = Array.isArray(plugin.inject) ? plugin.inject : (plugin.inject?.required ?? []);
      const missing = needs.filter((n) => !this.host.hasService(n));
      if (missing.length) throw new HostError('missing_service', `required service(s) not available: ${missing.join(', ')}`);
      // schemastery-style `Config` export: use it to fill defaults for undeclared keys (best effort)
      if (typeof plugin.Config === 'function' || typeof mod.Config === 'function') {
        try {
          const filled = (plugin.Config ?? mod.Config)({ ...this.config });
          if (filled && typeof filled === 'object') for (const [k, v] of Object.entries(filled)) if (this.config[k] === undefined) this.config[k] = v;
        } catch { /* not a schemastery schema, or validation failed: ignore */ }
      }
      const ctx = createContext(this, this.scope);
      process.env.TIPCL_PLUGIN_DATA = this.dataDir;
      await this.run(() => plugin.apply(ctx, configCells(this)));
    }
    await Promise.allSettled(this.pending);
    this.pending = [];
  }

  /** Import one entry (ESM via import(), CJS via require so reloads can clear the cache). */
  async importEntry(entry) {
    const file = resolvePath(this.dir, entry);
    if (!file.startsWith(this.dir + sep) && file !== this.dir) throw new HostError('bad_manifest', `main outside plugin dir: ${entry}`);
    if (isCommonJs(file)) {
      const req = createRequire(file);
      for (const key of Object.keys(req.cache)) if (key.startsWith(this.dir + sep)) delete req.cache[key];
      return req(file);
    }
    const n = (this.host.importCounts.get(file) ?? 0) + 1;
    this.host.importCounts.set(file, n);
    // second load of the same file (reload): bust the ESM cache for the entry module
    const url = pathToFileURL(file).href + (n > 1 ? `?reload=${n}` : '');
    return import(url);
  }

  async unload() {
    if (this.unloaded) return;
    this.unloaded = true;
    await this.scope.dispose();
    for (const [, ctl] of this.host.toolCalls) if (ctl.plugin === this) ctl.abort.abort(new Error('plugin unloaded'));
    this.host.web.closePlugin(this.id);
  }

  // ── inbound dispatch ──

  async executeTool(p) {
    const entry = this.tools.get(String(p.name));
    if (!entry) return { content: [{ type: 'text', text: `tool ${p.name} is not registered` }], isError: true };
    const { tool } = entry;
    const abort = new AbortController();
    const callId = String(p.callId ?? randomUUID());
    this.host.toolCalls.set(callId, { abort, plugin: this });
    const chatId = p.chatId ?? null;
    try {
      // make sure the sync session mirror has this chat (normally already true: events precede tool.execute)
      if (chatId && this.can('ai.sessions')) {
        await this.host.mirror.backfill(chatId, () => this.request('ai.chats.events', { chatId, from: 0 }));
      }
      const args = p.args ?? {};
      const exec = {
        callId, chatId, signal: abort.signal, messageText: p.messageText ?? '',
        agent: { id: chatId }, session: { id: chatId }, plugin: this.id,
      };
      const value = await this.run(() => tool.execute(args, exec));
      if (value && typeof value === 'object' && Array.isArray(value.content) && !tool.output?.render) {
        return { content: await normalizeContent(this, value.content), isError: !!value.isError };
      }
      const parts = tool.output?.render ? await this.run(() => tool.output.render(args, value)) : defaultParts(value);
      return { content: await normalizeContent(this, parts), isError: false };
    } catch (err) {
      const aborted = abort.signal.aborted;
      if (!aborted) emitLog(this.id, 'error', `tool ${p.name} failed: ${err?.stack ?? err}`);
      return { content: [{ type: 'text', text: aborted ? `cancelled: ${abort.signal.reason?.message ?? 'stopped'}` : `Error: ${err?.message ?? err}` }], isError: true };
    } finally {
      this.host.toolCalls.delete(callId);
    }
  }

  async invokeHook(hook, data) {
    const handlers = [...(this.hooks.get(hook) ?? [])].sort((a, b) => b.priority - a.priority);
    let current = data;
    let changed = false;
    for (const h of handlers) {
      try {
        const out = await this.run(() => h.fn(current));
        if (out !== undefined && out !== null) { current = out; changed = true; }
      } catch (err) {
        emitLog(this.id, 'error', `hook ${hook} failed: ${err?.stack ?? err}`);
      }
    }
    return changed ? current : null;
  }

  dispatchEvent(topic, data) {
    for (const h of [...this.eventHandlers]) {
      if (!topicMatches(h.pattern, topic)) continue;
      this.run(() => { try { const r = h.fn(data, topic); r?.catch?.((e) => emitLog(this.id, 'error', `event ${topic}: ${e?.stack ?? e}`)); } catch (err) { emitLog(this.id, 'error', `event ${topic}: ${err?.stack ?? err}`); } });
    }
  }

  setConfig(key, value) {
    if (this.config[key] === value) return;
    this.config[key] = value;
    for (const fn of [...this.configListeners]) this.run(() => { try { fn(key, value, { ...this.config }); } catch (err) { emitLog(this.id, 'error', `config listener: ${err?.stack ?? err}`); } });
    this.dispatchEvent('config.changed', { key, value });
    this.host.web.publish(this.id, 'config.changed', { key, value });
  }
}

// ── ctx ───────────────────────────────────────────────────────────────────

/**
 * Build the ctx object for one scope (the plugin's root scope, or a `ctx.inject` sub-scope).
 * Registrations made through it are tracked in that scope and undone when it is disposed.
 * @param {LoadedPlugin} plugin @param {Scope} scope
 */
export function createContext(plugin, scope) {
  const host = plugin.host;
  const track = (fn, label) => scope.add(fn, label);

  const logger = Object.fromEntries(['debug', 'info', 'warn', 'error'].map((level) => [level,
    (...args) => host.log(plugin.id, level, formatWithOptions({ colors: false, depth: 6 }, ...args))]));

  const paths = {
    plugin: plugin.dir,
    data: plugin.dataDir,
    cache: plugin.cacheDir,
    logs: dirname(plugin.logFile),
    logFile: plugin.logFile,
  };

  const tools = {
    /** @param {any} tool DSH-style or JSON-Schema tool definition */
    register(tool) {
      if (!tool || typeof tool.name !== 'string' || typeof tool.execute !== 'function') throw new TypeError('tool needs name and execute()');
      const name = tool.name;
      plugin.tools.set(name, { tool, scope });
      const spec = {
        name,
        description: String(tool.description ?? ''),
        parameters: toJsonSchema(tool.parameters ?? tool.inputSchema ?? tool.input),
        ...(tool.timeoutMs ? { timeoutMs: tool.timeoutMs } : {}),
        ...(tool.label ? { label: typeof tool.label === 'function' ? undefined : tool.label } : {}),
      };
      plugin.register('tools.register', { tool: spec }, `tools.register ${name}`);
      return track(() => {
        if (plugin.tools.get(name)?.tool === tool) plugin.tools.delete(name);
        return plugin.request('tools.unregister', { name }).catch(() => {});
      }, `tool ${name}`);
    },
  };

  const presets = {
    register(preset) {
      const p = { ...preset };
      if (!p.systemPrompt && p.systemPromptFile) {
        try { p.systemPrompt = readFileSync(resolvePath(plugin.dir, p.systemPromptFile), 'utf8'); delete p.systemPromptFile; } catch { /* TipCL may resolve it */ }
      }
      plugin.register('presets.register', { preset: p }, `presets.register ${p.id}`);
      return track(() => plugin.request('presets.unregister', { id: p.id }).catch(() => {}), `preset ${p.id}`);
    },
  };

  const connection = {
    fetch: {
      /** @param {{path: string, methods?: string[], fetch: (req: Request) => Response | Promise<Response>, requestBody?: 'buffered' | 'stream'}} spec */
      register(spec) {
        const remove = host.web.addRoute(plugin, spec);
        return track(remove, `route ${spec.path}`);
      },
    },
  };

  const attachments = {
    /** @param {{data: Uint8Array | Buffer | string, mediaType?: string, name?: string}[]} images */
    async saveImages(images) {
      const list = (Array.isArray(images) ? images : [images]).map((img) => ({
        data: toBase64(img.data), mediaType: img.mediaType ?? 'image/png', ...(img.name ? { name: img.name } : {}),
      }));
      const saved = await plugin.request('attachments.save', { images: list });
      return (saved ?? []).map((a, i) => ({ mediaType: list[i]?.mediaType, ...a }));
    },
  };

  const events = {
    /** @param {string} topic (supports `*`) @param {(data: any, topic: string) => void} fn */
    on(topic, fn) {
      const h = { pattern: String(topic), fn };
      plugin.eventHandlers.push(h);
      host.updateSubscriptions(plugin);
      return track(() => {
        const i = plugin.eventHandlers.indexOf(h);
        if (i >= 0) plugin.eventHandlers.splice(i, 1);
        host.updateSubscriptions(plugin);
      }, `event ${topic}`);
    },
  };

  const launcher = {
    /** Call any BackendApi method. `kwargs` may also be an array of positional args. */
    call(method, kwargs, args) {
      if (Array.isArray(kwargs)) return plugin.request('launcher.call', { method, args: kwargs });
      return plugin.request('launcher.call', { method, kwargs: kwargs ?? {}, ...(args ? { args } : {}) });
    },
    on: events.on,
  };

  const hooks = {
    /** @param {string} hook @param {(data: any) => any} fn @param {{priority?: number}} [opts] */
    register(hook, fn, opts = {}) {
      const list = plugin.hooks.get(hook) ?? [];
      const entry = { fn, priority: Number(opts.priority ?? 0) };
      list.push(entry);
      plugin.hooks.set(hook, list);
      if (list.length === 1) plugin.register('hooks.register', { hook, priority: entry.priority }, `hooks.register ${hook}`);
      return track(() => {
        const l = plugin.hooks.get(hook) ?? [];
        const i = l.indexOf(entry);
        if (i >= 0) l.splice(i, 1);
        if (!l.length) { plugin.hooks.delete(hook); return plugin.request('hooks.unregister', { hook }).catch(() => {}); }
      }, `hook ${hook}`);
    },
  };

  const config = {
    get: () => ({ ...plugin.config }),
    async set(key, value) { plugin.setConfig(key, value); await plugin.request('config.set', { key, value }); },
    /** @param {(key: string, value: any, all: object) => void} fn */
    onChange(fn) { plugin.configListeners.add(fn); return track(() => plugin.configListeners.delete(fn)); },
    cells: () => configCells(plugin),
  };

  const viewUrl = (kind, id, view) => `http://127.0.0.1:${host.webPort}/__tipcl/view/${encodeURIComponent(plugin.id)}/${encodeURIComponent(view)}?k=${plugin.pageToken}&kind=${kind}&id=${encodeURIComponent(id)}`;
  const uiRegister = (kind, spec) => {
    const id = spec.id ?? kind;
    const full = { ...spec, id, ...(spec.view ? { url: viewUrl(kind, id, spec.view) } : {}) };
    plugin.uiSpecs.set(`${kind}:${id}`, { kind, ...full });
    plugin.register('ui.register', { kind, spec: full }, `ui.register ${kind} ${id}`);
    return track(() => {
      plugin.uiSpecs.delete(`${kind}:${id}`);
      return plugin.request('ui.unregister', { kind, id }).catch(() => {});
    }, `ui ${kind} ${id}`);
  };
  const ui = {
    page: (spec) => uiRegister('page', spec),
    aiView: (spec) => uiRegister('aiView', spec),
    dashCard: (spec) => uiRegister('dashCard', spec),
    /**
     * 页面区块(kind=`block`):块 id 的命名空间是 `plugin:<本插件>:<块 id>`(布局文档 pages[].blocks[] 里引用它)。
     * 与 dashCard 同形状;插件被禁用/卸载后块**保留**,界面显示「不可用」。
     * @param {{ id: string, label?: string, view: string, group?: string }} spec
     */
    block: (spec) => uiRegister('block', spec),
    overlay: (spec) => uiRegister('overlay', spec),
    settings: (spec) => uiRegister('settings', { id: 'settings', ...spec }),
    /** @param {any} spec @param {(context: any) => void} handler */
    action(spec, handler) {
      if (handler) plugin.actions.set(spec.id, handler);
      const off = uiRegister('action', spec);
      return () => { plugin.actions.delete(spec.id); off(); };
    },
    toast: (text, kind = 'info') => plugin.request('ui.toast', { text: String(text), kind }),
    confirm: (title, text, opts = {}) => plugin.request('ui.confirm', typeof title === 'object' ? title : { title, text, ...opts }),
    prompt: (title, text, opts = {}) => plugin.request('ui.prompt', typeof title === 'object' ? title : { title, text, ...opts }),
    openPage: (page) => plugin.request('ui.openPage', { page }),
    openExternal: (url) => plugin.request('ui.openExternal', { url }),
    overlayUpdate: (id, patch) => plugin.request('ui.overlayUpdate', typeof id === 'object' ? id : { id, ...patch }),
    /** `ui.event` notifications (page opened/closed, overlay clicked...) */
    onEvent(fn) { plugin.uiListeners.add(fn); return track(() => plugin.uiListeners.delete(fn)); },
    /** URL of a view of this plugin (for opening in a browser on Win7, etc.) */
    viewUrl: (view, kind = 'page', id = view) => viewUrl(kind, id, view),
  };

  const llm = createLlmService(plugin);
  const credentials = {
    /** @returns {Promise<{value: string} | undefined>} */
    async resolve(name) { const r = await plugin.request('credentials.resolve', { name }); return r ?? undefined; },
    set: (name, value) => plugin.request('credentials.set', { name, value: value ?? null }),
    status: () => plugin.request('credentials.status', {}),
    onChange: (fn) => events.on('credentials.updated', fn),
  };
  const sessions = createSessionsService(plugin, track);

  const builtins = { llm, credentials, sessions, attachments, tools, connection, presets, launcher, events, hooks, config, ui, logger, paths };

  const ctx = {
    id: plugin.id,
    name: plugin.id,
    manifest: plugin.manifest,
    version: plugin.version,
    paths,
    logger,
    tools, presets, connection, attachments, launcher, events, hooks, config, ui,
    /** Ask TipCL to unload and load this plugin again (DESIGN §4 `plugin.reload`); resolves once the request is accepted. */
    reload: () => plugin.request('plugin.reload', {}),
    /** @param {() => (void | (() => any))} setup @param {string} [label] */
    effect(setup, label) {
      let cleanup;
      try { cleanup = setup(); } catch (err) { emitLog(plugin.id, 'error', `effect ${label ?? ''} failed: ${err?.stack ?? err}`); throw err; }
      const fn = typeof cleanup === 'function' ? cleanup
        : cleanup && typeof cleanup.dispose === 'function' ? () => cleanup.dispose()
        : cleanup && typeof cleanup.then === 'function' ? async () => { const c = await cleanup; if (typeof c === 'function') return c(); }
        : () => {};
      return track(fn, label);
    },
    /** @param {string} name */
    get(name) {
      if (Object.prototype.hasOwnProperty.call(builtins, name)) return builtins[name];
      return host.services.get(name)?.service;
    },
    /** Provide a service to other plugins (removed when this scope is disposed). */
    provide(name, service) {
      if (BUILTIN_SERVICES.includes(name)) throw new Error(`"${name}" is a built-in service`);
      const existing = host.services.get(name);
      if (existing && existing.plugin !== plugin) throw new Error(`service "${name}" is already provided by ${existing.plugin.id}`);
      host.setService(name, { service, plugin });
      return track(() => { if (host.services.get(name)?.service === service) host.setService(name, null); }, `provide ${name}`);
    },
    /**
     * Run `callback(subCtx)` when all `names` are available; undo it when one disappears; re-run on return.
     * @param {string[] | {required?: string[]}} names @param {(ctx: any) => void} callback
     */
    inject(names, callback) {
      const list = Array.isArray(names) ? names : (names?.required ?? []);
      /** @type {Scope | null} */
      let sub = null;
      const check = () => {
        const ok = list.every((n) => host.hasService(n));
        if (ok && !sub && !scope.disposed) {
          sub = new Scope(plugin, scope);
          const subCtx = createContext(plugin, sub);
          plugin.run(() => { try { const r = callback(subCtx); r?.catch?.((e) => emitLog(plugin.id, 'error', `inject callback: ${e?.stack ?? e}`)); } catch (err) { emitLog(plugin.id, 'error', `inject callback: ${err?.stack ?? err}`); } });
        } else if (!ok && sub) {
          const s = sub; sub = null; void s.dispose();
        }
      };
      host.serviceWatchers.add(check);
      check();
      return track(() => { host.serviceWatchers.delete(check); if (sub) { const s = sub; sub = null; return s.dispose(); } }, `inject ${list.join(',')}`);
    },
    /** Plugin-relative path. */
    resolve: (...p) => resolvePath(plugin.dir, ...p),
  };

  // DSH lets plugins read services as properties (`ctx.liveBody`); fall back to ctx.get for unknown keys.
  return new Proxy(ctx, {
    get(target, key, receiver) {
      if (typeof key === 'symbol' || key in target) return Reflect.get(target, key, receiver);
      if (key === 'then') return undefined;
      return target.get(key);
    },
  });
}

// ── services ──────────────────────────────────────────────────────────────

/** LLM service: `listProviders()` is synchronous (DSH code uses it without await) from a cache. */
function createLlmService(plugin) {
  const host = plugin.host;
  return {
    listProviders() {
      if (!plugin.can('ai.llm')) return [];
      void host.refreshProviders(plugin).catch(() => {});
      return [...(host.providerCache ?? [])];
    },
    listModels: (provider) => plugin.request('llm.models', { provider }),
    /**
     * Stream a completion. Yields `{type:'text-delta', text}`, `{type:'reasoning-delta', text}`, ...,
     * and finally `{type:'finish', reason:{kind}, usage?}`. Aborting `signal` sends `llm.abort` and throws.
     */
    stream(req) {
      return llmStream(plugin, req);
    },
    /** Convenience: whole text of one completion. */
    async complete(req) {
      let text = '';
      for await (const c of llmStream(plugin, req)) if (c.type === 'text-delta') text += c.text;
      return text;
    },
  };
}

async function* llmStream(plugin, req) {
  const host = plugin.host;
  const { signal, ...rest } = req ?? {};
  signal?.throwIfAborted();
  const requestId = randomUUID();
  const queue = new AsyncQueue();
  host.llmStreams.set(requestId, queue);
  const params = {
    requestId,
    provider: rest.provider,
    model: rest.model,
    ...(rest.system !== undefined ? { system: rest.system } : {}),
    messages: (rest.messages ?? []).map(normalizeMessage),
    ...(rest.maxTokens !== undefined ? { maxTokens: rest.maxTokens } : {}),
    ...(rest.temperature !== undefined ? { temperature: rest.temperature } : {}),
    ...(rest.reasoningEffort !== undefined ? { reasoningEffort: rest.reasoningEffort } : {}),
    ...(rest.tools !== undefined ? { tools: rest.tools } : {}),
    ...(rest.purpose !== undefined ? { purpose: rest.purpose } : {}),
  };
  let finished = false;
  const onAbort = () => {
    if (!finished) plugin.request('llm.abort', { requestId }).catch(() => {});
    queue.fail(signal.reason ?? new DOMException('aborted', 'AbortError'));
  };
  signal?.addEventListener('abort', onAbort, { once: true });
  plugin.request('llm.stream', params).then((r) => queue.end(r), (e) => queue.fail(e));
  let sawFinish = false;
  try {
    for await (const chunk of queue) {
      if (chunk?.type === 'finish') sawFinish = true;
      yield chunk;
    }
    finished = true;
    if (!sawFinish) {
      const r = queue.result ?? {};
      yield { type: 'finish', reason: r.finish ?? { kind: 'stop' }, ...(r.usage ? { usage: r.usage } : {}) };
    }
  } finally {
    signal?.removeEventListener('abort', onAbort);
    host.llmStreams.delete(requestId);
    // consumer stopped early (break / throw): stop the request too
    if (!queue.ended) { queue.fail(new Error('consumer stopped')); plugin.request('llm.abort', { requestId }).catch(() => {}); }
  }
}

/** DSH messages carry `id`, `source` and content-part arrays; TipCL takes `{role, content}`. */
function normalizeMessage(m) {
  let content = m?.content;
  if (Array.isArray(content)) {
    const parts = content.map((p) => {
      if (typeof p === 'string') return { type: 'text', text: p };
      if (p?.type === 'image') return { type: 'image', mediaType: p.mediaType ?? 'image/png', ...(p.attachment ? { attachment: { id: p.attachment.id ?? p.attachment } } : { data: toBase64(p.data) }) };
      return p;
    });
    content = parts.every((p) => p?.type === 'text') ? parts.map((p) => p.text).join('') : parts;
  }
  return { role: m?.role ?? 'user', content: content ?? '' };
}

/** Sessions service: synchronous mirror reads + async chat control (needs `ai.sessions`). */
function createSessionsService(plugin, track) {
  const host = plugin.host;
  const mirror = host.mirror;
  const fetcher = (chatId) => () => plugin.request('ai.chats.events', { chatId, from: 0 });
  const view = (chatId) => {
    const chat = mirror.chat(chatId);
    if (!chat.filled) void mirror.backfill(chatId, fetcher(chatId));
    return {
      id: chatId,
      chatId,
      get seq() { return chat.seq; },
      get running() { return chat.running; },
      /** events with seq > from, synchronously */
      snapshotEvents: (from = 0) => mirror.snapshot(chatId, from),
      getSnapshot: () => ({ seq: chat.seq, running: chat.running }),
      send: (text, opts = {}) => plugin.request('ai.chats.send', { chatId, text, mode: opts.mode ?? 'auto', ...(opts.images ? { images: opts.images } : {}) }),
      cancel: () => plugin.request('ai.chats.cancel', { chatId }),
      open: (opts = {}) => plugin.request('ai.chats.open', { chatId, ...opts }),
      /** wait until the mirror has back-filled */
      ready: () => mirror.backfill(chatId, fetcher(chatId)),
    };
  };
  return {
    /** Sync. Returns undefined without the `ai.sessions` permission. */
    get(chatId) {
      if (!plugin.can('ai.sessions') || !chatId) return undefined;
      return view(String(chatId));
    },
    list: () => plugin.request('ai.chats.list', {}),
    create: (opts = {}) => plugin.request('ai.chats.create', opts),
    send: (chatId, text, opts = {}) => plugin.request('ai.chats.send', { chatId, text, mode: opts.mode ?? 'auto', ...(opts.images ? { images: opts.images } : {}) }),
    cancel: (chatId) => plugin.request('ai.chats.cancel', { chatId }),
    open: (chatId, opts = {}) => plugin.request('ai.chats.open', { chatId, ...opts }),
    state: (chatId) => plugin.request('ai.chats.state', { chatId }),
    events: (chatId, from) => plugin.request('ai.chats.events', { chatId, ...(from !== undefined ? { from } : {}) }),
    /** Live events of one chat (sync callback from the mirror). */
    subscribe(chatId, fn) {
      if (!plugin.can('ai.sessions')) return () => {};
      const chat = mirror.chat(String(chatId));
      const wrapped = (e) => plugin.run(() => fn(e));
      chat.listeners.add(wrapped);
      return track(() => chat.listeners.delete(wrapped));
    },
  };
}

// ── helpers ───────────────────────────────────────────────────────────────

function applyDefaults(schema, values) {
  const out = { ...(values ?? {}) };
  if (schema && typeof schema === 'object') {
    for (const [k, spec] of Object.entries(schema)) if (out[k] === undefined && spec && 'default' in spec) out[k] = spec.default;
  }
  return out;
}

/** `{key: {get()}}` cells (DSH `config.x.get()`); any key works, undeclared keys read undefined. */
export function configCells(plugin) {
  const cache = new Map();
  return new Proxy({}, {
    get(_t, key) {
      if (typeof key !== 'string' || key === 'then') return undefined;
      let cell = cache.get(key);
      if (!cell) { cell = { get: () => plugin.config[key], set: (v) => plugin.setConfig(key, v) }; cache.set(key, cell); }
      return cell;
    },
    has: (_t, key) => typeof key === 'string',
    ownKeys: () => Object.keys(plugin.config),
    getOwnPropertyDescriptor: (_t, key) => (typeof key === 'string' ? { enumerable: true, configurable: true, value: { get: () => plugin.config[key] } } : undefined),
  });
}

function pickPluginExport(mod) {
  if (!mod) return null;
  if (typeof mod.apply === 'function') return mod;
  const d = mod.default;
  if (d && typeof d.apply === 'function') return d;
  if (typeof d === 'function') return { apply: d, inject: mod.inject, name: mod.name };
  if (d && d.default && typeof d.default.apply === 'function') return d.default;
  return null;
}

function isCommonJs(file) {
  if (file.endsWith('.cjs')) return true;
  if (file.endsWith('.mjs')) return false;
  // nearest package.json decides for .js
  let dir = dirname(file);
  for (;;) {
    const pkg = join(dir, 'package.json');
    if (existsSync(pkg)) {
      try { return JSON.parse(readFileSync(pkg, 'utf8')).type !== 'module'; } catch { return true; }
    }
    const up = dirname(dir);
    if (up === dir) return true;
    dir = up;
  }
}

function defaultParts(value) {
  if (value === undefined || value === null) return [{ type: 'text', text: '' }];
  if (typeof value === 'string') return [{ type: 'text', text: value }];
  if (Array.isArray(value) && value.every((p) => p && typeof p === 'object' && typeof p.type === 'string')) return value;
  return [{ type: 'text', text: JSON.stringify(value) }];
}

/** Normalize rendered parts to the wire content format of `tool.execute` results. */
async function normalizeContent(plugin, parts) {
  const out = [];
  for (const p of Array.isArray(parts) ? parts : [parts]) {
    if (p == null) continue;
    if (typeof p === 'string') { out.push({ type: 'text', text: p }); continue; }
    if (p.type === 'text') { out.push({ type: 'text', text: String(p.text ?? '') }); continue; }
    if (p.type === 'image') {
      const att = p.attachment ?? p.image;
      if (att && (typeof att === 'string' || att.id)) {
        out.push({ type: 'image', attachment: { id: typeof att === 'string' ? att : att.id }, mediaType: p.mediaType ?? att.mediaType ?? 'image/png' });
      } else if (p.data != null || (att && att.data != null)) {
        out.push({ type: 'image', data: toBase64(p.data ?? att.data), mediaType: p.mediaType ?? att?.mediaType ?? 'image/png' });
      }
      continue;
    }
    out.push({ type: 'text', text: typeof p.text === 'string' ? p.text : JSON.stringify(p) });
  }
  return out;
}

