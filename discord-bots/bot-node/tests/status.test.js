// 跑法：node --test discord-bots/bot-node/tests/status.test.js
//
// 鎖住 status snapshot 對 broker endpoint shape 的契約：
//   - isStatusTrigger 識別中英文常見變體
//   - formatSnapshot 純函式、不可丟例外即使某段資料缺
//   - countRecentGateTriggers 只算 1h 內 + 關鍵字命中
//   - buildStatusSnapshot 任一 endpoint 失敗仍給可讀的「—」輸出

import test from 'node:test';
import assert from 'node:assert/strict';
import { isStatusTrigger, formatSnapshot, countRecentGateTriggers, buildStatusSnapshot } from '../src/status.js';

test('isStatusTrigger recognises common variants', () => {
  assert.equal(isStatusTrigger('狀態'), true);
  assert.equal(isStatusTrigger('status'), true);
  assert.equal(isStatusTrigger('STATUS'), true);
  assert.equal(isStatusTrigger('/status'), true);
  assert.equal(isStatusTrigger('kpi'), true);
  assert.equal(isStatusTrigger('/kpi'), true);
  assert.equal(isStatusTrigger('  狀態  '), true);
  assert.equal(isStatusTrigger('狀態怎麼樣'), false);   // 多字不算
  assert.equal(isStatusTrigger(''), false);
  assert.equal(isStatusTrigger(null), false);
});

test('formatSnapshot renders all 6 fields when every endpoint returns data', () => {
  const out = formatSnapshot({
    anchor: { in_memory_anchor: 467, persisted: { last_seen_balance: 503.21 } },
    auto:   { enabled: true, watch_count: 4, position_states: { 'BTC-USDT': {}, 'ETH-USDT': {} } },
    pnl:    { realized_pnl_sum: 12.34 },
    logs:   { logs: [{ time: new Date().toISOString(), message: 'correlation cap: 0.92 > 0.85' }] },
  });
  assert.match(out, /Anchor: 467\.00 USDT/);
  assert.match(out, /Live: 503\.21 USDT \(\+7\.\d{2}%\)/);
  assert.match(out, /Open positions: 2/);
  assert.match(out, /24h PnL: \+12\.34 USDT/);
  assert.match(out, /AutoTrader: enabled \(4 watches\)/);
  assert.match(out, /Gates: 1 triggered/);
});

test('formatSnapshot tolerates all-null input without throwing', () => {
  const out = formatSnapshot({ anchor: null, auto: null, pnl: null, logs: null });
  assert.match(out, /Anchor: —/);
  assert.match(out, /Live: —/);
  assert.match(out, /Open positions: —/);
  assert.match(out, /24h PnL: —/);
  assert.match(out, /AutoTrader: disabled/);
  assert.match(out, /Gates: 0 triggered/);
});

test('formatSnapshot shows negative PnL with minus sign and not double sign', () => {
  const out = formatSnapshot({
    anchor: { in_memory_anchor: 467 }, auto: null,
    pnl: { realized_pnl_sum: -15.5 }, logs: null,
  });
  assert.match(out, /24h PnL: -15\.50 USDT/);
  assert.doesNotMatch(out, /\+-/);   // 不能有 +- 並排的 bug
});

test('countRecentGateTriggers ignores logs older than 1h', () => {
  const fresh = new Date(Date.now() - 30 * 60 * 1000).toISOString();   // 30min ago
  const old   = new Date(Date.now() - 2  * 3600 * 1000).toISOString(); // 2h ago
  const n = countRecentGateTriggers({ logs: [
    { time: fresh, message: 'pre-flight rejected: min qty' },
    { time: fresh, message: 'correlation cap: 0.91' },
    { time: old,   message: 'cooldown:120s' },   // 太舊
    { time: fresh, message: 'open positions reached max 5' },
    { time: fresh, message: 'opened BTC-USDT long 0.01' },   // 不是 gate
  ]});
  assert.equal(n, 3);
});

test('countRecentGateTriggers accepts both array and {logs:[]}', () => {
  const t = new Date().toISOString();
  const arr = [{ time: t, message: 'funding cap: 0.06%' }];
  assert.equal(countRecentGateTriggers(arr), 1);
  assert.equal(countRecentGateTriggers({ logs: arr }), 1);
  assert.equal(countRecentGateTriggers(null), 0);
  assert.equal(countRecentGateTriggers({}), 0);
});

test('buildStatusSnapshot survives partial broker failures', async () => {
  const fakeCallBroker = async (method, path) => {
    if (path.includes('/risk-anchor/')) {
      return { ok: true, data: { in_memory_anchor: 467, persisted: { last_seen_balance: 480 } } };
    }
    // 其他三條全部 fail
    return { ok: false, error: 'simulated' };
  };
  const out = await buildStatusSnapshot({ callBroker: fakeCallBroker });
  assert.match(out, /Anchor: 467\.00 USDT/);
  assert.match(out, /Live: 480\.00 USDT/);
  assert.match(out, /Open positions: —/);
  assert.match(out, /AutoTrader: disabled/);
});
