import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import { verifyConsumerSnapshot } from '../create-project/template/scripts/snapshot-integrity.mjs';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const browserRoot = path.join(repoRoot, 'packages', 'javascript', 'browser');
const roots = [];
for (let index = 2; index < process.argv.length; index += 1) {
    if (process.argv[index] === '--consumer-root' && process.argv[index + 1]) {
        roots.push(process.argv[index + 1]);
        index += 1;
    }
}

if (roots.length === 0) {
    console.error('Usage: node tools/scripts/verify-consumer-snapshots.mjs --consumer-root <published-root> [...]');
    process.exit(2);
}

const treeResult = spawnSync('git', ['-C', repoRoot, 'rev-parse', 'HEAD^{tree}'], { encoding: 'utf8' });
if (treeResult.status !== 0) {
    console.error('Cannot resolve the current Bricks4Agent Git tree.');
    process.exit(2);
}
const expectedTree = treeResult.stdout.trim();

let failed = 0;
for (const consumerRoot of roots) {
    try {
        const result = verifyConsumerSnapshot({ browserRoot, consumerRoot, expectedTree });
        console.log(`${result.valid ? 'PASS' : 'FAIL'} ${result.consumerRoot}`);
        for (const error of result.errors) console.log(`  - ${error}`);
        if (!result.valid) failed += 1;
    } catch (error) {
        failed += 1;
        console.log(`FAIL ${path.resolve(consumerRoot)}`);
        console.log(`  - ${error.message}`);
    }
}

process.exit(failed === 0 ? 0 : 1);
