// TipCL plugin host: NDJSON JSON-RPC over stdin/stdout (both directions).
//   request      {"id": 7, "method": "x", "params": {...}}
//   response     {"id": 7, "result": ...}  |  {"id": 7, "error": {"code", "message"}}
//   notification {"method": "x", "params": {...}}          (no id)
// stdout carries ONLY protocol lines; see host-console.mjs for how plugin output is kept off it.

import { HostError, toWireError } from './host-util.mjs';

export class Rpc {
  /**
   * @param {NodeJS.ReadableStream} input   usually process.stdin
   * @param {(line: string) => void} writeLine  writes one line (newline appended by caller-side here)
   */
  constructor(input, writeLine) {
    this.writeLine = writeLine;
    this.nextId = 1;
    /** @type {Map<number|string, {resolve: Function, reject: Function, method: string}>} */
    this.pending = new Map();
    /** @type {Map<string, (params: any, msg: any) => any>} */
    this.requestHandlers = new Map();
    /** @type {Map<string, (params: any) => void>} */
    this.notificationHandlers = new Map();
    /** called when input ends */
    this.onClose = () => {};
    let buffer = '';
    input.setEncoding?.('utf8');
    input.on('data', (chunk) => {
      buffer += chunk;
      let nl;
      while ((nl = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, nl).replace(/\r$/, '');
        buffer = buffer.slice(nl + 1);
        if (line.trim()) this.#receive(line);
      }
    });
    input.on('end', () => this.onClose());
  }

  /** Send a request; resolves with `result`, rejects with HostError(code, message). */
  request(method, params) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject, method });
      this.#send({ id, method, params: params ?? {} });
    });
  }

  notify(method, params) {
    this.#send({ method, params: params ?? {} });
  }

  /** Register a handler for incoming requests (its return value / thrown error becomes the response). */
  handle(method, handler) { this.requestHandlers.set(method, handler); }
  /** Register a handler for incoming notifications. */
  on(method, handler) { this.notificationHandlers.set(method, handler); }

  /** Reject every outstanding request (used on shutdown). */
  failAll(reason) {
    for (const [, p] of this.pending) p.reject(new HostError('closed', reason));
    this.pending.clear();
  }

  #send(msg) {
    let line;
    try { line = JSON.stringify(msg); } catch (err) {
      // e.g. BigInt / circular in a plugin result: report instead of crashing
      line = JSON.stringify(msg.id !== undefined && msg.method === undefined
        ? { id: msg.id, error: { code: 'serialize_error', message: String(err?.message ?? err) } }
        : { method: 'log', params: { level: 'error', text: `cannot serialize ${msg.method}: ${err}` } });
    }
    this.writeLine(line);
  }

  #receive(line) {
    let msg;
    try { msg = JSON.parse(line); } catch {
      process.stderr.write(`[host] [error] bad protocol line: ${line.slice(0, 200)}\n`);
      return;
    }
    // response to one of our requests
    if (msg.method === undefined && msg.id !== undefined && msg.id !== null) {
      const p = this.pending.get(msg.id);
      if (!p) return;
      this.pending.delete(msg.id);
      if (msg.error) p.reject(new HostError(msg.error.code ?? 'error', msg.error.message ?? String(msg.error)));
      else p.resolve(msg.result);
      return;
    }
    const isRequest = msg.id !== undefined && msg.id !== null;
    const handler = isRequest
      ? (this.requestHandlers.get(msg.method) ?? this.notificationHandlers.get(msg.method))
      : (this.notificationHandlers.get(msg.method) ?? this.requestHandlers.get(msg.method));
    if (!handler) {
      if (isRequest) this.#send({ id: msg.id, error: { code: 'method_not_found', message: `unknown method ${msg.method}` } });
      return;
    }
    let out;
    try { out = handler(msg.params ?? {}, msg); } catch (err) { out = Promise.reject(err); }
    if (!isRequest) {
      Promise.resolve(out).catch((err) => process.stderr.write(`[host] [error] ${msg.method}: ${err?.stack ?? err}\n`));
      return;
    }
    Promise.resolve(out).then(
      (result) => this.#send({ id: msg.id, result: result === undefined ? null : result }),
      (err) => this.#send({ id: msg.id, error: toWireError(err) }),
    );
  }
}
