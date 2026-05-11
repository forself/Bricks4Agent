// Position management intents — 把 LLM 高階意圖（ADD/HOLD/TRIM/EXIT）翻譯成
// broker `/api/v1/perpetual/order` 的 payload。
//
// 為什麼分離：
//   - LLM 用「我想加碼 / 縮小一半 / 全平」這種人話、比 「下一個 reduce_only=true 反向單」
//     好寫好讀；意圖映射跟 place_order 邏輯解耦。
//   - 平倉永遠走 reduce_only + 反向 side，避免不小心翻倉、套牢。
//   - 純函數方便加單元測試、不用 mock broker。
//
// 走 trading.* 前綴 → 已被 access.js 的 isPrivilegedTool 認定為敏感工具、
// 自動套頻道層 + 工具層權限；下單 endpoint 自帶 approval gate（commit 32c19c5）。

/** @typedef {{symbol:string, side:string, quantity:number}} CurrentPosition */

const VALID_INTENTS = ['ADD', 'HOLD', 'TRIM', 'EXIT'];
const VALID_SIDES = ['long', 'short'];

/**
 * 規畫一筆 position intent → broker order payload。
 *
 * @param {object} input
 * @param {'ADD'|'HOLD'|'TRIM'|'EXIT'} input.intent
 * @param {string} input.symbol
 * @param {'long'|'short'} input.position_side
 * @param {string} input.exchange
 * @param {number=} input.add_qty   ADD 必填、要新增多少 contract
 * @param {number=} input.trim_pct  TRIM 用、預設 50；clamp 到 1-99
 * @param {CurrentPosition|null} currentPosition
 *   TRIM/EXIT 用、由 caller 從 trading.positions 撈來；HOLD/ADD 不需要傳
 *
 * @returns {{kind:'noop', message:string} | {kind:'order', payload:object} | {kind:'error', error:string}}
 */
export function planPositionIntent(input, currentPosition) {
  const intent = String(input?.intent || '').toUpperCase();
  if (!VALID_INTENTS.includes(intent)) {
    return { kind: 'error', error: `unknown intent: ${input?.intent} (allowed: ${VALID_INTENTS.join('/')})` };
  }
  if (intent === 'HOLD') {
    return { kind: 'noop', message: 'HOLD: 維持現狀、不下單' };
  }

  const symbol = String(input?.symbol || '').trim();
  const positionSide = String(input?.position_side || '').toLowerCase();
  const exchange = String(input?.exchange || 'bingx');

  if (!symbol) return { kind: 'error', error: 'symbol required' };
  if (!VALID_SIDES.includes(positionSide)) {
    return { kind: 'error', error: `position_side must be long or short (got: ${input?.position_side})` };
  }

  // ADD：開新單同向、reduce_only=false。需要使用者明確傳 add_qty——
  // 不從現有部位推斷，避免「想加一點點」LLM 解成「翻倍」這種壞案例。
  if (intent === 'ADD') {
    const addQty = Number(input?.add_qty);
    if (!Number.isFinite(addQty) || addQty <= 0) {
      return { kind: 'error', error: 'ADD requires positive add_qty (contracts to add)' };
    }
    return {
      kind: 'order',
      payload: {
        exchange, symbol,
        side: positionSide === 'long' ? 'buy' : 'sell',
        position_side: positionSide,
        order_type: 'market',
        quantity: addQty,
        reduce_only: false,
      },
    };
  }

  // TRIM/EXIT：需要當前部位才能算 reduce 數量
  if (!currentPosition || !Number.isFinite(currentPosition.quantity) || currentPosition.quantity <= 0) {
    return { kind: 'error', error: `no ${positionSide} position on ${symbol} (${exchange}) — nothing to ${intent}` };
  }
  const currentQty = Math.abs(currentPosition.quantity);

  if (intent === 'EXIT') {
    return {
      kind: 'order',
      payload: {
        exchange, symbol,
        side: positionSide === 'long' ? 'sell' : 'buy',
        position_side: positionSide,
        order_type: 'market',
        quantity: currentQty,
        reduce_only: true,
      },
    };
  }

  // TRIM
  const rawPct = Number(input?.trim_pct);
  const trimPct = Number.isFinite(rawPct) ? Math.min(99, Math.max(1, rawPct)) : 50;
  const trimQty = currentQty * trimPct / 100;
  if (trimQty <= 0) {
    return { kind: 'error', error: `TRIM resolved to non-positive quantity (currentQty=${currentQty}, trim_pct=${trimPct})` };
  }
  return {
    kind: 'order',
    payload: {
      exchange, symbol,
      side: positionSide === 'long' ? 'sell' : 'buy',
      position_side: positionSide,
      order_type: 'market',
      quantity: trimQty,
      reduce_only: true,
    },
  };
}

/**
 * 從 broker `/api/v1/perpetual/positions` 回傳的陣列中挑出符合 (symbol, position_side) 的部位。
 * 兩邊欄位命名兜：broker 的 position 用 `side` 欄存 long/short（見 TradingPerpetualHandler.GetPositions）。
 *
 * @param {Array<{symbol?:string, side?:string, quantity?:number}>} positions
 * @returns {CurrentPosition|null}
 */
export function findPosition(positions, symbol, positionSide) {
  if (!Array.isArray(positions)) return null;
  const symU = String(symbol || '').toUpperCase();
  const sideL = String(positionSide || '').toLowerCase();
  const m = positions.find(p =>
    String(p?.symbol || '').toUpperCase() === symU &&
    String(p?.side || '').toLowerCase() === sideL,
  );
  if (!m) return null;
  return { symbol: m.symbol, side: m.side, quantity: Number(m.quantity) };
}
