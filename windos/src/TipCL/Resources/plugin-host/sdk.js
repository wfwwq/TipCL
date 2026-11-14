// TipCL browser SDK (DESIGN §7), served at /__tipcl/sdk.js. Plain ESM, no dependencies.
//
// The view shell page calls `boot(window.__TIPCL_BOOT__)`, which builds the `tipcl` object, imports the
// plugin's `client` module, calls its `apply(ui, tipcl)` and mounts the requested view:
//
//   export function apply(ui) {
//     ui.view('library', (el, props, tipcl) => { ...; return () => {} });   // returns an unmount fn
//     ui.css(`...`);
//   }
//
// Pages can also `import { createTipcl } from '/__tipcl/sdk.js'` and pass {plugin:{id}, token} themselves.

/** Topic pattern match with `*` wildcards (same rule as the host). */
function topicMatches(pattern, topic) {
  if (pattern === '*' || pattern === topic) return true;
  if (!pattern.includes('*')) return false;
  const re = new RegExp('^' + pattern.split('*').map((s) => s.replace(/[.+?^${}()|[\]\\]/g, '\\$&')).join('.*') + '$');
  return re.test(topic);
}

/** Error from a host call, with `.code` (`permission_denied`, `busy`, ...). */
export class TipclError extends Error {
  constructor(code, message) { super(message); this.code = code; this.name = 'TipclError'; }
}

/**
 * Event hub. Browsers allow only ~6 HTTP/1.1 connections per origin (shared by every WebView2 in the same
 * profile), and plugin pages need their own SSE routes too. So all views of a plugin share ONE
 * /__tipcl/events stream through a SharedWorker (sdk-events-worker.js, named by the page token); where
 * SharedWorker is unavailable the page opens its own EventSource. Topic changes reconnect with the union.
 */
class EventHub {
  constructor(token) {
    this.token = token;
    /** @type {Map<string, Set<Function>>} */ this.handlers = new Map();
    this.es = null;
    this.port = null;
    this.noWorker = typeof SharedWorker !== 'function';
    this.topicsKey = null;
    this.timer = 0;
    this.openWaiters = [];
    this.isOpen = false;
  }
  on(topic, fn) {
    let set = this.handlers.get(topic);
    if (!set) { set = new Set(); this.handlers.set(topic, set); this.schedule(); }
    set.add(fn);
    return () => {
      set.delete(fn);
      if (!set.size) { this.handlers.delete(topic); this.schedule(); }
    };
  }
  schedule() {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.connect(), 0);
  }
  dispatch(topic, data) {
    for (const [pattern, set] of this.handlers) {
      if (!topicMatches(pattern, topic)) continue;
      for (const fn of [...set]) {
        try { fn(data, topic); } catch (err) { console.error('[tipcl] event handler failed', err); }
      }
    }
  }
  opened() { this.isOpen = true; for (const w of this.openWaiters.splice(0)) w(); }
  /** Lazily attach to the shared worker; false when not possible. */
  attachWorker() {
    if (this.port) return true;
    if (this.noWorker) return false;
    try {
      const worker = new SharedWorker('/__tipcl/events-worker.js', { name: this.token });
      this.port = worker.port;
      this.port.onmessage = (m) => {
        const msg = m.data ?? {};
        if (msg.type === 'event') this.dispatch(msg.topic, msg.data);
        else if (msg.type === 'open') this.opened();
      };
      this.port.start?.();
      worker.onerror = () => { this.port = null; this.noWorker = true; this.topicsKey = null; this.connect(); };
      addEventListener('pagehide', () => { try { this.port?.postMessage({ type: 'bye' }); } catch { /* gone */ } });
      return true;
    } catch {
      this.noWorker = true;
      return false;
    }
  }
  connect() {
    const topics = [...this.handlers.keys()].sort();
    const key = topics.join(',');
    if (key === this.topicsKey) return;
    this.topicsKey = key;
    this.isOpen = false;
    if (this.attachWorker()) { this.port.postMessage({ type: 'topics', topics }); return; }
    this.es?.close();
    this.es = null;
    if (!topics.length) return;
    const es = new EventSource(`/__tipcl/events?k=${encodeURIComponent(this.token)}&topics=${encodeURIComponent(key)}`);
    this.es = es;
    es.onopen = () => this.opened();
    es.onerror = () => { this.isOpen = false; };
    es.onmessage = (msg) => {
      let ev;
      try { ev = JSON.parse(msg.data); } catch { return; }
      this.dispatch(ev.topic, ev.data);
    };
  }
  /** Resolves when the stream is open (or after `ms`), so history + live can be stitched without gaps. */
  ready(ms = 3000) {
    if (this.isOpen && this.topicsKey === [...this.handlers.keys()].sort().join(',')) return Promise.resolve();
    return new Promise((resolve) => { this.openWaiters.push(resolve); setTimeout(resolve, ms); });
  }
}

/** Apply a theme to the document: CSS variables `--tipcl-*` and `<html data-theme>`. */
function applyTheme(theme) {
  const root = document.documentElement;
  root.dataset.theme = theme.mode === 'light' ? 'light' : 'dark';
  for (const [k, v] of Object.entries(theme.colors ?? {})) root.style.setProperty(`--tipcl-${k.replace(/[A-Z]/g, (c) => '-' + c.toLowerCase())}`, String(v));
  if (theme.font) root.style.setProperty('--tipcl-font', theme.font);
  root.style.colorScheme = theme.mode === 'light' ? 'light' : 'dark';
}

/**
 * Build the `tipcl` object.
 * @param {{plugin: {id: string, version?: string, dir?: string}, token: string, kind?: string, id?: string, theme?: any}} cfg
 */
export function createTipcl(cfg) {
  const token = cfg.token;
  const hub = new EventHub(token);
  const surface = { kind: cfg.kind ?? 'page', id: cfg.id ?? cfg.view ?? '' };

  /** Call any host API method (permission-checked by TipCL with this page's plugin identity). */
  async function call(method, params = {}) {
    const res = await fetch('/__tipcl/call', {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-tipcl-page': token },
      body: JSON.stringify({ method, params }),
    });
    let body = null;
    try { body = await res.json(); } catch { /* non-JSON */ }
    if (!res.ok || body?.error) throw new TipclError(body?.error?.code ?? `http_${res.status}`, body?.error?.message ?? res.statusText);
    return body?.result ?? null;
  }

  const events = {
    /** @param {string} topic (supports `*`) @param {(data: any, topic: string) => void} fn @returns {() => void} */
    on: (topic, fn) => hub.on(topic, fn),
  };

  // theme: live object + change listeners
  const themeListeners = new Set();
  const theme = {
    mode: cfg.theme?.mode ?? 'dark',
    colors: { ...(cfg.theme?.colors ?? {}) },
    font: cfg.theme?.font ?? '',
    /** @param {(theme) => void} fn */
    onChange(fn) { themeListeners.add(fn); return () => themeListeners.delete(fn); },
  };
  hub.on('theme.changed', (t) => {
    if (!t) return;
    theme.mode = t.mode ?? t.theme ?? theme.mode;
    theme.colors = { ...theme.colors, ...(t.colors ?? {}) };
    if (t.font) theme.font = t.font;
    applyTheme(theme);
    for (const fn of [...themeListeners]) { try { fn(theme); } catch (e) { console.error(e); } }
  });

  const ai = {
    /** New chat (optionally with this plugin's preset / aiView, a first prompt) → `{chatId}`. Opens it unless `open:false`. */
    createChat: (opts = {}) => call('ai.chats.create', { open: true, ...opts }),
    /** mode: 'auto' (new turn if idle, steer if running) | 'queue' | 'steer' */
    send: (chatId, text, opts = {}) => call('ai.chats.send', { chatId, text, mode: opts.mode ?? 'auto', ...(opts.images ? { images: opts.images } : {}) }),
    cancel: (chatId) => call('ai.chats.cancel', { chatId }),
    open: (chatId, opts = {}) => call('ai.chats.open', { chatId, ...opts }),
    list: () => call('ai.chats.list', {}),
    /** `{seq, events}` with seq > from */
    events: (chatId, from) => call('ai.chats.events', { chatId, ...(from !== undefined ? { from } : {}) }),
    state: (chatId) => call('ai.chats.state', { chatId }),
    /**
     * History first, then live events, each delivered once in seq order. `fn(event)`.
     * @param {string} chatId @param {(event: any) => void} fn @param {{from?: number}} [opts]
     * @returns {() => void}
     */
    subscribe(chatId, fn, opts = {}) {
      let last = Number(opts.from ?? 0);
      let live = false;
      let closed = false;
      const buffered = [];
      const deliver = (e) => {
        if (closed || !e || typeof e.seq !== 'number' || e.seq <= last) return;
        last = e.seq;
        try { fn(e.time === undefined && e.at !== undefined ? { ...e, time: e.at } : e); } catch (err) { console.error('[tipcl] ai.subscribe handler failed', err); }
      };
      const off = hub.on('ai.event', (d) => {
        if (!d || d.chatId !== chatId) return;
        if (live) deliver(d.event); else buffered.push(d.event);
      });
      (async () => {
        await hub.ready();
        try {
          const history = await ai.events(chatId, last);
          for (const e of (history?.events ?? []).sort((a, b) => a.seq - b.seq)) deliver(e);
        } catch (err) { console.warn('[tipcl] ai history failed', err); }
        live = true;
        for (const e of buffered.splice(0).sort((a, b) => a.seq - b.seq)) deliver(e);
      })();
      return () => { closed = true; off(); };
    },
  };

  const tipcl = {
    plugin: { ...cfg.plugin },
    surface,
    props: cfg.props ?? {},
    theme,
    lang: cfg.lang ?? 'zh-CN',
    call,
    events,
    ai,
    llm: {
      providers: () => call('llm.providers', {}),
      models: (provider) => call('llm.models', { provider }),
    },
    credentials: {
      status: () => call('credentials.status', {}),
      set: (name, value) => call('credentials.set', { name, value: value ?? null }),
      onChange: (fn) => hub.on('credentials.updated', fn),
    },
    config: {
      get: () => call('config.get', {}),
      set: (key, value) => call('config.set', { key, value }),
      /** @param {(key: string, value: any) => void} fn */
      onChange: (fn) => hub.on('config.changed', (d) => fn(d?.key, d?.value)),
    },
    nav: {
      /** Plugin page id (this plugin's) or built-in page id (`launch`, `ai`, ...) */
      open: (page) => call('ui.openPage', { page }),
    },
    ui: {
      toast: (text, kind = 'info') => call('ui.toast', { text: String(text), kind }),
      confirm: (title, text, opts = {}) => call('ui.confirm', typeof title === 'object' ? title : { title, text, ...opts }),
      prompt: (title, text, opts = {}) => call('ui.prompt', typeof title === 'object' ? title : { title, text, ...opts }),
      openExternal: (url) => call('ui.openExternal', { url }),
    },
    overlay: {
      /** Only inside an overlay view: resize / show / hide / click-through. */
      update(patch) {
        if (surface.kind !== 'overlay') return Promise.reject(new TipclError('not_overlay', 'tipcl.overlay is only available in overlay views'));
        return call('ui.overlayUpdate', { ...patch, id: surface.id });
      },
    },
    launcher: {
      call: (method, kwargs, args) => call('launcher.call', Array.isArray(kwargs) ? { method, args: kwargs } : { method, kwargs: kwargs ?? {}, ...(args ? { args } : {}) }),
    },
    /** URL of a static file in the plugin directory. */
    asset: (path) => `/__tipcl/plugin/${encodeURIComponent(cfg.plugin.id)}/${String(path).replace(/^\.?\//, '').split('/').map(encodeURIComponent).join('/')}`,
    /** URL of one of this plugin's routes (plain same-origin path; kept for symmetry). */
    route: (path) => path,
  };
  return tipcl;
}

/**
 * View shell entry: build tipcl, load the plugin client module, mount the view.
 * @param {any} cfg window.__TIPCL_BOOT__
 */
export async function boot(cfg) {
  const root = document.getElementById('tipcl-root') ?? document.body;
  const fail = (msg) => {
    const box = document.createElement('div');
    box.className = 'tipcl-error';
    box.textContent = msg;
    root.replaceChildren(box);
  };
  const tipcl = createTipcl(cfg);
  window.tipcl = tipcl;
  applyTheme(tipcl.theme);
  if (cfg.transparent) document.documentElement.setAttribute('data-transparent', '');

  /** @type {Map<string, Function>} */
  const views = new Map();
  const styles = [];
  const ui = {
    tipcl,
    /** @param {string} name @param {(el: HTMLElement, props: any, tipcl: any) => (void | (() => void))} mount */
    view(name, mount) { views.set(name, mount); return () => views.delete(name); },
    css(text) {
      const tag = document.createElement('style');
      tag.dataset.plugin = cfg.plugin.id;
      tag.textContent = text;
      document.head.append(tag);
      styles.push(tag);
      return () => tag.remove();
    },
  };

  if (!cfg.client) return fail(`插件 ${cfg.plugin.id} 没有网页端模块（manifest "client"）`);
  let mod;
  try {
    mod = await import(cfg.client);
  } catch (err) {
    return fail(`加载 ${cfg.client} 失败：\n${err?.stack ?? err}`);
  }
  const apply = mod.apply ?? mod.default?.apply ?? (typeof mod.default === 'function' ? mod.default : null);
  if (typeof apply !== 'function') return fail(`${cfg.client} 没有导出 apply(ui)`);
  try {
    await apply(ui, tipcl);
  } catch (err) {
    return fail(`apply() 出错：\n${err?.stack ?? err}`);
  }
  const mount = views.get(cfg.view);
  if (!mount) return fail(`插件没有注册视图 "${cfg.view}"（已注册：${[...views.keys()].join(', ') || '无'}）`);
  let unmount;
  try {
    unmount = await mount(root, { ...(cfg.props ?? {}), kind: cfg.kind, id: cfg.id }, tipcl);
  } catch (err) {
    return fail(`视图 "${cfg.view}" 挂载失败：\n${err?.stack ?? err}`);
  }
  addEventListener('pagehide', () => { try { typeof unmount === 'function' && unmount(); } catch { /* ignore */ } });
  document.documentElement.dataset.tipclReady = '';
}

export default { createTipcl, boot, TipclError };
