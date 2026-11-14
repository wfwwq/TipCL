// SharedWorker behind the browser SDK's event hub (served at /__tipcl/events-worker.js).
// All views of one plugin (page, AI tab, overlay, dash card — separate WebView2s in the same profile)
// share ONE /__tipcl/events connection, because Chromium allows only 6 HTTP/1.1 connections per origin
// and plugin pages need their own SSE routes too. The worker is named after the page token, so each
// plugin gets its own worker and stream.
//
// page → worker: {type:'topics', topics:[...]} (that page's full list) | {type:'bye'}
// worker → page: {type:'event', topic, data} | {type:'open'}

/** @type {Map<MessagePort, string[]>} */
const ports = new Map();
let es = null;
let key = '';
let open = false;
let timer = 0;
const token = self.name;

function topicMatches(pattern, topic) {
  if (pattern === '*' || pattern === topic) return true;
  if (!pattern.includes('*')) return false;
  return new RegExp('^' + pattern.split('*').map((s) => s.replace(/[.+?^${}()|[\]\\]/g, '\\$&')).join('.*') + '$').test(topic);
}

function reconnect() {
  const union = new Set();
  for (const list of ports.values()) for (const t of list) union.add(t);
  const topics = [...union].sort();
  const next = topics.join(',');
  if (next === key && es) return;
  key = next;
  es?.close();
  es = null;
  open = false;
  if (!topics.length) return;
  const src = new EventSource(`/__tipcl/events?k=${encodeURIComponent(token)}&topics=${encodeURIComponent(next)}`);
  es = src;
  src.onopen = () => { open = true; for (const p of ports.keys()) p.postMessage({ type: 'open' }); };
  src.onerror = () => { open = false; };
  src.onmessage = (msg) => {
    let ev;
    try { ev = JSON.parse(msg.data); } catch { return; }
    for (const [port, list] of ports) {
      if (list.some((t) => topicMatches(t, ev.topic))) port.postMessage({ type: 'event', topic: ev.topic, data: ev.data });
    }
  };
}

const schedule = () => { clearTimeout(timer); timer = setTimeout(reconnect, 0); };

self.onconnect = (e) => {
  const port = e.ports[0];
  ports.set(port, []);
  port.onmessage = (m) => {
    const msg = m.data ?? {};
    if (msg.type === 'topics') {
      ports.set(port, Array.isArray(msg.topics) ? msg.topics : []);
      schedule();
      if (open && key === [...new Set([...ports.values()].flat())].sort().join(',')) port.postMessage({ type: 'open' });
    } else if (msg.type === 'bye') {
      ports.delete(port);
      schedule();
    }
  };
  port.start?.();
};
