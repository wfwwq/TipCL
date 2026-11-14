// Mirrors dsh-game runtime.ts patterns: DSH_HOME read at module load, assets found via import.meta.url.
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
export const RUNS_DIR = path.join(process.env.DSH_HOME ?? os.tmpdir(), 'dsh-game', 'runs');
export const here = path.dirname(fileURLToPath(import.meta.url));
export const hooks = {};
export const listeners = new Set();
export function broadcast(event) { for (const l of listeners) l(event); }
