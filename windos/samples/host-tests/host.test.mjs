// End-to-end tests of the Node plugin host against a fake TipCL.   Run:  node --test samples/host-tests/
// Tests in this file run in order and share one host process.

import { describe, it, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHmac } from 'node:crypto';
import { FakeTipcl, SAMPLES_DIR, loadParams } from './fake-tipcl.mjs';

const HELLO = join(SAMPLES_DIR, 'hello-plugin');
const CONSUMER = join(SAMPLES_DIR, 'hello-consumer');
const PROBE = join(SAMPLES_DIR, 'host-tests', 'fixtures', 'probe');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

describe('TipCL node plugin host', () => {
  /** @type {FakeTipcl} */ let t;
  let home;
  let token;
  let this_sse;

  before(async () => {
    home = await mkdtemp(join(tmpdir(), 'tipcl-host-test-'));
    t = new FakeTipcl({ home });
    await t.hello;
  });
  after(async () => {
    if (t) await t.stop();
    await rm(home, { recursive: true, force: true });
  });

  it('says host.hello with its web port', async () => {
    const hello = await t.hello;
    assert.equal(typeof hello.webPort, 'number');
    assert.ok(hello.webPort > 0);
    assert.equal((await t.http('/__tipcl/health')).text, 'ok');
  });

  it('loads a consumer before its provider (late binding pending)', async () => {
    const r = await t.request('plugin.load', await loadParams(CONSUMER, { home }));
    assert.equal(r.ok, true, r.error);
    const status = JSON.parse((await t.http('/api/hello-consumer/status')).text);
    assert.deepEqual(status, { hasHello: false, defineTool: 'function' }); // require('tipcl:tools') works in CJS
    assert.equal((await t.http('/api/hello-consumer/greet')).status, 404);
  });

  it('loads hello-plugin: apply, registrations, console attribution, env', async () => {
    const since = t.calls.length;
    const r = await t.request('plugin.load', await loadParams(HELLO, { home }));
    assert.equal(r.ok, true, r.error);
    token = r.pageToken;
    assert.equal(token, createHmac('sha256', 'test-host-token').update('hello-plugin').digest('hex').slice(0, 32));
    assert.equal(r.webPort, t.port);

    const tools = t.callsOf('tools.register').slice(0).map((c) => c.params);
    const wait = tools.find((x) => x.tool.name === 'hello_wait');
    assert.equal(wait.plugin, 'hello-plugin');
    assert.deepEqual(wait.tool.parameters.required, ['note']);
    assert.equal(wait.tool.parameters.properties.seconds.type, 'integer');
    assert.equal(wait.tool.parameters.properties.note.required, undefined);
    assert.equal(wait.tool.timeoutMs, 15 * 60_000);
    assert.ok(tools.find((x) => x.tool.name === 'hello_badge').tool.parameters.properties.color);
    assert.ok(tools.find((x) => x.tool.name === 'hello_ask'));

    assert.equal(t.callsOf('presets.register')[0].params.preset.id, 'hello-quick');
    const kinds = t.callsOf('ui.register').map((c) => c.params.kind).sort();
    assert.deepEqual(kinds, ['action', 'aiView', 'block', 'dashCard', 'overlay', 'page']);
    const page = t.callsOf('ui.register', (p) => p.kind === 'page')[0].params.spec;
    assert.equal(page.url, `http://127.0.0.1:${t.port}/__tipcl/view/hello-plugin/main?k=${token}&kind=page&id=hello`);
    assert.equal(t.callsOf('hooks.register')[0].params.hook, 'launch.before');

    const sub = await t.waitForCall('events.subscribe', (p) => p.plugin === 'hello-plugin', { since });
    assert.ok(sub.params.topics.includes('game.log'));
    assert.ok(sub.params.topics.includes('ai.event'));

    assert.match(t.stderr, /^\[hello-plugin\] hello-plugin starting, data dir .*plugin-data[\\/]hello-plugin, DSH_HOME=.*plugin-data$/m);
    assert.ok(t.callsOf('log', (p) => p.plugin === 'hello-plugin' && p.level === 'info' && p.text === 'hello-plugin ready').length);
  });

  // A minimal plugin that only declares a layout block: proves `ctx.ui.block(...)` reaches TipCL as
  // `ui.register {kind:'block'}` with the view URL / id namespace the C# side expects
  // (`plugin:<plugin>:<blockId>`, see Pages/Layout/WidgetRegistry + NativePluginUi.Content).
  it('a minimal plugin can declare a layout block (kind=block)', async () => {
    const dir = await mkdtemp(join(tmpdir(), 'tipcl-block-fixture-'));
    try {
      await writeFile(join(dir, 'tipcl-plugin.json'), JSON.stringify({
        id: 'block-fixture', name: 'Block fixture', version: '0.0.1', runtime: 'node',
        main: ['block.mjs'], permissions: ['ui'],
      }), 'utf8');
      await writeFile(join(dir, 'block.mjs'), [
        "export const name = 'block-fixture';",
        'export function apply(ctx) {',
        "  ctx.effect(() => ctx.ui.block({ id: 'farm', label: '农场概览', view: 'farm' }), 'ui block');",
        '}',
        '',
      ].join('\n'), 'utf8');

      const r = await t.request('plugin.load', await loadParams(dir, { home }));
      assert.equal(r.ok, true, r.error);
      const call = t.callsOf('ui.register', (p) => p.kind === 'block' && p.plugin === 'block-fixture');
      assert.equal(call.length, 1, 'exactly one ui.register kind=block from this plugin');
      assert.equal(call[0].params.plugin, 'block-fixture');
      assert.equal(call[0].params.spec.id, 'farm');
      assert.equal(call[0].params.spec.label, '农场概览');
      assert.equal(call[0].params.spec.view, 'farm');
      // view URL: 与 page/dashCard 同一个模板,kind=block
      assert.equal(call[0].params.spec.url, `http://127.0.0.1:${t.port}/__tipcl/view/block-fixture/farm?k=${r.pageToken}&kind=block&id=farm`);

      // 卸载:宿主发 ui.unregister(kind=block);块本身留在 TipCL 的布局文档里(显示「不可用」)
      const un = await t.request('plugin.unload', { id: 'block-fixture' });
      assert.equal(un.ok, true, un.error);
      const gone = t.callsOf('ui.unregister', (p) => p.kind === 'block' && p.id === 'farm' && p.plugin === 'block-fixture');
      assert.equal(gone.length, 1, 'ui.unregister kind=block on unload');
      assert.equal(gone[0].params.plugin, 'block-fixture');
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it('provide/get across plugins: the earlier-loaded consumer binds late', async () => {
    assert.equal((await t.http('/api/hello-consumer/greet')).text, 'Hello, consumer!');
    assert.equal(JSON.parse((await t.http('/api/hello-consumer/status')).text).hasHello, true);
    assert.match(t.stderr, /^\[hello-consumer\] hello service is here$/m);
  });

  it('tool round trip: messageText from the sync mirror, text + image attachment', async () => {
    t.pushChatEvent('c1', 'turn/start', { turnId: 'x' });
    t.pushChatEvent('c1', 'assistant/message', { message: { content: [{ type: 'text', text: '好的我等等' }, { type: 'tool-call', id: 'call_1', name: 'hello_wait', args: { note: 'n' } }] } });
    const res = await t.request('tool.execute', { plugin: 'hello-plugin', name: 'hello_wait', callId: 'call_1', chatId: 'c1', args: { note: '测试', seconds: 1 }, messageText: '好的我等等' });
    assert.equal(res.isError, false);
    assert.equal(res.content[0].type, 'text');
    assert.match(res.content[0].text, /等「测试」：timeout/);
    assert.match(res.content[0].text, /你刚才说：好的我等等/);
    assert.equal(res.content[1].type, 'image');
    assert.equal(res.content[1].mediaType, 'image/png');
    const att = t.attachments.find((a) => a.id === res.content[1].attachment.id);
    assert.ok(att.data.startsWith('iVBORw0KGgo'), 'attachment is a base64 PNG');
  });

  it('blocking tool wakes on a route poke and on a steer message', async () => {
    t.pushChatEvent('c1', 'assistant/message', { message: { content: [{ type: 'tool-call', id: 'call_2', name: 'hello_wait', args: {} }] } });
    const p = t.request('tool.execute', { plugin: 'hello-plugin', name: 'hello_wait', callId: 'call_2', chatId: 'c1', args: { note: 'poke', seconds: 20 }, messageText: '' });
    await sleep(150);
    const poke = await t.http('/api/hello-plugin/poke', { method: 'POST' });
    assert.equal(JSON.parse(poke.text).woke, 1);
    assert.match((await p).content[0].text, /：poke，/);

    const p2 = t.request('tool.execute', { plugin: 'hello-plugin', name: 'hello_wait', callId: 'call_3', chatId: 'c1', args: { note: 'steer', seconds: 20 }, messageText: '' });
    await sleep(150);
    t.pushChatEvent('c1', 'user/message', { text: '别等了', source: 'steer' });
    assert.match((await p2).content[0].text, /steer:别等了/);
  });

  it('tool.cancel aborts the tool signal', async () => {
    const started = Date.now();
    const p = t.request('tool.execute', { plugin: 'hello-plugin', name: 'hello_wait', callId: 'call_cancel', chatId: 'c1', args: { note: 'cancel me', seconds: 30 }, messageText: '' });
    await sleep(150);
    t.notify('tool.cancel', { callId: 'call_cancel' });
    const res = await p;
    assert.equal(res.isError, true);
    assert.match(res.content[0].text, /^cancelled/);
    assert.ok(Date.now() - started < 3000);
  });

  it('raw image data and JSON-Schema tools', async () => {
    const res = await t.request('tool.execute', { plugin: 'hello-plugin', name: 'hello_badge', callId: 'b1', chatId: null, args: { color: '#ff0000' } });
    assert.equal(res.content[0].text, '徽章：#ff0000');
    assert.equal(res.content[1].type, 'image');
    assert.ok(res.content[1].data.startsWith('iVBORw0KGgo'));
    const missing = await t.request('tool.execute', { plugin: 'hello-plugin', name: 'nope', callId: 'b2', args: {} });
    assert.equal(missing.isError, true);
  });

  it('llm.stream: chunks via llm.chunk, finish synthesized from the result', async () => {
    const res = await t.request('tool.execute', { plugin: 'hello-plugin', name: 'hello_ask', callId: 'ask1', chatId: null, args: { question: 'haiku?' } });
    assert.equal(res.isError, false, res.content[0].text);
    assert.equal(res.content[0].text, '古池や蛙飛び込む水の音');
    const call = t.callsOf('llm.stream').at(-1).params;
    assert.equal(call.plugin, 'hello-plugin');
    assert.equal(call.provider, 'fake');
    assert.equal(call.model, 'm1');
    assert.equal(call.reasoningEffort, 'off');
    assert.deepEqual(call.messages, [{ role: 'user', content: 'haiku?' }]);
  });

  it('llm.stream over an SSE route, and abort on client disconnect', async () => {
    const s = await t.sse('/api/hello-plugin/haiku?topic=frog');
    assert.equal(s.status, 200);
    assert.match(s.headers['content-type'], /text\/event-stream/);
    const evs = await s.until((e) => e.some((x) => x.type === 'finish'));
    assert.equal(evs.filter((e) => e.type === 'text-delta').map((e) => e.text).join(''), '古池や蛙飛び込む水の音');
    assert.equal(evs.at(-1).reason.kind, 'stop');
    // messages with DSH-style content arrays are flattened to a string
    assert.equal(t.callsOf('llm.stream').at(-1).params.messages[0].content, 'frog');

    const since = t.calls.length;
    const slow = await t.sse('/api/hello-plugin/haiku?topic=slow');
    await slow.until((e) => e.length >= 2);
    slow.close();
    const abort = await t.waitForCall('llm.abort', () => true, { since });
    assert.equal(abort.params.plugin, 'hello-plugin');
  });

  it('sessions mirror: sync snapshotEvents(from), seq, running, dedupe, time alias, backfill', async () => {
    assert.equal((await t.http('/api/probe/snapshot?chat=c1')).status, 404); // probe not loaded yet
    const loaded = await t.request('plugin.load', await loadParams(PROBE, { home }));
    assert.equal(loaded.ok, true, loaded.error);
    let r = JSON.parse((await t.http('/api/probe/snapshot?chat=c1&from=2')).text);
    const all = t.chats.get('c1');
    assert.equal(r.seq, all.at(-1).seq);
    assert.equal(r.running, true); // turn/start seen, no turn/end yet
    assert.deepEqual(r.seqs, all.filter((e) => e.seq > 2).map((e) => e.seq));
    assert.equal(typeof r.time, 'number');

    // duplicate push is ignored; turn/end flips running
    t.notify('event', { topic: 'ai.event', data: { chatId: 'c1', event: all[0] } });
    t.pushChatEvent('c1', 'turn/end', { reason: 'stop' });
    await sleep(50);
    r = JSON.parse((await t.http('/api/probe/snapshot?chat=c1&from=0')).text);
    assert.equal(r.running, false);
    assert.equal(r.seqs.length, t.chats.get('c1').length);

    // a chat that existed before the host saw any of its events gets back-filled
    t.pushChatEvent('old', 'turn/start', {}, { send: false });
    t.pushChatEvent('old', 'user/message', { text: 'hi' }, { send: false });
    t.pushChatEvent('old', 'turn/end', { reason: 'stop' }, { send: false });
    const ready = JSON.parse((await t.http('/api/probe/ready?chat=old')).text);
    assert.equal(ready.seq, 3);
    r = JSON.parse((await t.http('/api/probe/snapshot?chat=old&from=1')).text);
    assert.deepEqual(r.seqs, [2, 3]);

    // subscribe(): live callback
    t.pushChatEvent('live-chat', 'turn/start', {});
    t.pushChatEvent('live-chat', 'turn/end', {});
    await sleep(50);
    assert.deepEqual(JSON.parse((await t.http('/api/probe/seen')).text), [1, 2]);
  });

  it('env vars and paths per plugin, launcher.call identity', async () => {
    const env = JSON.parse((await t.http('/api/probe/env')).text);
    assert.equal(env.envAtApply.TIPCL_PLUGIN_DATA, join(home, 'plugin-data', 'probe'));
    assert.equal(env.envAtApply.DSH_HOME, join(home, 'plugin-data'));
    assert.equal(env.paths.data, join(home, 'plugin-data', 'probe'));
    const l = JSON.parse((await t.http('/api/probe/launcher')).text);
    assert.deepEqual(l.echo, { method: 'instances_list', kwargs: { all: true }, plugin: 'probe' });
  });

  it('routes: SSE stream, binary, JSON echo, wildcard, 405, streamed upload', async () => {
    const s = await t.sse('/api/hello-plugin/ticks?n=3');
    const evs = await s.until(() => false, 3000).catch(() => s.events);
    assert.ok(s.ended, 'stream ends after n ticks');
    assert.deepEqual(evs.map((e) => e.tick), [1, 2, 3]);

    const img = await t.http('/api/hello-plugin/image?color=%2300ff00');
    assert.equal(img.headers['content-type'], 'image/png');
    assert.deepEqual([...img.body.subarray(0, 8)], [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

    const echo = await t.http('/api/hello-plugin/echo', { method: 'PUT', body: { a: 1 }, headers: { 'content-type': 'application/json', 'x-hello': 'yo' } });
    assert.deepEqual(JSON.parse(echo.text), { method: 'PUT', body: { a: 1 }, header: 'yo' });
    assert.equal((await t.http('/api/hello-plugin/echo')).status, 405);
    assert.equal((await t.http('/api/hello-plugin/files/a/b%20c.txt')).text, 'file:a/b%20c.txt');
    assert.equal((await t.http('/api/nothing')).status, 404);

    const up = await t.http('/api/probe/upload', { method: 'POST', body: Buffer.alloc(300_000, 7) });
    assert.deepEqual(JSON.parse(up.text), { bytes: 300_000 });
  });

  it('client disconnect cancels the route ReadableStream', async () => {
    const s = await t.sse('/api/hello-plugin/ticks?n=1000');
    await s.until((e) => e.length >= 2);
    s.close();
    await sleep(150);
    const status = JSON.parse((await t.http('/api/hello-plugin/status')).text);
    assert.equal(status.ticksCancelled, 1);
    assert.match(t.stderr, /^\[hello-plugin\] ticks stream cancelled by client$/m);
  });

  it('view shell page, sdk.js, static files with traversal protection and Range', async () => {
    const v = await t.http(`/__tipcl/view/hello-plugin/watch?k=${token}&kind=aiView&id=watch&chatId=c1`);
    assert.equal(v.status, 200);
    assert.match(v.headers['content-type'], /text\/html/);
    const boot = JSON.parse(/window\.__TIPCL_BOOT__=(.*?);<\/script>/s.exec(v.text)[1]);
    assert.equal(boot.token, token);
    assert.equal(boot.view, 'watch');
    assert.equal(boot.kind, 'aiView');
    assert.equal(boot.props.chatId, 'c1');
    assert.equal(boot.client, '/__tipcl/plugin/hello-plugin/lib/client.js');
    assert.match(v.text, /--tipcl-bg:#101014;/);
    assert.match(v.text, /data-theme="dark"/);
    assert.match(v.text, /import \{ boot \} from '\/__tipcl\/sdk\.js'/);
    const overlay = await t.http(`/__tipcl/view/hello-plugin/bubble?k=${token}&kind=overlay&id=bubble&theme=light`);
    assert.match(overlay.text, /data-transparent/);
    assert.match(overlay.text, /data-theme="light"/);
    assert.equal((await t.http('/__tipcl/view/hello-plugin/main?k=wrong')).status, 403);
    // without kind/id (TipCL's plain ViewUrl) the shell infers them from the registered overlay
    const inferred = JSON.parse(/window\.__TIPCL_BOOT__=(.*?);<\/script>/s.exec((await t.http(`/__tipcl/view/hello-plugin/bubble?k=${token}`)).text)[1]);
    assert.equal(inferred.kind, 'overlay');
    assert.equal(inferred.id, 'bubble');
    assert.equal(inferred.transparent, true);

    const sdk = await t.http('/__tipcl/sdk.js');
    assert.match(sdk.headers['content-type'], /javascript/);
    assert.match(sdk.text, /export async function boot/);

    const client = await t.http('/__tipcl/plugin/hello-plugin/lib/client.js');
    assert.match(client.headers['content-type'], /text\/javascript/);
    const icon = await t.http('/__tipcl/plugin/hello-plugin/assets/icon.png');
    assert.equal(icon.headers['content-type'], 'image/png');
    const part = await t.http('/__tipcl/plugin/hello-plugin/assets/icon.png', { headers: { range: 'bytes=0-7' } });
    assert.equal(part.status, 206);
    assert.equal(part.body.length, 8);
    for (const bad of ['..%2f..%2fhost-tests%2fhost.test.mjs', '..%5c..%5cREADME.md', 'lib/..%2f..%2f..%2fREADME.md', '%2e%2e/x']) {
      const r = await t.http(`/__tipcl/plugin/hello-plugin/${bad}`);
      assert.ok(r.status === 403 || r.status === 404, `${bad} → ${r.status}`);
    }
    assert.equal((await t.http('/__tipcl/plugin/hello-plugin/nope.txt')).status, 404);
  });

  it('/__tipcl/call forwards with the page identity; rejects bad token, bad Host, cross-site, denied methods', async () => {
    const since = t.calls.length;
    const ok = await t.http('/__tipcl/call', { method: 'POST', headers: { 'x-tipcl-page': token, 'content-type': 'application/json' }, body: { method: 'ui.toast', params: { text: 'hi', plugin: 'evil' } } });
    assert.equal(ok.status, 200);
    assert.deepEqual(JSON.parse(ok.text), { result: null });
    const fwd = await t.waitForCall('ui.toast', (p) => p.text === 'hi', { since });
    assert.equal(fwd.params.plugin, 'hello-plugin');

    const st = await t.http('/__tipcl/call', { method: 'POST', headers: { 'x-tipcl-page': token }, body: { method: 'credentials.status' } });
    assert.deepEqual(JSON.parse(st.text).result, { HELLO_API_KEY: { configured: true } });

    assert.equal((await t.http('/__tipcl/call', { method: 'POST', headers: { 'x-tipcl-page': 'nope' }, body: { method: 'ui.toast' } })).status, 403);
    assert.equal((await t.http('/__tipcl/call', { method: 'POST', body: { method: 'ui.toast' } })).status, 403);
    assert.equal((await t.http('/__tipcl/call', { method: 'POST', headers: { 'x-tipcl-page': token }, body: { method: 'tools.register' } })).status, 403);
    assert.equal((await t.http('/__tipcl/call', { method: 'POST', host: 'evil.example:80', headers: { 'x-tipcl-page': token }, body: { method: 'ui.toast' } })).status, 421);
    assert.equal((await t.http('/api/hello-plugin/status', { host: `attacker.test:${t.port}` })).status, 421);
    assert.equal((await t.http('/api/hello-plugin/poke', { method: 'POST', headers: { origin: 'https://evil.example' } })).status, 403);
    assert.equal((await t.http('/api/hello-plugin/poke', { method: 'POST', headers: { 'sec-fetch-site': 'cross-site' } })).status, 403);
    assert.equal((await t.http('/api/hello-plugin/status', { host: `localhost:${t.port}` })).status, 200);
    // errors from TipCL come back with their code
    t.handlers['ui.prompt'] = () => { throw Object.assign(new Error('nope'), { code: 'permission_denied' }); };
    const denied = await t.http('/__tipcl/call', { method: 'POST', headers: { 'x-tipcl-page': token }, body: { method: 'ui.prompt', params: {} } });
    assert.equal(denied.status, 403);
    assert.equal(JSON.parse(denied.text).error.code, 'permission_denied');
  });

  it('/__tipcl/events fans out host events to pages (and updates the subscription)', async () => {
    const since = t.calls.length;
    const s = await t.sse(`/__tipcl/events?k=${token}&topics=game.*,theme.changed,config.changed`);
    assert.equal(s.status, 200);
    const sub = await t.waitForCall('events.subscribe', (p) => p.plugin === 'hello-plugin' && p.topics.includes('game.*'), { since });
    assert.ok(sub.params.topics.includes('game.*'));
    assert.ok(t.subscriptions['hello-plugin'].has('theme.changed')); // always subscribed (host keeps the theme current)
    assert.equal((await t.sse('/__tipcl/events?k=bad&topics=x')).status, 403);
    // one topic TipCL refuses does not take the others down; closing the page unsubscribes its topics
    const s2 = await t.sse(`/__tipcl/events?k=${token}&topics=forbidden.topic,game.started`);
    await t.waitForCall('events.subscribe', (p) => p.plugin === 'hello-plugin' && p.topics.length === 1 && p.topics[0] === 'game.started', { since });
    await sleep(30);
    assert.ok(t.subscriptions['hello-plugin'].has('game.started'));
    assert.ok(!t.subscriptions['hello-plugin'].has('forbidden.topic'));
    s2.close();
    await t.waitForCall('events.unsubscribe', (p) => p.plugin === 'hello-plugin' && p.topics.includes('game.started'), { since });
    await sleep(30);
    assert.ok(!t.subscriptions['hello-plugin'].has('game.started'));
    assert.ok(t.subscriptions['hello-plugin'].has('game.*'));

    t.notify('event', { topic: 'game.log', data: { instance: 'i1', line: '[Server] Done' }, plugin: 'hello-plugin' });
    t.notify('event', { topic: 'game.log', data: { instance: 'i1', line: 'for probe only' }, plugin: 'probe' });
    t.notify('event', { topic: 'theme.changed', data: { theme: 'light', colors: { bg: '#ffffff' } } });
    const evs = await s.until((e) => e.length >= 2);
    assert.deepEqual(evs[0], { topic: 'game.log', data: { instance: 'i1', line: '[Server] Done' } });
    assert.equal(evs[1].topic, 'theme.changed');
    assert.equal(evs[1].data.mode, 'light');
    assert.equal(evs[1].data.colors.bg, '#ffffff');
    assert.equal(evs[1].data.colors.fg, '#f0f0f0'); // merged with the current theme
    // node-side handler saw the event too
    assert.equal(JSON.parse((await t.http('/api/hello-plugin/status')).text).lastLog, '[Server] Done');
    // the shell now renders the new theme
    assert.match((await t.http(`/__tipcl/view/hello-plugin/main?k=${token}`)).text, /--tipcl-bg:#ffffff;/);
    this_sse = s;
  });

  it('config cells update live (node + pages) and onChange side effects run', async () => {
    const since = t.calls.length;
    t.notify('plugin.configChanged', { id: 'hello-plugin', key: 'greeting', value: 'Hi' });
    await sleep(50);
    assert.equal(JSON.parse((await t.http('/api/hello-plugin/config')).text).greeting, 'Hi');
    assert.equal((await t.http('/api/hello-consumer/greet')).text, 'Hi, consumer!');
    const evs = await this_sse.until((e) => e.some((x) => x.topic === 'config.changed'));
    assert.deepEqual(evs.find((x) => x.topic === 'config.changed').data, { key: 'greeting', value: 'Hi' });

    // a page writes config through /__tipcl/call → TipCL → plugin.configChanged → onChange → overlayUpdate
    await t.http('/__tipcl/call', { method: 'POST', headers: { 'x-tipcl-page': token }, body: { method: 'config.set', params: { key: 'showOverlay', value: false } } });
    const upd = await t.waitForCall('ui.overlayUpdate', () => true, { since });
    assert.deepEqual(upd.params, { plugin: 'hello-plugin', id: 'bubble', visible: false });
    this_sse.close();
  });

  it('hook.invoke returns modified data; null when unchanged / unknown', async () => {
    const out = await t.request('hook.invoke', { plugin: 'hello-plugin', hook: 'launch.before', data: { instance: 'i1', jvmArgs: ['-Xmx2G'], gameArgs: [], env: {} } });
    assert.deepEqual(out.jvmArgs, ['-Xmx2G', '-Dhello.plugin=Hi']);
    assert.equal(out.env.HELLO_PLUGIN, '1');
    assert.equal(await t.request('hook.invoke', { plugin: 'hello-plugin', hook: 'game.exit', data: {} }), null);
    assert.equal(await t.request('hook.invoke', { plugin: 'ghost', hook: 'launch.before', data: {} }), null);
  });

  it('action.invoke runs the handler; credentials resolve', async () => {
    const since = t.calls.length;
    t.notify('action.invoke', { plugin: 'hello-plugin', id: 'say-hi', context: { instance: '1.21' } });
    const toast = await t.waitForCall('ui.toast', (p) => p.text.includes('1.21'), { since });
    assert.equal(toast.params.kind, 'success');
    const st = JSON.parse((await t.http('/api/hello-plugin/status')).text);
    assert.equal(st.credentialConfigured, true);
    assert.deepEqual(st.providers, [{ id: 'fake', name: 'Fake' }]);
  });

  it('unload: effects disposed in reverse order, registrations undone, routes and streams closed, dependents unbound', async () => {
    const live = await t.sse('/api/hello-plugin/ticks?n=1000');
    await live.until((e) => e.length >= 1);
    const page = await t.sse(`/__tipcl/events?k=${token}&topics=game.*`);
    const since = t.calls.length;
    const r = await t.request('plugin.unload', { id: 'hello-plugin' });
    assert.equal(r.ok, true);

    const disposed = t.calls.slice(since).filter((c) => c.method === 'log' && c.params.plugin === 'hello-plugin' && c.params.text.startsWith('effect disposed: ')).map((c) => c.params.text.slice('effect disposed: '.length));
    const registered = ['provide hello', 'ui page', 'ui aiView', 'ui dashCard', 'ui overlay', 'ui action', 'hook launch.before', 'game.log',
      'route ticks', 'route image', 'route echo', 'route files', 'route config', 'route haiku', 'route poke', 'route status',
      'preset hello-quick', 'tool hello_wait', 'tool hello_badge', 'tool hello_ask'];
    const firstIndex = registered.map((l) => disposed.indexOf(l));
    assert.ok(firstIndex.every((i) => i >= 0), `all effects disposed: ${JSON.stringify(disposed)}`);
    for (let i = 1; i < firstIndex.length; i++) assert.ok(firstIndex[i] < firstIndex[i - 1], `${registered[i]} disposed before ${registered[i - 1]}`);

    assert.deepEqual(t.callsOf('tools.unregister', (p) => p.plugin === 'hello-plugin').map((c) => c.params.name).sort(), ['hello_ask', 'hello_badge', 'hello_wait']);
    assert.equal(t.callsOf('presets.unregister', (p) => p.id === 'hello-quick').length, 1);
    assert.equal(t.callsOf('hooks.unregister', (p) => p.hook === 'launch.before').length, 1);
    assert.equal(t.callsOf('ui.unregister', (p) => p.plugin === 'hello-plugin').length, 6);

    await live.until(() => false, 1000).catch(() => {});
    assert.ok(live.ended, 'open route stream closed');
    await page.until(() => false, 1000).catch(() => {});
    assert.ok(page.ended, 'page SSE closed');
    assert.equal((await t.http('/api/hello-plugin/status')).status, 404);
    assert.equal((await t.http(`/__tipcl/view/hello-plugin/main?k=${token}`)).status, 403);
    assert.equal((await t.http('/api/hello-consumer/greet')).status, 404);
    assert.equal(JSON.parse((await t.http('/api/hello-consumer/status')).text).hasHello, false);
    assert.match(t.stderr, /^\[hello-consumer\] hello service went away$/m);
  });

  it('reload: loading the same plugin again works and re-binds dependents', async () => {
    const r = await t.request('plugin.load', await loadParams(HELLO, { home, config: { greeting: 'Yo' } }));
    assert.equal(r.ok, true, r.error);
    assert.equal((await t.http('/api/hello-consumer/greet')).text, 'Yo, consumer!');
  });

  it('a failing plugin reports {ok:false} and leaves nothing behind', async () => {
    const r = await t.request('plugin.load', { id: 'broken', dir: PROBE, main: 'missing.mjs', manifest: { id: 'broken' }, granted: [] });
    assert.equal(r.ok, false);
    assert.match(r.error, /missing\.mjs|Cannot find/);
    const unknown = await t.request('nope.method', {}).catch((e) => e);
    assert.equal(unknown.code, 'method_not_found');
  });

  it('stdout carried only protocol lines', () => {
    // FakeTipcl JSON.parses every stdout line and would have thrown otherwise; console output went to stderr
    assert.match(t.stderr, /^\[host\] TipCL plugin host /m);
  });
});
