// B4A Discord Bot — Node.js implementation
//
// Phase 1: echo bot
//   - Discord.js client 連 Gateway
//   - 收到 @mention 或 DM、access.json 過濾後回覆 "echo: <text>"
//   - 不接 LLM、純為了確認 sandbox + 連線 + 權限過濾 OK
//
// Phase 2 之後加：claude --print 接 LLM、tool calling、broker 派發等。

import { Client, GatewayIntentBits, Events, Partials } from 'discord.js';
import { loadAccess, isAllowed } from './access.js';
import path from 'node:path';

const TOKEN = process.env.DISCORD_BOT_TOKEN;
const ACCESS_PATH = process.env.ACCESS_JSON_PATH || '/app/access.json';
const PHASE = process.env.BOT_PHASE || '1';

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
  // DM 訊息預設不會被 cache、要明確 partial 才收得到
  partials: [Partials.Channel, Partials.Message],
});

client.once(Events.ClientReady, (c) => {
  console.log(`[bot] logged in as ${c.user.tag} (id=${c.user.id})`);
  console.log(`[bot] in ${c.guilds.cache.size} guilds`);
});

client.on(Events.MessageCreate, async (msg) => {
  // 不回應自己 / 其他 bot
  if (msg.author.bot) return;

  // 公開頻道訊息：必須 @mention 才回；DM 訊息：直接回
  const isMention = msg.mentions.users.has(client.user.id);
  const isDm = !msg.guildId;
  if (!isMention && !isDm) return;

  // access.json 過濾
  if (!isAllowed(msg)) {
    console.log(`[access] rejected msg from user=${msg.author.id} channel=${msg.channelId} guild=${msg.guildId}`);
    return;
  }

  // 取乾淨內容（去掉 mention prefix）
  let content = msg.content || '';
  content = content.replace(/<@!?\d+>/g, '').trim();
  if (!content) {
    await msg.reply('(empty message — phase 1 echo bot 不會處理空訊息)');
    return;
  }

  console.log(`[msg] from ${msg.author.username} (${msg.author.id}): ${content.slice(0, 100)}`);

  // Phase 1：echo
  try {
    await msg.reply(`echo (phase ${PHASE}): ${content}`);
  } catch (e) {
    console.error('[reply] failed:', e.message);
  }
});

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
