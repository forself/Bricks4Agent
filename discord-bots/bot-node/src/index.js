// B4A Discord Bot — Node.js implementation
//
// Phase 4: tool calling via text protocol + trading.order through approval gate
//   - LLM 在回應裡輸出 ```json {"call": "...", "args": {...}} ``` 形式的 tool_call
//   - bot 抓到 → dispatch broker capability → 結果包成 [tool_result] 餵下一輪
//   - max 5 輪 防無限 loop
//   - 純文字回應 = final answer、送 Discord
//   - trading.order 會回 "Pending admin approval, approval_id=X"、由 admin 手動 approve

import { Client, GatewayIntentBits, Events, Partials } from 'discord.js';
import { loadAccess, isAllowed, isPrivilegedUser, isPrivilegedTool } from './access.js';
import { callClaude } from './llm.js';
import { extractToolCall, dispatchTool } from './tools.js';
import { pushLlmReasoning } from './audit.js';
import { getHistory, pushTurn, clearHistory, stats as histStats } from './history.js';
import { startApprovalPoller, handleInteraction as handleApprovalInteraction } from './approvals.js';
import { startLineWebhookServer } from './line-webhook.js';
import { isStatusTrigger, buildStatusSnapshot } from './status.js';

const TOKEN = process.env.DISCORD_BOT_TOKEN;
const ACCESS_PATH = process.env.ACCESS_JSON_PATH || '/app/access.json';
const PHASE = process.env.BOT_PHASE || '4';
const MAX_TOOL_TURNS = 5;

if (!TOKEN) {
  console.error('[fatal] DISCORD_BOT_TOKEN env var not set');
  process.exit(1);
}

console.log(`[bot] B4A bot-node starting, phase=${PHASE}`);
loadAccess(ACCESS_PATH);

// LINE webhook server 在 Discord client 之外獨立跑、跟 Discord client 共用 LLM/tools/access
// 沒設 LINE_CHANNEL_SECRET 會 graceful skip、不影響 Discord 那邊
startLineWebhookServer();

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
    GatewayIntentBits.DirectMessages,
  ],
  partials: [Partials.Channel, Partials.Message],
});

client.once(Events.ClientReady, (c) => {
  console.log(`[bot] logged in as ${c.user.tag} (id=${c.user.id})`);
  console.log(`[bot] in ${c.guilds.cache.size} guilds`);
  // 開 approval polling — pending approval 推 Discord 按鈕、手機可審
  startApprovalPoller(client);
});

client.on(Events.InteractionCreate, async (interaction) => {
  try {
    await handleApprovalInteraction(interaction);
  } catch (e) {
    console.error('[interaction] handler error:', e);
  }
});

const inFlight = new Set();

client.on(Events.MessageCreate, async (msg) => {
  if (msg.author.bot) return;

  const isMention = msg.mentions.users.has(client.user.id);
  const isDm = !msg.guildId;
  if (!isMention && !isDm) return;

  if (!isAllowed(msg)) {
    console.log(`[access] rejected msg from user=${msg.author.id} channel=${msg.channelId}`);
    return;
  }

  let content = msg.content || '';
  content = content.replace(/<@!?\d+>/g, '').trim();
  if (!content) {
    await msg.reply('(空訊息)').catch(() => {});
    return;
  }

  if (content === '/reset' || content === '/clear') {
    clearHistory(msg.author.id, msg.channelId);
    await msg.reply('✓ 對話歷史已清空');
    return;
  }
  if (content === '/stats') {
    const s = histStats();
    await msg.reply(`📊 sessions=${s.sessions} total_turns=${s.total_turns} phase=${PHASE}`);
    return;
  }
  // 快捷狀態指令：跟 dashboard KPI bar 同一組數字、不走 LLM
  if (isStatusTrigger(content)) {
    try {
      const snap = await buildStatusSnapshot();
      // Discord 用 ``` 包成 code block、固定寬字、好讀
      await msg.reply('```\n' + snap + '\n```');
    } catch (e) {
      await msg.reply(`⚠ 狀態查詢失敗：${e.message}`).catch(() => {});
    }
    return;
  }

  const lockKey = `${msg.author.id}:${msg.channelId}`;
  if (inFlight.has(lockKey)) {
    await msg.reply('（前一條訊息還在處理、稍候）').catch(() => {});
    return;
  }
  inFlight.add(lockKey);

  console.log(`[msg] from ${msg.author.username} (${msg.author.id}): ${content.slice(0, 100)}`);

  let typingTimer = null;
  try {
    if (msg.channel.sendTyping) {
      await msg.channel.sendTyping().catch(() => {});
      typingTimer = setInterval(() => msg.channel.sendTyping().catch(() => {}), 8000);
    }

    const finalText = await runMultiTurn(msg.author.id, msg.channelId, content);
    if (finalText == null) return;  // 已 reply 錯誤訊息
    pushTurn(msg.author.id, msg.channelId, content, finalText);
    await sendChunked(msg, finalText);
  } catch (e) {
    console.error('[handler] error:', e);
    await msg.reply(`⚠ 內部錯誤：${e.message}`).catch(() => {});
  } finally {
    if (typingTimer) clearInterval(typingTimer);
    inFlight.delete(lockKey);
  }

  // ── multi-turn 主邏輯 ──
  // 失敗時用 msg.reply、回 null 表示「已自己 reply 過、外層不要再送」
  async function runMultiTurn(userId, channelId, userMsg) {
    const messages = [
      ...getHistory(userId, channelId),
      { role: 'user', content: userMsg },
    ];

    for (let turn = 0; turn < MAX_TOOL_TURNS; turn++) {
      const startMs = Date.now();
      const result = await callClaude(messages);
      const dur = Date.now() - startMs;
      console.log(`[llm] turn=${turn} ${result.ok ? 'ok' : 'fail'} ${dur}ms`);

      if (!result.ok) {
        await msg.reply(`⚠ LLM 失敗：${result.error.slice(0, 300)}`).catch(() => {});
        return null;
      }

      const toolCall = extractToolCall(result.text);
      if (!toolCall) {
        // 沒 tool_call → 純文字回答、結束 multi-turn
        return result.text;
      }

      console.log(`[tool] turn=${turn} call=${toolCall.call} args=${JSON.stringify(toolCall.args).slice(0, 100)}`);
      messages.push({ role: 'assistant', content: result.text });

      const caller = {
        userId,
        isPrivileged: isPrivilegedUser(userId),
        privilegedTool: isPrivilegedTool(toolCall.call),
      };
      if (caller.privilegedTool && !caller.isPrivileged) {
        console.log(`[acl] tool=${toolCall.call} blocked for non-privileged user=${userId}`);
      }
      const toolResult = await dispatchTool(toolCall, caller);
      const statusLabel = toolResult.status || (toolResult.ok ? 'success' : 'failed');
      console.log(`[tool] turn=${turn} ${statusLabel} ${toolResult.error || ''}`);

      // W13: dispatch 完才推 audit、帶真實結果（pending_approval / success / failed / denied）
      // 順序：dispatch 結果是 source of truth、audit 在後面比較貼近真實。
      // 失敗 audit 不會中斷主流程（audit.js 內部 catch、只 log warning）。
      await pushLlmReasoning({
        source: 'discord',
        userId,
        channelId: msg.channel?.id || '',
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

    // 跑滿 MAX_TOOL_TURNS 還沒給最終答案——強制收尾
    return `（已嘗試 ${MAX_TOOL_TURNS} 輪 tool 仍無最終答案、可能訊息過於複雜、請拆成幾條問。）`;
  }
});

async function sendChunked(msg, text) {
  const MAX = 1900;
  if (text.length <= MAX) {
    await msg.reply(text);
    return;
  }
  let first = true;
  for (let i = 0; i < text.length; i += MAX) {
    const chunk = text.slice(i, i + MAX);
    if (first) { await msg.reply(chunk); first = false; }
    else       { await msg.channel.send(chunk).catch(() => {}); }
  }
}

client.on(Events.Error, (e) => console.error('[client error]', e));
client.on(Events.Warn, (w) => console.warn('[client warn]', w));

process.on('SIGTERM', () => {
  console.log('[bot] SIGTERM received, shutting down');
  client.destroy().then(() => process.exit(0));
});

client.login(TOKEN).catch((e) => {
  console.error('[fatal] login failed:', e.message);
  process.exit(1);
});
