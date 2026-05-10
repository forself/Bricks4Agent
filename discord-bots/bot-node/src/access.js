// Access control — 讀 access.json 決定要不要回應某個 Discord user / channel。
// 預設 deny：access.json 不存在或格式錯都當「不回應」處理、避免新部署時 bot 對所有人開放。
//
// 兩層權限：
//   1. 頻道層（isAllowed）—— 訊息進不進來
//      - DM：user 必須在 allowed_user_ids（陌生人密 bot 直接擋）
//      - 群組：頻道在 allowed_channel_ids 或 guild 在 allowed_guild_ids → 任何成員可用
//   2. 工具層（isPrivileged）—— 訊息進來後、敏感工具還要再卡
//      - 預設敏感前綴 = trading.* + audit.*（下單 / 倉位 / 內部拓樸）
//      - 只有 privileged_user_ids 內的人能 trigger 這些工具
//      - 沒設 privileged_user_ids → fallback allowed_user_ids（向下相容）

import { readFileSync, existsSync } from 'node:fs';

let cached = null;
let cachedPath = null;

const DEFAULT_PRIVILEGED_PREFIXES = ['trading.', 'audit.'];

export function loadAccess(path) {
  if (!existsSync(path)) {
    console.warn(`[access] access.json not found at ${path}; bot will deny all messages`);
    cached = emptyAccess();
    cachedPath = path;
    return cached;
  }
  try {
    const raw = readFileSync(path, 'utf-8');
    const parsed = JSON.parse(raw);
    const allowedUsers = Array.isArray(parsed.allowed_user_ids) ? parsed.allowed_user_ids : [];
    cached = {
      allowed_user_ids:           allowedUsers,
      allowed_channel_ids:        Array.isArray(parsed.allowed_channel_ids) ? parsed.allowed_channel_ids : [],
      allowed_guild_ids:          Array.isArray(parsed.allowed_guild_ids)   ? parsed.allowed_guild_ids   : [],
      // privileged_user_ids 沒設 → fallback allowed_user_ids（單人部署的舊行為）
      privileged_user_ids:        Array.isArray(parsed.privileged_user_ids) ? parsed.privileged_user_ids : allowedUsers,
      privileged_tool_prefixes:   Array.isArray(parsed.privileged_tool_prefixes) ? parsed.privileged_tool_prefixes : DEFAULT_PRIVILEGED_PREFIXES,
    };
    cachedPath = path;
    console.log(`[access] loaded from ${path}: ${cached.allowed_user_ids.length} users, ${cached.allowed_channel_ids.length} channels, ${cached.allowed_guild_ids.length} guilds, ${cached.privileged_user_ids.length} privileged, prefixes=[${cached.privileged_tool_prefixes.join(',')}]`);
    return cached;
  } catch (e) {
    console.error(`[access] parse error: ${e.message}`);
    cached = emptyAccess();
    cachedPath = path;
    return cached;
  }
}

function emptyAccess() {
  return {
    allowed_user_ids: [], allowed_channel_ids: [], allowed_guild_ids: [],
    privileged_user_ids: [], privileged_tool_prefixes: DEFAULT_PRIVILEGED_PREFIXES,
  };
}

/**
 * 頻道層：訊息能不能被 bot 看見。
 *   - DM：user_id 必須在 allowed_user_ids
 *   - 群組：channel_id 在 allowed_channel_ids 或 guild_id 在 allowed_guild_ids
 *           即可（不再要求 user_id 也在 allowed_user_ids、頻道內所有人能用）
 */
export function isAllowed(message) {
  if (!cached) throw new Error('access.json not loaded; call loadAccess() first');
  const userId    = message.author?.id;
  const channelId = message.channelId;
  const guildId   = message.guildId;

  if (!userId) return false;

  if (!guildId) {
    // DM：嚴格、必須在 allowed_user_ids
    return cached.allowed_user_ids.includes(userId);
  }

  // 群組：頻道或 guild 過了就放行（不再卡 user_id）
  if (cached.allowed_channel_ids.includes(channelId)) return true;
  if (cached.allowed_guild_ids.includes(guildId)) return true;
  return false;
}

/**
 * 工具層：這個 user 能不能 call 敏感工具（trading.* / audit.* 等）。
 * 唯讀工具（quote.* / strategy.* / health.*）任何頻道內成員都能用、本檢查不影響。
 */
export function isPrivilegedUser(userId) {
  if (!cached) throw new Error('access.json not loaded; call loadAccess() first');
  return !!userId && cached.privileged_user_ids.includes(userId);
}

/**
 * 這個 tool 名稱算不算敏感（要 privileged user 才能 call）。
 */
export function isPrivilegedTool(toolName) {
  if (!cached) throw new Error('access.json not loaded; call loadAccess() first');
  if (!toolName) return false;
  return cached.privileged_tool_prefixes.some(p => toolName.startsWith(p));
}
