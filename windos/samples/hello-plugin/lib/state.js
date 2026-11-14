// State shared by the two entries of hello-plugin (index.js and agent.js run in the same Node process,
// like dsh-game's index + agent sub-plugins sharing runtime.ts).

/** Callbacks of hello_wait calls that are currently waiting; POST /api/hello-plugin/poke wakes them. */
export const waiters = new Set();

/** Wake every waiting hello_wait with a reason. Returns how many were woken. */
export function poke(reason = 'poke') {
  const n = waiters.size;
  for (const wake of [...waiters]) wake(reason);
  return n;
}

/** Last `game.log` line seen (shown on the dash card). */
export const status = { lastLog: null, launches: 0, overlayVisible: true };
