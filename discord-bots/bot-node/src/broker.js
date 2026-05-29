// Broker HTTP client for bot-node。
// 帶 X-Internal-Bot-Token header、broker 端 InternalBotAuthMiddleware 會把這個請求
// 認成 prn_dc_bot/role_user。Admin 端點（/api/v1/admin/*）需要另一個 admin token。
//
// 環境變數：
//   BROKER_URL                   預設 http://broker:5000（容器內網路名）、本機開發用 http://localhost:5100
//   BOT_INTERNAL_TOKEN           共享 secret、跟 broker 端 BOT_INTERNAL_TOKEN env 對齊（user role）
//   BOT_INTERNAL_ADMIN_TOKEN     另一把 secret、給 admin endpoints 用（手機按鈕審核）

const BROKER_URL = process.env.BROKER_URL || 'http://broker:5000';
const BOT_TOKEN = process.env.BOT_INTERNAL_TOKEN || '';
const BOT_ADMIN_TOKEN = process.env.BOT_INTERNAL_ADMIN_TOKEN || '';

if (!BOT_TOKEN) {
  console.warn('[broker] BOT_INTERNAL_TOKEN not set; broker calls will fail with 401');
}
if (!BOT_ADMIN_TOKEN) {
  console.warn('[broker] BOT_INTERNAL_ADMIN_TOKEN not set; mobile approval buttons will not work');
}

// broker 容器 recreate 會換 docker IP。預設 keep-alive 會卡在指向舊 IP 的死連線、
// 以前要手動 restart bot-node 才恢復。用短 keep-alive 的 dispatcher(連線幾乎不重用)→
// 每次請求重開連線、重解析 DNS,broker 重啟後自動連到新 IP。
// undici 經 discord.js 帶入;拿不到就純靠 callBroker 的 retry。只套在 broker 呼叫、不動 discord.js。
let brokerDispatcher = null;
try {
  const { Agent } = await import('undici');
  brokerDispatcher = new Agent({ keepAliveTimeout: 10, keepAliveMaxTimeout: 10 });
  console.log('[broker] short keep-alive dispatcher active (auto-reconnect on broker IP change)');
} catch {
  console.warn('[broker] undici Agent unavailable; relying on retry for reconnect');
}

/**
 * 通用 broker 呼叫。
 * @param {'GET'|'POST'|'DELETE'} method
 * @param {string} path 例：/api/v1/strategy/list
 * @param {object} [body] POST 才用、會 JSON.stringify
 * @param {{admin?: boolean}} [opts] admin=true 時用 admin token、給 /admin/* 端點呼叫
 * @returns {Promise<{ok:boolean, status:number, data?:any, error?:string}>}
 */
export async function callBroker(method, path, body = null, opts = {}) {
  const url = `${BROKER_URL}${path}`;
  const headers = { 'Content-Type': 'application/json' };
  if (opts.admin) {
    if (!BOT_ADMIN_TOKEN) {
      return { ok: false, status: 0, error: 'BOT_INTERNAL_ADMIN_TOKEN not configured' };
    }
    headers['X-Internal-Bot-Admin-Token'] = BOT_ADMIN_TOKEN;
  } else {
    headers['X-Internal-Bot-Token'] = BOT_TOKEN;
  }
  // 網路錯誤(broker recreate 換 IP / 暫時不可達)重試 3 次:第一次可能撞到死連線、
  // 重試會重開新連線(短 keep-alive + 重解析 DNS)連到新 broker IP,免手動 restart。
  let lastErr;
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      const resp = await fetch(url, {
        method,
        headers,
        body: body ? JSON.stringify(body) : undefined,
        signal: AbortSignal.timeout(30_000),
        ...(brokerDispatcher ? { dispatcher: brokerDispatcher } : {}),
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
      lastErr = e;  // 網路類錯誤才會到這(HTTP 4xx/5xx 上面已 return)
      if (attempt < 2) await new Promise(r => setTimeout(r, 800 * (attempt + 1)));
    }
  }
  return { ok: false, status: 0, error: `network: ${lastErr?.message || 'unknown'} (after 3 tries)` };
}
