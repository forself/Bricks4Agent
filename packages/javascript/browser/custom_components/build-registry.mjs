#!/usr/bin/env node

import fs from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import { analyzeCustomComponentDefinition } from './CustomComponentDefinition.js';

const moduleDir = path.dirname(fileURLToPath(import.meta.url));

function normalizeForComparison(value) {
    return value.replaceAll('\r\n', '\n');
}

export function stableStringify(value) {
    return `${JSON.stringify(value, null, 2)}\n`;
}

function isPathInside(rootPath, targetPath) {
    const relative = path.relative(rootPath, targetPath);
    return relative === '' || (
        relative !== '..' &&
        !relative.startsWith(`..${path.sep}`) &&
        !path.isAbsolute(relative)
    );
}

async function assertNonSymlinkDirectory(directoryPath, label) {
    const stats = await fs.lstat(directoryPath);
    if (stats.isSymbolicLink()) {
        throw new Error(`${label} must not be a symbolic link: ${directoryPath}`);
    }
    if (!stats.isDirectory()) {
        throw new Error(`${label} must be a directory: ${directoryPath}`);
    }
}

async function resolveBuildPaths(options) {
    const customRoot = path.resolve(options.customRoot ?? moduleDir);
    await assertNonSymlinkDirectory(customRoot, 'Custom component root');
    const customRootReal = await fs.realpath(customRoot);

    const definitionsDir = path.resolve(options.definitionsDir ?? path.join(customRoot, 'definitions'));
    if (!isPathInside(customRoot, definitionsDir)) {
        throw new Error(`Definitions directory escapes the custom component root: ${definitionsDir}`);
    }
    await assertNonSymlinkDirectory(definitionsDir, 'Definitions directory');
    const definitionsDirReal = await fs.realpath(definitionsDir);
    if (!isPathInside(customRootReal, definitionsDirReal)) {
        throw new Error(`Definitions directory realpath escapes the custom component root: ${definitionsDirReal}`);
    }

    const registryPath = path.resolve(options.registryPath ?? path.join(customRoot, 'registry.json'));
    if (!isPathInside(customRoot, registryPath)) {
        throw new Error(`Registry output escapes the custom component root: ${registryPath}`);
    }
    const registryParent = path.dirname(registryPath);
    await assertNonSymlinkDirectory(registryParent, 'Registry output directory');
    const registryParentReal = await fs.realpath(registryParent);
    if (!isPathInside(customRootReal, registryParentReal)) {
        throw new Error(`Registry output directory realpath escapes the custom component root: ${registryParentReal}`);
    }
    try {
        const registryStats = await fs.lstat(registryPath);
        if (registryStats.isSymbolicLink() || !registryStats.isFile()) {
            throw new Error(`Registry output must be a regular file: ${registryPath}`);
        }
    } catch (error) {
        if (error?.code !== 'ENOENT') throw error;
    }

    return {
        customRoot,
        customRootReal,
        definitionsDir,
        definitionsDirReal,
        registryPath,
    };
}

async function loadBuiltinNames(options, customRoot) {
    if (options.builtinNames !== undefined) {
        return new Set(options.builtinNames);
    }

    const catalogPath = path.resolve(
        options.builtinCatalogPath ?? path.join(customRoot, '..', 'ui_components', 'metadata', 'component-catalog.json'),
    );
    const catalog = JSON.parse(await fs.readFile(catalogPath, 'utf8'));
    if (catalog?.by_registry_name && typeof catalog.by_registry_name === 'object') {
        return new Set(Object.keys(catalog.by_registry_name));
    }
    if (Array.isArray(catalog?.components)) {
        return new Set(catalog.components.map((entry) => entry?.registry_name).filter(Boolean));
    }
    throw new Error(`Built-in component catalog has no registry index: ${catalogPath}`);
}

async function readDefinitionFiles(paths) {
    const entries = await fs.readdir(paths.definitionsDir, { withFileTypes: true });
    const jsonEntries = [];

    for (const entry of entries) {
        if (entry.isSymbolicLink()) {
            throw new Error(`Symbolic links are forbidden in definitions/: ${entry.name}`);
        }
        if (entry.isFile() && entry.name.endsWith('.json')) {
            jsonEntries.push(entry.name);
        }
    }
    jsonEntries.sort((left, right) => left.localeCompare(right));

    const records = [];
    for (const fileName of jsonEntries) {
        const filePath = path.join(paths.definitionsDir, fileName);
        const stats = await fs.lstat(filePath);
        if (stats.isSymbolicLink() || !stats.isFile()) {
            throw new Error(`Definition must be a regular file: ${filePath}`);
        }

        const fileRealPath = await fs.realpath(filePath);
        if (!isPathInside(paths.definitionsDirReal, fileRealPath)) {
            throw new Error(`Definition realpath escapes definitions/: ${filePath}`);
        }

        let definition;
        try {
            definition = JSON.parse(await fs.readFile(fileRealPath, 'utf8'));
        } catch (error) {
            throw new Error(`Invalid JSON definition ${fileName}: ${error instanceof Error ? error.message : String(error)}`);
        }

        records.push({
            definition,
            fileName,
            filePath,
            relativePath: path.relative(paths.customRoot, filePath).replaceAll('\\', '/'),
        });
    }

    if (records.length === 0) {
        throw new Error(`No custom component definitions found in ${paths.definitionsDir}`);
    }
    return records;
}

function indexDefinitions(records, builtinNames) {
    const byComponentId = new Map();
    const byRegistryName = new Map();

    for (const record of records) {
        const { definition, fileName } = record;
        if (typeof definition?.component_id === 'string') {
            if (byComponentId.has(definition.component_id)) {
                throw new Error(
                    `Duplicate component_id "${definition.component_id}" in ${byComponentId.get(definition.component_id).fileName} and ${fileName}`,
                );
            }
            byComponentId.set(definition.component_id, record);
        }
        if (typeof definition?.registry_name === 'string') {
            if (byRegistryName.has(definition.registry_name)) {
                throw new Error(
                    `Duplicate registry_name "${definition.registry_name}" in ${byRegistryName.get(definition.registry_name).fileName} and ${fileName}`,
                );
            }
            if (builtinNames.has(definition.registry_name)) {
                throw new Error(`Custom registry_name collides with a built-in component: ${definition.registry_name}`);
            }
            byRegistryName.set(definition.registry_name, record);
        }
    }

    return {
        byComponentId,
        byRegistryName,
        resolve(reference) {
            return byRegistryName.get(reference)?.definition ?? byComponentId.get(reference)?.definition ?? null;
        },
    };
}

function formatAnalysisErrors(record, analysis) {
    return analysis.errors.map((error) => {
        const definition = error.definition ? ` [${error.definition}]` : '';
        return `${record.fileName}${definition} ${error.path}: ${error.code} ${error.message}`;
    });
}

/**
 * Read, validate, analyze, and normalize definitions into a deterministic registry object.
 */
export async function buildRegistry(options = {}) {
    const paths = await resolveBuildPaths(options);
    const builtinNames = await loadBuiltinNames(options, paths.customRoot);
    const records = await readDefinitionFiles(paths);
    const index = indexDefinitions(records, builtinNames);
    const normalizedEntries = [];
    const failures = [];

    for (const record of records) {
        const analysis = analyzeCustomComponentDefinition(record.definition, {
            builtinNames,
            resolveCustom: (reference) => index.resolve(reference),
        });
        if (!analysis.valid) {
            failures.push(...formatAnalysisErrors(record, analysis));
            continue;
        }

        normalizedEntries.push({
            component_id: record.definition.component_id,
            registry_name: record.definition.registry_name,
            display_name: record.definition.display_name,
            version: record.definition.version,
            kind: record.definition.kind,
            path: record.relativePath,
            metrics: {
                max_depth: analysis.max_depth,
                atomic_leaf_count: analysis.atomic_leaf_count,
                custom_reference_count: analysis.custom_reference_count,
                composite_reference_count: analysis.composite_reference_count,
                unresolved: analysis.unresolved,
                cycles: analysis.cycles,
            },
        });
    }

    if (failures.length > 0) {
        throw new Error(`Custom component validation failed:\n${failures.map((failure) => `- ${failure}`).join('\n')}`);
    }

    normalizedEntries.sort((left, right) => (
        left.component_id.localeCompare(right.component_id) ||
        left.registry_name.localeCompare(right.registry_name)
    ));
    const registryIndex = Object.fromEntries(
        [...normalizedEntries]
            .sort((left, right) => left.registry_name.localeCompare(right.registry_name))
            .map((entry) => [entry.registry_name, entry]),
    );

    return {
        registry: {
            schema_version: 1,
            component_count: normalizedEntries.length,
            components: normalizedEntries,
            by_registry_name: registryIndex,
        },
        registryPath: paths.registryPath,
        sourceFiles: records.map((record) => record.filePath),
    };
}

/**
 * Build and either write registry.json or compare it byte-for-byte in check mode.
 */
export async function runRegistryBuild(options = {}) {
    const result = await buildRegistry(options);
    const expected = stableStringify(result.registry);

    if (options.checkOnly) {
        let actual;
        try {
            actual = await fs.readFile(result.registryPath, 'utf8');
        } catch (error) {
            if (error?.code === 'ENOENT') {
                throw new Error(`Missing generated registry: ${result.registryPath}`);
            }
            throw error;
        }
        if (normalizeForComparison(actual) !== expected) {
            throw new Error(`Generated registry is out of date: ${result.registryPath}`);
        }
        return result.registry;
    }

    await fs.writeFile(result.registryPath, expected, 'utf8');
    return result.registry;
}

function parseCliArgs(argv) {
    const options = {};
    for (let index = 0; index < argv.length; index += 1) {
        const arg = argv[index];
        if (arg === '--check') {
            options.checkOnly = true;
            continue;
        }
        const keyMap = {
            '--root': 'customRoot',
            '--definitions': 'definitionsDir',
            '--registry': 'registryPath',
            '--catalog': 'builtinCatalogPath',
        };
        const key = keyMap[arg];
        if (!key) throw new Error(`Unknown argument: ${arg}`);
        const value = argv[index + 1];
        if (!value) throw new Error(`Missing value for ${arg}`);
        options[key] = path.resolve(value);
        index += 1;
    }
    return options;
}

const invokedPath = process.argv[1] ? path.resolve(process.argv[1]) : '';
const modulePath = path.resolve(fileURLToPath(import.meta.url));
if (invokedPath.toLowerCase() === modulePath.toLowerCase()) {
    runRegistryBuild(parseCliArgs(process.argv.slice(2)))
        .then((registry) => {
            const action = process.argv.includes('--check') ? 'validated' : 'generated';
            process.stdout.write(`Custom component registry ${action} (${registry.component_count} components).\n`);
        })
        .catch((error) => {
            process.stderr.write(`${error instanceof Error ? error.stack ?? error.message : String(error)}\n`);
            process.exitCode = 1;
        });
}
