/**
 * PageGenerator 測試腳本
 *
 * 測試頁面生成器的完整流程
 * 執行方式: node --experimental-modules test-generator.js
 *
 * @module examples/test-generator
 */

import { PageGenerator, validateDefinition } from '../index.js';
import { DiaryEditorDefinition } from './DiaryEditorDefinition.js';
import * as fs from 'fs';
import * as path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

console.log('========================================');
console.log('PageGenerator 測試');
console.log('========================================\n');

// 1. 驗證頁面定義
console.log('1. 驗證頁面定義...');
const validation = validateDefinition(DiaryEditorDefinition);
if (validation.valid) {
    console.log('   ✓ 頁面定義有效\n');
} else {
    console.log('   ✗ 頁面定義無效:');
    validation.errors.forEach(err => console.log(`     - ${err}`));
    process.exit(1);
}

// 2. 生成頁面程式碼
console.log('2. 生成頁面程式碼...');
const generator = new PageGenerator({
    baseImportPath: '../../core/BasePage.js',
    usePackages: false
});

const result = generator.generate(DiaryEditorDefinition);

if (result.errors.length > 0) {
    console.log('   ✗ 生成失敗:');
    result.errors.forEach(err => console.log(`     - ${err}`));
    process.exit(1);
}

console.log('   ✓ 生成成功\n');

// 3. 輸出結果
const outputPath = path.join(__dirname, 'generated', 'DiaryEditorPage.js');
const outputDir = path.dirname(outputPath);

// 確保輸出目錄存在
if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true });
}

fs.writeFileSync(outputPath, result.code, 'utf8');
console.log(`3. 已輸出到: ${outputPath}\n`);

// 4. 顯示生成的程式碼統計
const lines = result.code.split('\n').length;
const size = Buffer.byteLength(result.code, 'utf8');
console.log('4. 程式碼統計:');
console.log(`   - 行數: ${lines}`);
console.log(`   - 大小: ${(size / 1024).toFixed(2)} KB`);

console.log('\n========================================');
console.log('測試完成');
console.log('========================================');
