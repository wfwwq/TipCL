// Transliteration of dsh-game src/index.ts host patterns (types stripped, otherwise unchanged).
import { RUNS_DIR, here, hooks, listeners } from './runtime.js';

export const name = 'dsh-game';
export const inject = ['connection'];
// schemastery-like export: callable, returns defaults merged (dsh-game bundles schemastery)
export const Config = Object.assign((v = {}) => ({ voice: 'zh_female', censor: true, ...v }), { dict: {} });

export function apply(ctx, config = {}) {
  hooks.live = () => ctx.get?.('liveBody');
  ctx.effect(() => () => { hooks.live = undefined; }, 'dsh-game live body');
  const route = (path, methods, fetch, label) =>
    ctx.effect(() => ctx.connection.fetch.register({ path, methods, requestBody: 'buffered', fetch }), label);

  route('/api/dsh-game/events', ['GET'], (request) => {
    const params = new URL(request.url).searchParams;
    const page = params.get('page') ?? `anon-${Math.random().toString(36).slice(2)}`;
    let listen = null;
    let ping = null;
    const stream = new ReadableStream({
      start(controller) {
        const enc = new TextEncoder();
        const send = (text) => { try { controller.enqueue(enc.encode(text)); } catch { /* 已关闭 */ } };
        listen = (event) => { if ((controller.desiredSize ?? 1) <= 0 && event.type === 'frame') return; send(`data: ${JSON.stringify({ ...event, page })}\n\n`); };
        listeners.add(listen);
        send(`data: ${JSON.stringify({ type: 'game-state', state: null, at: Date.now() })}\n\n`);
        ping = setInterval(() => send(': ping\n\n'), 15_000);
        send(': connected\n\n');
      },
      cancel() { if (listen) listeners.delete(listen); if (ping) clearInterval(ping); },
    });
    return new Response(stream, { headers: { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' } });
  }, 'dsh-game events');

  route('/api/dsh-game/state', ['GET'], () => new Response(JSON.stringify({
    voice: config.voice?.get(), censor: config.censor?.get(), missing: config.nope?.get() ?? null,
    runsDir: RUNS_DIR, here, live: !!hooks.live?.(), onAir: hooks.live?.()?.onAir ?? null,
  }), { headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } }), 'dsh-game state');

  // dsh-game listModels hook: listProviders() used synchronously
  hooks.listModels = async () => {
    const llm = ctx.get?.('llm');
    if (!llm?.listProviders || !llm.listModels) return null;
    const providers = llm.listProviders();
    const lists = await Promise.all(providers.map(async (p) => {
      try {
        const models = await llm.listModels(p.id);
        return models.map((m) => ({ id: `${p.id}/${m.id}`, provider: p.id, providerName: p.name || p.id, name: m.name || m.id }));
      } catch { return []; }
    }));
    return lists.flat();
  };
  route('/api/dsh-game/models', ['GET'], async () => new Response(JSON.stringify(await hooks.listModels())), 'dsh-game models');
  route('/api/dsh-game/said', ['POST'], (request) => new Response(`ok ${new URL(request.url).searchParams.get('id')}`), 'dsh-game said');
}
