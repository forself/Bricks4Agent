import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import {
    buildLibraryContentManifest,
    PUBLISHED_LIBRARIES,
    verifyConsumerSnapshot,
} from '../../create-project/template/scripts/snapshot-integrity.mjs';

function createFixture() {
    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-snapshot-test-'));
    const browserRoot = path.join(root, 'source');
    const libRoot = path.join(root, 'consumer', 'lib');
    for (const library of PUBLISHED_LIBRARIES) {
        mkdirSync(path.join(browserRoot, library), { recursive: true });
        mkdirSync(path.join(libRoot, library), { recursive: true });
        writeFileSync(path.join(browserRoot, library, 'index.js'), `export const name = '${library}';\n`);
        writeFileSync(path.join(libRoot, library, 'index.js'), `export const name = '${library}';\n`);
    }
    return { root, browserRoot, libRoot, consumerRoot: path.dirname(libRoot) };
}

function writeSnapshot(libRoot, browserRoot, overrides = {}) {
    const snapshot = {
        bricks4agent: { tree: 'fixture-tree', dirty: false },
        content: buildLibraryContentManifest(browserRoot),
        ...overrides,
    };
    writeFileSync(path.join(libRoot, 'SNAPSHOT.json'), `${JSON.stringify(snapshot, null, 2)}\n`);
}

test('consumer snapshot requires clean matching provenance and byte-identical libraries', () => {
    const fixture = createFixture();
    try {
        writeSnapshot(fixture.libRoot, fixture.browserRoot);
        const result = verifyConsumerSnapshot({
            browserRoot: fixture.browserRoot,
            consumerRoot: fixture.consumerRoot,
            expectedTree: 'fixture-tree',
        });
        assert.equal(result.valid, true);
    } finally {
        rmSync(fixture.root, { recursive: true, force: true });
    }
});

test('consumer snapshot rejects drift even when provenance text is unchanged', () => {
    const fixture = createFixture();
    try {
        writeSnapshot(fixture.libRoot, fixture.browserRoot);
        writeFileSync(path.join(fixture.libRoot, 'ui_components', 'index.js'), 'export const drift = true;\n');
        const result = verifyConsumerSnapshot({
            browserRoot: fixture.browserRoot,
            consumerRoot: fixture.consumerRoot,
            expectedTree: 'fixture-tree',
        });
        assert.equal(result.valid, false);
        assert(result.errors.some((error) => error.includes('content digest')));
        assert(result.errors.some((error) => error.startsWith('ui_components differs')));
    } finally {
        rmSync(fixture.root, { recursive: true, force: true });
    }
});

test('consumer snapshot rejects dirty or mismatched provenance', () => {
    const fixture = createFixture();
    try {
        writeSnapshot(fixture.libRoot, fixture.browserRoot, {
            bricks4agent: { tree: 'old-tree', dirty: true },
        });
        const result = verifyConsumerSnapshot({
            browserRoot: fixture.browserRoot,
            consumerRoot: fixture.consumerRoot,
            expectedTree: 'fixture-tree',
        });
        assert.equal(result.valid, false);
        assert(result.errors.some((error) => error.includes('dirty')));
        assert(result.errors.some((error) => error.includes('does not match expected tree')));
    } finally {
        rmSync(fixture.root, { recursive: true, force: true });
    }
});
