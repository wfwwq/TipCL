// TipCL plugin host: synchronous local mirror of AI chat events (DESIGN §5).
//
// DSH plugins read chat history synchronously: `ctx.get('sessions').get(id).snapshotEvents(from)`.
// TipCL pushes every chat event as an `event {topic:'ai.event', data:{chatId, event}}` notification;
// the mirror applies them in seq order and keeps the newest 2000 per chat. Because TipCL emits the
// `assistant/message` event BEFORE sending `tool.execute` on the same pipe, a tool can always find
// "its" message in the mirror. Chats first seen mid-way are back-filled with `ai.chats.events`.

const MAX_EVENTS = 2000;

/**
 * @typedef {{seq: number, type: string, data?: any, at?: number, time?: number}} ChatEvent
 * @typedef {{id: string, events: ChatEvent[], seq: number, running: boolean, filled: boolean, filling: Promise<void> | null, listeners: Set<(e: ChatEvent) => void>}} ChatState
 */

export class SessionMirror {
  constructor() {
    /** @type {Map<string, ChatState>} */
    this.chats = new Map();
  }

  /** @returns {ChatState} */
  chat(chatId) {
    let chat = this.chats.get(chatId);
    if (!chat) {
      chat = { id: chatId, events: [], seq: 0, running: false, filled: false, filling: null, listeners: new Set() };
      this.chats.set(chatId, chat);
    }
    return chat;
  }

  /** Apply one pushed event (duplicates / old seqs are ignored). */
  apply(chatId, event) {
    if (!chatId || !event || typeof event.seq !== 'number') return;
    const chat = this.chat(chatId);
    if (event.seq <= chat.seq) return;
    const e = withCompat(event);
    chat.events.push(e);
    if (chat.events.length > MAX_EVENTS) chat.events.splice(0, chat.events.length - MAX_EVENTS);
    chat.seq = e.seq;
    if (e.type === 'turn/start') chat.running = true;
    else if (e.type === 'turn/end') chat.running = false;
    for (const fn of chat.listeners) {
      try { fn(e); } catch (err) { console.error('session listener failed:', err); }
    }
  }

  /** Merge a fetched history (`ai.chats.events` result) under whatever arrived live meanwhile. */
  merge(chatId, result, running) {
    const chat = this.chat(chatId);
    const fetched = Array.isArray(result?.events) ? result.events.map(withCompat) : [];
    const bySeq = new Map();
    for (const e of fetched) bySeq.set(e.seq, e);
    for (const e of chat.events) bySeq.set(e.seq, e);
    chat.events = [...bySeq.values()].sort((a, b) => a.seq - b.seq).slice(-MAX_EVENTS);
    chat.seq = Math.max(chat.seq, Number(result?.seq) || 0, chat.events.at(-1)?.seq ?? 0);
    if (typeof running === 'boolean') chat.running = running;
    else {
      // derive from the last turn boundary we know about
      for (let i = chat.events.length - 1; i >= 0; i--) {
        const t = chat.events[i].type;
        if (t === 'turn/start') { chat.running = true; break; }
        if (t === 'turn/end') { chat.running = false; break; }
      }
    }
    chat.filled = true;
  }

  /**
   * Back-fill a chat once. `fetcher()` → `{seq, events}` (TipCL `ai.chats.events`).
   * @param {string} chatId
   * @param {() => Promise<any>} fetcher
   */
  backfill(chatId, fetcher) {
    const chat = this.chat(chatId);
    if (chat.filled) return Promise.resolve();
    if (!chat.filling) {
      chat.filling = fetcher().then((r) => this.merge(chatId, r), () => { /* stay unfilled; retry next time */ })
        .finally(() => { chat.filling = null; });
    }
    return chat.filling;
  }

  /** Events with seq > from (DSH `snapshotEvents(from)` semantics). */
  snapshot(chatId, from = 0) {
    const chat = this.chats.get(chatId);
    if (!chat) return [];
    const f = Number(from) || 0;
    // events are sorted by seq: binary search the first > from
    let lo = 0, hi = chat.events.length;
    while (lo < hi) { const mid = (lo + hi) >> 1; if (chat.events[mid].seq <= f) lo = mid + 1; else hi = mid; }
    return chat.events.slice(lo);
  }
}

/** DSH compatibility: DSH events carry `time`; TipCL's carry `at`. Expose both. */
function withCompat(event) {
  if (event.time === undefined && event.at !== undefined) return { ...event, time: event.at };
  if (event.at === undefined && event.time !== undefined) return { ...event, at: event.time };
  return event;
}
