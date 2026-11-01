// Runs the browser SDK (sdk.js) and hello-plugin's client views inside Node against a real host, with a
// tiny DOM / EventSource stand-in (no browser available in CI). Checks: boot mounts the view, the aiView
// renders history + live chat events, its input steers and its stop button cancels, theme updates live,
// overlay.update carries the overlay id, config/credentials calls go through /__tipcl/call.

import { describe, it, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { request as httpRequest } from 'node:http';
import { pathToFileURL } from 'node:url';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { FakeTipcl, HOST_DIR, SAMPLES_DIR, loadParams } from './fake-tipcl.mjs';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ── minimal DOM ──
class El {
  constructor(tag) { this.tagName = tag.toUpperCase(); this.children = []; this.attrs = {}; this.listeners = {}; this.style = { setProperty: (k, v) => { this.style[k] = v; } }; this.dataset = {}; this._text = ''; this.className = ''; this.scrollTop = 0; this.scrollHeight = 0; this.value = ''; }
  setAttribute(k, v) { this.attrs[k] = String(v); if (k === 'value') this.value = String(v); }
  removeAttribute(k) { delete this.attrs[k]; }
  addEventListener(type, fn) { (this.listeners[type] ??= []).push(fn); }
  dispatch(type, ev = {}) { for (const fn of this.listeners[type] ?? []) fn({ preventDefault() {}, target: this, ...ev }); }
  append(...nodes) { for (const n of nodes) { const c = typeof n === 'string' ? Object.assign(new El('#text'), { _text: n }) : n; c.parent = this; this.children.push(c); } }
  replaceChildren(...nodes) { this.children = []; this.append(...nodes); }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter((c) => c !== this); }
  get childElementCount() { return this.children.length; }
  get firstElementChild() { return this.children[0]; }
  set textContent(v) { this.children = []; this._text = String(v); }
  get textContent() { return this._text + this.children.map((c) => c.textContent).join(''); }
  /** depth-first search */
  find(pred) { for (const c of this.children) { if (pred(c)) return c; const f = c.find(pred); if (f) return f; } return null; }
  findAll(pred, out = []) { for (const c of this.children) { if (pred(c)) out.push(c); c.findAll(pred, out); } return out; }
}

function installDom(base) {
  const docEl = new El('html');
  const head = new El('head');
  const root = new El('div');
  globalThis.document = {
    documentElement: docEl, head, body: new El('body'),
    getElementById: (id) => (id === 'tipcl-root' ? root : null),
    createElement: (tag) => new El(tag),
  };
  globalThis.window = globalThis;
  globalThis.addEventListener = () => {};
  const realFetch = globalThis.fetch;
  globalThis.fetch = (url, init) => realFetch(new URL(url, base), init);
  // EventSource over node:http
  globalThis.EventSource = class {
    static opened = 0;
    constructor(url) {
      globalThis.EventSource.opened++;
      this.url = new URL(url, base);
      this.req = httpRequest(this.url, (res) => {
        this.onopen?.();
        let buf = '';
        res.setEncoding('utf8');
        res.on('data', (d) => {
          buf += d;
          let i;
          while ((i = buf.indexOf('\n\n')) >= 0) {
            const block = buf.slice(0, i); buf = buf.slice(i + 2);
            const data = block.split('\n').filter((l) => l.startsWith('data: ')).map((l) => l.slice(6)).join('\n');
            if (data) this.onmessage?.({ data });
          }
        });
        res.on('end', () => this.onerror?.());
      });
      this.req.on('error', () => this.onerror?.());
      this.req.end();
    }
    close() { this.req.destroy(); }
  };
  return { root, docEl, head };
}

describe('browser SDK + hello-plugin client views', () => {
  let t, home, token, dom, boot;

  before(async () => {
    home = await mkdtemp(join(tmpdir(), 'tipcl-sdk-test-'));
    t = new FakeTipcl({ home });
    await t.hello;
    const r = await t.request('plugin.load', await loadParams(join(SAMPLES_DIR, 'hello-plugin'), { home }));
    assert.equal(r.ok, true, r.error);
    token = r.pageToken;
    dom = installDom(`http://127.0.0.1:${t.port}`);
    ({ boot } = await import(pathToFileURL(join(HOST_DIR, 'sdk.js')).href));
  });
  after(async () => { await t?.stop(); await rm(home, { recursive: true, force: true }); });

  /** boot config as the shell page would embed it (client imported from disk instead of over HTTP) */
  async function bootCfg(view, kind, id, props = {}) {
    const page = await t.http(`/__tipcl/view/hello-plugin/${view}?k=${token}&kind=${kind}&id=${id}&props=${encodeURIComponent(JSON.stringify(props))}`);
    const cfg = JSON.parse(/window\.__TIPCL_BOOT__=(.*?);<\/script>/s.exec(page.text)[1]);
    return { ...cfg, client: pathToFileURL(join(SAMPLES_DIR, 'hello-plugin', 'lib', 'client.js')).href };
  }

  it('aiView: history + live events rendered once, steer input, stop button', async () => {
    t.pushChatEvent('chat-a', 'turn/start', {});
    t.pushChatEvent('chat-a', 'user/message', { text: '开始吧', source: 'user' });
    t.pushChatEvent('chat-a', 'assistant/message', { message: { content: [{ type: 'text', text: '好嘞' }, { type: 'tool-call', id: 'k1', name: 'hello_wait', args: { note: 'x' } }] } });
    dom.root.replaceChildren();
    await boot(await bootCfg('watch', 'aiView', 'watch', { chatId: 'chat-a' }));
    assert.equal(document.documentElement.dataset.tipclReady, '');
    assert.equal(document.documentElement.dataset.theme, 'dark');
    assert.equal(document.documentElement.style['--tipcl-bg'], '#101014');
    await sleep(300);
    const term = () => dom.root.find((e) => e.className === 'term').children.map((c) => c.textContent);
    assert.deepEqual(term(), ['你 › 开始吧', '说 › 好嘞', '▸ hello_wait {"note":"x"}']);

    // live event arrives over /__tipcl/events
    t.pushChatEvent('chat-a', 'tool/result', { callId: 'k1', name: 'hello_wait', content: [{ type: 'text', text: '等完了' }, { type: 'image', attachment: { id: 'a' } }], isError: false });
    await sleep(200);
    assert.equal(term().at(-1), '◆ 等完了 [1 张图]');

    // typing while running → steer, plus the poke route
    const since = t.calls.length;
    const input = dom.root.find((e) => e.tagName === 'INPUT');
    input.value = '快点';
    dom.root.find((e) => e.tagName === 'FORM').dispatch('submit');
    const send = await t.waitForCall('ai.chats.send', () => true, { since });
    assert.deepEqual(send.params, { plugin: 'hello-plugin', chatId: 'chat-a', text: '快点', mode: 'steer' });
    await sleep(200);
    assert.equal(term().at(-1), '你 › 快点'); // echoed back by the fake as a user/message event

    dom.root.find((e) => e.tagName === 'BUTTON' && e.textContent === '■ 停止').dispatch('click');
    const cancel = await t.waitForCall('ai.chats.cancel', () => true, { since });
    assert.equal(cancel.params.chatId, 'chat-a');
  });

  it('page view: config, credentials and providers through /__tipcl/call; createChat', async () => {
    dom.root.replaceChildren();
    t.config['hello-plugin'] = { greeting: 'Hey', showOverlay: true };
    await boot(await bootCfg('main', 'page', 'hello'));
    await sleep(300);
    assert.match(dom.root.find((e) => e.tagName === 'H2').textContent, /^Hey，hello-plugin 0\.1\.0$/);
    assert.match(dom.root.textContent, /已配置/);
    assert.match(dom.root.textContent, /Fake/);
    const since = t.calls.length;
    dom.root.find((e) => e.tagName === 'BUTTON' && e.textContent.startsWith('用 Hello 预设')).dispatch('click');
    const create = await t.waitForCall('ai.chats.create', () => true, { since });
    assert.deepEqual(create.params, { plugin: 'hello-plugin', open: true, preset: 'hello', view: 'watch', prompt: '帮我等 10 秒，看看会发生什么。' });
  });

  it('overlay: transparent, overlay.update carries the overlay id; theme updates live', async () => {
    dom.root.replaceChildren();
    const cfg = await bootCfg('bubble', 'overlay', 'bubble');
    assert.equal(cfg.transparent, true);
    await boot(cfg);
    const since = t.calls.length;
    dom.root.find((e) => e.className === 'bubble').dispatch('click');
    const upd = await t.waitForCall('ui.overlayUpdate', () => true, { since });
    assert.deepEqual(upd.params, { plugin: 'hello-plugin', width: 320, height: 160, id: 'bubble' });
    await sleep(100);
    t.notify('event', { topic: 'theme.changed', data: { mode: 'light', colors: { bg: '#fafafa' } } });
    await sleep(200);
    assert.equal(document.documentElement.dataset.theme, 'light');
    assert.equal(document.documentElement.style['--tipcl-bg'], '#fafafa');
    assert.equal(dom.root.find((e) => e.className === 'bubble').title, '主题：light');
  });

  it('SharedWorker: views of one plugin share a single /__tipcl/events connection', async () => {
    // fake SharedWorker: runs sdk-events-worker.js in a vm context, one instance per name
    const workers = new Map();
    const openPorts = [];
    const workerSrc = readFileSync(join(HOST_DIR, 'sdk-events-worker.js'), 'utf8');
    globalThis.SharedWorker = class {
      constructor(url, { name }) {
        assert.equal(url, '/__tipcl/events-worker.js');
        let ctx = workers.get(name);
        if (!ctx) {
          ctx = vm.createContext({ self: { name }, EventSource: globalThis.EventSource, setTimeout, clearTimeout, console, encodeURIComponent, JSON, RegExp, Set, Map });
          vm.runInContext(workerSrc, ctx);
          workers.set(name, ctx);
        }
        const { port1, port2 } = new MessageChannel();
        ctx.self.onconnect({ ports: [port2] });
        openPorts.push(port1, port2);
        this.port = port1;
      }
    };
    const { createTipcl } = await import(pathToFileURL(join(HOST_DIR, 'sdk.js')).href);
    const before = globalThis.EventSource.opened;
    const a = createTipcl({ plugin: { id: 'hello-plugin' }, token, kind: 'page', id: 'hello' });
    const b = createTipcl({ plugin: { id: 'hello-plugin' }, token, kind: 'overlay', id: 'bubble' });
    const gotA = [], gotB = [];
    a.events.on('game.log', (d) => gotA.push(d.line));
    b.events.on('game.*', (d) => gotB.push(d.line));
    await sleep(300);
    assert.equal(globalThis.EventSource.opened - before, 1, 'one shared stream');
    t.notify('event', { topic: 'game.log', data: { line: 'shared!' }, plugin: 'hello-plugin' });
    await sleep(200);
    assert.deepEqual(gotA, ['shared!']);
    assert.deepEqual(gotB, ['shared!']);
    // history + live stitching works through the worker too
    t.pushChatEvent('chat-w', 'turn/start', {});
    const seen = [];
    const off = a.ai.subscribe('chat-w', (e) => seen.push(e.seq));
    await sleep(300);
    t.pushChatEvent('chat-w', 'turn/end', {});
    await sleep(200);
    assert.deepEqual(seen, [1, 2]);
    off();
    for (const p of openPorts) p.close();
    for (const ctx of workers.values()) ctx.self.onconnect = null;
    delete globalThis.SharedWorker;
  });

  it('a view that does not exist shows an error box instead of throwing', async () => {
    dom.root.replaceChildren();
    await boot(await bootCfg('nope', 'page', 'nope'));
    assert.match(dom.root.find((e) => e.className === 'tipcl-error').textContent, /没有注册视图 "nope"/);
  });
});
