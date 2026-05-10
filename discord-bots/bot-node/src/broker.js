// Broker HTTP client for bot-node。
// 帶 X-Internal-Bot-Token header、broker 端 InternalBotAuthMiddleware 會把這個請求
// 認成 prn_dc_bot/role_user。
//
// 環境變數：
//   BROKER_URL          預設 http://broker:5000（容器內網路名）、本機開發用 http://localhost:5100
//   BOT_INTERNAL_TOKEN  共享 secret、跟 broker 端 BOT_INTERNAL_TOKEN env 對齊

const BROKER_URL = process.env.BROKER_URL || 'http://broker:5000';
const BOT_TOKEN = process.env.BOT_INTERNAL_TOKEN || '';

if (!BOT_TOKEN) {
  console.warn('[broker] BOT_INTERNAL_TOKEN not set; broker calls will fail with 401');
}

/**
 * 通用 broker 呼叫。
 * @param {'GET'|'POST'} method
 * @param {string} path 例：/api/v1/strategy/list
 * @param {object} [body] POST 才用、會 JSON.stringify
 * @returns {Promise<{ok:boolean, status:number, data?:any, error?:string}>}
 */
export async function callBroker(method, path, body = null) {
  const url = `${BROKER_URL}${path}`;
  const headers = {
    'Content-Type': 'application/json',
    'X-Internal-Bot-Token': BOT_TOKEN,
  };
  try {
    const resp = await fetch(url, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined,
      // 30s timeout
      signal: AbortSignal.timeout(30_000),
    });
    const text = await resp.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { /* 非 JSON、留 null */ }

    if (!resp.ok) {
      return {
        ok: false,
        status: resp.status,
        error: `HTTP ${resp.status}: ${(data && (data.message || data.error)) || text.slice(0, 200)}`,
      };
    }
    // broker 用 ApiResponseHelper 包：{ success, message, data, ... }
    if (data && typeof data === 'object' && 'success' in data) {
      if (!data.success) {
        return { ok: false, status: resp.status, error: data.message || 'broker error' };
      }
      return { ok: true, status: resp.status, data: data.data };
    }
    return { ok: true, status: resp.status, data };
  } catch (e) {
    return { ok: false, status: 0, error: `network: ${e.message}` };
  }
}
