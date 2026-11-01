export function apply(ctx) {
  ctx.provide('liveBody', { onAir: true, showGame() {}, attention: () => new Promise(() => {}) });
}
