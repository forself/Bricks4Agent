/**
 * PermissionGate 字串（命名空間 permissionGate）。
 * permission-gate.js 以副作用方式匯入本檔，載入時自行註冊到 Locale。
 */
import Locale from '../i18n/index.js';

Locale.register('zh-TW', 'permissionGate', {
    deniedReason: '您沒有執行此操作的權限'
});

Locale.register('en', 'permissionGate', {
    deniedReason: 'You do not have permission to perform this action'
});
