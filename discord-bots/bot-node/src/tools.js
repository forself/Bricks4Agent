// Phase 3 tool catalog + dispatcher。
// LLM 看到 system prompt 知道有哪些 tool 可呼叫、要呼叫時輸出固定格式 JSON。
// 本檔負責：
//   - 提供 tool descriptions（給 system prompt）
//   - parse LLM 回應抓 tool_call JSON
//   - 對應到 broker capability、HTTP POST 過去
//   - 結果包成 JSON 字串給 LLM 下一輪用
//
// 故意只放唯讀 tool。phase 4 再加會動帳戶的（trading.order）+ approval workflow 串接。

import { callBroker } from './broker.js';
import { planPositionIntent, findPosition } from './position_intent.js';

/**
 * Tool catalog：每個 tool 對應一個 broker endpoint。
 *
 * key = tool name（LLM 用這個 call）
 * args_schema 純文字描述、LLM 看 system prompt 學
 * dispatch(args) → broker call、回 {ok, data, error}
 */
export const TOOLS = {
  'quote.prices': {
    description: '取得 quote-worker 在追蹤的所有 symbol 最新報價。args: {} （不接參數、回所有 watched symbols）',
    dispatch: async () => callBroker('GET', '/api/v1/workers/quote/prices'),
  },

  'quote.ohlcv': {
    description: '取得單一 symbol 的 K 線。args: {symbol: string (例 "BTC-USDT"), interval: "1h"|"4h"|"1d", limit: number(預設 100, max 500)}',
    dispatch: async (args) => {
      const symbol = encodeURIComponent(args.symbol || '');
      const interval = encodeURIComponent(args.interval || '1h');
      const limit = Math.min(parseInt(args.limit, 10) || 100, 500);
      return await callBroker('GET', `/api/v1/workers/quote/ohlcv/?symbol=${symbol}&interval=${interval}&limit=${limit}`);
    },
  },

  'strategy.list': {
    description: '列出所有策略 + metadata（category / min_capital_usdt / param_schema）。args: {}',
    dispatch: async () => callBroker('GET', '/api/v1/strategy/list'),
  },

  'strategy.signal': {
    description: '對 K 線跑單一策略產生訊號。args: {strategy: string, symbol: string, interval: string, bars?: array, bars_limit?: number(預設 100, max 500)}。**bars 沒給的話 tool 會自動先 quote.ohlcv 拿、不用你 chain 兩次 call**——只給 {strategy, symbol, interval} 就夠。要自帶 bars 也支援。',
    dispatch: async (args) => {
      let bars = args.bars;
      // 沒帶 bars / 空 / 不是陣列 → tool 自己先抓 ohlcv
      if (!Array.isArray(bars) || bars.length < 2) {
        const limit = Math.min(parseInt(args.bars_limit, 10) || 100, 500);
        const symbol = encodeURIComponent(args.symbol || '');
        const interval = encodeURIComponent(args.interval || '1h');
        const ohlcv = await callBroker('GET',
          `/api/v1/workers/quote/ohlcv/?symbol=${symbol}&interval=${interval}&limit=${limit}`);
        if (!ohlcv.ok) {
          return { ok: false, status: ohlcv.status, error: `auto ohlcv fetch failed: ${ohlcv.error}` };
        }
        bars = ohlcv.data?.bars;
        if (!Array.isArray(bars) || bars.length < 2) {
          return { ok: false, status: 400, error: `auto-fetched only ${bars?.length || 0} bars, need >= 2` };
        }
      }
      return await callBroker('POST', '/api/v1/strategy/signal', {
        strategy: args.strategy,
        symbol: args.symbol,
        interval: args.interval,
        bars,
      });
    },
  },

  'trading.account': {
    description: '看 perp 帳戶餘額 + 可用保證金。args: {exchange: "bingx"}',
    dispatch: async (args) => {
      const ex = encodeURIComponent(args.exchange || 'bingx');
      return await callBroker('GET', `/api/v1/perpetual/account?exchange=${ex}`);
    },
  },

  'trading.positions': {
    description: '看 perp 部位列表。args: {exchange: "bingx"}',
    dispatch: async (args) => {
      const ex = encodeURIComponent(args.exchange || 'bingx');
      return await callBroker('GET', `/api/v1/perpetual/positions?exchange=${ex}`);
    },
  },

  'trading.order': {
    description: '下 perp 單（**會走 admin 核准流程**——你 call 完幾乎一定會收到 "Pending admin approval, approval_id=apr_..." 的錯誤訊息、那是正常的、不是失敗、把 approval_id 轉達使用者請他到 dashboard 「待審」分頁裁決即可、不要重試）。args: {symbol: string(例 "BTC-USDT"), side: "buy"|"sell", position_side: "long"|"short", quantity: number, order_type?: "market"|"limit"(預設 market), limit_price?: number(限價單必填), leverage?: number(預設 1, 開倉用), reduce_only?: bool(平倉設 true), exchange?: "bingx"}',
    dispatch: async (args) => {
      const payload = {
        exchange: args.exchange || 'bingx',
        symbol: args.symbol,
        side: args.side,
        position_side: args.position_side,
        order_type: args.order_type || 'market',
        quantity: args.quantity,
      };
      if (args.limit_price != null) payload.limit_price = args.limit_price;
      if (args.stop_price != null) payload.stop_price = args.stop_price;
      if (args.leverage != null) payload.leverage = args.leverage;
      if (args.reduce_only != null) payload.reduce_only = args.reduce_only;
      return await callBroker('POST', '/api/v1/perpetual/order', payload);
    },
  },

  'trading.position_intent': {
    description: '高階部位管理意圖：ADD（加碼）/ HOLD（保留不動）/ TRIM（部分平倉）/ EXIT（全部平倉）。本工具會自動翻譯成 reduce_only 反向單（TRIM/EXIT）或同向開倉（ADD）、再走 approval gate。**跟 trading.order 一樣會收到 "Pending admin approval, approval_id=..." 是正常的**、把 ID 轉達使用者。比 trading.order 更安全：TRIM/EXIT 不會翻倉、強制 reduce_only=true。args: {intent: "ADD"|"HOLD"|"TRIM"|"EXIT", symbol: string, position_side: "long"|"short", add_qty?: number(ADD 必填), trim_pct?: number(TRIM 用、1-99、預設 50), exchange?: "bingx"}',
    dispatch: async (args) => {
      const intent = String(args?.intent || '').toUpperCase();

      // HOLD / 純錯誤：不用 fetch 部位、直接 plan
      if (intent === 'HOLD' || intent === 'ADD') {
        const plan = planPositionIntent(args, null);
        return planResultToBroker(plan);
      }

      // TRIM / EXIT：先查現有部位
      const ex = encodeURIComponent(args?.exchange || 'bingx');
      const positionsResp = await callBroker('GET', `/api/v1/perpetual/positions?exchange=${ex}`);
      if (!positionsResp.ok) {
        return { ok: false, status: positionsResp.status, error: `position lookup failed: ${positionsResp.error}` };
      }
      const current = findPosition(positionsResp.data?.positions, args?.symbol, args?.position_side);
      const plan = planPositionIntent(args, current);
      return planResultToBroker(plan);
    },
  },

  'health.score': {
    description: '看平台整體健康分數（0-100）+ 每 worker breakdown。args: {}',
    dispatch: async () => callBroker('GET', '/api/v1/health/score'),
  },

  'audit.topology': {
    description: '看過去 N 分鐘 capability 派發拓撲（誰呼叫了什麼）。args: {since_minutes: number(default 60)}',
    dispatch: async (args) => {
      const since = parseInt(args.since_minutes, 10) || 60;
      return await callBroker('GET', `/api/v1/audit/topology?since_minutes=${since}`);
    },
  },
};

// 把 planPositionIntent 的結果包成 broker 風格回應；HOLD/error 直接收尾、order 才轉發。
async function planResultToBroker(plan) {
  if (plan.kind === 'error') {
    return { ok: false, status: 400, error: plan.error };
  }
  if (plan.kind === 'noop') {
    return { ok: true, status: 200, data: { intent: 'HOLD', action: 'no-op', message: plan.message } };
  }
  return await callBroker('POST', '/api/v1/perpetual/order', plan.payload);
}

/**
 * 給 system prompt 用的 tool 描述列表。
 */
export function toolCatalogText() {
  const lines = ['你可以呼叫以下 tool 來查 B4A 平台資訊：'];
  for (const [name, def] of Object.entries(TOOLS)) {
    lines.push(`- \`${name}\`：${def.description}`);
  }
  return lines.join('\n');
}

/**
 * 從 LLM 回應抓 tool_call JSON。
 * 接受兩種格式：
 *   1. fenced code block ```json {"call": "...", "args": {...}} ```
 *   2. inline JSON line（少見、容錯用）
 * 回傳第一個有效的 tool_call 或 null。
 */
export function extractToolCall(text) {
  if (!text) return null;

  // 找 ```json ... ``` 區塊（不限 json 標籤、tolerate ``` only）
  const fenceRe = /```(?:json)?\s*\n([\s\S]*?)\n?```/m;
  const m = fenceRe.exec(text);
  if (m) {
    return tryParseToolCall(m[1]);
  }

  // 退而求其次：找最像 JSON 的整段
  const startIdx = text.indexOf('{"call"');
  if (startIdx >= 0) {
    // 找配對的 }（簡單 brace counting、遇到第一個平衡點停）
    const sub = extractBalanced(text, startIdx);
    if (sub) return tryParseToolCall(sub);
  }

  return null;
}

function tryParseToolCall(jsonStr) {
  try {
    const obj = JSON.parse(jsonStr);
    if (obj && typeof obj === 'object'
        && typeof obj.call === 'string'
        && TOOLS[obj.call]) {
      return { call: obj.call, args: obj.args || {} };
    }
  } catch { /* parse 失敗 */ }
  return null;
}

function extractBalanced(text, startIdx) {
  let depth = 0;
  for (let i = startIdx; i < text.length; i++) {
    const c = text[i];
    if (c === '{') depth++;
    else if (c === '}') {
      depth--;
      if (depth === 0) return text.slice(startIdx, i + 1);
    }
  }
  return null;
}

/**
 * 執行 tool call。
 * @param {{call:string,args:object}} toolCall
 * @param {{userId:string, isPrivileged:boolean}} caller — 訊息來源者、決定能不能 call 敏感工具
 * @returns {Promise<{ok:boolean, summary:string, data?:any, error?:string}>}
 *   summary = 給 LLM 看的精簡文字（避免塞超大 JSON）
 */
export async function dispatchTool(toolCall, caller = { userId: '?', isPrivileged: true }) {
  const { call, args } = toolCall;
  const def = TOOLS[call];
  if (!def) {
    return { ok: false, status: 'failed', summary: `unknown tool: ${call}`, error: 'unknown_tool' };
  }

  // 工具層權限：privileged tool（trading.* / audit.*）只放 privileged user 過、
  // 唯讀 tool（quote.* / strategy.* / health.*）任何頻道成員都能 call。
  // 這裡擋下來會給 LLM 看到 access_denied、它應該轉成「請聯絡 anthonylee 開帳號」訊息。
  if (caller.privilegedTool && !caller.isPrivileged) {
    return {
      ok: false,
      status: 'denied',
      summary: `tool=${call} access_denied: this tool requires platform-account-holder privilege. The Discord user (id=${caller.userId}) lacks it. Politely tell them they need a platform account from anthonylee to use trading / audit features.`,
      error: 'access_denied',
    };
  }

  const result = await def.dispatch(args);

  // 結構化 pending_approval 偵測（broker `ApprovalAwareResponseHelper` 統一格式）：
  // approval gate 卡住的單 broker 回 success + data.status='pending_approval' + data.approval_id。
  // 必須在 success 判斷前先攔、否則會被當成「真成功」回 status='success'。
  if (result.ok && result.data && result.data.status === 'pending_approval') {
    const aid = result.data.approval_id || 'unknown';
    return {
      ok: true,
      status: 'pending_approval',
      summary: `tool=${call} pending_approval: approval_id=${aid}. ${result.data.note || 'Awaiting admin approval.'}`,
      data: result.data,
    };
  }

  if (!result.ok) {
    // Legacy fallback：舊版 broker / 沒走 ApprovalAwareResponseHelper 的 endpoint 可能還是把
    // approval 訊息塞在 error string 裡（HTTP 也回非 2xx）。用 approval_id=apr_ regex 抓、
    // 避免誤判其它含 "approval" 的真錯誤。新版 broker 不會走到這條。
    const isPendingApproval = !!result.error && /approval_id=apr_/i.test(result.error);
    if (isPendingApproval) {
      return {
        ok: true,
        status: 'pending_approval',
        summary: `tool=${call} pending_approval: ${result.error}`,
        data: { pending: true, message: result.error },
        error: result.error,
      };
    }
    return {
      ok: false,
      status: 'failed',
      summary: `tool=${call} failed: ${result.error}`,
      error: result.error,
    };
  }

  // 把 data 轉 JSON 字串、太長截短（避免下一輪 prompt 爆）
  const dataStr = JSON.stringify(result.data, null, 2);
  const truncated = dataStr.length > 4000
    ? dataStr.slice(0, 4000) + `\n... (truncated, full size ${dataStr.length} chars)`
    : dataStr;

  return {
    ok: true,
    status: 'success',
    summary: `tool=${call} ok\n${truncated}`,
    data: result.data,
  };
}
