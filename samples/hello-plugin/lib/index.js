/**
 * hello-plugin root entry (host side). Written in the same style as dsh-game's index.ts:
 *   - UI contributions: a sidebar page, an AI-chat tab, a dash card, an overlay, an action
 *   - routes (Fetch API):
 *       GET  /api/hello-plugin/ticks?n=5      SSE stream (ReadableStream body), cancel() on disconnect
 *       GET  /api/hello-plugin/image          binary PNG
 *       POST /api/hello-plugin/echo           echoes method / JSON body / a header
 *       GET  /api/hello-plugin/files/*        wildcard route
 *       GET  /api/hello-plugin/config         live config cells
 *       GET  /api/hello-plugin/haiku?topic=   SSE of an llm.stream completion
 *       POST /api/hello-plugin/poke           wakes a waiting hello_wait tool
 *       GET  /api/hello-plugin/status         dash card data, credential configured?
 *   - a launch.before hook that adds a JVM arg, a game.log subscription
 *   - a service `hello` for other plugins (consumed by samples/hello-consumer)
 */

import { badge } from './png.js';
import { poke, status } from './state.js';

export const name = 'hello-plugin';
export const inject = ['connection'];

const json = (body, init = {}) => new Response(JSON.stringify(body), { ...init, headers: { 'content-type': 'application/json', 'cache-control': 'no-store', ...(init.headers ?? {}) } });

/** @param {import('../../../src/TipCL/Resources/plugin-host/tipcl-plugin').PluginContext} ctx @param {import('../../../src/TipCL/Resources/plugin-host/tipcl-plugin').ConfigCells} config */
export function apply(ctx, config) {
  console.log(`hello-plugin starting, data dir ${ctx.paths.data}, DSH_HOME=${process.env.DSH_HOME}`);

  // ── service for other plugins (ctx.get('hello') in hello-consumer) ──
  ctx.effect(() => ctx.provide('hello', {
    greet: (who) => `${config.greeting.get()}, ${who}!`,
    get launches() { return status.launches; },
  }), 'provide hello');

  // ── UI ──
  ctx.effect(() => ctx.ui.page({ id: 'hello', label: 'Hello', icon: '\ue80f', order: 50, view: 'main', group: 'top' }), 'ui page');
  ctx.effect(() => ctx.ui.aiView({ id: 'watch', label: '观察', order: 30, view: 'watch', presets: ['hello', 'hello-quick'] }), 'ui aiView');
  ctx.effect(() => ctx.ui.dashCard({ id: 'card', label: 'Hello 卡片', view: 'card', defaultW: 2, defaultH: 1 }), 'ui dashCard');
  ctx.effect(() => ctx.ui.block({ id: 'panel', label: 'Hello 面板', view: 'card' }), 'ui block');
  status.overlayVisible = !!config.showOverlay.get();
  ctx.effect(() => ctx.ui.overlay({ id: 'bubble', view: 'bubble', width: 220, height: 120, anchor: 'bottom-right', margin: 16, clickThrough: false, visible: status.overlayVisible }), 'ui overlay');
  ctx.effect(() => ctx.ui.action({ id: 'say-hi', label: 'Say hi', where: 'launch' }, (context) => {
    void ctx.ui.toast(`${config.greeting.get()}！（${context?.instance ?? '没有实例'}）`, 'success');
  }), 'ui action');

  // config changes apply live (cells read the new value; onChange for side effects)
  ctx.config.onChange((key, value) => {
    if (key === 'showOverlay') {
      status.overlayVisible = !!value;
      void ctx.ui.overlayUpdate('bubble', { visible: !!value }).catch(() => {});
    }
  });

  // ── launcher integration ──
  ctx.effect(() => ctx.hooks.register('launch.before', (data) => {
    status.launches++;
    return {
      ...data,
      jvmArgs: [...(data.jvmArgs ?? []), `-Dhello.plugin=${config.greeting.get()}`],
      env: { ...(data.env ?? {}), HELLO_PLUGIN: '1' },
    };
  }), 'hook launch.before');
  ctx.effect(() => ctx.events.on('game.log', (data) => { status.lastLog = data?.line ?? null; }), 'game.log');

  // ── routes (DSH style: register inside effects) ──
  const route = (path, methods, fetch, label) =>
    ctx.effect(() => ctx.connection.fetch.register({ path, methods, requestBody: 'buffered', fetch }), label);

  route('/api/hello-plugin/ticks', ['GET'], (request) => {
    const n = Math.min(1000, Number(new URL(request.url).searchParams.get('n') ?? 5));
    let timer = null;
    const stream = new ReadableStream({
      start(controller) {
        const enc = new TextEncoder();
        let i = 0;
        controller.enqueue(enc.encode(': connected\n\n'));
        timer = setInterval(() => {
          i++;
          controller.enqueue(enc.encode(`data: ${JSON.stringify({ tick: i, at: Date.now() })}\n\n`));
          if (i >= n) { clearInterval(timer); controller.close(); }
        }, 30);
      },
      cancel() {
        clearInterval(timer);
        status.ticksCancelled = (status.ticksCancelled ?? 0) + 1;
        console.log('ticks stream cancelled by client');
      },
    });
    return new Response(stream, { headers: { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' } });
  }, 'route ticks');

  route('/api/hello-plugin/image', ['GET'], (request) => {
    const color = new URL(request.url).searchParams.get('color') ?? '#4f8cff';
    return new Response(badge(1, color), { headers: { 'content-type': 'image/png' } });
  }, 'route image');

  route('/api/hello-plugin/echo', ['POST', 'PUT'], async (request) => {
    const body = await request.json().catch(() => null);
    return json({ method: request.method, body, header: request.headers.get('x-hello') });
  }, 'route echo');

  route('/api/hello-plugin/files/*', ['GET'], (request) => {
    const rest = new URL(request.url).pathname.slice('/api/hello-plugin/files/'.length);
    return new Response(`file:${rest}`);
  }, 'route files');

  route('/api/hello-plugin/config', ['GET'], () => json({
    greeting: config.greeting.get(), maxWaitSeconds: config.maxWaitSeconds.get(), showOverlay: config.showOverlay.get(), mood: config.mood.get(),
  }), 'route config');

  // llm.stream → SSE: the page shows the text as it arrives
  route('/api/hello-plugin/haiku', ['GET'], (request) => {
    const topic = new URL(request.url).searchParams.get('topic') || 'Minecraft';
    const llm = ctx.get('llm');
    const abort = new AbortController();
    const stream = new ReadableStream({
      async start(controller) {
        const enc = new TextEncoder();
        const send = (obj) => { try { controller.enqueue(enc.encode(`data: ${JSON.stringify(obj)}\n\n`)); } catch { /* closed */ } };
        try {
          let [provider, model] = String(config.haikuModel.get() || '').split('/');
          if (!provider) {
            provider = llm.listProviders()[0]?.id; // synchronous, like DSH
            model = (await llm.listModels(provider))[0]?.id;
          }
          for await (const chunk of llm.stream({
            provider, model, system: '写一首中文俳句。', reasoningEffort: 'off', maxTokens: 200, signal: abort.signal,
            messages: [{ role: 'user', content: [{ type: 'text', text: topic }] }],
          })) send(chunk);
        } catch (err) {
          send({ type: 'error', message: String(err?.message ?? err) });
        }
        try { controller.close(); } catch { /* closed */ }
      },
      cancel() { abort.abort(); },
    });
    return new Response(stream, { headers: { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' } });
  }, 'route haiku');

  route('/api/hello-plugin/poke', ['POST'], () => json({ woke: poke('poke') }), 'route poke');

  route('/api/hello-plugin/status', ['GET'], async () => {
    const cred = await ctx.get('credentials').resolve('HELLO_API_KEY').catch(() => undefined);
    return json({ ...status, credentialConfigured: !!cred?.value, providers: ctx.get('llm').listProviders() });
  }, 'route status');

  ctx.logger.info('hello-plugin ready');
}
