/**
 * DirtyGuard 字串（命名空間 dirtyGuard）。
 * dirty-guard.js 以副作用方式匯入本檔，載入時自行註冊到 Locale。
 */
import Locale from '../i18n/index.js';

Locale.register('zh-TW', 'dirtyGuard', {
    title: '尚未儲存的變更',
    message: '您有尚未儲存的變更。確定要離開嗎？離開後這些變更將會遺失。',
    leave: '離開',
    stay: '留在此頁'
});

Locale.register('en', 'dirtyGuard', {
    title: 'Unsaved changes',
    message: 'You have unsaved changes. Leave anyway? Your changes will be lost.',
    leave: 'Leave',
    stay: 'Stay on this page'
});
