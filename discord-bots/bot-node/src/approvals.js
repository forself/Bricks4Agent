// Mobile approval workflow: poll broker pending approvals, post Discord
// message with [核准並執行] / [拒絕] buttons, handle button interactions.
//
// 為什麼不靠 broker 的 webhook 通知：webhook 訊息按下按鈕會走 Discord App
// interaction endpoint、要 broker 開公網 HTTPS 並做簽章驗證。bot-node 已經
// 透過 discord.js 開 gateway 連線、用它送訊息收 interaction、不需要任何公網。
//
// 流程：
//   1. 每 N 秒 GET /api/v1/admin/approvals?status=pending（admin token）
//   2. 對沒推過的 approval_id、送訊息到 APPROVAL_CHANNEL_ID（含兩顆按鈕）
//   3. interactionCreate event 到 → 驗證點按鈕的人是 privileged_user
//   4. 是 → call /admin/approvals/{id}/approve-and-dispatch 或 /reject
//      不是 → ephemeral reply「不是你能審的」

import { EmbedBuilder, ActionRowBuilder, ButtonBuilder, ButtonStyle } from 'discord.js';
import { callBroker } from './broker.js';
import { isPrivilegedUser } from './access.js';

const APPROVAL_CHANNEL_ID = process.env.APPROVAL_CHANNEL_ID || '';
const POLL_INTERVAL_MS = Math.max(10_000, parseInt(process.env.APPROVAL_POLL_INTERVAL_MS || '30000', 10));

const BTN_APPROVE = 'b4a:apr:approve';   // custom_id prefix
const BTN_REJECT  = 'b4a:apr:reject';

/** 已推過 message 的 approval_id → discord message id（重啟會清空、broker 端有自己的 _seenPendingIds 防 spam） */
const seen = new Map();

export function startApprovalPoller(client) {
  if (!APPROVAL_CHANNEL_ID) {
    console.warn('[approvals] APPROVAL_CHANNEL_ID not set, mobile approval disabled');
    return;
  }
  console.log(`[approvals] poller started, interval=${POLL_INTERVAL_MS}ms, channel=${APPROVAL_CHANNEL_ID}`);

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
      const msg = await sendApprovalMessage(client, apr);
      if (msg) seen.set(apr.approval_id, msg.id);
    } catch (e) {
      console.error(`[approvals] send msg failed apr=${apr.approval_id}:`, e.message);
      seen.delete(apr.approval_id);  // rollback、下輪重試
    }
  }
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

  if (action === 'approve') {
    const r = await callBroker('POST', `/api/v1/admin/approvals/${approvalId}/approve-and-dispatch`, {
      reason: `Approved via Discord button by <@${userId}>`,
    }, { admin: true });
    if (!r.ok) {
      await interaction.editReply(`❌ 核准失敗：${r.error}`);
      return true;
    }
    const dispatched = r.data?.dispatch_success;
    const dispatchErr = r.data?.dispatch_error;
    const traceId = r.data?.trace_id;
    await disableButtons(interaction.message,
      dispatched ? `✅ 已核准並執行 by <@${userId}>` : `⚠ 已核准但派發失敗：${dispatchErr || '?'}`);
    await interaction.editReply(
      dispatched
        ? `✅ approved + dispatched · trace=\`${traceId?.slice(0, 16) || '?'}\``
        : `⚠ approved 但派發失敗：${dispatchErr || '?'}\ntrace=\`${traceId?.slice(0, 16) || '?'}\``);
    return true;
  }

  if (action === 'reject') {
    const r = await callBroker('POST', `/api/v1/admin/approvals/${approvalId}/reject`, {
      reason: `Rejected via Discord button by <@${userId}>`,
    }, { admin: true });
    if (!r.ok) {
      await interaction.editReply(`❌ 拒絕失敗：${r.error}`);
      return true;
    }
    await disableButtons(interaction.message, `🚫 已拒絕 by <@${userId}>`);
    await interaction.editReply(`🚫 rejected.`);
    return true;
  }

  await interaction.editReply(`unknown action: ${action}`);
  return true;
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
