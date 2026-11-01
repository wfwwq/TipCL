/**
 * hello-consumer: a CommonJS plugin that uses the `hello` service provided by hello-plugin.
 *   GET /api/hello-consumer/status   { hasHello }   (ctx.get works any time, returns undefined when absent)
 *   GET /api/hello-consumer/greet    only exists while hello-plugin is loaded (ctx.inject late binding)
 */
const { defineTool } = require('tipcl:tools'); // the host's module alias also works with require()

exports.name = 'hello-consumer';

exports.apply = function apply(ctx) {
  ctx.connection.fetch.register({
    path: '/api/hello-consumer/status',
    methods: ['GET'],
    fetch: () => Response.json({ hasHello: !!ctx.get('hello'), defineTool: typeof defineTool }),
  });

  // Runs when `hello` becomes available (even if hello-plugin loads later), undone when it goes away.
  ctx.inject(['hello'], (sub) => {
    console.log('hello service is here');
    sub.connection.fetch.register({
      path: '/api/hello-consumer/greet',
      methods: ['GET'],
      fetch: () => new Response(sub.get('hello').greet('consumer')),
    });
    sub.effect(() => () => console.log('hello service went away'), 'consumer greet');
  });
};
