// Test-only plugin exposing host internals over routes (sessions mirror, env, streaming request bodies).
export const name = 'probe';
const seen = [];
export function apply(ctx) {
  const envAtApply = { TIPCL_PLUGIN_DATA: process.env.TIPCL_PLUGIN_DATA, DSH_HOME: process.env.DSH_HOME };
  const sessions = ctx.get('sessions');
  const json = (b) => Response.json(b);
  ctx.connection.fetch.register({ path: '/api/probe/env', fetch: () => json({ envAtApply, paths: ctx.paths }) });
  // synchronous read: no await between get() and snapshotEvents()
  ctx.connection.fetch.register({
    path: '/api/probe/snapshot',
    fetch: (req) => {
      const q = new URL(req.url).searchParams;
      const s = sessions.get(q.get('chat'));
      return json({ seq: s.seq, running: s.running, seqs: s.snapshotEvents(Number(q.get('from') ?? 0)).map((e) => e.seq), types: s.snapshotEvents(0).map((e) => e.type), time: s.snapshotEvents(0)[0]?.time ?? null });
    },
  });
  ctx.connection.fetch.register({
    path: '/api/probe/ready',
    fetch: async (req) => { const s = sessions.get(new URL(req.url).searchParams.get('chat')); await s.ready(); return json({ seq: s.seq }); },
  });
  ctx.effect(() => sessions.subscribe('live-chat', (e) => seen.push(e.seq)), 'subscribe');
  ctx.connection.fetch.register({ path: '/api/probe/seen', fetch: () => json(seen) });
  ctx.connection.fetch.register({
    path: '/api/probe/upload', methods: ['POST'], requestBody: 'stream',
    fetch: async (req) => { let n = 0; for await (const c of req.body) n += c.length; return json({ bytes: n }); },
  });
  ctx.connection.fetch.register({ path: '/api/probe/launcher', fetch: async () => json(await ctx.launcher.call('instances_list', { all: true })) });
}
