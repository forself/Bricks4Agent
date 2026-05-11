// LINE Flex Message helpers — 建構審核用 Flex bubble、解析 postback data。
//
// 為什麼分離出來：
//   - 純函數方便加單元測試（不用真的 push 到 LINE）
//   - approvals.js 已經是 Discord-centric、不適合塞 LINE 結構
//
// Postback data 格式跟 Discord button 的 customId 故意一致：
//   `b4a:apr:approve:{approvalId}`
//   `b4a:apr:reject:{approvalId}`
// 這樣 reviewer 看 broker audit log 一眼能對到哪邊按的也用同個 prefix 篩。

const POSTBACK_PREFIX = 'b4a:apr:';

/**
 * 把一筆 pending approval 包成 LINE Flex bubble。
 * 推給 line_privileged_user_ids 列表上的 user，按按鈕走 postback 回 webhook。
 *
 * @param {object} apr broker /api/v1/admin/approvals 回來的單筆 approval
 *                     {approval_id, capability_id, route, principal_id, role, payload, requested_at}
 * @returns {object} LINE message object（type=flex），可放進 messages 陣列
 */
export function buildApprovalFlex(apr) {
  const approvalId = apr?.approval_id || '?';
  const cap = apr?.capability_id || '?';
  const route = apr?.route || '—';
  const principal = apr?.principal_id || '?';
  const role = apr?.role || '—';

  // payload 截短到 LINE Flex text 安全長度（單一 text component 上限約 2000 字元）
  const payloadStr = truncate(String(apr?.payload || '{}'), 800);

  const altText = `[B4A] 待審：${cap} (${approvalId})`.slice(0, 400);

  return {
    type: 'flex',
    altText,
    contents: {
      type: 'bubble',
      header: {
        type: 'box',
        layout: 'vertical',
        backgroundColor: '#E6A23C',
        paddingAll: 'md',
        contents: [
          { type: 'text', text: '🔐 待審', color: '#FFFFFF', weight: 'bold', size: 'sm' },
          { type: 'text', text: cap, color: '#FFFFFF', weight: 'bold', size: 'lg', wrap: true },
        ],
      },
      body: {
        type: 'box',
        layout: 'vertical',
        spacing: 'sm',
        contents: [
          row('route',       route),
          row('principal',   `${principal} (${role})`),
          row('approval_id', approvalId),
          { type: 'separator', margin: 'md' },
          { type: 'text', text: 'payload', color: '#888888', size: 'xs', margin: 'md' },
          { type: 'text', text: payloadStr, size: 'xs', wrap: true, color: '#444444' },
        ],
      },
      footer: {
        type: 'box',
        layout: 'horizontal',
        spacing: 'sm',
        contents: [
          {
            type: 'button',
            style: 'primary',
            color: '#27AE60',
            height: 'sm',
            action: {
              type: 'postback',
              label: '核准並執行',
              data: `${POSTBACK_PREFIX}approve:${approvalId}`,
              displayText: `核准 ${approvalId}`,
            },
          },
          {
            type: 'button',
            style: 'primary',
            color: '#C0392B',
            height: 'sm',
            action: {
              type: 'postback',
              label: '拒絕',
              data: `${POSTBACK_PREFIX}reject:${approvalId}`,
              displayText: `拒絕 ${approvalId}`,
            },
          },
        ],
      },
    },
  };
}

/**
 * 解析 LINE postback `data` 字串成 (action, approvalId)。
 * 不是我們的 prefix 就回 null、由 caller 略過。
 *
 * @param {string} data
 * @returns {{action:'approve'|'reject', approvalId:string} | null}
 */
export function parseApprovalPostback(data) {
  if (typeof data !== 'string' || !data.startsWith(POSTBACK_PREFIX)) return null;
  const rest = data.slice(POSTBACK_PREFIX.length); // "approve:apr_xxx" 或 "reject:apr_xxx"
  const idx = rest.indexOf(':');
  if (idx < 0) return null;
  const action = rest.slice(0, idx);
  const approvalId = rest.slice(idx + 1);
  if ((action !== 'approve' && action !== 'reject') || !approvalId) return null;
  return { action, approvalId };
}

function row(label, value) {
  return {
    type: 'box',
    layout: 'baseline',
    spacing: 'sm',
    contents: [
      { type: 'text', text: label, color: '#888888', size: 'sm', flex: 2 },
      { type: 'text', text: String(value), wrap: true, color: '#222222', size: 'sm', flex: 5 },
    ],
  };
}

function truncate(s, n) {
  if (!s) return '';
  if (s.length <= n) return s;
  return s.slice(0, n) + ` ... (truncated, ${s.length} chars)`;
}
