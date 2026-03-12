#!/usr/bin/env node
/**
 * Portable validation — JS import smoke + demo reference check.
 * No ripgrep dependency.
 */
import { readdirSync, readFileSync, statSync, existsSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

const repoRoot = path.resolve('D:/Bricks4Agent');
const uiRoot = path.join(repoRoot, 'packages', 'javascript', 'browser', 'ui_components');

function walkFiles(dir, predicate, out = []) {
    for (const entry of readdirSync(dir)) {
        const fullPath = path.join(dir, entry);
        const stats = statSync(fullPath);
        if (stats.isDirectory()) {
            walkFiles(fullPath, predicate, out);
        } else if (predicate(fullPath)) {
            out.push(fullPath);
        }
    }
    return out;
}

// === 1. JS Import Smoke Test ===
console.log('=== JS Import Smoke Test ===');
const jsFiles = walkFiles(uiRoot, (fp) => fp.endsWith('.js') && !fp.endsWith('.bak'));
let importErrors = 0;

for (const fp of jsFiles) {
    try {
        await import(pathToFileURL(fp).href);
    } catch (e) {
        importErrors++;
        console.error(`FAIL: ${path.relative(repoRoot, fp)} => ${e.message}`);
    }
}
console.log(`Imported ${jsFiles.length} files, ${importErrors} errors\n`);

// === 2. Demo Reference Check ===
console.log('=== Demo Reference Check ===');
const demoFiles = walkFiles(uiRoot, (fp) => path.basename(fp) === 'demo.html');
const failures = [];
let checkedRefs = 0;

function normalizeRef(ref) {
    const n = ref.trim();
    if (!n) return null;
    if (n.startsWith('data:')) return null;
    if (n.startsWith('http://') || n.startsWith('https://')) return null;
    if (n.startsWith('mailto:') || n.startsWith('tel:')) return null;
    if (n.startsWith('#')) return null;
    return n.split('#')[0].split('?')[0];
}

for (const demoFile of demoFiles) {
    const source = readFileSync(demoFile, 'utf8');
    const refs = new Set();

    for (const m of source.matchAll(/\b(?:href|src)=["']([^"']+)["']/g)) {
        const r = normalizeRef(m[1]);
        if (r) refs.add(r);
    }
    for (const m of source.matchAll(/\bimport\s+(?:[^'"]+?\s+from\s+)?["']([^"']+)["']/g)) {
        const r = normalizeRef(m[1]);
        if (r) refs.add(r);
    }

    for (const ref of refs) {
        const targetPath = ref.startsWith('/')
            ? path.join(repoRoot, ref.slice(1).replaceAll('/', path.sep))
            : path.resolve(path.dirname(demoFile), ref);
        checkedRefs++;
        if (!existsSync(targetPath)) {
            failures.push({
                demoFile: path.relative(repoRoot, demoFile).replaceAll('\\', '/'),
                ref,
                targetPath
            });
        }
    }
}

if (failures.length > 0) {
    console.log('BROKEN REFERENCES:');
    failures.slice(0, 30).forEach((f) => {
        console.log(`  ${f.demoFile} -> ${f.ref}`);
    });
    if (failures.length > 30) {
        console.log(`  ...and ${failures.length - 30} more.`);
    }
}

console.log(`Checked ${demoFiles.length} demos, ${checkedRefs} references, ${failures.length} broken\n`);

// === Summary ===
if (importErrors > 0 || failures.length > 0) {
    console.log(`⚠ Validation failed: ${importErrors} import errors, ${failures.length} broken references.`);
    process.exitCode = 1;
} else {
    console.log('✓ All checks passed.');
}
