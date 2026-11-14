// TipCL plugin host: attribute console output to plugins and keep it off stdout.
//
// Every plugin callback (module import, apply, tool execute, route fetch, hook, event handler...) runs
// inside `pluginScope.run(pluginId, fn)`. AsyncLocalStorage carries the id through timers and promises
// created from there, so `console.log` anywhere in a plugin is written to stderr as `[plugin-id] text`.
// TipCL routes stderr lines by that prefix into plugin-logs/<id>.log.
// Line format:  `[<plugin-id>] <text>`  or  `[<plugin-id>] [warn|error|debug] <text>`  (`[host]` for the host itself).

import { AsyncLocalStorage } from 'node:async_hooks';
import { formatWithOptions, inspect } from 'node:util';

/** @type {AsyncLocalStorage<string>} */
export const pluginScope = new AsyncLocalStorage();

/** Plugin id of the code currently running, or undefined for host code. */
export const currentPlugin = () => pluginScope.getStore();

const stderrWrite = process.stderr.write.bind(process.stderr);

/** Write text to stderr, every line prefixed with `[id]` (and a level tag for non-log levels). */
export function emitLog(id, level, text) {
  const tag = level === 'log' || level === 'info' ? '' : `[${level}] `;
  const prefix = `[${id ?? 'host'}] ${tag}`;
  const lines = String(text).replace(/\r?\n$/, '').split(/\r?\n/);
  stderrWrite(lines.map((l) => prefix + l).join('\n') + '\n');
}

/**
 * Install the redirection. Returns the ORIGINAL stdout writer, which only the protocol may use.
 * @returns {(line: string) => void}
 */
export function installConsole() {
  const stdoutWrite = process.stdout.write.bind(process.stdout);
  const fmt = (args) => formatWithOptions({ colors: false, depth: 6 }, ...args);
  const route = (level) => (...args) => emitLog(currentPlugin(), level, fmt(args));
  console.log = route('log');
  console.info = route('info');
  console.debug = route('debug');
  console.warn = route('warn');
  console.error = route('error');
  console.trace = (...args) => emitLog(currentPlugin(), 'error', fmt(args) + '\n' + (new Error().stack ?? '').split('\n').slice(2).join('\n'));
  console.dir = (obj, opts) => emitLog(currentPlugin(), 'log', inspect(obj, { colors: false, ...opts }));
  // Anything a plugin writes to process.stdout directly (console.table, libraries) goes to stderr too.
  // NOTE: a child process spawned with stdio 'inherit' still writes to fd 1 directly; plugins must not do that.
  process.stdout.write = /** @type {any} */ ((chunk, encoding, cb) => {
    if (typeof encoding === 'function') { cb = encoding; encoding = undefined; }
    const text = typeof chunk === 'string' ? chunk : Buffer.from(chunk).toString(encoding || 'utf8');
    if (text.trim()) emitLog(currentPlugin(), 'log', text);
    if (typeof cb === 'function') queueMicrotask(cb);
    return true;
  });
  return (line) => { stdoutWrite(line + '\n'); };
}
