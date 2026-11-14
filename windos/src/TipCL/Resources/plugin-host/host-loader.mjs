// TipCL plugin host: module specifier shim.
// Maps `tipcl:tools`, `tipcl` and `@deepseek-ai/dsh-tools` to the built-in host-tools.mjs so ported DSH
// code can keep `import { defineTool } from '@deepseek-ai/dsh-tools'` (if it was not bundled anyway).
// Uses module.registerHooks (Node >= 22.15, in-thread, covers both import and require); falls back to
// module.register (import only) on older Node 22 builds.

import module from 'node:module';

const TOOLS_URL = new URL('./host-tools.mjs', import.meta.url).href;
const ALIASES = ['tipcl:tools', 'tipcl', '@deepseek-ai/dsh-tools'];

export function installLoader() {
  if (typeof module.registerHooks === 'function') {
    module.registerHooks({
      resolve(specifier, context, nextResolve) {
        if (ALIASES.includes(specifier)) return { url: TOOLS_URL, format: 'module', shortCircuit: true };
        return nextResolve(specifier, context);
      },
    });
    return 'registerHooks';
  }
  if (typeof module.register === 'function') {
    const source = `const A=${JSON.stringify(ALIASES)},U=${JSON.stringify(TOOLS_URL)};
export async function resolve(s,c,n){if(A.includes(s))return{url:U,format:'module',shortCircuit:true};return n(s,c);}`;
    module.register('data:text/javascript,' + encodeURIComponent(source));
    return 'register';
  }
  return 'none';
}
