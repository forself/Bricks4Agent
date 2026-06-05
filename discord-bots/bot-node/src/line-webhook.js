// LINE Messaging API webhook handler.
//
// 跟 Discord 的 gateway 連線（bot.connect）不同、LINE 是 push-based：
//   LINE Platform → POST our-public-https-url/webhook/line/ → 這個 server
//
// 公網 HTTPS 部分由 cloudflared / nginx-with-letsencrypt 在容器外處理、
// 本檔只處理收到 plain HTTP 後的事：
//   1. X-Line-Signature 驗證（HMAC-SHA256 over raw body using LINE_CHANNEL_SECRET）
//   2. parse events、抽 text 訊息
//   3. 過 access.json 兩層 ACL（line_allowed_user_ids → line_privileged_user_ids）
//   4. 走跟 Discord 同一套 multi-turn LLM + tool dispatch（共用 llm.js / tools.js）
//   5. final reply 透過 broker /api/v1/notifications/line/send 推回 LINE
//
// 為什麼不用 Express：避免加 npm dep；node 內建 http 已夠用、container 也 slim。

import { createServer } from 'node:http';
import crypto from 'node:crypto';
import { isLineAllowed, isLinePrivilegedUser, isPrivilegedTool } from './access.js';
import { callBroker } from './broker.js';
import { callClaude } from './llm.js';
import { extractToolCall, dispatchTool } from './tools.js';
import { pushLlmReasoning } from './audit.js';
import { getHistory, pushTurn } from './history.js';
import { handleLinePostback } from './approvals.js';
import { isStatusTrigger, buildStatusSnapshot } from './status.js';

const PORT = parseInt(process.env.LINE_WEBHOOK_PORT || '5358', 10);
const CHANNEL_SECRET = process.env.LINE_CHANNEL_SECRET || '';
const MAX_TOOL_TURNS = 5;

const inFlight = new Set();   // dedup per-user concurrent locks

export function startLineWebhookServer() {
  if (!CHANNEL_SECRET) {
    console.warn('[line] LINE_CHANNEL_SECRET not set, LINE webhook disabled');
    return null;
  }

  const server = createServer((req, res) => {
    handleRequest(req, res).catch((e) => {
      console.error('[line] handler crash:', e);
      try { res.statusCode = 500; res.end(); } catch {}
    });
  });

  // listen on 0.0.0.0 inside container; compose maps host port → container port
  server.listen(PORT, '0.0.0.0', () => {
    console.log(`[line] webhook listening on :${PORT}/webhook/line/`);
  });
  return server;
}

async function handleRequest(req, res) {
  // 健康檢查 / LINE verify 工具會打 GET、回 200 即可
  if (req.method === 'GET') {
    res.statusCode = 200;
    res.end('ok');
    return;
  }
  if (req.method !== 'POST' || !req.url?.startsWith('/webhook/line')) {
    res.statusCode = 404;
    res.end();
    return;
  }

  const body = await readBody(req);
  const sig = req.headers['x-line-signature'];
  if (!verifySignature(body, sig)) {
    console.warn('[line] invalid signature');
    res.statusCode = 403;
    res.end();
    return;
  }

  // ACK 立刻回 200——LINE 會在 1 秒內 timeout 重送、後續處理走非同步
  res.statusCode = 200;
  res.end();

  let parsed;
  try { parsed = JSON.parse(body || '{}'); }
  catch { console.warn('[line] non-JSON body'); return; }

  const events = Array.isArray(parsed.events) ? parsed.events : [];
  for (const evt of events) {
    processEvent(evt).catch((e) => console.error('[line] event error:', e));
  }
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', (c) => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf-8')));
    req.on('error', reject);
  });
}

function verifySignature(body, signature) {
  if (!signature || typeof signature !== 'string') return false;
  const expected = crypto.createHmac('sha256', CHANNEL_SECRET).update(body).digest('base64');
  try {
    const a = Buffer.from(signature);
    const b = Buffer.from(expected);
    if (a.length !== b.length) return false;
    return crypto.timingSafeEqual(a, b);
  } catch { return false; }
}

async function processEvent(evt) {
  // postback：審核按鈕回來、不走 LLM、直接派 approve/reject
  if (evt.type === 'postback') {
    const userId = evt.source?.userId;
    const data = evt.postback?.data || '';
    if (!userId) return;
    if (!isLineAllowed(userId)) {
      console.log(`[line postback] rejected user=${userId.slice(0, 8)}…`);
      return;
    }
    const result = await handleLinePostback(userId, data);
    if (!result.handled) return;   // 不是我們的 prefix、忽略
    if (result.replyText) {
      await sendLine(userId, result.replyText).catch(() => {});
    }
    return;
  }

  if (evt.type !== 'message') return;          // follow / unfollow 暫不處理
  if (evt.message?.type !== 'text') return;    // audio/image/sticker 暫略

  const userId = evt.source?.userId;
  const text   = evt.message.text;
  if (!userId || !text) return;

  if (!isLineAllowed(userId)) {
    // 完整 userId（非截斷）：onboard 新人時要把這串加進 access.json line_allowed_user_ids。
    console.log(`[line] rejected user=${userId} (加白名單用完整 id)`);
    return;
  }

  const lockKey = `line:${userId}`;
  if (inFlight.has(lockKey)) {
    await sendLine(userId, '（前一條訊息還在處理、稍候）').catch(() => {});
    return;
  }
  // 快捷狀態指令：跟 dashboard KPI bar 同一組數字、不走 LLM（不佔 lock）
  if (isStatusTrigger(text)) {
    try {
      const snap = await buildStatusSnapshot();
      await sendLine(userId, snap);
    } catch (e) {
      await sendLine(userId, `⚠ 狀態查詢失敗：${e.message}`).catch(() => {});
    }
    return;
  }
  inFlight.add(lockKey);
  console.log(`[line] from ${userId.slice(0, 8)}…: ${text.slice(0, 100)}`);

  try {
    const finalText = await runMultiTurn(userId, text);
    if (finalText) {
      // history 用 line: prefix 跟 Discord 同 user 區隔（ID namespace 不同也不混）
      pushTurn(`line:${userId}`, 'dm', text, finalText);
      await sendLine(userId, finalText);
    }
  } catch (e) {
    console.error('[line] handler error:', e);
    await sendLine(userId, `⚠ 內部錯誤：${e.message}`).catch(() => {});
  } finally {
    inFlight.delete(lockKey);
  }
}

async function runMultiTurn(userId, userMsg) {
  const history = getHistory(`line:${userId}`, 'dm');
  const messages = [...history, { role: 'user', content: userMsg }];

  for (let turn = 0; turn < MAX_TOOL_TURNS; turn++) {
    const start = Date.now();
    const result = await callClaude(messages);
    const dur = Date.now() - start;
    console.log(`[line llm] turn=${turn} ${result.ok ? 'ok' : 'fail'} ${dur}ms`);

    if (!result.ok) return `⚠ LLM 失敗：${result.error.slice(0, 300)}`;

    const toolCall = extractToolCall(result.text);
    if (!toolCall) return result.text;

    messages.push({ role: 'assistant', content: result.text });

    const caller = {
      userId: `line:${userId}`,
      isPrivileged: isLinePrivilegedUser(userId),
      privilegedTool: isPrivilegedTool(toolCall.call),
    };
    if (caller.privilegedTool && !caller.isPrivileged) {
      console.log(`[line acl] tool=${toolCall.call} blocked for line user=${userId.slice(0, 8)}…`);
    }
    const toolResult = await dispatchTool(toolCall, caller);
    const statusLabel = toolResult.status || (toolResult.ok ? 'success' : 'failed');
    console.log(`[line tool] turn=${turn} call=${toolCall.call} ${statusLabel}`);

    // W13: dispatch 完才推 audit、帶真實結果（pending_approval / success / failed / denied）
    await pushLlmReasoning({
      source: 'line',
      userId: `line:${userId}`,
      channelId: 'dm',
      turn,
      llmReasoning: result.text,
      toolName: toolCall.call,
      toolArgs: toolCall.args,
      aclAllowed: !caller.privilegedTool || caller.isPrivileged,
      dispatchResult: statusLabel,
      errorBrief: toolResult.error || '',
    });
    messages.push({ role: 'tool', content: toolResult.summary });
  }

  return `（已嘗試 ${MAX_TOOL_TURNS} 輪 tool 仍無最終答案、訊息可能太複雜、請拆成幾條問。）`;
}

async function sendLine(to, text) {
  // LINE 單則上限 5000 字、broker 端的 SendMessageHandler 會再截一次保險
  const chunked = text.length > 4900 ? text.slice(0, 4900) + '\n…(truncated)' : text;
  const r = await callBroker('POST', '/api/v1/notifications/line/send', { to, text: chunked });
  if (!r.ok) console.error(`[line] send failed to ${to.slice(0, 8)}…: ${r.error}`);
  return r;
}
