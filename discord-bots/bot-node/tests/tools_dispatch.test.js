// 跑法：node --test discord-bots/bot-node/tests/tools_dispatch.test.js
//
// 鎖住 dispatchTool 的 status 分流契約：success / pending_approval / failed / denied
// 重點是「broker 把 approval 訊息塞在 error string」這條 W14 P5 設計後遺症——
// dispatchTool 必須把它翻成 ok=true + status='pending_approval'、否則:
//   1. LLM 看到 "tool=X failed:" 會去 retry 或 hallucinate 失敗訊息（commit 後 user 反饋）
//   2. W13 audit 表寫 dispatch_result='failed' 是錯的、forensics 拉時間軸看不出來是「卡審核」
//
// 對應 discord-bots/bot-node/CHANGELOG（W13 + 灰區 2 mitigation）。

import test from 'node:test';
import assert from 'node:assert/strict';

// dispatchTool 內部 closure 不接 deps、要靠 TOOLS map 替換 dispatch
import { TOOLS, dispatchTool } from '../src/tools.js';

function stubTool(name, fakeResult) {
  const orig = TOOLS[name];
  TOOLS[name] = { ...orig, dispatch: async () => fakeResult };
  return () => { TOOLS[name] = orig; };
}

test('dispatchTool: success → status=success, ok=true', async () => {
  const restore = stubTool('quote.prices', { ok: true, status: 200, data: { BTC: 50000 } });
  try {
    const r = await dispatchTool({ call: 'quote.prices', args: {} });
    assert.equal(r.ok, true);
    assert.equal(r.status, 'success');
    assert.match(r.summary, /tool=quote\.prices ok/);
  } finally { restore(); }
});

test('dispatchTool: pending_approval (broker error mentions approval_id=apr_) → status=pending_approval, ok=true', async () => {
  // broker 走 W14 P5 approval gate 時、callBroker 收到 4xx + error 訊息含 "approval_id=apr_..."
  const restore = stubTool('trading.order', {
    ok: false,
    status: 403,
    error: 'Pending admin approval, approval_id=apr_abc123',
  });
  try {
    const r = await dispatchTool(
      { call: 'trading.order', args: { symbol: 'BTC-USDT', side: 'buy' } },
      { userId: 'u', isPrivileged: true, privilegedTool: true },
    );
    assert.equal(r.ok, true, 'pending_approval must NOT be ok=false (LLM 會誤判失敗)');
    assert.equal(r.status, 'pending_approval');
    assert.match(r.summary, /pending_approval/);
    assert.match(r.summary, /approval_id=apr_abc123/);
    assert.equal(r.data?.pending, true);
  } finally { restore(); }
});

test('dispatchTool: real broker failure (no approval_id) → status=failed, ok=false', async () => {
  const restore = stubTool('trading.order', {
    ok: false, status: 500, error: 'BingX API timeout after 30s',
  });
  try {
    const r = await dispatchTool(
      { call: 'trading.order', args: {} },
      { userId: 'u', isPrivileged: true, privilegedTool: true },
    );
    assert.equal(r.ok, false);
    assert.equal(r.status, 'failed');
    assert.match(r.summary, /failed: BingX API timeout/);
  } finally { restore(); }
});

test('dispatchTool: privileged tool blocked for non-privileged user → status=denied', async () => {
  const r = await dispatchTool(
    { call: 'trading.order', args: {} },
    { userId: 'discord_random_id', isPrivileged: false, privilegedTool: true },
  );
  assert.equal(r.ok, false);
  assert.equal(r.status, 'denied');
  assert.equal(r.error, 'access_denied');
});

test('dispatchTool: unknown tool → status=failed', async () => {
  const r = await dispatchTool({ call: 'nonexistent.tool', args: {} });
  assert.equal(r.ok, false);
  assert.equal(r.status, 'failed');
  assert.equal(r.error, 'unknown_tool');
});

test('dispatchTool: edge — error string contains "approval" but no approval_id=apr_ → still failed (不誤判)', async () => {
  // 防 false positive：broker 別處錯誤訊息可能含 "approval"，但沒有 apr_ id 就不是 pending
  const restore = stubTool('trading.order', {
    ok: false, status: 400, error: 'approval workflow misconfigured: rule not found',
  });
  try {
    const r = await dispatchTool(
      { call: 'trading.order', args: {} },
      { userId: 'u', isPrivileged: true, privilegedTool: true },
    );
    assert.equal(r.status, 'failed', 'no apr_ id → not pending');
  } finally { restore(); }
});
