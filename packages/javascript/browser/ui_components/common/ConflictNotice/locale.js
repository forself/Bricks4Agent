/**
 * ConflictNotice 字串（命名空間 conflictNotice）。
 * ConflictNotice.js 以副作用方式匯入本檔，載入時自行註冊到 Locale。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'conflictNotice', {
    title: '資料已被其他人更新',
    message: '您編輯的期間，這筆資料已在伺服器上被更新。請選擇要載入最新資料，或以您的版本覆寫。',
    updatedByAt: '最後由 {user} 於 {time} 更新',
    updatedBy: '最後由 {user} 更新',
    updatedAt: '最後更新於 {time}',
    diffCaption: '您的內容與目前資料的差異',
    columnField: '欄位',
    columnLocal: '您的值',
    columnServer: '目前的值',
    empty: '—',
    yes: '是',
    no: '否',
    actionsLabel: '處理方式',
    reload: '載入最新資料',
    overwrite: '以我的版本覆寫',
    cancel: '取消',
    confirmOverwriteMessage: '覆寫會取代伺服器上較新的變更，而且無法復原。確定要覆寫嗎？',
    confirmOverwrite: '確定覆寫',
    back: '返回'
});

Locale.register('en', 'conflictNotice', {
    title: 'This record was changed by someone else',
    message: 'While you were editing, this record was updated on the server. Load the latest version, or overwrite it with your changes.',
    updatedByAt: 'Last updated by {user} at {time}',
    updatedBy: 'Last updated by {user}',
    updatedAt: 'Last updated at {time}',
    diffCaption: 'Differences between your version and the current data',
    columnField: 'Field',
    columnLocal: 'Your value',
    columnServer: 'Current value',
    empty: '—',
    yes: 'Yes',
    no: 'No',
    actionsLabel: 'Choose how to proceed',
    reload: 'Load latest',
    overwrite: 'Overwrite with mine',
    cancel: 'Cancel',
    confirmOverwriteMessage: 'Overwriting replaces newer changes on the server and cannot be undone. Overwrite anyway?',
    confirmOverwrite: 'Overwrite anyway',
    back: 'Back'
});
