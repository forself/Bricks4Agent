// Access control — 讀 access.json 決定要不要回應某個 Discord user / channel。
// 預設 deny：access.json 不存在或格式錯都當「不回應」處理，避免新部署時 bot 對所有人開放。

import { readFileSync, existsSync } from 'node:fs';

let cached = null;
let cachedPath = null;

export function loadAccess(path) {
  if (!existsSync(path)) {
    console.warn(`[access] access.json not found at ${path}; bot will deny all messages`);
    cached = { allowed_user_ids: [], allowed_channel_ids: [], allowed_guild_ids: [] };
    cachedPath = path;
    return cached;
  }
  try {
    const raw = readFileSync(path, 'utf-8');
    const parsed = JSON.parse(raw);
    cached = {
      allowed_user_ids:    Array.isArray(parsed.allowed_user_ids)    ? parsed.allowed_user_ids    : [],
      allowed_channel_ids: Array.isArray(parsed.allowed_channel_ids) ? parsed.allowed_channel_ids : [],
      allowed_guild_ids:   Array.isArray(parsed.allowed_guild_ids)   ? parsed.allowed_guild_ids   : [],
    };
    cachedPath = path;
    console.log(`[access] loaded from ${path}: ${cached.allowed_user_ids.length} users, ${cached.allowed_channel_ids.length} channels, ${cached.allowed_guild_ids.length} guilds`);
    return cached;
  } catch (e) {
    console.error(`[access] parse error: ${e.message}`);
    cached = { allowed_user_ids: [], allowed_channel_ids: [], allowed_guild_ids: [] };
    cachedPath = path;
    return cached;
  }
}

/**
 * 訊息是否可以被處理。預設 deny。
 * 通過條件（任一）：
 *   - DM 且 user_id 在 allowed_user_ids
 *   - guild 訊息且 (channel_id 在 allowed_channel_ids 或 guild_id 在 allowed_guild_ids)
 *     且 user_id 在 allowed_user_ids
 */
export function isAllowed(message) {
  if (!cached) throw new Error('access.json not loaded; call loadAccess() first');
  const userId    = message.author?.id;
  const channelId = message.channelId;
  const guildId   = message.guildId;

  if (!userId || !cached.allowed_user_ids.includes(userId)) return false;

  if (!guildId) {
    // DM
    return true;
  }

  if (cached.allowed_channel_ids.includes(channelId)) return true;
  if (cached.allowed_guild_ids.includes(guildId)) return true;
  return false;
}
