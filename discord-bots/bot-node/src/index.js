// B4A Discord Bot — Node.js implementation
//
// Phase 2: claude --print headless integration
//   - Discord.js 收訊息、access.json 過濾
//   - 對話送 claude --print subprocess、拿 text 回應
//   - per-user 多輪歷史（in-memory）
//   - Discord typing indicator 蓋掉 cold start 延遲
//   - 純對話、無 tool calling（phase 3 才接 broker capability）

import { Client, GatewayIntentBits, Events, Partials } from 'discord.js';
import { loadAccess, isAllowed } from './access.js';
import { callClaude } from './llm.js';
import { getHistory, pushTurn, clearHistory, stats as histStats } from './history.js';

const TOKEN = process.env.DISCORD_BOT_TOKEN;
const ACCESS_PATH = process.env.ACCESS_JSON_PATH || '/app/access.json';
const PHASE = process.env.BOT_PHASE || '2';

if (!TOKEN) {
  console.error('[fatal] DISCORD_BOT_TOKEN env var not set');
  process.exit(1);
}

console.log(`[bot] B4A bot-node starting, phase=${PHASE}`);
loadAccess(ACCESS_PATH);

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
});

// 簡單 dispatch：每個 user × channel 同時間只跑一條 LLM 呼叫、避免 spam 同時送多條訊息撞炸
const inFlight = new Set();

client.on(Events.MessageCreate, async (msg) => {
  if (msg.author.bot) return;

  const isMention = msg.mentions.users.has(client.user.id);
  const isDm = !msg.guildId;
  if (!isMention && !isDm) return;

  if (!isAllowed(msg)) {
    console.log(`[access] rejected msg from user=${msg.author.id} channel=${msg.channelId} guild=${msg.guildId}`);
    return;
  }

  let content = msg.content || '';
  content = content.replace(/<@!?\d+>/g, '').trim();
  if (!content) {
    await msg.reply('(空訊息、phase 2 助理不會處理)').catch(() => {});
    return;
  }

  // 內建簡單指令、不送 LLM
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

  const lockKey = `${msg.author.id}:${msg.channelId}`;
  if (inFlight.has(lockKey)) {
    await msg.reply('（前一條訊息還在處理、稍候）').catch(() => {});
    return;
  }
  inFlight.add(lockKey);

  console.log(`[msg] from ${msg.author.username} (${msg.author.id}): ${content.slice(0, 100)}`);

  // 顯示 typing indicator 直到回應送出（Discord 自動 10s timeout、我們 LLM 跑超過再續一次）
  let typingTimer = null;
  try {
    if (msg.channel.sendTyping) {
      await msg.channel.sendTyping().catch(() => {});
      typingTimer = setInterval(() => msg.channel.sendTyping().catch(() => {}), 8000);
    }

    const history = getHistory(msg.author.id, msg.channelId);
    const startMs = Date.now();
    const result = await callClaude(content, history);
    const durationMs = Date.now() - startMs;
    console.log(`[llm] ${result.ok ? 'ok' : 'fail'} ${durationMs}ms hist=${history.length}`);

    if (!result.ok) {
      await msg.reply(`⚠ LLM 呼叫失敗：${result.error.slice(0, 300)}`).catch(() => {});
      return;
    }

    // Discord 單訊息上限 2000 字、超過要分段
    const text = result.text;
    pushTurn(msg.author.id, msg.channelId, content, text);
    await sendChunked(msg, text);
  } catch (e) {
    console.error('[handler] error:', e);
    await msg.reply(`⚠ 內部錯誤：${e.message}`).catch(() => {});
  } finally {
    if (typingTimer) clearInterval(typingTimer);
    inFlight.delete(lockKey);
  }
});

/** Discord 訊息上限 2000 字、超過分段送 */
async function sendChunked(msg, text) {
  const MAX = 1900;
  if (text.length <= MAX) {
    await msg.reply(text);
    return;
  }
  let first = true;
  for (let i = 0; i < text.length; i += MAX) {
    const chunk = text.slice(i, i + MAX);
    if (first) {
      await msg.reply(chunk);
      first = false;
    } else {
      await msg.channel.send(chunk).catch(() => {});
    }
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
