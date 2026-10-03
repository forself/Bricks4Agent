/**
 * RemoteSelect 語系字串（自我註冊；RemoteSelect.js 以副作用方式匯入）。
 * 命名空間：remoteSelect
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'remoteSelect', {
    placeholder: '輸入關鍵字搜尋',
    typeToSearch: '請輸入關鍵字開始搜尋',
    minQuery: '請至少輸入 {count} 個字元',
    loading: '載入中…',
    loadingMore: '載入更多中…',
    loadMore: '載入更多',
    empty: '查無符合的項目',
    error: '載入失敗',
    retry: '重試',
    resultCount: '共 {count} 筆結果',
    resultCountMore: '已載入 {count} 筆結果，還有更多',
    maxReached: '最多可選 {max} 項',
    clear: '清除選取',
    toggle: '顯示選項',
    removeTag: '移除 {label}',
    selectedItems: '已選取項目',
    resolving: '載入中…'
});

Locale.register('en', 'remoteSelect', {
    placeholder: 'Type to search',
    typeToSearch: 'Start typing to search',
    minQuery: 'Type at least {count} characters',
    loading: 'Loading…',
    loadingMore: 'Loading more…',
    loadMore: 'Load more',
    empty: 'No matching results',
    error: 'Failed to load results',
    retry: 'Retry',
    resultCount: '{count} results',
    resultCountMore: '{count} results loaded, more available',
    maxReached: 'You can select up to {max} items',
    clear: 'Clear selection',
    toggle: 'Show options',
    removeTag: 'Remove {label}',
    selectedItems: 'Selected items',
    resolving: 'Loading…'
});
