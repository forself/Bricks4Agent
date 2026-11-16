import { Buffer } from 'node:buffer';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';

export const PUBLISHED_LIBRARIES = ['ui_components', 'page-generator', 'custom_components'];

function normalizedRelative(root, file) {
    return path.relative(root, file).replaceAll('\\', '/');
}

function shouldInclude(relativePath, library) {
    const segments = relativePath.split('/');
    if (segments.includes('node_modules')) return false;
    if (relativePath.endsWith('.test.mjs')) return false;
    if (library === 'page-generator' && segments[0] === 'examples') return false;
    return true;
}

export function buildDirectoryInventory(root, library = '') {
    if (!existsSync(root)) {
        throw new Error(`Snapshot directory does not exist: ${root}`);
    }

    const entries = [];
    const queue = [root];
    while (queue.length > 0) {
        const current = queue.pop();
        for (const entry of readdirSync(current, { withFileTypes: true })) {
            const fullPath = path.join(current, entry.name);
            if (entry.isDirectory()) {
                queue.push(fullPath);
                continue;
            }
            if (!entry.isFile()) continue;

            const relativePath = normalizedRelative(root, fullPath);
            if (!shouldInclude(relativePath, library)) continue;
            const body = readFileSync(fullPath);
            entries.push({
                path: relativePath,
                bytes: statSync(fullPath).size,
                sha256: createHash('sha256').update(body).digest('hex'),
            });
        }
    }

    // The digest must not depend on the host's ICU locale or operating system.
    entries.sort((left, right) => Buffer.compare(Buffer.from(left.path), Buffer.from(right.path)));
    const digest = createHash('sha256');
    for (const entry of entries) {
        digest.update(`${entry.path}\0${entry.bytes}\0${entry.sha256}\n`);
    }

    return {
        algorithm: 'sha256',
        fileCount: entries.length,
        totalBytes: entries.reduce((total, entry) => total + entry.bytes, 0),
        digest: digest.digest('hex'),
        entries,
    };
}

export function buildLibraryContentManifest(browserRoot) {
    const libraries = {};
    const combined = createHash('sha256');
    for (const library of PUBLISHED_LIBRARIES) {
        const inventory = buildDirectoryInventory(path.join(browserRoot, library), library);
        libraries[library] = {
            fileCount: inventory.fileCount,
            totalBytes: inventory.totalBytes,
            digest: inventory.digest,
            entries: inventory.entries,
        };
        combined.update(`${library}\0${inventory.digest}\n`);
    }

    return {
        algorithm: 'sha256',
        combinedDigest: combined.digest('hex'),
        libraries,
    };
}

export function compareManifestInventory(expectedEntries, actualInventory) {
    if (!Array.isArray(expectedEntries)) {
        return { equal: false, missing: actualInventory.entries.map(entry => entry.path),
            extra: [], changed: [] };
    }
    const expectedByPath = new Map();
    const invalid = [];
    for (const entry of expectedEntries) {
        if (!entry || Object.keys(entry).sort().join('|') !== 'bytes|path|sha256' ||
            typeof entry.path !== 'string' || entry.path.startsWith('/') ||
            entry.path.includes('\\') || entry.path.split('/').some(part => !part || part === '.' || part === '..') ||
            !Number.isSafeInteger(entry.bytes) || entry.bytes < 0 || !/^[a-f0-9]{64}$/.test(entry.sha256) ||
            expectedByPath.has(entry.path)) {
            invalid.push(String(entry?.path ?? '(invalid path)'));
            continue;
        }
        expectedByPath.set(entry.path, entry);
    }
    const actualByPath = new Map(actualInventory.entries.map(entry => [entry.path, entry]));
    const missing = [], extra = [], changed = [...invalid];
    for (const [file, expected] of expectedByPath) {
        const actual = actualByPath.get(file);
        if (!actual) missing.push(file);
        else if (actual.sha256 !== expected.sha256 || actual.bytes !== expected.bytes) changed.push(file);
    }
    for (const file of actualByPath.keys()) if (!expectedByPath.has(file)) extra.push(file);
    return { equal: !missing.length && !extra.length && !changed.length, missing, extra, changed };
}

export function compareInventories(source, published) {
    const sourceByPath = new Map(source.entries.map((entry) => [entry.path, entry]));
    const publishedByPath = new Map(published.entries.map((entry) => [entry.path, entry]));
    const missing = [];
    const extra = [];
    const changed = [];

    for (const [file, sourceEntry] of sourceByPath) {
        const publishedEntry = publishedByPath.get(file);
        if (!publishedEntry) missing.push(file);
        else if (sourceEntry.sha256 !== publishedEntry.sha256) changed.push(file);
    }
    for (const file of publishedByPath.keys()) {
        if (!sourceByPath.has(file)) extra.push(file);
    }

    return {
        equal: missing.length === 0 && extra.length === 0 && changed.length === 0,
        missing,
        extra,
        changed,
    };
}

function resolveLibRoot(consumerRoot) {
    const root = path.resolve(consumerRoot);
    const candidates = [path.join(root, 'lib'), path.join(root, 'wwwroot', 'lib'), root];
    const libRoot = candidates.find((candidate) =>
        existsSync(path.join(candidate, 'ui_components')) &&
        existsSync(path.join(candidate, 'SNAPSHOT.json')),
    );
    if (!libRoot) {
        throw new Error(`Cannot locate lib/SNAPSHOT.json and lib/ui_components under ${root}`);
    }
    return libRoot;
}

export function verifyConsumerSnapshot({ browserRoot, consumerRoot, expectedTree }) {
    const libRoot = resolveLibRoot(consumerRoot);
    const snapshotPath = path.join(libRoot, 'SNAPSHOT.json');
    const snapshot = JSON.parse(readFileSync(snapshotPath, 'utf8'));
    const errors = [];
    const details = {};
    const policyTrimmed = typeof snapshot.publishPolicy === 'string' && snapshot.publishPolicy.length > 0;
    const legacyFullSnapshot = !policyTrimmed && PUBLISHED_LIBRARIES.every((library) =>
        snapshot.content?.libraries?.[library] &&
        !Object.hasOwn(snapshot.content.libraries[library], 'entries'));
    if (Object.hasOwn(snapshot, 'publishPolicy') && snapshot.publishPolicy !== null && !policyTrimmed)
        errors.push('SNAPSHOT.json publish policy marker is invalid');

    const recordedTree = snapshot?.bricks4agent?.tree ?? '';
    if (snapshot?.bricks4agent?.dirty !== false) {
        errors.push('snapshot provenance is dirty or does not explicitly declare dirty=false');
    }
    if (!recordedTree || recordedTree !== expectedTree) {
        errors.push(`snapshot tree ${recordedTree || '(missing)'} does not match expected tree ${expectedTree}`);
    }

    const actualContent = buildLibraryContentManifest(libRoot);
    if (!snapshot.content || snapshot.content.combinedDigest !== actualContent.combinedDigest) {
        errors.push('SNAPSHOT.json content digest is missing or does not match the published files');
    }

    for (const library of PUBLISHED_LIBRARIES) {
        const source = buildDirectoryInventory(path.join(browserRoot, library), library);
        const published = buildDirectoryInventory(path.join(libRoot, library), library);
        const comparison = compareInventories(source, published);
        details[library] = comparison;
        if (legacyFullSnapshot) {
            details[library].manifest = { equal: true, legacy: true };
        } else {
            const expectedManifest = compareManifestInventory(snapshot.content?.libraries?.[library]?.entries, published);
            details[library].manifest = expectedManifest;
            if (!expectedManifest.equal) {
                errors.push(`${library} does not match the file inventory recorded in SNAPSHOT.json: missing=${expectedManifest.missing.length}, extra=${expectedManifest.extra.length}, changed=${expectedManifest.changed.length}`);
            }
        }
        if (policyTrimmed ? (comparison.extra.length > 0 || comparison.changed.length > 0) : !comparison.equal) {
            errors.push(`${library} differs: missing=${comparison.missing.length}, extra=${comparison.extra.length}, changed=${comparison.changed.length}`);
        }
    }

    return {
        valid: errors.length === 0,
        consumerRoot: path.resolve(consumerRoot),
        snapshotPath,
        recordedTree,
        expectedTree,
        errors,
        details,
    };
}
