// Built-in module that plugin code can import as `tipcl:tools` (or `@deepseek-ai/dsh-tools`, for code
// ported from DSH). The host maps those specifiers here with a module resolve hook (host-loader.mjs).
// Everything is a plain passthrough: the host accepts plain objects, these helpers only exist so that
// ported code keeps compiling/running unchanged.

/**
 * Define an AI tool. Returns the definition unchanged.
 * @template T
 * @param {T} tool
 * @returns {T}
 */
export function defineTool(tool) { return tool; }

/** Define an agent preset (passthrough). */
export function definePreset(preset) { return preset; }

/** Define a plugin module `{ name, inject, apply }` (passthrough). */
export function definePlugin(plugin) { return plugin; }

/** `[{type:'text', text}]` */
export const text = (value) => [{ type: 'text', text: String(value) }];

/**
 * Image content part. `source` is an attachment from `ctx.attachments.saveImages` or raw bytes/base64.
 * @param {{id: string} | Uint8Array | string} source
 * @param {string} [mediaType]
 */
export function image(source, mediaType = 'image/png') {
  if (source && typeof source === 'object' && 'id' in source) return { type: 'image', attachment: source, mediaType: source.mediaType ?? mediaType };
  return { type: 'image', data: source, mediaType };
}

export default { defineTool, definePreset, definePlugin, text, image };
