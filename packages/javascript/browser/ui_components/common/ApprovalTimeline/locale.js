/**
 * ApprovalTimeline 語系字串（自我註冊；ApprovalTimeline.js 以副作用方式匯入）
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'approvalTimeline', {
    label: '審核歷程',
    empty: '尚無審核紀錄',
    attachments: '附件',
    newTab: '（在新分頁開啟）',
    status: {
        pending: '待審核',
        current: '審核中',
        approved: '已核准',
        rejected: '已駁回',
        returned: '已退回',
        skipped: '已略過',
        cancelled: '已取消'
    }
});

Locale.register('en', 'approvalTimeline', {
    label: 'Approval history',
    empty: 'No review steps yet',
    attachments: 'Attachments',
    newTab: '(opens in a new tab)',
    status: {
        pending: 'Pending',
        current: 'In review',
        approved: 'Approved',
        rejected: 'Rejected',
        returned: 'Returned',
        skipped: 'Skipped',
        cancelled: 'Cancelled'
    }
});
