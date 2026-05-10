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

/**
 * Tool catalog：每個 tool 對應一個 broker endpoint。
 *
 * key = tool name（LLM 用這個 call）
 * args_schema 純文字描述、LLM 看 system prompt 學
 * dispatch(args) → broker call、回 {ok, data, error}
 */
export const TOOLS = {
  'quote.prices': {
    description: '取得即時報價。args: {symbols: string[]}（例：["BTC-USDT", "ETH-USDT"]）',
    dispatch: async (args) => {
      return await callBroker('POST', '/api/v1/quote/prices', {
        symbols: args.symbols || [],
      });
    },
  },

  'quote.ohlcv': {
    description: '取得 K 線。args: {symbol: string, interval: "1h"|"4h"|"1d", limit: number(<=500)}',
    dispatch: async (args) => {
      return await callBroker('POST', '/api/v1/quote/ohlcv', {
        symbol: args.symbol,
        interval: args.interval || '1h',
        limit: Math.min(args.limit || 100, 500),
      });
    },
  },

  'strategy.list': {
    description: '列出所有策略 + metadata（category / min_capital_usdt / param_schema）。args: {}',
    dispatch: async () => callBroker('GET', '/api/v1/strategy/list'),
  },

  'strategy.signal': {
    description: '對給定 K 線跑單一策略產生訊號。args: {strategy: string, symbol: string, interval: string, bars: array}。注意 bars 一般要先 quote.ohlcv 拿。',
    dispatch: async (args) => {
      return await callBroker('POST', '/api/v1/strategy/signal', args);
    },
  },

  'trading.account': {
    description: '看 perp 帳戶餘額 + 槓桿。args: {exchange: "bingx"}',
    dispatch: async (args) => {
      return await callBroker('POST', '/api/v1/perpetual/account', {
        exchange: args.exchange || 'bingx',
      });
    },
  },

  'trading.positions': {
    description: '看 perp 部位列表。args: {exchange: "bingx"}',
    dispatch: async (args) => {
      return await callBroker('POST', '/api/v1/perpetual/positions', {
        exchange: args.exchange || 'bingx',
      });
    },
  },

  'health.score': {
    description: '看平台整體健康分數（0-100）+ 每 worker breakdown。args: {}',
    dispatch: async () => callBroker('GET', '/api/v1/health/score'),
  },

  'audit.topology': {
    description: '看過去 N 分鐘 capability 派發拓撲（誰呼叫了什麼）。args: {since_minutes: number(default 60)}',
    dispatch: async (args) => {
      return await callBroker('GET', `/api/v1/audit/topology?since_minutes=${args.since_minutes || 60}`);
    },
  },
};

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
 * @returns {Promise<{ok:boolean, summary:string, data?:any, error?:string}>}
 *   summary = 給 LLM 看的精簡文字（避免塞超大 JSON）
 */
export async function dispatchTool(toolCall) {
  const { call, args } = toolCall;
  const def = TOOLS[call];
  if (!def) {
    return { ok: false, summary: `unknown tool: ${call}`, error: 'unknown_tool' };
  }

  const result = await def.dispatch(args);
  if (!result.ok) {
    return {
      ok: false,
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
    summary: `tool=${call} ok\n${truncated}`,
    data: result.data,
  };
}
