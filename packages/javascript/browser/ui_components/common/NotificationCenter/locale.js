/**
 * NotificationCenter 語系字串（自我註冊；NotificationCenter.js 以副作用方式匯入）
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'notificationCenter', {
    label: '通知',
    triggerLabel: '{label}（{count} 則未讀）',
    markAllRead: '全部標為已讀',
    loadMore: '載入更多',
    loading: '載入中…',
    loadFailed: '載入失敗，請再試一次',
    empty: '目前沒有通知',
    unread: '未讀',
    unreadAnnouncement: '{count} 則未讀通知',
    noUnread: '沒有未讀通知',
    variant: {
        info: '資訊',
        success: '成功',
        warning: '警告',
        danger: '錯誤'
    }
});

Locale.register('en', 'notificationCenter', {
    label: 'Notifications',
    triggerLabel: '{label} ({count} unread)',
    markAllRead: 'Mark all as read',
    loadMore: 'Load more',
    loading: 'Loading…',
    loadFailed: 'Could not load more notifications. Please try again.',
    empty: 'No notifications',
    unread: 'Unread',
    unreadAnnouncement: '{count} unread notifications',
    noUnread: 'No unread notifications',
    variant: {
        info: 'Info',
        success: 'Success',
        warning: 'Warning',
        danger: 'Error'
    }
});
