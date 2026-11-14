/**
 * hello-plugin agent entry: tools for the model (like dsh-game's agent.ts) and a runtime preset.
 *
 *   hello_wait   blocks up to N seconds (config maxWaitSeconds caps it) until the user steers the chat,
 *                POST /api/hello-plugin/poke is called, or time runs out; cancelled by the chat's stop.
 *                Returns text (incl. the words of the message that called it) + a generated PNG attachment.
 *   hello_badge  returns a PNG as raw image data (no attachment) — the other image path
 *   hello_ask    asks a model via ctx.get('llm').stream and returns its text
 */

import { defineTool } from 'tipcl:tools';
import { badge } from './png.js';
import { waiters } from './state.js';

export const name = 'hello-plugin-agent';
export const inject = ['tools', 'attachments'];

const text = (value) => [{ type: 'text', text: value }];

/** Words of the assistant message that contains this tool call, read synchronously from the session mirror. */
function ownWords(ctx, exec) {
  const session = ctx.get('sessions')?.get(exec.agent?.id);
  if (!session) return exec.messageText;
  const events = session.snapshotEvents(Math.max(0, Number(session.seq) - 300));
  const message = [...events].reverse().find((e) => e?.type === 'assistant/message' && (e.data?.message?.content ?? []).some((p) => p?.type === 'tool-call' && p.id === exec.callId));
  if (!message) return exec.messageText;
  return message.data.message.content.filter((p) => p?.type === 'text').map((p) => p.text).join('\n');
}

export function apply(ctx, config) {
  ctx.effect(() => ctx.presets.register({
    id: 'hello-quick',
    name: 'Hello（快速）',
    description: '运行时注册的预设：只带 hello_badge',
    systemPrompt: '你是演示助手。需要图的时候调用 hello_badge。',
    replaceSystemPrompt: true,
    includeRuntimeContext: false,
    tools: { include: ['hello_badge'], builtin: false },
    maxRounds: 0,
    view: 'watch',
  }), 'preset hello-quick');

  ctx.effect(() => ctx.tools.register(defineTool({
    name: 'hello_wait',
    description: '等一会儿：直到用户插话、页面上点了「戳一下」或者超时。返回等待结果和一张图。',
    parameters: {
      note: { type: 'string', required: true, description: '你在等什么' },
      seconds: { type: 'integer', description: '最多等几秒（不超过设置里的上限）' },
      color: { type: 'string', description: '图的颜色，#rrggbb' },
    },
    timeoutMs: 15 * 60_000,
    output: {
      schema: { type: 'object', properties: { text: { type: 'string', required: true }, image: { type: 'json', required: true } } },
      render: (_args, value) => [...text(value.text), ...(value.image ? [{ type: 'image', attachment: value.image }] : [])],
    },
    async execute(args, exec) {
      const words = ownWords(ctx, exec);
      const limit = Math.max(1, Math.min(Number(args.seconds ?? config.maxWaitSeconds.get()), Number(config.maxWaitSeconds.get())));
      const started = Date.now();
      const session = ctx.get('sessions')?.get(exec.agent?.id);
      const fromSeq = session ? session.seq : 0;
      const reason = await new Promise((resolve, reject) => {
        const finish = (r) => { cleanup(); resolve(r); };
        const wake = (r) => finish(r);
        // steer detection: poll the synchronous mirror like dsh-game's voiceTurnEnds does
        const poll = setInterval(() => {
          const steer = session?.snapshotEvents(fromSeq).find((e) => e.type === 'user/message');
          if (steer) finish(`steer:${steer.data?.text ?? ''}`);
        }, 50);
        const timer = setTimeout(() => finish('timeout'), limit * 1000);
        const onAbort = () => { cleanup(); reject(exec.signal.reason ?? new Error('aborted')); };
        const cleanup = () => { clearInterval(poll); clearTimeout(timer); waiters.delete(wake); exec.signal.removeEventListener('abort', onAbort); };
        waiters.add(wake);
        if (exec.signal.aborted) onAbort(); else exec.signal.addEventListener('abort', onAbort, { once: true });
      });
      const waited = (Date.now() - started) / 1000;
      const [image] = await ctx.attachments.saveImages([{ data: badge(Math.min(1, waited / limit), args.color ?? '#4f8cff'), mediaType: 'image/png', name: 'hello-wait' }]);
      return {
        text: `等「${args.note}」：${reason}，等了 ${waited.toFixed(1)} 秒。你刚才说：${words || '（没说话）'}`,
        image,
      };
    },
  })), 'tool hello_wait');

  ctx.effect(() => ctx.tools.register({
    name: 'hello_badge',
    description: '画一个圆形徽章（PNG）。',
    parameters: { type: 'object', properties: { color: { type: 'string' } } }, // JSON Schema style works too
    execute: (args) => [
      { type: 'text', text: `徽章：${args.color ?? '#4f8cff'}` },
      { type: 'image', data: badge(0.8, args.color ?? '#4f8cff'), mediaType: 'image/png' },
    ],
  }), 'tool hello_badge');

  ctx.effect(() => ctx.tools.register(defineTool({
    name: 'hello_ask',
    description: '问另一个模型一个问题，返回它的回答。',
    parameters: { question: { type: 'string', required: true } },
    async execute(args, exec) {
      const llm = ctx.get('llm');
      const provider = llm.listProviders()[0]?.id;
      const model = (await llm.listModels(provider))[0]?.id;
      let out = '';
      for await (const chunk of llm.stream({ provider, model, messages: [{ role: 'user', content: args.question }], reasoningEffort: 'off', signal: exec.signal })) {
        if (chunk.type === 'text-delta') out += chunk.text;
        if (chunk.type === 'finish' && chunk.reason?.kind !== 'stop') throw new Error(`model stopped: ${chunk.reason?.kind}`);
      }
      return out.trim();
    },
  })), 'tool hello_ask');
}
