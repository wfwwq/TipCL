// TipCL plugin host: small shared helpers (no dependencies).

import { createHmac, randomBytes } from 'node:crypto';

/**
 * Topic pattern match. `*` matches any run of characters (so `game.*` matches `game.log`,
 * `*` matches everything). Exact strings match only themselves.
 * @param {string} pattern
 * @param {string} topic
 */
export function topicMatches(pattern, topic) {
  if (pattern === '*' || pattern === topic) return true;
  if (!pattern.includes('*')) return false;
  return globToRegExp(pattern).test(topic);
}

/** @type {Map<string, RegExp>} */
const globCache = new Map();
/** `*` → `.*`; everything else literal. Anchored. */
export function globToRegExp(glob) {
  let re = globCache.get(glob);
  if (!re) {
    re = new RegExp('^' + glob.split('*').map((s) => s.replace(/[.+?^${}()|[\]\\]/g, '\\$&')).join('.*') + '$');
    globCache.set(glob, re);
  }
  return re;
}

/**
 * Page token for a plugin's web views. Deterministic so TipCL (C#) can compute the same value:
 *   pageToken = lowercase hex( HMAC-SHA256( key = UTF8(TIPCL_HOST_TOKEN), msg = UTF8(pluginId) ) )[0..32)
 * TipCL also passes it as `plugin.load` params.pageToken (which wins); the host echoes it in the result.
 * @param {string} hostToken
 * @param {string} pluginId
 */
export function pageTokenFor(hostToken, pluginId) {
  return createHmac('sha256', hostToken).update(pluginId, 'utf8').digest('hex').slice(0, 32);
}

export const randomToken = () => randomBytes(24).toString('hex');

// ── tool parameters: DSH style → JSON Schema ─────────────────────────────

const TYPE_ALIASES = { int: 'integer', natural: 'integer', float: 'number', bool: 'boolean', str: 'string', list: 'array', dict: 'object' };

/** Is this already a JSON Schema object (as opposed to DSH's `{k: {type, required}}` map)? */
function isJsonSchemaObject(params) {
  return params && typeof params === 'object' && typeof params.type === 'string' &&
    (params.type === 'object' || params.$schema !== undefined) &&
    (params.properties === undefined || (typeof params.properties === 'object' && !Array.isArray(params.properties)));
}

/** Normalize one property schema (DSH or JSON Schema flavoured) into JSON Schema. */
function normalizeProp(spec) {
  if (typeof spec === 'string') spec = { type: spec };
  if (!spec || typeof spec !== 'object') return {};
  const out = { ...spec };
  delete out.required; // DSH uses `required: true` on the property; JSON Schema lists it on the parent
  delete out.optional;
  if (typeof out.type === 'string') {
    const t = TYPE_ALIASES[out.type] ?? out.type;
    if (t === 'json' || t === 'any' || t === 'unknown') delete out.type; else out.type = t;
  }
  if (out.type === 'array' && out.items) out.items = normalizeProp(out.items);
  if (out.properties && typeof out.properties === 'object') {
    const nested = convertPropertyMap(out.properties, Array.isArray(spec.required) ? spec.required : []);
    out.properties = nested.properties;
    if (nested.required.length) out.required = nested.required; else delete out.required;
    if (!out.type) out.type = 'object';
  }
  return out;
}

function convertPropertyMap(map, initialRequired = []) {
  /** @type {Record<string, any>} */
  const properties = {};
  const required = [...initialRequired];
  for (const [key, spec] of Object.entries(map)) {
    if (spec && typeof spec === 'object' && spec.required === true && !required.includes(key)) required.push(key);
    properties[key] = normalizeProp(spec);
  }
  return { properties, required };
}

/**
 * Tool parameters → JSON Schema. Accepts:
 *   - DSH style: `{ game: { type: 'string', required: true, description } }`
 *   - JSON Schema: `{ type: 'object', properties: {...}, required: [...] }`
 *   - nothing (no parameters)
 * @param {any} params
 */
export function toJsonSchema(params) {
  if (!params || typeof params !== 'object') return { type: 'object', properties: {} };
  if (typeof params.toJSON === 'function' && !isJsonSchemaObject(params)) {
    try { params = params.toJSON(); } catch { /* keep as is */ }
  }
  if (isJsonSchemaObject(params)) {
    const out = normalizeProp(params);
    out.type = 'object';
    if (!out.properties) out.properties = {};
    return out;
  }
  const { properties, required } = convertPropertyMap(params);
  return required.length ? { type: 'object', properties, required } : { type: 'object', properties };
}

// ── binary helpers ───────────────────────────────────────────────────────

/** Uint8Array | Buffer | ArrayBuffer | base64 string | data: URL → base64 string */
export function toBase64(data) {
  if (data == null) return '';
  if (typeof data === 'string') {
    const m = /^data:[^;,]*;base64,(.*)$/s.exec(data);
    return m ? m[1] : data;
  }
  if (Buffer.isBuffer(data)) return data.toString('base64');
  if (data instanceof ArrayBuffer) return Buffer.from(data).toString('base64');
  if (ArrayBuffer.isView(data)) return Buffer.from(data.buffer, data.byteOffset, data.byteLength).toString('base64');
  if (Array.isArray(data)) return Buffer.from(data).toString('base64');
  throw new TypeError('image data must be Buffer, Uint8Array, ArrayBuffer or base64 string');
}

export const MIME = {
  html: 'text/html; charset=utf-8', htm: 'text/html; charset=utf-8', js: 'text/javascript; charset=utf-8', mjs: 'text/javascript; charset=utf-8',
  cjs: 'text/javascript; charset=utf-8', css: 'text/css; charset=utf-8', json: 'application/json; charset=utf-8', map: 'application/json; charset=utf-8',
  txt: 'text/plain; charset=utf-8', md: 'text/markdown; charset=utf-8', xml: 'application/xml', svg: 'image/svg+xml',
  png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif', webp: 'image/webp', avif: 'image/avif', ico: 'image/x-icon', bmp: 'image/bmp',
  webm: 'video/webm', mp4: 'video/mp4', ogv: 'video/ogg', mp3: 'audio/mpeg', wav: 'audio/wav', ogg: 'audio/ogg', oga: 'audio/ogg', m4a: 'audio/mp4', flac: 'audio/flac', opus: 'audio/opus',
  wasm: 'application/wasm', woff: 'font/woff', woff2: 'font/woff2', ttf: 'font/ttf', otf: 'font/otf', pdf: 'application/pdf', zip: 'application/zip',
};
export function mimeOf(file) {
  const ext = file.slice(file.lastIndexOf('.') + 1).toLowerCase();
  return MIME[ext] ?? 'application/octet-stream';
}

/** A minimal async queue usable with `for await`. */
export class AsyncQueue {
  constructor() {
    /** @type {any[]} */ this.items = [];
    /** @type {((v: IteratorResult<any>) => void) | null} */ this.waiter = null;
    /** @type {((e: any) => void) | null} */ this.failer = null;
    this.ended = false;
    this.error = null;
    this.result = undefined;
  }
  push(item) {
    if (this.ended) return;
    if (this.waiter) { const w = this.waiter; this.waiter = this.failer = null; w({ value: item, done: false }); } else this.items.push(item);
  }
  end(result) {
    if (this.ended) return;
    this.ended = true; this.result = result;
    if (this.waiter) { const w = this.waiter; this.waiter = this.failer = null; w({ value: undefined, done: true }); }
  }
  fail(error) {
    if (this.ended) return;
    this.ended = true; this.error = error ?? new Error('failed');
    if (this.failer) { const f = this.failer; this.waiter = this.failer = null; f(this.error); }
  }
  next() {
    if (this.items.length) return Promise.resolve({ value: this.items.shift(), done: false });
    if (this.error) return Promise.reject(this.error);
    if (this.ended) return Promise.resolve({ value: undefined, done: true });
    return new Promise((resolve, reject) => { this.waiter = resolve; this.failer = reject; });
  }
  [Symbol.asyncIterator]() { return this; }
}

/** Turn anything thrown into `{code, message}` for the wire. */
export function toWireError(err) {
  if (err && typeof err === 'object') return { code: typeof err.code === 'string' ? err.code : 'error', message: String(err.message ?? err) };
  return { code: 'error', message: String(err) };
}

/** Error carrying a protocol error code (`permission_denied`, ...). */
export class HostError extends Error {
  constructor(code, message) { super(message); this.code = code; }
}
