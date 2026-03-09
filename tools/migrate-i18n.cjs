/**
 * i18n 遷移腳本 — 將元件中的硬編碼中文字串替換為 Locale 呼叫
 *
 * 策略：
 * 1. 在檔案頂部加入 import Locale
 * 2. 依據各元件的具體情況做精確替換
 */
const fs = require('fs');
const path = require('path');

const UI = path.resolve(__dirname, '../packages/javascript/browser/ui_components');
let totalReplacements = 0;
let modifiedFiles = 0;
const report = [];

function readFile(relPath) {
    return fs.readFileSync(path.join(UI, relPath), 'utf-8');
}

function writeFile(relPath, content) {
    fs.writeFileSync(path.join(UI, relPath), content, 'utf-8');
}

/**
 * 在檔案頂部加入 Locale import（在最後一個 import 之後）
 */
function addLocaleImport(content, depth) {
    // depth: 從元件檔到 i18n/ 的相對路徑深度
    const importPath = '../'.repeat(depth) + 'i18n/index.js';
    const importLine = `import Locale from '${importPath}';`;

    // 如果已經有 import Locale，跳過
    if (content.includes('import Locale from')) return content;

    // 找到最後一個 import 語句的位置
    const importRegex = /^import\s+.*?;?\s*$/gm;
    let lastImportEnd = 0;
    let match;
    while ((match = importRegex.exec(content)) !== null) {
        lastImportEnd = match.index + match[0].length;
    }

    if (lastImportEnd > 0) {
        return content.slice(0, lastImportEnd) + '\n' + importLine + content.slice(lastImportEnd);
    }

    // 找到 JSDoc 結尾後插入
    const jsdocEnd = content.indexOf('*/');
    if (jsdocEnd > -1) {
        const insertPos = content.indexOf('\n', jsdocEnd) + 1;
        return content.slice(0, insertPos) + importLine + '\n\n' + content.slice(insertPos);
    }

    // 否則放在檔案頂部
    return importLine + '\n\n' + content;
}

function replace(content, oldStr, newStr) {
    if (!content.includes(oldStr)) return content;
    totalReplacements++;
    return content.replace(oldStr, newStr);
}

function replaceAll(content, oldStr, newStr) {
    if (!content.includes(oldStr)) return content;
    const count = content.split(oldStr).length - 1;
    totalReplacements += count;
    return content.split(oldStr).join(newStr);
}

// ======== Notification ========
function migrateNotification() {
    const file = 'common/Notification/Notification.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    // 靜態方法中的標題
    c = replace(c, "title: '成功',", "title: Locale.t('notification.success'),");
    c = replace(c, "title: '錯誤',", "title: Locale.t('notification.error'),");
    c = replace(c, "title: '警告',", "title: Locale.t('notification.warning'),");
    c = replace(c, "title: '提示',", "title: Locale.t('notification.info'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 4 replacements`); }
}

// ======== SimpleDialog ========
function migrateSimpleDialog() {
    const file = 'common/Dialog/SimpleDialog.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "title: '提示',\n                content: message,\n                onConfirm: () => resolve(true),\n                container",
        "title: Locale.t('dialog.alert'),\n                content: message,\n                onConfirm: () => resolve(true),\n                container");
    c = replace(c, "title: '確認',\n                content: message,\n                onConfirm: () => resolve(true),\n                onCancel: () => resolve(false),",
        "title: Locale.t('dialog.confirm'),\n                content: message,\n                onConfirm: () => resolve(true),\n                onCancel: () => resolve(false),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 2 replacements`); }
}

// ======== LoadingSpinner ========
function migrateLoadingSpinner() {
    const file = 'common/LoadingSpinner/LoadingSpinner.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "static showOverlay(text = '載入中...',", "static showOverlay(text = Locale.t('loadingSpinner.text'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 1 replacement`); }
}

// ======== Breadcrumb ========
function migrateBreadcrumb() {
    const file = 'common/Breadcrumb/Breadcrumb.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "text: '首頁',", "text: Locale.t('breadcrumb.home'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 1 replacement`); }
}

// ======== Pagination ========
function migratePagination() {
    const file = 'common/Pagination/Pagination.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, `title="第一頁"`, `title="${'${Locale.t("pagination.first")}'}"`);
    // Actually, since these are template literals in HTML strings, let's use a different approach
    // The titles are inside template literals, so we need to be more careful
    // Let me re-read the actual format

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}`); }
}

// ======== BasicButton ========
function migrateBasicButton() {
    const file = 'common/BasicButton/BasicButton.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    // ICONS 物件中的 label 替換
    const labelMap = {
        "label: '刪除',": "label: Locale.t('basicButton.delete'),",
        "label: '確認',": "label: Locale.t('basicButton.confirm'),",
        "label: '是',": "label: Locale.t('basicButton.yes'),",
        "label: '取消',": "label: Locale.t('basicButton.cancel'),",
        "label: '否',": "label: Locale.t('basicButton.no'),",
        "label: '完成',": "label: Locale.t('basicButton.done'),",
        "label: '關閉',": "label: Locale.t('basicButton.close'),",
        "label: '搜尋',": "label: Locale.t('basicButton.search'),",
        "label: '清除',": "label: Locale.t('basicButton.clear'),",
        "label: '重設',": "label: Locale.t('basicButton.reset'),",
        "label: '儲存',": "label: Locale.t('basicButton.save'),",
        "label: '套用',": "label: Locale.t('basicButton.apply'),",
        "label: '複製',": "label: Locale.t('basicButton.copy'),",
        "label: '刷新',": "label: Locale.t('basicButton.refresh'),",
        "label: '增加一列',": "label: Locale.t('basicButton.addRow'),",
        "label: '全選',": "label: Locale.t('basicButton.selectAll'),",
        "label: '取消全選',": "label: Locale.t('basicButton.deselectAll'),",
        "label: '返回',": "label: Locale.t('basicButton.back'),",
        "label: '下一步',": "label: Locale.t('basicButton.next'),",
        "label: '上一步',": "label: Locale.t('basicButton.prev'),",
        "label: '展開全部',": "label: Locale.t('basicButton.expandAll'),",
        "label: '收合全部',": "label: Locale.t('basicButton.collapseAll'),",
    };

    for (const [old, rep] of Object.entries(labelMap)) {
        c = replace(c, old, rep);
    }

    // createDialogButtons 預設值
    c = replace(c, "confirmLabel = '確認',", "confirmLabel = Locale.t('basicButton.confirmLabel'),");
    c = replace(c, "cancelLabel = '取消',", "cancelLabel = Locale.t('basicButton.cancelLabel'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: ${Object.keys(labelMap).length + 2} replacements`); }
}

// ======== ActionButton ========
function migrateActionButton() {
    const file = 'common/ActionButton/ActionButton.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    const labelMap = {
        "label: '新增',": "label: Locale.t('actionButton.add'),",
        "label: '刪除',": "label: Locale.t('actionButton.delete'),",
        "label: '編輯',": "label: Locale.t('actionButton.edit'),",
        "label: '詳細',": "label: Locale.t('actionButton.detail'),",
        "label: '送出',": "label: Locale.t('actionButton.submit'),",
        "label: '退回',": "label: Locale.t('actionButton.reject'),",
        "label: '歸檔',": "label: Locale.t('actionButton.archive'),",
        "label: '整合',": "label: Locale.t('actionButton.merge'),",
        "label: '檢核',": "label: Locale.t('actionButton.verify'),",
        "label: '撤管',": "label: Locale.t('actionButton.withdraw'),",
        "label: '陳報',": "label: Locale.t('actionButton.report'),",
        "label: '審轉',": "label: Locale.t('actionButton.transfer'),",
        "label: '審核',": "label: Locale.t('actionButton.approve'),",
        "label: '修改',": "label: Locale.t('actionButton.modify'),",
    };

    for (const [old, rep] of Object.entries(labelMap)) {
        c = replace(c, old, rep);
    }

    c = replace(c, "confirmMessage: '確定要執行此操作嗎？',", "confirmMessage: Locale.t('actionButton.confirmMessage'),");
    c = replace(c, "confirmMessage: '確定要刪除嗎？'", "confirmMessage: Locale.t('actionButton.confirmDelete')");
    c = replace(c, "confirmMessage: '確定要退回嗎？'", "confirmMessage: Locale.t('actionButton.confirmReject')");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: ${Object.keys(labelMap).length + 3} replacements`); }
}

// ======== AuthButton ========
function migrateAuthButton() {
    const file = 'common/AuthButton/AuthButton.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "label: '登入',", "label: Locale.t('authButton.login'),");
    c = replace(c, "label: '登出',", "label: Locale.t('authButton.logout'),");
    c = replace(c, "confirmMessage: '確定要登出嗎？',", "confirmMessage: Locale.t('authButton.confirmLogout'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 3 replacements`); }
}

// ======== EditorButton ========
function migrateEditorButton() {
    const file = 'common/EditorButton/EditorButton.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    // EditorButton CONFIG 中所有 label 替換
    const labels = {
        "label: '粗體',": "label: Locale.t('editorButton.bold'),",
        "label: '斜體',": "label: Locale.t('editorButton.italic'),",
        "label: '底線',": "label: Locale.t('editorButton.underline'),",
        "label: '刪除線',": "label: Locale.t('editorButton.strikethrough'),",
        "label: '下標',": "label: Locale.t('editorButton.subscript'),",
        "label: '上標',": "label: Locale.t('editorButton.superscript'),",
        "label: '大標題',": "label: Locale.t('editorButton.heading1'),",
        "label: '中標題',": "label: Locale.t('editorButton.heading2'),",
        "label: '小標題',": "label: Locale.t('editorButton.heading3'),",
        "label: '段落',": "label: Locale.t('editorButton.paragraph'),",
        "label: '引用',": "label: Locale.t('editorButton.quote'),",
        "label: '程式碼',": "label: Locale.t('editorButton.code'),",
        "label: '靠左',": "label: Locale.t('editorButton.alignLeft'),",
        "label: '置中',": "label: Locale.t('editorButton.alignCenter'),",
        "label: '靠右',": "label: Locale.t('editorButton.alignRight'),",
        "label: '兩端對齊',": "label: Locale.t('editorButton.alignJustify'),",
        "label: '項目符號',": "label: Locale.t('editorButton.listBullet'),",
        "label: '編號',": "label: Locale.t('editorButton.listNumber'),",
        "label: '增加縮排',": "label: Locale.t('editorButton.indent'),",
        "label: '減少縮排',": "label: Locale.t('editorButton.outdent'),",
        "label: '復原',": "label: Locale.t('editorButton.undo'),",
        "label: '重做',": "label: Locale.t('editorButton.redo'),",
        "label: '連結',": "label: Locale.t('editorButton.link'),",
        "label: '圖片',": "label: Locale.t('editorButton.image'),",
        "label: '表格',": "label: Locale.t('editorButton.table'),",
        "label: '分隔線',": "label: Locale.t('editorButton.line'),",
        "label: '分頁',": "label: Locale.t('editorButton.pageBreak'),",
        "label: '畫筆',": "label: Locale.t('editorButton.pen'),",
        "label: '橡皮擦',": "label: Locale.t('editorButton.eraser'),",
        "label: '直線',": "label: Locale.t('editorButton.lineTool'),",
        "label: '螢光筆',": "label: Locale.t('editorButton.highlighter'),",
        "label: '矩形',": "label: Locale.t('editorButton.rect'),",
        "label: '圓形',": "label: Locale.t('editorButton.circle'),",
        "label: '箭頭',": "label: Locale.t('editorButton.arrow'),",
        "label: '文字',": "label: Locale.t('editorButton.text'),",
        "label: '選擇',": "label: Locale.t('editorButton.select'),",
        "label: '測距',": "label: Locale.t('editorButton.measureDistance'),",
        "label: '測面積',": "label: Locale.t('editorButton.measureArea'),",
        "label: '座標',": "label: Locale.t('editorButton.coordinate'),",
        "label: '匯出 PDF',": "label: Locale.t('editorButton.exportPdf'),",
        "label: '匯出 Word',": "label: Locale.t('editorButton.exportWord'),",
        "label: '匯出 Markdown',": "label: Locale.t('editorButton.exportMarkdown'),",
        "label: '匯出 PNG',": "label: Locale.t('editorButton.exportPng'),",
        "label: '匯出 JSON',": "label: Locale.t('editorButton.exportJson'),",
        "label: '搜尋',": "label: Locale.t('editorButton.search'),",
        "label: '取代',": "label: Locale.t('editorButton.replace'),",
        "label: '全螢幕',": "label: Locale.t('editorButton.fullscreen'),",
        "label: '清除',": "label: Locale.t('editorButton.clear'),",
        "label: '清空',": "label: Locale.t('editorButton.clearAll'),",
        "label: '清除格式',": "label: Locale.t('editorButton.removeFormat'),",
        "label: '複製',": "label: Locale.t('editorButton.copy'),",
        "label: '貼上',": "label: Locale.t('editorButton.paste'),",
        "label: '剪下',": "label: Locale.t('editorButton.cut'),",
        "label: '目錄',": "label: Locale.t('editorButton.toc'),",
        "label: '設定',": "label: Locale.t('editorButton.settings'),",
        "label: '圖層',": "label: Locale.t('editorButton.layers'),",
        "label: '放大',": "label: Locale.t('editorButton.zoomIn'),",
        "label: '縮小',": "label: Locale.t('editorButton.zoomOut'),",
        "label: '繪圖',": "label: Locale.t('editorButton.insertDrawing'),",
        "label: '頁首',": "label: Locale.t('editorButton.header'),",
        "label: '頁尾',": "label: Locale.t('editorButton.footer'),",
        "label: '頁碼',": "label: Locale.t('editorButton.pageNumber'),",
        "label: '邊界',": "label: Locale.t('editorButton.margin'),",
        "label: '生成目錄',": "label: Locale.t('editorButton.generateToc'),",
    };

    for (const [old, rep] of Object.entries(labels)) {
        c = replaceAll(c, old, rep);
    }

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: ${Object.keys(labels).length} replacements`); }
}

// ======== DatePicker ========
function migrateDatePicker() {
    const file = 'form/DatePicker/DatePicker.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "placeholder: '請選擇日期',", "placeholder: Locale.t('datePicker.placeholder'),");
    // 星期標題
    c = replace(c,
        "${['日', '一', '二', '三', '四', '五', '六'].map(d => `<div style=\"font-size:10px;color:var(--cl-text-muted);padding:2px 0;\">${d}</div>`).join('')}",
        "${Object.values(Locale.t('datePicker.weekdays')).map(d => `<div style=\"font-size:10px;color:var(--cl-text-muted);padding:2px 0;\">${d}</div>`).join('')}"
    );

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}`); }
}

// ======== TimePicker ========
function migrateTimePicker() {
    const file = 'form/TimePicker/TimePicker.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "placeholder: '請選擇時間',", "placeholder: Locale.t('timePicker.placeholder'),");
    c = replace(c, "this._createColumn('小時',", "this._createColumn(Locale.t('timePicker.hour'),");
    c = replace(c, "this._createColumn('分鐘',", "this._createColumn(Locale.t('timePicker.minute'),");
    c = replace(c, "confirmBtn.textContent = '確認';", "confirmBtn.textContent = Locale.t('timePicker.confirm');");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 4 replacements`); }
}

// ======== Dropdown ========
function migrateDropdown() {
    const file = 'form/Dropdown/Dropdown.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "placeholder: '請選擇',", "placeholder: Locale.t('dropdown.placeholder'),");
    c = replace(c, "emptyText: '無符合項目',", "emptyText: Locale.t('dropdown.emptyText'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 2 replacements`); }
}

// ======== MultiSelectDropdown ========
function migrateMultiSelectDropdown() {
    const file = 'form/MultiSelectDropdown/MultiSelectDropdown.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "placeholder: '請選擇',", "placeholder: Locale.t('multiSelect.placeholder'),");
    c = replace(c, "emptyText: '無符合項目',", "emptyText: Locale.t('multiSelect.emptyText'),");
    c = replace(c, "modalTitle: '選擇項目',", "modalTitle: Locale.t('multiSelect.modalTitle'),");
    c = replace(c, "expandBtn.title = '展開全部選項';", "expandBtn.title = Locale.t('multiSelect.expandAll');");
    c = replace(c, "searchInput.placeholder = '搜尋選項...';", "searchInput.placeholder = Locale.t('multiSelect.searchPlaceholder');");
    c = replace(c, "selectAllBtn.textContent = '全選';", "selectAllBtn.textContent = Locale.t('multiSelect.selectAll');");
    c = replace(c, "deselectAllBtn.textContent = '全不選';", "deselectAllBtn.textContent = Locale.t('multiSelect.deselectAll');");
    c = replace(c, "cancelBtn.textContent = '取消';", "cancelBtn.textContent = Locale.t('multiSelect.cancel');");
    c = replace(c, "confirmBtn.textContent = '確定';", "confirmBtn.textContent = Locale.t('multiSelect.confirm');");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 9 replacements`); }
}

// ======== SearchForm ========
function migrateSearchForm() {
    const file = 'form/SearchForm/SearchForm.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "searchText: '搜尋',", "searchText: Locale.t('searchForm.searchText'),");
    c = replace(c, "resetText: '重設',", "resetText: Locale.t('searchForm.resetText'),");
    c = replace(c, "expandBtn.innerHTML = this._expanded ? '收合 ▲' : '展開 ▼';",
        "expandBtn.innerHTML = this._expanded ? Locale.t('searchForm.collapse') : Locale.t('searchForm.expand');");
    // Second occurrence
    c = replace(c, "expandBtn.innerHTML = this._expanded ? '收合 ▲' : '展開 ▼';",
        "expandBtn.innerHTML = this._expanded ? Locale.t('searchForm.collapse') : Locale.t('searchForm.expand');");
    c = replace(c, "placeholder: placeholder || '請選擇',", "placeholder: placeholder || Locale.t('searchForm.selectPlaceholder'),");
    c = replace(c, "placeholder: placeholder || '選擇日期',", "placeholder: placeholder || Locale.t('searchForm.datePlaceholder'),");
    c = replace(c, "placeholder: '開始日期',", "placeholder: Locale.t('searchForm.startDate'),");
    c = replace(c, "separator.textContent = '至';", "separator.textContent = Locale.t('searchForm.dateSeparator');");
    c = replace(c, "placeholder: '結束日期',", "placeholder: Locale.t('searchForm.endDate'),");
    c = replace(c, "component.setError('此欄位為必填');", "component.setError(Locale.t('searchForm.requiredError'));");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 10 replacements`); }
}

// ======== DataTable ========
function migrateDataTable() {
    const file = 'layout/DataTable/DataTable.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    // DEFAULT_TEXT_LABELS
    c = replace(c,
        "pagination: { rowsPerPage: '每頁筆數:', displayRows: '共' },",
        "pagination: { rowsPerPage: Locale.t('dataTable.rowsPerPage'), displayRows: Locale.t('dataTable.displayRows') },");
    c = replace(c,
        "body: { noMatch: '無查詢結果' },",
        "body: { noMatch: Locale.t('dataTable.noMatch') },");
    c = replace(c,
        "selectedRows: { text: '筆' },",
        "selectedRows: { text: Locale.t('dataTable.selectedUnit') },");

    // 分頁按鈕 title
    c = replace(c, `title="第一頁"`, `title="\${Locale.t('dataTable.firstPage')}"`);
    c = replace(c, `title="上一頁"`, `title="\${Locale.t('dataTable.prevPage')}"`);
    c = replace(c, `title="下一頁"`, `title="\${Locale.t('dataTable.nextPage')}"`);
    c = replace(c, `title="最後一頁"`, `title="\${Locale.t('dataTable.lastPage')}"`);

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 7 replacements`); }
}

// ======== SideMenu ========
function migrateSideMenu() {
    const file = 'layout/SideMenu/SideMenu.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replaceAll(c, "collapsed ? '展開選單' : '收合選單'",
        "collapsed ? Locale.t('sideMenu.expand') : Locale.t('sideMenu.collapse')");
    c = replaceAll(c, "this.options.collapsed ? '展開選單' : '收合選單'",
        "this.options.collapsed ? Locale.t('sideMenu.expand') : Locale.t('sideMenu.collapse')");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 2 replacements`); }
}

// ======== ModalPanel ========
function migrateModalPanel() {
    const file = 'layout/Panel/ModalPanel.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    // confirm 方法
    c = replace(c, "title = '確認',\n            confirmText = '確認',\n            cancelText = '取消',",
        "title = Locale.t('modalPanel.confirmTitle'),\n            confirmText = Locale.t('modalPanel.confirmText'),\n            cancelText = Locale.t('modalPanel.cancelText'),");
    // alert 方法
    c = replace(c, "title = '提示',\n", "title = Locale.t('modalPanel.alertTitle'),\n");
    c = replace(c, "confirmText = '確定',\n", "confirmText = Locale.t('modalPanel.okText'),\n");
    // prompt 方法
    c = replace(c, "title = '輸入',\n", "title = Locale.t('modalPanel.promptTitle'),\n");
    c = replace(c, "confirmText = '確認',\n            cancelText = '取消',",
        "confirmText = Locale.t('modalPanel.confirmText'),\n            cancelText = Locale.t('modalPanel.cancelText'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 5 replacements`); }
}

// ======== InfoPanel ========
function migrateInfoPanel() {
    const file = 'layout/InfoPanel/InfoPanel.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "panel.title || '未命名面板'", "panel.title || Locale.t('infoPanel.untitledPanel')");
    c = replace(c, "data.chartType || '圖表區域'", "data.chartType || Locale.t('infoPanel.chartPlaceholder')");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 2 replacements`); }
}

// ======== WorkflowPanel ========
function migrateWorkflowPanel() {
    const file = 'layout/WorkflowPanel/WorkflowPanel.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "badge.textContent = '目前';", "badge.textContent = Locale.t('workflowPanel.currentBadge');");
    c = replace(c, "nextStage.NextUnit || '待定'", "nextStage.NextUnit || Locale.t('workflowPanel.pending')");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 2 replacements`); }
}

// ======== AddressInput ========
function migrateAddressInput() {
    const file = 'input/AddressInput/AddressInput.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "label: '縣市',", "label: Locale.t('addressInput.cityLabel'),");
    c = replace(c, "placeholder: '請選擇縣市',", "placeholder: Locale.t('addressInput.cityPlaceholder'),");
    c = replace(c, "label: '行政區',", "label: Locale.t('addressInput.districtLabel'),");
    c = replace(c, "placeholder: '請選擇行政區',", "placeholder: Locale.t('addressInput.districtPlaceholder'),");
    c = replace(c, "label: '詳細地址',", "label: Locale.t('addressInput.detailLabel'),");
    c = replace(c, "placeholder: '請輸入街道巷弄號碼',", "placeholder: Locale.t('addressInput.detailPlaceholder'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 6 replacements`); }
}

// ======== AddressListInput ========
function migrateAddressListInput() {
    const file = 'input/AddressListInput/AddressListInput.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "title: '地址列表',", "title: Locale.t('addressListInput.title'),");
    c = replace(c, "addButtonText: '新增地址',", "addButtonText: Locale.t('addressListInput.addButton'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 2 replacements`); }
}

// ======== DateTimeInput ========
function migrateDateTimeInput() {
    const file = 'input/DateTimeInput/DateTimeInput.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "label: '日期',", "label: Locale.t('dateTimeInput.dateLabel'),");
    c = replace(c, "label: '時間',", "label: Locale.t('dateTimeInput.timeLabel'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 2 replacements`); }
}

// ======== PersonInfoList ========
function migratePersonInfoList() {
    const file = 'input/PersonInfoList/PersonInfoList.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "title: '個人基本資料',", "title: Locale.t('personInfoList.title'),");
    c = replace(c, "addButtonText: '新增人員',", "addButtonText: Locale.t('personInfoList.addButton'),");
    c = replace(c, "{ name: 'name', label: '姓名', type: 'text', placeholder: '請輸入姓名' },",
        "{ name: 'name', label: Locale.t('personInfoList.nameLabel'), type: 'text', placeholder: Locale.t('personInfoList.namePlaceholder') },");
    c = replace(c, "{ name: 'gender', label: '性別', type: 'select', options: ['男', '女', '其他'] },",
        "{ name: 'gender', label: Locale.t('personInfoList.genderLabel'), type: 'select', options: Object.values(Locale.t('personInfoList.genderOptions')) },");
    c = replace(c, "{ name: 'age', label: '年齡', type: 'number', min: 0, max: 150 },",
        "{ name: 'age', label: Locale.t('personInfoList.ageLabel'), type: 'number', min: 0, max: 150 },");
    c = replace(c, "{ name: 'id', label: '身分證號', type: 'text', maxLength: 20, placeholder: '請輸入號碼' },",
        "{ name: 'id', label: Locale.t('personInfoList.idLabel'), type: 'text', maxLength: 20, placeholder: Locale.t('personInfoList.idPlaceholder') },");
    c = replace(c, "{ name: 'otherId', label: '其他證號', type: 'text' }",
        "{ name: 'otherId', label: Locale.t('personInfoList.otherIdLabel'), type: 'text' }");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 7 replacements`); }
}

// ======== OrganizationInput ========
function migrateOrganizationInput() {
    const file = 'input/OrganizationInput/OrganizationInput.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "label: '一級單位',", "label: Locale.t('organizationInput.level1Label'),");
    c = replace(c, "label: '二級單位',", "label: Locale.t('organizationInput.level2Label'),");
    c = replace(c, "label: '三級單位',", "label: Locale.t('organizationInput.level3Label'),");
    c = replace(c, "label: '四級單位',", "label: Locale.t('organizationInput.level4Label'),");
    c = replaceAll(c, "placeholder: '請選擇',", "placeholder: Locale.t('organizationInput.placeholder'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 8 replacements`); }
}

// ======== StudentInput ========
function migrateStudentInput() {
    const file = 'input/StudentInput/StudentInput.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "checkboxLabel: '是否為在學學生',", "checkboxLabel: Locale.t('studentInput.checkboxLabel'),");
    c = replace(c, "label: '學籍身份',", "label: Locale.t('studentInput.statusLabel'),");
    c = replace(c, "label: '學校名稱',", "label: Locale.t('studentInput.schoolLabel'),");
    c = replace(c, "placeholder: '請輸入就讀學校',", "placeholder: Locale.t('studentInput.schoolPlaceholder'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 4 replacements`); }
}

// ======== ListInput ========
function migrateListInput() {
    const file = 'input/ListInput/ListInput.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "addButtonText: '新增項目',", "addButtonText: Locale.t('listInput.addButton'),");
    c = replace(c, "templateBtn.title = '下載 CSV 範本';", "templateBtn.title = Locale.t('listInput.csvTemplate');");
    c = replace(c, "dragHandle.title = '拖曳排序';", "dragHandle.title = Locale.t('listInput.dragToSort');");
    c = replace(c, "moveUpBtn.title = '上移';", "moveUpBtn.title = Locale.t('listInput.moveUp');");
    c = replace(c, "moveDownBtn.title = '下移';", "moveDownBtn.title = Locale.t('listInput.moveDown');");
    c = replace(c, "deleteBtn.title = '移除項目';", "deleteBtn.title = Locale.t('listInput.removeItem');");
    c = replace(c, "field.placeholder || '請選擇'", "field.placeholder || Locale.t('listInput.selectPlaceholder')");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 7 replacements`); }
}

// ======== SocialMediaList ========
function migrateSocialMediaList() {
    const file = 'input/SocialMediaList/SocialMediaList.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "title: '社群軟體列表',", "title: Locale.t('socialMediaList.title'),");
    c = replace(c, "addButtonText: '新增帳號',", "addButtonText: Locale.t('socialMediaList.addButton'),");
    c = replace(c, "accountInput.placeholder = '請輸入 ID 或連結';", "accountInput.placeholder = Locale.t('socialMediaList.placeholder');");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 3 replacements`); }
}

// ======== PhoneListInput ========
function migratePhoneListInput() {
    const file = 'input/PhoneListInput/PhoneListInput.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "title: '電話列表',", "title: Locale.t('phoneListInput.title'),");
    c = replace(c, "addButtonText: '新增電話',", "addButtonText: Locale.t('phoneListInput.addButton'),");
    c = replace(c, "numberInput.placeholder = '請輸入電話號碼';", "numberInput.placeholder = Locale.t('phoneListInput.placeholder');");
    // 電話類型
    c = replace(c, "['手機', '市話', '公司', '傳真'].forEach",
        "Object.values(Locale.t('phoneListInput.types')).forEach");
    c = replace(c, "type: '手機',", "type: Locale.t('phoneListInput.types').mobile,");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 5 replacements`); }
}

// ======== ChainedInput ========
function migrateChainedInput() {
    const file = 'input/ChainedInput/ChainedInput.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replaceAll(c, "field.placeholder || '請選擇'", "field.placeholder || Locale.t('chainedInput.placeholder')");
    c = replace(c, "field.checkboxLabel || '是'", "field.checkboxLabel || Locale.t('chainedInput.checkboxYes')");
    c = replace(c, "loadingOpt.textContent = '載入中...';", "loadingOpt.textContent = Locale.t('chainedInput.loading');");
    c = replace(c, "input.options[0].textContent = '無選項';", "input.options[0].textContent = Locale.t('chainedInput.noOptions');");
    c = replace(c, "input.options[0].textContent = '載入失敗';", "input.options[0].textContent = Locale.t('chainedInput.loadError');");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 5+ replacements`); }
}

// ======== 執行所有遷移 ========
console.log('🌐 開始 i18n 遷移...\n');

// common/
migrateNotification();
migrateSimpleDialog();
migrateLoadingSpinner();
migrateBreadcrumb();
migrateBasicButton();
migrateActionButton();
migrateAuthButton();
migrateEditorButton();

// form/
migrateDatePicker();
migrateTimePicker();
migrateDropdown();
migrateMultiSelectDropdown();
migrateSearchForm();

// layout/
migrateDataTable();
migrateSideMenu();
migrateModalPanel();
migrateInfoPanel();
migrateWorkflowPanel();

// input/
migrateAddressInput();
migrateAddressListInput();
migrateDateTimeInput();
migratePersonInfoList();
migrateOrganizationInput();
migrateStudentInput();
migrateListInput();
migrateSocialMediaList();
migratePhoneListInput();
migrateChainedInput();

console.log('\n📊 遷移報告:');
report.forEach(r => console.log(`  ${r}`));
console.log(`\n✅ 完成: ${modifiedFiles} 個檔案修改, ${totalReplacements} 處替換`);
