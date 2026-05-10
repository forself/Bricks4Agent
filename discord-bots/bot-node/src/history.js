// Per-user/channel 對話歷史——in-memory only（容器重啟丟、acceptable for phase 2）。
//
// 預期使用模式：每個 (user_id, channel_id) 各自一條 ring buffer、最多 6 turn（user/assistant
// 各 3）。超過 6 turn 砍最舊的、保留最近脈絡。
//
// 不持久化 to disk：sandbox container 是 read_only rootfs + ephemeral tmpfs、寫進去重啟就丟。
// Phase 4 之後若需要長期 memory、再想要不要存 broker DB。

const MAX_TURNS = 6;
const map = new Map();   // key = `${userId}:${channelId}` → Array<{role, content}>

function keyOf(userId, channelId) {
  return `${userId}:${channelId || 'dm'}`;
}

export function getHistory(userId, channelId) {
  return map.get(keyOf(userId, channelId)) ?? [];
}

export function pushTurn(userId, channelId, userMsg, assistantMsg) {
  const key = keyOf(userId, channelId);
  const arr = map.get(key) ?? [];
  arr.push({ role: 'user',      content: userMsg });
  arr.push({ role: 'assistant', content: assistantMsg });
  // 砍最舊、保留 MAX_TURNS（user+assistant 各算一個）
  while (arr.length > MAX_TURNS * 2) arr.shift();
  map.set(key, arr);
}

export function clearHistory(userId, channelId) {
  map.delete(keyOf(userId, channelId));
}

export function stats() {
  return {
    sessions: map.size,
    total_turns: Array.from(map.values()).reduce((s, a) => s + a.length, 0),
  };
}
