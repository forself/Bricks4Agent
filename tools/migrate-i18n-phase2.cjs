/**
 * i18n 遷移腳本 Phase 2 — 處理第一輪未覆蓋的字串
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
function addLocaleImport(content, depth) {
    const importPath = '../'.repeat(depth) + 'i18n/index.js';
    const importLine = `import Locale from '${importPath}';`;
    if (content.includes('import Locale from')) return content;
    const importRegex = /^import\s+.*?;?\s*$/gm;
    let lastImportEnd = 0;
    let match;
    while ((match = importRegex.exec(content)) !== null) {
        lastImportEnd = match.index + match[0].length;
    }
    if (lastImportEnd > 0) {
        return content.slice(0, lastImportEnd) + '\n' + importLine + content.slice(lastImportEnd);
    }
    return importLine + '\n\n' + content;
}
function replace(content, oldStr, newStr) {
    if (!content.includes(oldStr)) return content;
    totalReplacements++;
    return content.replace(oldStr, newStr);
}

// ======== SimpleDialog — 修復第一輪失敗的替換 ========
function fixSimpleDialog() {
    const file = 'common/Dialog/SimpleDialog.js';
    let c = readFile(file);
    const orig = c;

    // 按鈕文字
    c = replace(c, "cancelBtn.textContent = '取消';", "cancelBtn.textContent = Locale.t('dialog.cancelBtn');");
    c = replace(c, "confirmBtn.textContent = '確定';", "confirmBtn.textContent = Locale.t('dialog.confirmBtn');");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: button labels fixed`); }
}

// ======== ModalPanel — 修復殘留 ========
function fixModalPanel() {
    const file = 'layout/Panel/ModalPanel.js';
    let c = readFile(file);
    const orig = c;

    // 檢查是否還有殘留（有些可能因為格式不同未被第一輪替換）
    // confirm 中的第二處
    c = replace(c, "title = '確認',", "title = Locale.t('modalPanel.confirmTitle'),");
    c = replace(c, "confirmText = '確認',", "confirmText = Locale.t('modalPanel.confirmText'),");
    c = replace(c, "cancelText = '取消',", "cancelText = Locale.t('modalPanel.cancelText'),");
    c = replace(c, "title = '提示',", "title = Locale.t('modalPanel.alertTitle'),");
    c = replace(c, "confirmText = '確定',", "confirmText = Locale.t('modalPanel.okText'),");
    c = replace(c, "title = '輸入',", "title = Locale.t('modalPanel.promptTitle'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: remaining defaults fixed`); }
}

// ======== PhotoWall ========
function migratePhotoWall() {
    const file = 'layout/PhotoWall/PhotoWall.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "this.downloadBtn.textContent = '下載選取 (0)';",
        "this.downloadBtn.textContent = Locale.t('photoWall.downloadSelected', { count: 0 });");
    c = replace(c, "if (textSpan) textSpan.textContent = `下載選取 (${count})`;",
        "if (textSpan) textSpan.textContent = Locale.t('photoWall.downloadSelected', { count });");
    c = replace(c, "this.downloadBtn.querySelector('span').textContent = '打包中...';",
        "this.downloadBtn.querySelector('span').textContent = Locale.t('photoWall.packing');");
    c = replace(c, 'ModalPanel.alert({ message: "打包失敗，請重試" });',
        "ModalPanel.alert({ message: Locale.t('photoWall.packError') });");

    // 刪除確認對話框
    c = replace(c, "title: '刪除確認',", "title: Locale.t('photoWall.deleteConfirmTitle'),");
    c = replace(c, "message: '確定要刪除這張照片嗎？',", "message: Locale.t('photoWall.deleteConfirmMessage'),");
    c = replace(c, "confirmText: '確定',", "confirmText: Locale.t('photoWall.confirmBtn'),");
    c = replace(c, "cancelText: '取消',", "cancelText: Locale.t('photoWall.cancelBtn'),");
    c = replace(c, "title: '再次確認',", "title: Locale.t('photoWall.doubleConfirmTitle'),");
    c = replace(c, "message: '請輸入 \"是\" 以確認刪除操作：',",
        "message: Locale.t('photoWall.doubleConfirmMessage'),");
    c = replace(c, "placeholder: '是',", "placeholder: Locale.t('photoWall.doubleConfirmPlaceholder'),");
    c = replace(c, "validate: (value) => value === '是',",
        "validate: (value) => value === Locale.t('photoWall.doubleConfirmKeyword'),");
    c = replace(c, "confirmText: '確認刪除',", "confirmText: Locale.t('photoWall.doubleConfirmBtn'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 13 replacements`); }
}

// ======== DocumentWall ========
function migrateDocumentWall() {
    const file = 'layout/DocumentWall/DocumentWall.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "this.downloadBtn.textContent = '下載選取 (0)';",
        "this.downloadBtn.textContent = Locale.t('documentWall.downloadSelected', { count: 0 });");
    c = replace(c, "if (textSpan) textSpan.textContent = `下載選取 (${count})`;",
        "if (textSpan) textSpan.textContent = Locale.t('documentWall.downloadSelected', { count });");
    c = replace(c, "this.downloadBtn.querySelector('span').textContent = '打包中...';",
        "this.downloadBtn.querySelector('span').textContent = Locale.t('documentWall.packing');");
    c = replace(c, 'ModalPanel.alert({ message: "打包下載失敗" });',
        "ModalPanel.alert({ message: Locale.t('documentWall.packError') });");
    c = replace(c, "ModalPanel.alert({ message: `文件說明：${doc.description || '無'}` });",
        "ModalPanel.alert({ message: `${Locale.t('documentWall.descriptionPrefix')}${doc.description || Locale.t('documentWall.noDescription')}` });");

    // 刪除確認
    c = replace(c, "title: '刪除確認',", "title: Locale.t('documentWall.deleteConfirmTitle'),");
    c = replace(c, /message: `確定要刪除文件 "\$\{doc\.title\}" 嗎？`,/.source, "SKIP");
    // 用字串方式處理
    c = c.replace("message: `確定要刪除文件 \"${doc.title}\" 嗎？`,",
        "message: Locale.t('documentWall.deleteConfirmMessage', { title: doc.title }),");
    totalReplacements++;
    c = replace(c, "confirmText: '確定',", "confirmText: Locale.t('documentWall.confirmBtn'),");
    c = replace(c, "cancelText: '取消',", "cancelText: Locale.t('documentWall.cancelBtn'),");
    c = replace(c, "title: '再次確認',", "title: Locale.t('documentWall.doubleConfirmTitle'),");
    c = replace(c, "message: '請輸入 \"是\" 以確認刪除操作：',",
        "message: Locale.t('documentWall.doubleConfirmMessage'),");
    c = replace(c, "placeholder: '是',", "placeholder: Locale.t('documentWall.doubleConfirmPlaceholder'),");
    c = replace(c, "validate: (value) => value === '是',",
        "validate: (value) => value === Locale.t('documentWall.doubleConfirmKeyword'),");
    c = replace(c, "confirmText: '確認刪除',", "confirmText: Locale.t('documentWall.doubleConfirmBtn'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 14 replacements`); }
}

// ======== DocumentCard ========
function migrateDocumentCard() {
    const file = 'layout/DocumentWall/DocumentCard.js';
    let c = readFile(file);
    const orig = c;

    c = addLocaleImport(c, 2);

    c = replace(c, "const editBtn = createBtn('編輯',", "const editBtn = createBtn(Locale.t('documentWall.editBtn'),");
    c = replace(c, "const descBtn = createBtn('說明',", "const descBtn = createBtn(Locale.t('documentWall.descBtn'),");
    c = replace(c, "const downloadBtn = createBtn('下載',", "const downloadBtn = createBtn(Locale.t('documentWall.downloadBtn'),");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: 3 replacements`); }
}

// ======== MultiSelectDropdown — 計數標籤 ========
function fixMultiSelectCount() {
    const file = 'form/MultiSelectDropdown/MultiSelectDropdown.js';
    let c = readFile(file);
    const orig = c;

    c = replace(c, "countLabel.textContent = `已選 ${modalValues.size}${max} 項`;",
        "countLabel.textContent = Locale.t('multiSelect.selectedCount', { count: modalValues.size, max });");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: count label fixed`); }
}

// ======== DatePicker — 民國年 ========
function fixDatePickerRoc() {
    const file = 'form/DatePicker/DatePicker.js';
    let c = readFile(file);
    const orig = c;

    c = replace(c, "return `民國 ${year - 1911} 年`;",
        "return Locale.t('datePicker.rocYear', { year: year - 1911 });");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: ROC year fixed`); }
}

// ======== Breadcrumb — aria-label ========
function fixBreadcrumbAria() {
    const file = 'common/Breadcrumb/Breadcrumb.js';
    let c = readFile(file);
    const orig = c;

    c = replace(c, "container.setAttribute('aria-label', '麵包屑導航');",
        "container.setAttribute('aria-label', 'breadcrumb');");

    if (c !== orig) { writeFile(file, c); modifiedFiles++; report.push(`✓ ${file}: aria-label fixed`); }
}

// ======== 執行 ========
console.log('🌐 開始 i18n 遷移 Phase 2...\n');

fixSimpleDialog();
fixModalPanel();
migratePhotoWall();
migrateDocumentWall();
migrateDocumentCard();
fixMultiSelectCount();
fixDatePickerRoc();
fixBreadcrumbAria();

console.log('\n📊 Phase 2 報告:');
report.forEach(r => console.log(`  ${r}`));
console.log(`\n✅ 完成: ${modifiedFiles} 個檔案修改, ${totalReplacements} 處替換`);
