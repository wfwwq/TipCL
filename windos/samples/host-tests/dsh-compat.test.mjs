// Acceptance: dsh-game's host-side code patterns (transliterated from src/index.ts, src/agent.ts and
// src/runtime.ts, imports unchanged incl. '@deepseek-ai/dsh-tools') run on the TipCL host as-is.

import { describe, it, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { FakeTipcl, SAMPLES_DIR, loadParams } from './fake-tipcl.mjs';

const FIX = join(SAMPLES_DIR, 'host-tests', 'fixtures');

describe('dsh-game compatibility', () => {
  let t, home;
  before(async () => {
    home = await mkdtemp(join(tmpdir(), 'tipcl-dsh-test-'));
    t = new FakeTipcl({ home });
    await t.hello;
    t.credentials['dsh-compat'] = { DOUBAO_TTS_API_KEY: 'k' };
    const r = await t.request('plugin.load', await loadParams(join(FIX, 'dsh-compat'), { home }));
    assert.equal(r.ok, true, r.error);
  });
  after(async () => { await t?.stop(); await rm(home, { recursive: true, force: true }); });

  it('config cells, schemastery Config defaults, DSH_HOME at module load, import.meta.url', async () => {
    const s = JSON.parse((await t.http('/api/dsh-game/state')).text);
    assert.equal(s.voice, 'zh_female'); // manifest default
    assert.equal(s.censor, true); // filled from the exported Config()
    assert.equal(s.missing, null); // undeclared key → cell reading undefined
    assert.equal(s.runsDir, join(home, 'plugin-data', 'dsh-game', 'runs'));
    assert.equal(s.here, join(FIX, 'dsh-compat', 'lib'));
    assert.equal(s.live, false);
  });

  it('ctx.get("liveBody") from another plugin appears when it loads', async () => {
    const r = await t.request('plugin.load', await loadParams(join(FIX, 'live-body'), { home }));
    assert.equal(r.ok, true, r.error);
    const s = JSON.parse((await t.http('/api/dsh-game/state')).text);
    assert.equal(s.live, true);
    assert.equal(s.onAir, true);
  });

  it('llm.listProviders() synchronously + listModels', async () => {
    assert.deepEqual(JSON.parse((await t.http('/api/dsh-game/models')).text), [{ id: 'fake/m1', provider: 'fake', providerName: 'Fake', name: 'Model 1' }]);
  });

  it('game_continue: defineTool params, snapshotEvents, llm.stream with DSH messages, credentials, saveImages, render', async () => {
    const tool = t.callsOf('tools.register', (p) => p.plugin === 'dsh-compat')[0].params.tool;
    assert.deepEqual(tool.parameters, {
      type: 'object',
      properties: { after: { type: 'integer', description: '你上一次看到的时刻编号（第一次填 0）' }, mood: { type: 'string', description: '表情' } },
      required: ['after'],
    });
    const stage = await t.sse('/api/dsh-game/events?page=p1');
    await stage.until((e) => e.length >= 1);
    t.pushChatEvent('g1', 'turn/start', {});
    t.pushChatEvent('g1', 'assistant/message', { message: { content: [{ type: 'text', text: '好嘞，开局！' }, { type: 'tool-call', id: 'call_9', name: 'game_continue', args: { after: 0 } }] } });
    const res = await t.request('tool.execute', { plugin: 'dsh-compat', name: 'game_continue', callId: 'call_9', chatId: 'g1', args: { after: 0 }, messageText: '好嘞，开局！' });
    assert.equal(res.isError, false, JSON.stringify(res));
    assert.equal(res.content[0].text, 'said=好嘞，开局！ summary=古池や蛙飛び込む水の音 key=yes after=0');
    assert.deepEqual(res.content[1], { type: 'image', attachment: { id: t.attachments.at(-1).id }, mediaType: 'image/png' });
    const call = t.callsOf('llm.stream').at(-1).params;
    assert.deepEqual(call.messages, [{ role: 'user', content: 'line one\nline two' }]);
    assert.equal(call.purpose, 'dsh-game-summary');
    const evs = await stage.until((e) => e.some((x) => x.type === 'say'));
    assert.equal(evs.find((x) => x.type === 'say').text, '好嘞，开局！');
    stage.close();
    assert.equal((await t.http('/api/dsh-game/said?id=abc', { method: 'POST' })).text, 'ok abc');
  });
});
