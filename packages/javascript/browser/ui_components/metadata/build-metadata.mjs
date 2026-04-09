import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import {
    buildMetadataArtifacts,
    createManifestSkeleton,
    introspectBrowserMetadata,
    resolveManifestRelativePath,
    validateManifestMap,
    writeJsonFile,
} from './index.js';

const metadataDir = path.dirname(fileURLToPath(import.meta.url));
const browserRoot = path.resolve(metadataDir, '..', '..');

const args = new Set(process.argv.slice(2));
const checkOnly = args.has('--check');

function stableStringify(value) {
    return `${JSON.stringify(value, null, 2)}\n`;
}

function ensureManifestFiles(introspection) {
    const expectedManifestPaths = new Set();

    for (const componentName of [...introspection.registryNames].sort()) {
        const location = introspection.componentLocations[componentName];
        if (!location) {
            throw new Error(`Missing component source for registry entry: ${componentName}`);
        }

        const manifestPath = path.join(
            browserRoot,
            resolveManifestRelativePath(location, componentName),
        );
        expectedManifestPaths.add(path.normalize(manifestPath));

        if (checkOnly) {
            if (!fs.existsSync(manifestPath)) {
                throw new Error(`Missing component manifest: ${manifestPath}`);
            }
            continue;
        }

        const manifest = createManifestSkeleton(introspection, componentName);
        writeJsonFile(manifestPath, manifest);
    }

    const existingManifestPaths = [];
    const uiComponentsRoot = path.join(browserRoot, 'ui_components');
    const queue = [uiComponentsRoot];
    while (queue.length > 0) {
        const currentPath = queue.pop();
        for (const entry of fs.readdirSync(currentPath, { withFileTypes: true })) {
            const fullPath = path.join(currentPath, entry.name);
            if (entry.isDirectory()) {
                queue.push(fullPath);
                continue;
            }
            if (entry.isFile() && entry.name.endsWith('.manifest.json')) {
                existingManifestPaths.push(path.normalize(fullPath));
            }
        }
    }

    for (const manifestPath of existingManifestPaths) {
        if (!expectedManifestPaths.has(manifestPath)) {
            fs.rmSync(manifestPath);
        }
    }
}

function verifyGeneratedFile(relativePath, value) {
    const absolutePath = path.join(browserRoot, relativePath);
    const expected = stableStringify(value);

    if (checkOnly) {
        if (!fs.existsSync(absolutePath)) {
            throw new Error(`Missing generated metadata file: ${relativePath}`);
        }

        const actual = fs.readFileSync(absolutePath, 'utf8');
        if (actual !== expected) {
            throw new Error(`Generated metadata file is out of date: ${relativePath}`);
        }
        return;
    }

    writeJsonFile(absolutePath, value);
}

const introspection = introspectBrowserMetadata(browserRoot);
ensureManifestFiles(introspection);

const artifacts = buildMetadataArtifacts(browserRoot);
const validation = validateManifestMap(artifacts.manifestMap, artifacts.introspection, browserRoot);
if (validation.missingRegistryEntries.length > 0 || validation.invalidManifests.length > 0) {
    throw new Error(`Metadata validation failed: ${JSON.stringify(validation, null, 2)}`);
}

verifyGeneratedFile(
    'ui_components/metadata/component-catalog.json',
    artifacts.componentCatalog,
);
verifyGeneratedFile(
    'ui_components/metadata/generator-support-matrix.json',
    artifacts.generatorSupportMatrix,
);

if (!checkOnly) {
    process.stdout.write('Component metadata artifacts generated.\n');
}
