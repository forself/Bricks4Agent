/**
 * PageGenerator 完整測試腳本
 *
 * 測試多個頁面定義的生成
 *
 * @module examples/test-all
 */

import { PageGenerator, validateDefinition } from '../index.js';
import { DiaryEditorDefinition } from './DiaryEditorDefinition.js';
import { ContactFormDefinition } from './ContactFormDefinition.js';
import * as fs from 'fs';
import * as path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const definitions = [
    { name: 'DiaryEditor', def: DiaryEditorDefinition },
    { name: 'ContactForm', def: ContactFormDefinition }
];

console.log('========================================');
console.log('PageGenerator 完整測試');
console.log('========================================\n');

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

console.log('\n總結:', allPassed ? '全部通過' : '有測試失敗');
console.log('========================================');

process.exit(allPassed ? 0 : 1);
