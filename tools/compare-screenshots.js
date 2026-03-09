#!/usr/bin/env node
/**
 * 截圖比對工具
 * 比較修改前後截圖的檔案大小差異，快速發現視覺異常
 */
import fs from 'fs';
import path from 'path';

const BEFORE_DIR = path.resolve('docs/manuals/screenshots/before');
const AFTER_DIR = path.resolve('docs/manuals/screenshots/after');

const beforeFiles = fs.readdirSync(BEFORE_DIR).filter(f => f.endsWith('.png'));
const afterFiles = fs.readdirSync(AFTER_DIR).filter(f => f.endsWith('.png'));

console.log(`\n📊 截圖比對報告`);
console.log(`修改前：${beforeFiles.length} 張  |  修改後：${afterFiles.length} 張\n`);

let identical = 0;
let similar = 0;
let different = 0;
const diffs = [];

for (const file of beforeFiles) {
    const beforePath = path.join(BEFORE_DIR, file);
    const afterPath = path.join(AFTER_DIR, file);

    if (!fs.existsSync(afterPath)) {
        console.log(`  ❌ ${file} — 修改後缺少`);
        different++;
        continue;
    }

    const beforeBuf = fs.readFileSync(beforePath);
    const afterBuf = fs.readFileSync(afterPath);

    if (beforeBuf.equals(afterBuf)) {
        identical++;
        console.log(`  ✅ ${file} — 完全相同`);
    } else {
        const sizeDiff = Math.abs(afterBuf.length - beforeBuf.length);
        const pct = ((sizeDiff / beforeBuf.length) * 100).toFixed(1);

        if (pct < 5) {
            similar++;
            console.log(`  🔸 ${file} — 微小差異 (${pct}%)`);
        } else {
            different++;
            diffs.push({ file, pct, beforeSize: beforeBuf.length, afterSize: afterBuf.length });
            console.log(`  ⚠️  ${file} — 差異 ${pct}% (${beforeBuf.length} → ${afterBuf.length} bytes)`);
        }
    }
}

console.log(`\n${'='.repeat(50)}`);
console.log(`  完全相同：${identical}  微小差異：${similar}  顯著差異：${different}`);
console.log(`${'='.repeat(50)}\n`);

if (diffs.length > 0) {
    console.log(`⚠️  以下截圖有顯著差異，建議人工檢查：`);
    for (const d of diffs) {
        console.log(`  - ${d.file} (${d.pct}%)`);
    }
    console.log('');
}
