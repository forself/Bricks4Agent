/**
 * 定義網站外殼的語系字串（自我註冊；boot.js 以副作用方式匯入）
 */
import Locale from './runtime/ui_components/i18n/index.js';

Locale.register('zh-TW', 'definitionSite', {
    defaultTitle: '系統原型',
    navLabel: '頁面',
    yes: '是',
    no: '否',
    create: '新增',
    created: '已新增一筆資料',
    updated: '已儲存變更',
    deleted: '已刪除資料',
    deleteTitle: '刪除資料',
    deleteMessage: '確定要刪除這筆資料嗎？',
    noRecord: '尚未選取資料，請從列表選擇一筆。',
    recordMissing: '找不到這筆資料（原型資料只保存在目前分頁，重新整理後會清空）。',
    backToList: '回到列表',
    unknownPage: '找不到這個頁面。',
    noEndpoint: '此頁面未設定對應的資料端點。',
    notLinked: '沒有可前往的對應頁面。',
    cannotEdit: '這個表單只能新增，不能編輯。',
    loadFailed: '載入失敗：{message}',
    saveFailed: '儲存失敗：{message}',
    deleteFailed: '刪除失敗：{message}',
    bootFailed: '原型載入失敗：{message}',
    memoryNotice: '原型資料只保存在目前瀏覽器分頁的記憶體中。',
    roleDenied: '測試角色沒有此操作權限。',
    roleSelectLabel: '雛型測試角色',
    formalNotice: 'Broker 驗證工作階段；資料範圍由核定政策附加於每次資料操作。',
    sqliteNotice: '隔離的功能雛型：資料存於 SQLite，伺服器檢查角色範圍；測試角色切換不代表真實登入。',
    browserNotice: '隔離的功能雛型：測試角色不代表真實登入或正式授權。資料保存在此瀏覽器。'
});

Locale.register('en', 'definitionSite', {
    defaultTitle: 'Prototype',
    navLabel: 'Pages',
    yes: 'Yes',
    no: 'No',
    create: 'New',
    created: 'Record created',
    updated: 'Changes saved',
    deleted: 'Record deleted',
    deleteTitle: 'Delete record',
    deleteMessage: 'Delete this record?',
    noRecord: 'No record selected; pick one from the list.',
    recordMissing: 'Record not found (prototype data lives only in this tab and is cleared on reload).',
    backToList: 'Back to list',
    unknownPage: 'Page not found.',
    noEndpoint: 'This page has no data endpoint.',
    notLinked: 'There is no matching page to open.',
    cannotEdit: 'This form can create records but cannot edit them.',
    loadFailed: 'Loading failed: {message}',
    saveFailed: 'Saving failed: {message}',
    deleteFailed: 'Deleting failed: {message}',
    bootFailed: 'The prototype failed to load: {message}',
    memoryNotice: 'Prototype data is kept in this browser tab only.',
    roleDenied: 'The test role is not allowed to do this.',
    roleSelectLabel: 'Prototype test role',
    formalNotice: 'Broker-verified session; the approved policy scopes every data operation.',
    sqliteNotice: 'Isolated functional prototype: data is stored in SQLite and the server enforces role scope; switching test roles is not a real sign-in.',
    browserNotice: 'Isolated functional prototype: test roles are not a real sign-in or formal authorisation. Data is kept in this browser.'
});
