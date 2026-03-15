import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const repoRoot = path.resolve(import.meta.dirname, '..', '..');
const canonicalPath = path.join(repoRoot, 'packages', 'csharp', 'database', 'BaseOrm', 'net8', 'BaseOrm.cs');
const mirrorPaths = [
    path.join(repoRoot, 'tools', 'spa-generator', 'backend', 'Data', 'BaseOrm.cs'),
    path.join(repoRoot, 'templates', 'spa', 'backend', 'Data', 'BaseOrm.cs')
];

function normalize(content) {
    return content.replace(/\r\n/g, '\n');
}

const canonicalContent = normalize(fs.readFileSync(canonicalPath, 'utf8'));
const mismatches = [];

for (const filePath of mirrorPaths) {
    const content = normalize(fs.readFileSync(filePath, 'utf8'));
    if (content !== canonicalContent) {
        mismatches.push(path.relative(repoRoot, filePath));
    }
}

if (mismatches.length > 0) {
    console.error('BaseOrm sync check failed.');
    console.error(`Canonical source: ${path.relative(repoRoot, canonicalPath)}`);
    for (const filePath of mismatches) {
        console.error(`Out of sync: ${filePath}`);
    }
    process.exit(1);
}

console.log('BaseOrm sync check passed.');
