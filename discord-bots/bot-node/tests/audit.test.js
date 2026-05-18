// 跑法：node --test discord-bots/bot-node/tests/audit.test.js
//
// 鎖住 W13 LLM reasoning audit pipeline（commit 3a7929e）的 client-side 契約：
//   - pushLlmReasoning 必須 POST 到 /api/v1/audit/llm-reasoning
//   - payload 欄位 source / user_id / channel_id / turn / llm_reasoning / tool_name /
//     tool_args / acl_allowed / dispatch_result 全部正確帶
//   - broker call 失敗時 fire-and-forget、不丟 exception
//   - acl_allowed 必須是 boolean（false-ish 進來會 coerce）
//
// 對應 thesis Ch 5.5.4 + Ch 6.3.5 灰區二 mitigation 的測試覆蓋。
//
// ESM module namespace 不能 redefine、用 audit.js 提供的 deps DI 參數注入 fake callBroker。

import test from 'node:test';
import assert from 'node:assert/strict';
import { pushLlmReasoning } from '../src/audit.js';

/** 建一個會記錄呼叫的 fake callBroker；可 override 回傳值 */
function makeFakeBroker(returnValue = { ok: true, status: 200, data: { entry_id: 1 } }) {
  const calls = [];
  const fn = async (method, path, body, opts) => {
    calls.push({ method, path, body, opts });
    if (typeof returnValue === 'function') return returnValue();
    return returnValue;
  };
  return { fn, calls };
}

test('pushLlmReasoning POSTs to /api/v1/audit/llm-reasoning with correct payload', async () => {
  const stub = makeFakeBroker();
  await pushLlmReasoning({
    source: 'discord',
    userId: 'user_test',
    channelId: 'ch_test',
    turn: 2,
    llmReasoning: 'I will fetch BTC price for the user.',
    toolName: 'quote.prices',
    toolArgs: { symbol: 'BTC-USDT' },
    aclAllowed: true,
  }, { callBroker: stub.fn });

  assert.equal(stub.calls.length, 1, 'callBroker must be called exactly once');
  const { method, path, body } = stub.calls[0];
  assert.equal(method, 'POST');
  assert.equal(path, '/api/v1/audit/llm-reasoning');
  assert.equal(body.source, 'discord');
  assert.equal(body.user_id, 'user_test');
  assert.equal(body.channel_id, 'ch_test');
  assert.equal(body.turn, 2);
  assert.equal(body.llm_reasoning, 'I will fetch BTC price for the user.');
  assert.equal(body.tool_name, 'quote.prices');
  assert.deepEqual(body.tool_args, { symbol: 'BTC-USDT' });
  assert.equal(body.acl_allowed, true);
  assert.equal(body.dispatch_result, 'pending', 'initial state must be pending');
});

test('pushLlmReasoning coerces acl_allowed to boolean', async () => {
  const stub = makeFakeBroker();
  await pushLlmReasoning({
    source: 'discord', userId: 'u', channelId: 'c', turn: 0,
    llmReasoning: 'x', toolName: 't', toolArgs: {},
    aclAllowed: 'truthy-string',   // 不是 boolean、應該被 coerce
  }, { callBroker: stub.fn });

  assert.equal(stub.calls[0].body.acl_allowed, true, 'truthy non-bool must become true');
});

test('pushLlmReasoning fills defaults for missing fields', async () => {
  const stub = makeFakeBroker();
  // 只給 reasoning + tool、其他都缺
  await pushLlmReasoning({ llmReasoning: 'foo', toolName: 'bar' }, { callBroker: stub.fn });

  const body = stub.calls[0].body;
  assert.equal(body.source, 'discord', 'default source = discord');
  assert.equal(body.user_id, '');
  assert.equal(body.channel_id, '');
  assert.equal(body.turn, 0);
  assert.deepEqual(body.tool_args, {});
  assert.equal(body.acl_allowed, false);
});

test('pushLlmReasoning swallows broker failures (fire-and-forget)', async () => {
  const stub = makeFakeBroker({ ok: false, status: 500, error: 'simulated server error' });

  // 不應該 throw —— 主對話流程不能被 audit 失敗打斷
  await assert.doesNotReject(
    pushLlmReasoning(
      { source: 'discord', userId: 'u', channelId: 'c', turn: 0, llmReasoning: 'x', toolName: 't', toolArgs: {} },
      { callBroker: stub.fn },
    ),
    'broker failure must not bubble up as exception'
  );
});

test('pushLlmReasoning swallows network exceptions (fire-and-forget)', async () => {
  const throwingCallBroker = async () => { throw new Error('network died'); };

  await assert.doesNotReject(
    pushLlmReasoning(
      { source: 'discord', userId: 'u', channelId: 'c', turn: 0, llmReasoning: 'x', toolName: 't', toolArgs: {} },
      { callBroker: throwingCallBroker },
    ),
    'thrown exception inside callBroker must be swallowed'
  );
});

test('pushLlmReasoning writes dispatchResult verbatim when provided', async () => {
  const stub = makeFakeBroker();
  await pushLlmReasoning({
    source: 'discord', userId: 'u', channelId: 'c', turn: 0,
    llmReasoning: 'x', toolName: 't', toolArgs: {}, aclAllowed: true,
    dispatchResult: 'success',
  }, { callBroker: stub.fn });
  assert.equal(stub.calls[0].body.dispatch_result, 'success');
});

test('pushLlmReasoning pending_approval status round-trips', async () => {
  const stub = makeFakeBroker();
  await pushLlmReasoning({
    source: 'discord', userId: 'u', channelId: 'c', turn: 0,
    llmReasoning: 'x', toolName: 'trading.order', toolArgs: {}, aclAllowed: true,
    dispatchResult: 'pending_approval',
  }, { callBroker: stub.fn });
  assert.equal(stub.calls[0].body.dispatch_result, 'pending_approval');
});

test('pushLlmReasoning appends errorBrief to dispatch_result (failure capture)', async () => {
  const stub = makeFakeBroker();
  await pushLlmReasoning({
    source: 'discord', userId: 'u', channelId: 'c', turn: 0,
    llmReasoning: 'x', toolName: 't', toolArgs: {}, aclAllowed: true,
    dispatchResult: 'failed',
    errorBrief: 'broker returned 500: db connection lost',
  }, { callBroker: stub.fn });
  assert.equal(stub.calls[0].body.dispatch_result, 'failed: broker returned 500: db connection lost');
});

test('pushLlmReasoning truncates errorBrief to 200 chars (DoS guard)', async () => {
  const stub = makeFakeBroker();
  const longErr = 'x'.repeat(500);
  await pushLlmReasoning({
    source: 'discord', userId: 'u', channelId: 'c', turn: 0,
    llmReasoning: 'x', toolName: 't', toolArgs: {}, aclAllowed: true,
    dispatchResult: 'failed', errorBrief: longErr,
  }, { callBroker: stub.fn });
  // "failed: " (8) + 200 chars = 208
  assert.equal(stub.calls[0].body.dispatch_result.length, 208);
});

test('pushLlmReasoning preserves multi-source field (LINE vs Discord)', async () => {
  const stub = makeFakeBroker();
  await pushLlmReasoning({
    source: 'line',
    userId: 'U1234',
    channelId: 'U1234',  // LINE 個人對話、channelId == userId
    turn: 0,
    llmReasoning: 'reasoning text',
    toolName: 'trading.account/get_account',
    toolArgs: { exchange: 'bingx' },
    aclAllowed: true,
  }, { callBroker: stub.fn });

  assert.equal(stub.calls[0].body.source, 'line', 'source field must be preserved verbatim, not defaulted');
});
