/**
 * IssueList 語系字串（自我註冊；IssueList.js 以副作用方式匯入）
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'issueList', {
    label: '問題清單',
    empty: '沒有問題',
    filteredEmpty: '目前的篩選條件下沒有項目',
    all: '全部',
    summaryLabel: '依嚴重度篩選',
    severity: {
        error: '錯誤',
        warning: '警告',
        info: '提示'
    },
    countLabel: '{label} {count}',
    groupHeading: '{label}（{count}）',
    dismiss: '忽略：{title}',
    announceSummary: '共 {total} 項：錯誤 {error}、警告 {warning}、提示 {info}',
    announceEmpty: '沒有問題'
});

Locale.register('en', 'issueList', {
    label: 'Issues',
    empty: 'No issues',
    filteredEmpty: 'No issues match the current filter',
    all: 'All',
    summaryLabel: 'Filter by severity',
    severity: {
        error: 'Error',
        warning: 'Warning',
        info: 'Info'
    },
    countLabel: '{label} {count}',
    groupHeading: '{label} ({count})',
    dismiss: 'Dismiss: {title}',
    announceSummary: '{total} issues: {error} errors, {warning} warnings, {info} info',
    announceEmpty: 'No issues'
});
