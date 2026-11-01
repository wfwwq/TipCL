// Transliteration of dsh-game src/agent.ts patterns. The import is exactly dsh-game's.
import { defineTool } from '@deepseek-ai/dsh-tools';
import { randomUUID } from 'node:crypto';
import { broadcast } from './runtime.js';

export const name = 'dsh-game-agent';
export const inject = ['tools', 'attachments'];

const text = (value) => [{ type: 'text', text: value }];
const wordsOf = (event) => (event?.data?.message?.content ?? []).filter((p) => p?.type === 'text' && typeof p.text === 'string').map((p) => p.text.trim()).filter(Boolean).join('\n');

function recentEvents(ctx, sessionId, from) {
  const session = ctx.get?.('sessions')?.get?.(sessionId);
  if (!session) return [];
  return session.snapshotEvents(from ?? Math.max(0, Number(session.seq) - 300));
}

async function ttsKey(ctx) {
  const credentials = ctx.get?.('credentials');
  const hit = credentials ? await credentials.resolve('DOUBAO_TTS_API_KEY').catch(() => undefined) : undefined;
  return hit?.value ?? process.env.DOUBAO_TTS_API_KEY ?? '';
}

async function summarizeWithDsh(ctx, lines, instruction) {
  const llm = ctx.get?.('llm');
  if (!llm) return '';
  let out = '';
  for await (const chunk of llm.stream({
    provider: 'fake', model: 'm1', system: instruction,
    messages: [{ id: randomUUID(), role: 'user', content: [{ type: 'text', text: lines.join('\n') }], source: { kind: 'dsh-game' } }],
    maxTokens: 2000, reasoningEffort: 'off', purpose: 'dsh-game-summary', signal: AbortSignal.timeout(60_000),
  })) {
    if (chunk.type === 'text-delta') out += chunk.text;
    if (chunk.type === 'finish' && chunk.reason?.kind !== 'stop') throw new Error(`摘要没有正常结束：${chunk.reason?.kind}`);
  }
  return out.trim();
}

export function apply(ctx) {
  ctx.effect(() => ctx.tools.register(defineTool({
    name: 'game_continue',
    description: '让游戏接着往下走',
    parameters: {
      after: { type: 'integer', required: true, description: '你上一次看到的时刻编号（第一次填 0）' },
      mood: { type: 'string', description: '表情' },
    },
    output: {
      schema: { type: 'object', properties: { text: { type: 'string', required: true }, image: { type: 'json', required: true } }, additionalProperties: false },
      render: (_args, value) => [...text(value.text), ...(value.image ? [{ type: 'image', attachment: value.image }] : [])],
    },
    async execute(args, exec) {
      const events = recentEvents(ctx, exec.agent?.id);
      const message = [...events].reverse().find((e) => e?.type === 'assistant/message' && (e.data?.message?.content ?? []).some((p) => p?.type === 'tool-call' && p.id === exec.callId));
      const said = wordsOf(message);
      broadcast({ type: 'say', text: said, at: Date.now() });
      const summary = await summarizeWithDsh(ctx, ['line one', 'line two'], '摘要');
      const key = await ttsKey(ctx);
      const [image] = await ctx.attachments.saveImages([{ data: new Uint8Array([0x89, 0x50, 0x4e, 0x47]), mediaType: 'image/png', name: 'game-screen' }]);
      return { text: `said=${said} summary=${summary} key=${key ? 'yes' : 'no'} after=${args.after}`, image };
    },
  })), 'game_continue tool');
}
