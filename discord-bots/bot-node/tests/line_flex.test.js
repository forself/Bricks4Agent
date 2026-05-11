// 鎖住 LINE Flex builder + postback 解析的契約。
// 跑法：node --test discord-bots/bot-node/tests/line_flex.test.js

import test from 'node:test';
import assert from 'node:assert/strict';
import { buildApprovalFlex, parseApprovalPostback } from '../src/line_flex.js';

const sampleApr = {
  approval_id:    'apr_abc12345',
  capability_id:  'trading.perpetual',
  route:          'place_order',
  principal_id:   'prn_lab',
  role:           'role_user',
  payload:        '{"symbol":"BTC-USDT","side":"buy","quantity":0.01}',
  requested_at:   '2026-05-11T08:00:00Z',
};

test('buildApprovalFlex emits a flex bubble with header/body/footer', () => {
  const flex = buildApprovalFlex(sampleApr);
  assert.equal(flex.type, 'flex');
  assert.ok(flex.altText.includes('trading.perpetual'));
  assert.equal(flex.contents.type, 'bubble');
  assert.equal(flex.contents.header.type, 'box');
  assert.equal(flex.contents.body.type, 'box');
  assert.equal(flex.contents.footer.type, 'box');
});

test('buildApprovalFlex includes approval_id, route, principal in body text', () => {
  const flex = buildApprovalFlex(sampleApr);
  const bodyTexts = collectText(flex.contents.body);
  assert.ok(bodyTexts.includes('apr_abc12345'), 'approval_id 應出現在 body');
  assert.ok(bodyTexts.includes('place_order'),  'route 應出現在 body');
  assert.ok(bodyTexts.includes('prn_lab'),      'principal 應出現在 body');
});

test('buildApprovalFlex footer has two postback buttons with correct data', () => {
  const flex = buildApprovalFlex(sampleApr);
  const buttons = flex.contents.footer.contents;
  assert.equal(buttons.length, 2);
  assert.equal(buttons[0].action.type, 'postback');
  assert.equal(buttons[0].action.data, 'b4a:apr:approve:apr_abc12345');
  assert.equal(buttons[1].action.type, 'postback');
  assert.equal(buttons[1].action.data, 'b4a:apr:reject:apr_abc12345');
});

test('buildApprovalFlex truncates long payload', () => {
  const longApr = { ...sampleApr, payload: 'x'.repeat(5000) };
  const flex = buildApprovalFlex(longApr);
  const bodyTexts = collectText(flex.contents.body);
  assert.ok(bodyTexts.includes('truncated'),
    'payload 過長時應有 truncated 標記');
});

test('buildApprovalFlex tolerates missing fields', () => {
  const flex = buildApprovalFlex({});
  // 不應該拋例外、應該有預設替代值
  assert.equal(flex.type, 'flex');
  const bodyTexts = collectText(flex.contents.body);
  assert.ok(bodyTexts.length > 0);
});

// ── parseApprovalPostback ───────────────────────────────────

test('parseApprovalPostback parses approve action', () => {
  const r = parseApprovalPostback('b4a:apr:approve:apr_xyz');
  assert.deepEqual(r, { action: 'approve', approvalId: 'apr_xyz' });
});

test('parseApprovalPostback parses reject action', () => {
  const r = parseApprovalPostback('b4a:apr:reject:apr_xyz');
  assert.deepEqual(r, { action: 'reject', approvalId: 'apr_xyz' });
});

test('parseApprovalPostback preserves approval_id with colons inside (last segment greedy)', () => {
  // 防衛：approval_id 含 ':' 不太可能但保險
  const r = parseApprovalPostback('b4a:apr:approve:apr_part1:part2');
  assert.equal(r?.action, 'approve');
  assert.equal(r?.approvalId, 'apr_part1:part2');
});

test('parseApprovalPostback returns null for foreign prefix', () => {
  assert.equal(parseApprovalPostback('other:thing:approve:xyz'), null);
  assert.equal(parseApprovalPostback(''), null);
  assert.equal(parseApprovalPostback(null), null);
  assert.equal(parseApprovalPostback(undefined), null);
});

test('parseApprovalPostback returns null for unknown action verb', () => {
  assert.equal(parseApprovalPostback('b4a:apr:yolo:apr_x'), null);
});

test('parseApprovalPostback returns null when approval_id missing', () => {
  assert.equal(parseApprovalPostback('b4a:apr:approve:'), null);
  assert.equal(parseApprovalPostback('b4a:apr:approve'), null);
});

// 把 Flex box 樹裡所有 text 拼起來方便斷言
function collectText(node) {
  if (!node) return '';
  if (Array.isArray(node)) return node.map(collectText).join(' ');
  if (typeof node !== 'object') return '';
  let acc = '';
  if (typeof node.text === 'string') acc += node.text + ' ';
  if (Array.isArray(node.contents)) acc += collectText(node.contents);
  return acc;
}
