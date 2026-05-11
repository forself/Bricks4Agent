// 跑法：node --test discord-bots/bot-node/tests/position_intent.test.js
// 用 node 內建 test runner、不引外部 dep。
//
// 鎖住 planPositionIntent 的契約：
//   - HOLD 永遠 noop、不會生 order payload
//   - ADD 強制要 add_qty、產出 reduce_only=false 同向單
//   - TRIM 沒 currentPosition 必 error；有部位時按 trim_pct（預設 50）算反向 reduce_only
//   - EXIT 沒 currentPosition 必 error；有部位時生反向 reduce_only 全平
//   - 不合法 intent / position_side 直接 error，不會偷偷下單

import test from 'node:test';
import assert from 'node:assert/strict';
import { planPositionIntent, findPosition } from '../src/position_intent.js';

const longPos  = { symbol: 'BTC-USDT', side: 'long',  quantity: 0.01 };
const shortPos = { symbol: 'BTC-USDT', side: 'short', quantity: 0.02 };

test('HOLD always returns noop, never an order', () => {
  const r = planPositionIntent({ intent: 'HOLD', symbol: 'BTC-USDT', position_side: 'long' }, longPos);
  assert.equal(r.kind, 'noop');
});

test('unknown intent returns error', () => {
  const r = planPositionIntent({ intent: 'YOLO', symbol: 'BTC-USDT', position_side: 'long' }, longPos);
  assert.equal(r.kind, 'error');
  assert.match(r.error, /unknown intent/);
});

test('invalid position_side returns error', () => {
  const r = planPositionIntent({ intent: 'EXIT', symbol: 'BTC-USDT', position_side: 'sideways' }, longPos);
  assert.equal(r.kind, 'error');
  assert.match(r.error, /position_side/);
});

test('ADD requires add_qty', () => {
  const r = planPositionIntent({ intent: 'ADD', symbol: 'BTC-USDT', position_side: 'long' }, null);
  assert.equal(r.kind, 'error');
  assert.match(r.error, /add_qty/);
});

test('ADD long produces buy + same position_side, reduce_only false', () => {
  const r = planPositionIntent(
    { intent: 'ADD', symbol: 'BTC-USDT', position_side: 'long', add_qty: 0.005 },
    null,
  );
  assert.equal(r.kind, 'order');
  assert.equal(r.payload.side, 'buy');
  assert.equal(r.payload.position_side, 'long');
  assert.equal(r.payload.quantity, 0.005);
  assert.equal(r.payload.reduce_only, false);
});

test('ADD short produces sell + same position_side, reduce_only false', () => {
  const r = planPositionIntent(
    { intent: 'ADD', symbol: 'BTC-USDT', position_side: 'short', add_qty: 0.01 },
    null,
  );
  assert.equal(r.kind, 'order');
  assert.equal(r.payload.side, 'sell');
  assert.equal(r.payload.position_side, 'short');
  assert.equal(r.payload.reduce_only, false);
});

test('TRIM without currentPosition errors', () => {
  const r = planPositionIntent({ intent: 'TRIM', symbol: 'BTC-USDT', position_side: 'long' }, null);
  assert.equal(r.kind, 'error');
  assert.match(r.error, /no long position/);
});

test('TRIM long defaults 50% reduce_only sell', () => {
  const r = planPositionIntent(
    { intent: 'TRIM', symbol: 'BTC-USDT', position_side: 'long' },
    longPos,
  );
  assert.equal(r.kind, 'order');
  assert.equal(r.payload.side, 'sell');
  assert.equal(r.payload.position_side, 'long');
  assert.equal(r.payload.reduce_only, true);
  assert.equal(r.payload.quantity, 0.005); // 50% of 0.01
});

test('TRIM short with custom trim_pct produces buy reduce_only', () => {
  const r = planPositionIntent(
    { intent: 'TRIM', symbol: 'BTC-USDT', position_side: 'short', trim_pct: 25 },
    shortPos,
  );
  assert.equal(r.kind, 'order');
  assert.equal(r.payload.side, 'buy');
  assert.equal(r.payload.position_side, 'short');
  assert.equal(r.payload.reduce_only, true);
  assert.equal(r.payload.quantity, 0.005); // 25% of 0.02
});

test('TRIM clamps trim_pct to 1-99 range', () => {
  const eps = 1e-9; // float 精度容差
  const r1 = planPositionIntent(
    { intent: 'TRIM', symbol: 'BTC-USDT', position_side: 'long', trim_pct: 0 },
    longPos,
  );
  assert.equal(r1.kind, 'order');
  assert.ok(Math.abs(r1.payload.quantity - 0.0001) < eps, `1% clamp: got ${r1.payload.quantity}`);

  const r2 = planPositionIntent(
    { intent: 'TRIM', symbol: 'BTC-USDT', position_side: 'long', trim_pct: 200 },
    longPos,
  );
  assert.equal(r2.kind, 'order');
  // 99% clamp — 不允許 100% 走 TRIM、那是 EXIT
  assert.ok(Math.abs(r2.payload.quantity - 0.0099) < eps, `99% clamp: got ${r2.payload.quantity}`);
});

test('EXIT without currentPosition errors', () => {
  const r = planPositionIntent({ intent: 'EXIT', symbol: 'BTC-USDT', position_side: 'long' }, null);
  assert.equal(r.kind, 'error');
  assert.match(r.error, /no long position/);
});

test('EXIT long produces full-quantity reduce_only sell', () => {
  const r = planPositionIntent(
    { intent: 'EXIT', symbol: 'BTC-USDT', position_side: 'long' },
    longPos,
  );
  assert.equal(r.kind, 'order');
  assert.equal(r.payload.side, 'sell');
  assert.equal(r.payload.position_side, 'long');
  assert.equal(r.payload.reduce_only, true);
  assert.equal(r.payload.quantity, 0.01);
});

test('EXIT short produces full-quantity reduce_only buy', () => {
  const r = planPositionIntent(
    { intent: 'EXIT', symbol: 'BTC-USDT', position_side: 'short' },
    shortPos,
  );
  assert.equal(r.kind, 'order');
  assert.equal(r.payload.side, 'buy');
  assert.equal(r.payload.reduce_only, true);
  assert.equal(r.payload.quantity, 0.02);
});

test('TRIM/EXIT never accidentally flips position (always reduce_only=true)', () => {
  // 防衛性測試：若有人重構意外把 reduce_only 拿掉、會造成翻倉
  for (const intent of ['TRIM', 'EXIT']) {
    for (const side of ['long', 'short']) {
      const pos = { symbol: 'X', side, quantity: 1 };
      const r = planPositionIntent({ intent, symbol: 'X', position_side: side }, pos);
      assert.equal(r.kind, 'order');
      assert.equal(r.payload.reduce_only, true,
        `${intent} ${side} must be reduce_only=true to prevent flip`);
    }
  }
});

// ── findPosition ────────────────────────────────────────────

test('findPosition matches by symbol+side case-insensitively', () => {
  const positions = [
    { symbol: 'BTC-USDT', side: 'LONG',  quantity: 0.01 },
    { symbol: 'eth-usdt', side: 'short', quantity: 0.5 },
  ];
  const r = findPosition(positions, 'btc-usdt', 'long');
  assert.equal(r?.quantity, 0.01);

  const r2 = findPosition(positions, 'ETH-USDT', 'SHORT');
  assert.equal(r2?.quantity, 0.5);
});

test('findPosition returns null when not found', () => {
  assert.equal(findPosition([], 'BTC', 'long'), null);
  assert.equal(findPosition(null, 'BTC', 'long'), null);
  assert.equal(findPosition([{ symbol: 'BTC', side: 'long', quantity: 1 }], 'ETH', 'long'), null);
});
