/**
 * PageGenerator 完整測試腳本
 *
 * 測試多個頁面定義的生成，包含 SPA 模式與 Packages 模式
 *
 * @module examples/test-all
 */

import { PageGenerator, ComponentPaths, validateDefinition } from '../index.js';
import { FieldTypes, PageTypes } from '../PageDefinition.js';
import { DiaryEditorDefinition } from './DiaryEditorDefinition.js';
import { ContactFormDefinition } from './ContactFormDefinition.js';
import * as fs from 'fs';
import * as path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// ========================================
// Phase 1: SPA 模式測試 (usePackages: false)
// ========================================

const definitions = [
    { name: 'DiaryEditor', def: DiaryEditorDefinition },
    { name: 'ContactForm', def: ContactFormDefinition }
];

console.log('========================================');
console.log('PageGenerator 完整測試');
console.log('========================================');

console.log('\n--- Phase 1: SPA 模式 (usePackages: false) ---\n');

const generator = new PageGenerator({
    baseImportPath: '../../core/BasePage.js',
    usePackages: false
});

const outputDir = path.join(__dirname, 'generated');
if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true });
}

let allPassed = true;
const results = [];

for (const { name, def } of definitions) {
    console.log(`\n--- 測試: ${name} ---`);

    // 1. 驗證
    console.log('1. 驗證頁面定義...');
    const validation = validateDefinition(def);
    if (!validation.valid) {
        console.log(`   ✗ 驗證失敗:`);
        validation.errors.forEach(err => console.log(`     - ${err}`));
        allPassed = false;
        results.push({ name, success: false, error: '驗證失敗' });
        continue;
    }
    console.log('   ✓ 驗證通過');

    // 2. 生成
    console.log('2. 生成程式碼...');
    const result = generator.generate(def);
    if (result.errors.length > 0) {
        console.log(`   ✗ 生成失敗:`);
        result.errors.forEach(err => console.log(`     - ${err}`));
        allPassed = false;
        results.push({ name, success: false, error: '生成失敗' });
        continue;
    }
    console.log('   ✓ 生成成功');

    // 3. 輸出檔案
    const outputPath = path.join(outputDir, `${def.name}.js`);
    fs.writeFileSync(outputPath, result.code, 'utf8');
    console.log(`3. 已輸出: ${path.basename(outputPath)}`);

    // 4. 統計
    const lines = result.code.split('\n').length;
    const size = Buffer.byteLength(result.code, 'utf8');
    console.log(`4. 統計: ${lines} 行, ${(size / 1024).toFixed(2)} KB`);

    results.push({
        name,
        success: true,
        lines,
        size: (size / 1024).toFixed(2) + ' KB'
    });
}

// ========================================
// Phase 2: Packages 模式測試 (usePackages: true)
// ========================================

console.log('\n\n--- Phase 2: Packages 模式 (usePackages: true) ---\n');

// 使用包含 packages 元件的定義
const PackagesTestDefinition = {
    name: 'PackagesTestPage',
    type: PageTypes.FORM,
    description: '測試 Packages 模式元件路徑',
    components: [
        'WebTextEditor',
        'DrawingBoard',
        'BasicButton',
        'ButtonGroup',
        'DateTimeInput',
        'AddressInput',
        'OrganizationInput'
    ],
    fields: [
        { name: 'content', type: FieldTypes.RICHTEXT, label: '內容', required: true },
        { name: 'sketch', type: FieldTypes.CANVAS, label: '草圖', required: false },
        { name: 'eventTime', type: FieldTypes.DATETIME, label: '時間', required: false },
        { name: 'location', type: FieldTypes.ADDRESS, label: '地址', required: false },
        { name: 'org', type: FieldTypes.ORGANIZATION, label: '組織', required: false }
    ],
    api: { baseUrl: '/api/test', endpoints: {} },
    behaviors: {}
};

const pkgGenerator = new PageGenerator({
    baseImportPath: '../../core/BasePage.js',
    usePackages: true
});

// 2a. 生成程式碼
console.log('1. 生成 Packages 模式程式碼...');
const pkgResult = pkgGenerator.generate(PackagesTestDefinition);
if (pkgResult.errors.length > 0) {
    console.log(`   ✗ 生成失敗:`);
    pkgResult.errors.forEach(err => console.log(`     - ${err}`));
    allPassed = false;
    results.push({ name: 'PackagesTest', success: false, error: '生成失敗' });
} else {
    console.log('   ✓ 生成成功');

    // 2b. 驗證 import 路徑指向實際存在的檔案
    console.log('2. 驗證元件路徑...');
    const uiComponentsRoot = path.resolve(__dirname, '..', '..', 'ui_components');
    const pathErrors = [];

    for (const comp of PackagesTestDefinition.components) {
        const compPath = ComponentPaths[comp];
        if (!compPath || !compPath.packages) {
            pathErrors.push(`${comp}: 缺少 packages 路徑映射`);
            continue;
        }

        // @component-library/ui_components/... → 轉換為實際檔案路徑
        const relativePath = compPath.packages.replace('@component-library/', '');
        const fullPath = path.resolve(__dirname, '..', '..', relativePath);

        if (!fs.existsSync(fullPath)) {
            pathErrors.push(`${comp}: 檔案不存在 → ${relativePath}`);
        } else {
            console.log(`   ✓ ${comp} → ${relativePath}`);
        }
    }

    if (pathErrors.length > 0) {
        console.log(`   ✗ 路徑驗證失敗:`);
        pathErrors.forEach(err => console.log(`     - ${err}`));
        allPassed = false;
        results.push({ name: 'PackagesTest', success: false, error: `${pathErrors.length} 個路徑錯誤` });
    } else {
        // 2c. 驗證生成的程式碼確實包含正確的 import 路徑
        console.log('3. 驗證生成程式碼的 import 語句...');
        const importErrors = [];

        for (const comp of PackagesTestDefinition.components) {
            const expectedPath = ComponentPaths[comp].packages;
            if (!pkgResult.code.includes(expectedPath)) {
                importErrors.push(`${comp}: import 路徑 "${expectedPath}" 未出現在生成程式碼中`);
            }
        }

        if (importErrors.length > 0) {
            console.log(`   ✗ import 驗證失敗:`);
            importErrors.forEach(err => console.log(`     - ${err}`));
            allPassed = false;
            results.push({ name: 'PackagesTest', success: false, error: `${importErrors.length} 個 import 錯誤` });
        } else {
            console.log('   ✓ 所有 import 路徑正確');

            const outputPath = path.join(outputDir, 'PackagesTestPage.js');
            fs.writeFileSync(outputPath, pkgResult.code, 'utf8');

            const lines = pkgResult.code.split('\n').length;
            const size = Buffer.byteLength(pkgResult.code, 'utf8');
            console.log(`4. 統計: ${lines} 行, ${(size / 1024).toFixed(2)} KB`);

            results.push({
                name: 'PackagesTest',
                success: true,
                lines,
                size: (size / 1024).toFixed(2) + ' KB'
            });
        }
    }
}

// ========================================
// Phase 3: ComponentPaths 完整性檢查
// ========================================

console.log('\n\n--- Phase 3: ComponentPaths 完整性檢查 ---\n');

const uiComponentsRoot = path.resolve(__dirname, '..', '..', 'ui_components');
let pathCheckPassed = true;

for (const [compName, paths] of Object.entries(ComponentPaths)) {
    if (!paths.packages) continue; // 跳過 SPA-only 元件

    const relativePath = paths.packages.replace('@component-library/', '');
    const fullPath = path.resolve(__dirname, '..', '..', relativePath);

    if (!fs.existsSync(fullPath)) {
        console.log(`✗ ${compName}: ${relativePath} (檔案不存在)`);
        pathCheckPassed = false;
        allPassed = false;
    } else {
        console.log(`✓ ${compName}: ${relativePath}`);
    }
}

if (pathCheckPassed) {
    console.log('\n   ✓ 所有 ComponentPaths 路徑有效');
} else {
    console.log('\n   ✗ 有路徑指向不存在的檔案');
}

// ========================================
// 測試摘要
// ========================================

console.log('\n========================================');
console.log('測試摘要');
console.log('========================================');

for (const r of results) {
    if (r.success) {
        console.log(`✓ ${r.name}: ${r.lines} 行, ${r.size}`);
    } else {
        console.log(`✗ ${r.name}: ${r.error}`);
    }
}
console.log(`✓ ComponentPaths: ${pathCheckPassed ? '全部有效' : '有無效路徑'}`);

console.log('\n總結:', allPassed ? '全部通過' : '有測試失敗');
console.log('========================================');

process.exit(allPassed ? 0 : 1);
