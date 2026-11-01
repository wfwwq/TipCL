/**
 * hello-plugin web side (browser ESM, plain DOM, no framework). Loaded by the TipCL view shell, which
 * calls apply(ui) and mounts the view TipCL asked for:
 *   main    sidebar page: greeting, settings & credential, new chat with the preset, SSE + llm demos
 *   watch   AI-chat tab: renders the chat's events like dsh-game's game tab, input that steers, stop button
 *   card    dash card: last game log line, launches counted by the launch.before hook
 *   bubble  overlay: transparent, click to grow/shrink via tipcl.overlay.update
 */

const h = (tag, attrs = {}, ...children) => {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k === 'class') el.className = v;
    else el.setAttribute(k, v);
  }
  el.append(...children.filter((c) => c != null));
  return el;
};

const CSS = `
.hp{padding:16px;display:flex;flex-direction:column;gap:12px;font-size:14px}
.hp h2{margin:0;font-size:18px}
.hp .row{display:flex;gap:8px;align-items:center;flex-wrap:wrap}
.hp button{background:var(--tipcl-accent);color:#fff;border:0;border-radius:6px;padding:6px 12px;cursor:pointer}
.hp button.ghost{background:transparent;color:var(--tipcl-fg);border:1px solid var(--tipcl-border)}
.hp input{background:var(--tipcl-card);color:var(--tipcl-fg);border:1px solid var(--tipcl-border);border-radius:6px;padding:6px 8px;flex:1;min-width:120px}
.hp .card{background:var(--tipcl-card);border:1px solid var(--tipcl-border);border-radius:8px;padding:12px}
.hp .muted{color:var(--tipcl-muted)}
.hp pre{margin:0;white-space:pre-wrap;font:12px/1.5 ui-monospace,Consolas,monospace}
.hw{display:flex;flex-direction:column;height:100%}
.hw .bar{display:flex;gap:8px;align-items:center;padding:8px 12px;border-bottom:1px solid var(--tipcl-border)}
.hw .bar .live{color:#3ddc84}
.hw .term{flex:1;overflow:auto;padding:8px 12px;font:12px/1.6 ui-monospace,Consolas,monospace}
.hw .term .u{color:#ffd479}.hw .term .a{color:var(--tipcl-fg)}.hw .term .t{color:var(--tipcl-accent)}.hw .term .r{color:var(--tipcl-muted)}.hw .term .e{color:var(--tipcl-danger)}
.hw form{display:flex;gap:8px;padding:8px 12px;border-top:1px solid var(--tipcl-border)}
.bubble{position:fixed;right:0;bottom:0;margin:8px;padding:10px 14px;border-radius:16px;background:color-mix(in srgb,var(--tipcl-accent) 85%,transparent);color:#fff;cursor:pointer;user-select:none;font-size:14px;box-shadow:0 4px 18px #0006}
`;

export function apply(ui) {
  ui.css(CSS);

  // ── sidebar page ──
  ui.view('main', (el, _props, tipcl) => {
    const greet = h('h2', {}, '…');
    const cfgBox = h('pre', {}, '');
    const credState = h('span', { class: 'muted' }, '');
    const keyInput = h('input', { type: 'password', placeholder: 'HELLO_API_KEY' });
    const providers = h('span', { class: 'muted' }, '');
    const ticks = h('pre', {}, '');
    const haiku = h('pre', {}, '');
    const topic = h('input', { placeholder: '俳句主题', value: '苦力怕' });

    const refresh = async () => {
      const cfg = await tipcl.config.get();
      greet.textContent = `${cfg.greeting}，${tipcl.plugin.id} ${tipcl.plugin.version}`;
      cfgBox.textContent = JSON.stringify(cfg, null, 2);
      const st = await tipcl.credentials.status();
      credState.textContent = st?.HELLO_API_KEY?.configured ? '已配置' : '未配置';
      providers.textContent = (await tipcl.llm.providers().catch(() => [])).map((p) => p.name || p.id).join('、') || '无';
    };
    void refresh();
    const offs = [tipcl.config.onChange(() => void refresh()), tipcl.credentials.onChange(() => void refresh())];

    let es = null;
    const startTicks = () => {
      es?.close();
      ticks.textContent = '';
      es = new EventSource('/api/hello-plugin/ticks?n=10');
      es.onmessage = (m) => { ticks.textContent += m.data + '\n'; };
      es.onerror = () => es.close(); // server closes the stream after n ticks
    };
    const writeHaiku = () => {
      haiku.textContent = '';
      const s = new EventSource(`/api/hello-plugin/haiku?topic=${encodeURIComponent(topic.value)}`);
      s.onmessage = (m) => {
        const c = JSON.parse(m.data);
        if (c.type === 'text-delta') haiku.textContent += c.text;
        if (c.type === 'finish' || c.type === 'error') { s.close(); if (c.type === 'error') haiku.textContent += `\n[${c.message}]`; }
      };
      s.onerror = () => s.close();
    };

    el.append(h('div', { class: 'hp' },
      greet,
      h('div', { class: 'row' },
        h('button', { onclick: async () => { const { chatId } = await tipcl.ai.createChat({ preset: 'hello', view: 'watch', prompt: '帮我等 10 秒，看看会发生什么。' }); void tipcl.ui.toast(`新对话 ${chatId}`, 'success'); } }, '用 Hello 预设开新对话'),
        h('button', { class: 'ghost', onclick: () => tipcl.nav.open('ai') }, '去 AI 页'),
        h('button', { class: 'ghost', onclick: async () => { const ok = await tipcl.ui.confirm('确认', '切换浮层显示？'); if (ok) { const c = await tipcl.config.get(); await tipcl.config.set('showOverlay', !c.showOverlay); } } }, '切换浮层'),
        h('button', { class: 'ghost', onclick: () => fetch('/api/hello-plugin/poke', { method: 'POST' }) }, '戳一下（唤醒 hello_wait）'),
      ),
      h('div', { class: 'card' }, h('div', { class: 'muted' }, '当前设置（manifest config，改了立即生效）'), cfgBox),
      h('div', { class: 'card row' }, 'HELLO_API_KEY：', credState, keyInput,
        h('button', { onclick: async () => { await tipcl.credentials.set('HELLO_API_KEY', keyInput.value || null); keyInput.value = ''; void refresh(); } }, '保存')),
      h('div', { class: 'card row' }, '模型供应商：', providers),
      h('div', { class: 'card' }, h('div', { class: 'row' }, h('button', { onclick: startTicks }, 'SSE 计数'), h('img', { src: '/api/hello-plugin/image?color=%23ff8c42', width: '32', height: '32', alt: '' }), h('img', { src: tipcl.asset('assets/icon.png'), width: '32', height: '32', alt: '' })), ticks),
      h('div', { class: 'card' }, h('div', { class: 'row' }, topic, h('button', { onclick: writeHaiku }, 'llm.stream 写俳句')), haiku),
    ));
    return () => { es?.close(); offs.forEach((off) => off()); };
  });

  // ── AI chat tab: like dsh-game's game tab ──
  ui.view('watch', (el, { chatId }, tipcl) => {
    const term = h('div', { class: 'term' });
    const live = h('span', { class: 'live' }, '');
    const input = h('input', { placeholder: '跟它说话（它在跑时会 steer，下一步就能看到）' });
    let running = false;
    const line = (cls, text) => {
      term.append(h('div', { class: cls }, text));
      while (term.childElementCount > 300) term.firstElementChild.remove();
      term.scrollTop = term.scrollHeight;
    };
    const setRunning = (v) => { running = v; live.textContent = v ? '● 运行中' : ''; };
    const off = tipcl.ai.subscribe(chatId, (e) => {
      switch (e.type) {
        case 'turn/start': setRunning(true); break;
        case 'turn/end': setRunning(false); line('r', `— 回合结束（${e.data?.reason ?? ''}）`); break;
        case 'user/message': line('u', `你 › ${e.data?.text ?? ''}`); break;
        case 'assistant/message':
          for (const p of e.data?.message?.content ?? []) {
            if (p.type === 'text' && p.text?.trim()) line('a', `说 › ${p.text}`);
            if (p.type === 'tool-call') line('t', `▸ ${p.name} ${JSON.stringify(p.args ?? {})}`);
          }
          break;
        case 'tool/result': {
          const t = (e.data?.content ?? []).filter((p) => p.type === 'text').map((p) => p.text).join(' ');
          const imgs = (e.data?.content ?? []).filter((p) => p.type === 'image').length;
          line(e.data?.isError ? 'e' : 'r', `◆ ${t}${imgs ? ` [${imgs} 张图]` : ''}`);
          break;
        }
        default: break;
      }
    });
    void tipcl.ai.state(chatId).then((s) => setRunning(!!s?.running)).catch(() => {});
    const form = h('form', {
      onsubmit: async (ev) => {
        ev.preventDefault();
        const text = input.value.trim();
        if (!text) return;
        input.value = '';
        const r = await tipcl.ai.send(chatId, text, { mode: running ? 'steer' : 'auto' });
        if (!r?.ok) { void tipcl.ui.toast(r?.error === 'busy' ? 'AI 正在别的对话里忙' : `没发出去：${r?.error ?? ''}`, 'warn'); return; }
        if (running) void fetch('/api/hello-plugin/poke', { method: 'POST' }); // like dsh-game's nudge
      },
    }, input, h('button', { type: 'submit' }, '发送'));
    el.append(h('div', { class: 'hw hp', style: 'padding:0;gap:0' },
      h('div', { class: 'bar' }, h('strong', {}, '观察'), h('span', { class: 'muted' }, chatId ?? '(无对话)'), live, h('span', { style: 'flex:1' }),
        h('button', { class: 'ghost', onclick: () => tipcl.ai.cancel(chatId) }, '■ 停止')),
      term, form));
    return off;
  });

  // ── dash card ──
  ui.view('card', (el, _props, tipcl) => {
    const body = h('div', { class: 'muted' }, '…');
    const load = async () => {
      const s = await fetch('/api/hello-plugin/status').then((r) => r.json()).catch(() => null);
      body.textContent = s ? `启动 ${s.launches} 次 · 最后一行日志：${s.lastLog ?? '无'}` : '读取失败';
    };
    void load();
    const off = tipcl.events.on('game.log', () => void load());
    el.append(h('div', { class: 'hp' }, h('strong', {}, 'Hello'), body));
    return off;
  });

  // ── overlay (transparent page) ──
  ui.view('bubble', (el, _props, tipcl) => {
    let big = false;
    const bubble = h('div', { class: 'bubble', onclick: async () => {
      big = !big;
      bubble.textContent = big ? '变大啦！再点一下变小' : 'Hello 👋';
      await tipcl.overlay.update(big ? { width: 320, height: 160 } : { width: 220, height: 120 }).catch(() => {});
    } }, 'Hello 👋');
    el.append(bubble);
    const off = tipcl.theme.onChange((t) => { bubble.title = `主题：${t.mode}`; });
    return off;
  });
}
