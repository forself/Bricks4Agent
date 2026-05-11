// Mobile approval workflow: poll broker pending approvals, post Discord
// message with [核准並執行] / [拒絕] buttons, handle button interactions.
// LINE 平行推 Flex Message bubble 含相同兩顆 postback 按鈕、共用同套執行路徑。
//
// 為什麼不靠 broker 的 webhook 通知：webhook 訊息按下按鈕會走 Discord App
// interaction endpoint、要 broker 開公網 HTTPS 並做簽章驗證。bot-node 已經
// 透過 discord.js 開 gateway 連線、用它送訊息收 interaction、不需要任何公網。
// LINE postback 走的是同一支 line-webhook（已有公網 + HMAC 驗章）、不另開洞。
//
// 流程：
//   1. 每 N 秒 GET /api/v1/admin/approvals?status=pending（admin token）
//   2. 對沒推過的 approval_id：
//      - 送訊息到 APPROVAL_CHANNEL_ID（Discord embed + 兩顆 button）
//      - 推 Flex bubble 給 LINE_APPROVAL_USER_IDS 列表上每個 user（含同樣兩顆 postback）
//   3. interactionCreate event 到 → 驗證點按鈕的人是 privileged_user → executeApproval
//   4. line-webhook postback → handleLinePostback → 同個 executeApproval

import { EmbedBuilder, ActionRowBuilder, ButtonBuilder, ButtonStyle } from 'discord.js';
import { callBroker } from './broker.js';
import { isPrivilegedUser, isLinePrivilegedUser } from './access.js';
import { buildApprovalFlex, parseApprovalPostback } from './line_flex.js';

const APPROVAL_CHANNEL_ID = process.env.APPROVAL_CHANNEL_ID || '';
const LINE_APPROVAL_USER_IDS = (process.env.LINE_APPROVAL_USER_IDS || '')
  .split(',').map((s) => s.trim()).filter(Boolean);
const POLL_INTERVAL_MS = Math.max(10_000, parseInt(process.env.APPROVAL_POLL_INTERVAL_MS || '30000', 10));

const BTN_APPROVE = 'b4a:apr:approve';   // custom_id prefix
const BTN_REJECT  = 'b4a:apr:reject';

/** 已推過 message 的 approval_id → discord message id（重啟會清空、broker 端有自己的 _seenPendingIds 防 spam） */
const seen = new Map();

export function startApprovalPoller(client) {
  const hasDiscord = !!APPROVAL_CHANNEL_ID;
  const hasLine = LINE_APPROVAL_USER_IDS.length > 0;
  if (!hasDiscord && !hasLine) {
    console.warn('[approvals] neither APPROVAL_CHANNEL_ID nor LINE_APPROVAL_USER_IDS set, mobile approval disabled');
    return;
  }
  console.log(`[approvals] poller started interval=${POLL_INTERVAL_MS}ms discord=${hasDiscord ? APPROVAL_CHANNEL_ID : 'off'} line=${hasLine ? LINE_APPROVAL_USER_IDS.length + ' user(s)' : 'off'}`);

  // 啟動先吸收一次「現在已 pending 的」、避免重啟把舊的全推一遍
  pollOnce(client, /* skipMessage */ true).catch((e) =>
    console.error('[approvals] initial poll error:', e.message));

  setInterval(() => {
    pollOnce(client).catch((e) => console.error('[approvals] poll error:', e.message));
  }, POLL_INTERVAL_MS);
}

async function pollOnce(client, skipMessage = false) {
  const r = await callBroker('GET', '/api/v1/admin/approvals?status=pending&limit=50', null, { admin: true });
  if (!r.ok) {
    // broker 沒設 admin token / 還沒起來 → 安靜失敗、下個週期再試
    if (!seen._warnedOnce) {
      console.warn(`[approvals] poll failed: ${r.error}`);
      seen._warnedOnce = true;
    }
    return;
  }
  seen._warnedOnce = false;

  const list = Array.isArray(r.data) ? r.data : [];
  for (const apr of list) {
    if (seen.has(apr.approval_id)) continue;
    seen.set(apr.approval_id, null);  // 先 mark 避免並發 race
    if (skipMessage) continue;        // 啟動吸收期、不送訊息

    try {
      // 雙推：Discord embed + LINE Flex（任一 channel 失敗不影響另一邊）
      const tasks = [];
      if (APPROVAL_CHANNEL_ID && client) {
        tasks.push(sendApprovalMessage(client, apr).then((msg) => {
          if (msg) seen.set(apr.approval_id, msg.id);
        }).catch((e) => console.error(`[approvals] discord send failed apr=${apr.approval_id}: ${e.message}`)));
      }
      if (LINE_APPROVAL_USER_IDS.length > 0) {
        tasks.push(pushLineApproval(apr)
          .catch((e) => console.error(`[approvals] line push failed apr=${apr.approval_id}: ${e.message}`)));
      }
      await Promise.all(tasks);
    } catch (e) {
      console.error(`[approvals] notify failed apr=${apr.approval_id}:`, e.message);
      seen.delete(apr.approval_id);  // rollback、下輪重試
    }
  }
}

async function pushLineApproval(apr) {
  const flex = buildApprovalFlex(apr);
  // LINE Push API 一次送給一個 user、多個 recipient 各推一發
  const sends = LINE_APPROVAL_USER_IDS.map(async (to) => {
    const r = await callBroker('POST', '/api/v1/notifications/line/send', {
      to,
      messages: [flex],
    });
    if (!r.ok) {
      console.error(`[approvals] line flex send to ${to.slice(0, 8)}… failed: ${r.error}`);
    }
  });
  await Promise.all(sends);
}

async function sendApprovalMessage(client, apr) {
  const channel = await client.channels.fetch(APPROVAL_CHANNEL_ID).catch(() => null);
  if (!channel || !channel.isTextBased?.()) {
    console.error(`[approvals] channel ${APPROVAL_CHANNEL_ID} not found or not text-based`);
    return null;
  }

  const payloadPretty = truncate(apr.payload || '{}', 500);
  const embed = new EmbedBuilder()
    .setColor(0xE6A23C)
    .setTitle(`🔐 待審：${apr.capability_id}`)
    .setDescription([
      `**route**: \`${apr.route || '—'}\``,
      `**principal**: \`${apr.principal_id}\` (${apr.role || '—'})`,
      `**approval_id**: \`${apr.approval_id}\``,
    ].join('\n'))
    .addFields({ name: 'payload', value: '```json\n' + payloadPretty + '\n```' })
    .setTimestamp(apr.requested_at ? new Date(apr.requested_at) : new Date());

  const row = new ActionRowBuilder().addComponents(
    new ButtonBuilder()
      .setCustomId(`${BTN_APPROVE}:${apr.approval_id}`)
      .setLabel('核准並執行')
      .setStyle(ButtonStyle.Success),
    new ButtonBuilder()
      .setCustomId(`${BTN_REJECT}:${apr.approval_id}`)
      .setLabel('拒絕')
      .setStyle(ButtonStyle.Danger),
  );

  return await channel.send({ embeds: [embed], components: [row] });
}

/**
 * 對 broker 發 approve-and-dispatch 或 reject。Discord 跟 LINE handler 共用。
 *
 * @param {'approve'|'reject'} action
 * @param {string} approvalId
 * @param {string} byDisplay 用來填 broker reason 欄位、給 audit log 看誰按的
 * @returns {Promise<{ok:boolean, dispatched?:boolean, traceId?:string, dispatchError?:string|null, error?:string}>}
 */
export async function executeApproval(action, approvalId, byDisplay) {
  if (action === 'approve') {
    const r = await callBroker('POST', `/api/v1/admin/approvals/${approvalId}/approve-and-dispatch`, {
      reason: `Approved by ${byDisplay}`,
    }, { admin: true });
    if (!r.ok) return { ok: false, error: r.error };
    return {
      ok: true,
      dispatched: !!r.data?.dispatch_success,
      dispatchError: r.data?.dispatch_error || null,
      traceId: r.data?.trace_id || null,
    };
  }
  if (action === 'reject') {
    const r = await callBroker('POST', `/api/v1/admin/approvals/${approvalId}/reject`, {
      reason: `Rejected by ${byDisplay}`,
    }, { admin: true });
    if (!r.ok) return { ok: false, error: r.error };
    return { ok: true };
  }
  return { ok: false, error: `unknown action: ${action}` };
}

/**
 * Discord interactionCreate handler。在 index.js 由 client.on() 呼叫。
 */
export async function handleInteraction(interaction) {
  if (!interaction.isButton?.()) return false;
  const { customId } = interaction;
  if (!customId.startsWith('b4a:apr:')) return false;

  const [, , action, approvalId] = customId.split(':');
  if (!action || !approvalId) return true;  // 我們的 prefix 但格式怪、吃掉不傳出去

  const userId = interaction.user.id;
  if (!isPrivilegedUser(userId)) {
    await interaction.reply({
      content: '⚠ 只有平台帳戶持有者能審核此請求。',
      ephemeral: true,
    });
    return true;
  }

  await interaction.deferReply({ ephemeral: true });

  const r = await executeApproval(action, approvalId, `Discord button <@${userId}>`);
  if (!r.ok) {
    await interaction.editReply(`❌ ${action === 'approve' ? '核准' : '拒絕'}失敗：${r.error}`);
    return true;
  }

  if (action === 'approve') {
    const traceLabel = r.traceId?.slice(0, 16) || '?';
    await disableButtons(interaction.message,
      r.dispatched ? `✅ 已核准並執行 by <@${userId}>` : `⚠ 已核准但派發失敗：${r.dispatchError || '?'}`);
    await interaction.editReply(
      r.dispatched
        ? `✅ approved + dispatched · trace=\`${traceLabel}\``
        : `⚠ approved 但派發失敗：${r.dispatchError || '?'}\ntrace=\`${traceLabel}\``);
  } else {
    await disableButtons(interaction.message, `🚫 已拒絕 by <@${userId}>`);
    await interaction.editReply(`🚫 rejected.`);
  }
  return true;
}

/**
 * LINE postback handler。line-webhook.js 收到 postback event 時呼叫。
 *
 * @param {string} lineUserId
 * @param {string} postbackData
 * @returns {Promise<{handled:boolean, replyText?:string}>}
 *   handled=false：不是我們的 prefix、caller 應略過
 *   handled=true：caller 應把 replyText 推回 LINE user
 */
export async function handleLinePostback(lineUserId, postbackData) {
  const parsed = parseApprovalPostback(postbackData);
  if (!parsed) return { handled: false };

  if (!isLinePrivilegedUser(lineUserId)) {
    return { handled: true, replyText: '⚠ 只有平台帳戶持有者能審核此請求。' };
  }

  const r = await executeApproval(parsed.action, parsed.approvalId, `LINE postback ${lineUserId.slice(0, 8)}…`);
  if (!r.ok) {
    return { handled: true, replyText: `❌ ${parsed.action === 'approve' ? '核准' : '拒絕'}失敗：${r.error}` };
  }
  if (parsed.action === 'approve') {
    const traceLabel = r.traceId?.slice(0, 16) || '?';
    return {
      handled: true,
      replyText: r.dispatched
        ? `✅ 已核准並執行 (trace=${traceLabel})`
        : `⚠ 已核准但派發失敗：${r.dispatchError || '?'} (trace=${traceLabel})`,
    };
  }
  return { handled: true, replyText: '🚫 已拒絕。' };
}

async function disableButtons(message, footerText) {
  try {
    const disabledRow = ActionRowBuilder.from(message.components[0]);
    for (const c of disabledRow.components) c.setDisabled(true);
    const oldEmbed = message.embeds[0];
    const newEmbed = EmbedBuilder.from(oldEmbed).setFooter({ text: footerText });
    await message.edit({ embeds: [newEmbed], components: [disabledRow] });
  } catch (e) {
    console.warn('[approvals] disable buttons failed:', e.message);
  }
}

function truncate(s, n) {
  if (!s) return '';
  if (s.length <= n) return s;
  return s.slice(0, n) + ` ... (truncated, ${s.length} chars)`;
}
