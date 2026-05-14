// Status snapshot：跟 dashboard 上常駐 KPI bar 同一組數字，文字版回給 Discord / LINE。
//
// 設計目標：
//   - 純 broker 呼叫（同 KPI bar 那 3 個 endpoint）+ 1 個 logs 算 gate 觸發
//   - 不走 LLM、不打 Anthropic API（省 token、< 500ms）
//   - 失敗任一 endpoint 用「—」表示而不是整個 fail（user 還是看得到其他 KPI）
//
// 觸發詞：buildStatusSnapshot() 在 callsite 由訊息文字決定（isStatusTrigger）

import { callBroker } from './broker.js';

/** 視為 status 觸發詞 */
export function isStatusTrigger(text) {
  if (!text) return false;
  const t = text.trim().toLowerCase();
  return t === '狀態' || t === 'status' || t === '/status' || t === 'kpi' || t === '/kpi';
}

/**
 * 收集 broker 4 個 endpoint、回 markdown-ish 純文字。
 * deps 用於測試注入 fake callBroker。
 */
export async function buildStatusSnapshot(deps = {}) {
  const _call = deps.callBroker || callBroker;
  const since = new Date(Date.now() - 86400000).toISOString();

  // 4 個獨立呼叫、Promise.allSettled 不互卡
  const [anchorR, autoR, pnlR, logsR] = await Promise.allSettled([
    _call('GET', '/api/v1/trading/risk-anchor/bingx'),
    _call('GET', '/api/v1/auto-trader/status'),
    _call('GET', `/api/v1/trading/pnl-summary?exchange=bingx&since=${encodeURIComponent(since)}`),
    _call('GET', '/api/v1/auto-trader/logs?limit=50'),
  ]);

  const pick = r => (r.status === 'fulfilled' && r.value?.ok) ? r.value.data : null;
  return formatSnapshot({
    anchor: pick(anchorR),
    auto:   pick(autoR),
    pnl:    pick(pnlR),
    logs:   pick(logsR),
  });
}

/** 純函式、可單測。對應 broker 真實 shape，不再亂猜 fallback 路徑。 */
export function formatSnapshot({ anchor, auto, pnl, logs }) {
  const lines = ['🛡 Bricks4Agent 狀態', ''];

  // Anchor：risk-anchor endpoint 的 in_memory_anchor
  const anchorVal = anchor?.in_memory_anchor ?? null;
  lines.push(`⚓ Anchor: ${fmtUsd(anchorVal)}`);

  // Live balance：persisted.last_seen_balance（BalanceAnchorService 5min poll）
  const live = anchor?.persisted?.last_seen_balance ?? null;
  const delta = (live != null && anchorVal != null && anchorVal > 0)
    ? ((live - anchorVal) / anchorVal * 100) : null;
  const deltaTag = delta != null ? ` (${delta >= 0 ? '+' : ''}${delta.toFixed(2)}%)` : '';
  lines.push(`💰 Live: ${fmtUsd(live)}${deltaTag}`);

  // Open positions：auto-trader/status.position_states 是 dict、key 數 = 開倉中
  const open = auto?.position_states ? Object.keys(auto.position_states).length : null;
  lines.push(`📊 Open positions: ${open ?? '—'}`);

  // 24h realized：pnl-summary.realized_pnl_sum
  const day = pnl?.realized_pnl_sum;
  const dayTag = day != null && isFinite(day)
    ? `${day >= 0 ? '+' : ''}${Number(day).toFixed(2)} USDT`
    : '—';
  lines.push(`📈 24h PnL: ${dayTag}`);

  // AutoTrader enabled + watch count
  const enabled = auto?.enabled === true;
  const watches = auto?.watch_count ?? null;
  lines.push(`🤖 AutoTrader: ${enabled ? 'enabled' : 'disabled'}${watches != null ? ` (${watches} watches)` : ''}`);

  // Gate triggers last 1h
  const gates = countRecentGateTriggers(logs);
  lines.push(`🚦 Gates: ${gates} triggered in last 1h`);

  lines.push('', `🕐 ${new Date().toISOString().replace('T', ' ').slice(0, 19)} UTC`);
  return lines.join('\n');
}

function fmtUsd(n) {
  if (n == null || !isFinite(n)) return '—';
  return `${Number(n).toFixed(2)} USDT`;
}

/** 從最近 logs 數出 1 小時內 gate 觸發次數。logs 可以是 array 或 {logs:[]} */
export function countRecentGateTriggers(logs) {
  const arr = Array.isArray(logs) ? logs : (Array.isArray(logs?.logs) ? logs.logs : null);
  if (!arr) return 0;
  const oneHourAgo = Date.now() - 3600 * 1000;
  const gateRe = /reject|cap:|cooldown|threshold|exhausted|reached|liq alert|slippage/i;
  let n = 0;
  for (const l of arr) {
    const t = new Date(l.time).getTime();
    if (!isFinite(t)) continue;
    if (t >= oneHourAgo && gateRe.test(l.message || '')) n++;
  }
  return n;
}
