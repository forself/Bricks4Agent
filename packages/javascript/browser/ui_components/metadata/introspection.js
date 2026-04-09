import fs from 'node:fs';
import path from 'node:path';

import { AvailableComponents } from '../../page-generator/PageDefinition.js';

const SUPPLEMENTAL_COMPONENTS = ['DateTimeInput', 'GeolocationService', 'WeatherService'];
const SEARCH_CATEGORIES = ['common', 'form', 'input', 'layout', 'social', 'editor', 'data', 'viz', 'utils'];

function normalizeRelative(browserRoot, absolutePath) {
    return path.relative(browserRoot, absolutePath).replaceAll('\\', '/');
}

function walkFiles(dirPath) {
    const entries = fs.readdirSync(dirPath, { withFileTypes: true });
    const files = [];

    for (const entry of entries) {
        const fullPath = path.join(dirPath, entry.name);
        if (entry.isDirectory()) {
            files.push(...walkFiles(fullPath));
            continue;
        }

        files.push(fullPath);
    }

    return files;
}

function parseRegistryNames(componentFactorySource) {
    return [...componentFactorySource.matchAll(/'([^']+)':\s*[A-Za-z0-9_]+,/g)]
        .map((match) => match[1])
        .filter((value, index, array) => array.indexOf(value) === index);
}

function parseTriggerActions(triggerSource) {
    return [...triggerSource.matchAll(/registerAction\('([^']+)'/g)].map((match) => match[1]);
}

function findMethodBody(sourceText, methodName) {
    const definitionPattern = new RegExp(`^\\s*${methodName}\\s*\\(`, 'm');
    const definitionMatch = definitionPattern.exec(sourceText);
    const startIndex = definitionMatch?.index ?? -1;
    if (startIndex < 0) {
        return '';
    }

    const bodyStart = sourceText.indexOf('{', startIndex);
    if (bodyStart < 0) {
        return '';
    }

    let depth = 1;
    for (let index = bodyStart + 1; index < sourceText.length; index += 1) {
        const char = sourceText[index];
        if (char === '{') {
            depth += 1;
        } else if (char === '}') {
            depth -= 1;
            if (depth === 0) {
                return sourceText.slice(bodyStart + 1, index);
            }
        }
    }

    return '';
}

function parseFieldTypeCreators(fieldResolverSource) {
    return [...fieldResolverSource.matchAll(/_typeMap\.set\('([^']+)',\s*\(def\)\s*=>\s*this\._create([A-Za-z0-9_]+)/g)]
        .map((match) => ({
            fieldType: match[1],
            creatorMethod: `_create${match[2]}`,
        }));
}

function resolveCreatorComponents(fieldResolverSource, creatorMethods) {
    const creatorComponents = new Map();

    for (const creatorMethod of creatorMethods) {
        const body = findMethodBody(fieldResolverSource, creatorMethod);
        const moduleMatch = body.match(/_getModule\('([^']+)'\)/);
        creatorComponents.set(creatorMethod, moduleMatch?.[1] ?? null);
    }

    return creatorComponents;
}

function buildSourceIndex(browserRoot) {
    const uiRoot = path.join(browserRoot, 'ui_components');
    const index = new Map();

    for (const category of SEARCH_CATEGORIES) {
        const categoryPath = path.join(uiRoot, category);
        if (!fs.existsSync(categoryPath)) {
            continue;
        }

        for (const filePath of walkFiles(categoryPath)) {
            if (!filePath.endsWith('.js')) {
                continue;
            }
            if (filePath.endsWith('.bak')) {
                continue;
            }
            if (path.basename(filePath) === 'index.js') {
                continue;
            }

            const fileName = path.basename(filePath, '.js');
            const entry = {
                category,
                absolutePath: filePath,
                relativePath: normalizeRelative(browserRoot, filePath),
            };

            if (!index.has(fileName)) {
                index.set(fileName, []);
            }

            index.get(fileName).push(entry);
        }
    }

    return index;
}

function resolveSourceEntry(sourceIndex, componentName) {
    const candidates = sourceIndex.get(componentName) ?? [];
    if (candidates.length === 0) {
        return null;
    }

    const ranked = [...candidates].sort((left, right) => {
        const leftSegments = left.relativePath.split('/').length;
        const rightSegments = right.relativePath.split('/').length;
        if (leftSegments !== rightSegments) {
            return leftSegments - rightSegments;
        }
        return left.relativePath.localeCompare(right.relativePath);
    });

    return ranked[0];
}

function resolveDocsPath(browserRoot, sourceEntry) {
    if (!sourceEntry) {
        return '';
    }

    const sourceDir = path.dirname(sourceEntry.absolutePath);
    const candidates = fs.readdirSync(sourceDir, { withFileTypes: true })
        .filter((entry) => entry.isFile() && /^README.*\.md$/i.test(entry.name))
        .map((entry) => normalizeRelative(browserRoot, path.join(sourceDir, entry.name)))
        .sort();

    return candidates[0] ?? '';
}

function extractOptionKeys(sourceText) {
    const match = sourceText.match(/this\.options\s*=\s*\{([\s\S]*?)\.\.\.options\s*\}/m);
    if (!match) {
        return [];
    }

    return [...match[1].matchAll(/^\s*([A-Za-z0-9_]+)\s*:/gm)]
        .map((entry) => entry[1])
        .filter((value) => value !== '...');
}

function extractListenerEvents(sourceText) {
    const listeners = new Set();

    if (/onChange/.test(sourceText)) listeners.add('change');
    if (/onBlur/.test(sourceText)) listeners.add('blur');
    if (/onComplete/.test(sourceText)) listeners.add('complete');
    if (/onClick/.test(sourceText)) listeners.add('click');

    return [...listeners].sort();
}

export function introspectBrowserMetadata(browserRoot) {
    const componentFactoryPath = path.join(browserRoot, 'ui_components', 'binding', 'ComponentFactory.js');
    const fieldResolverPath = path.join(browserRoot, 'page-generator', 'FieldResolver.js');
    const triggerEnginePath = path.join(browserRoot, 'page-generator', 'TriggerEngine.js');

    const componentFactorySource = fs.readFileSync(componentFactoryPath, 'utf8');
    const fieldResolverSource = fs.readFileSync(fieldResolverPath, 'utf8');
    const triggerSource = fs.readFileSync(triggerEnginePath, 'utf8');

    const registryNames = parseRegistryNames(componentFactorySource);
    const triggerActions = parseTriggerActions(triggerSource);
    const fieldTypeCreators = parseFieldTypeCreators(fieldResolverSource);
    const creatorComponents = resolveCreatorComponents(
        fieldResolverSource,
        [...new Set(fieldTypeCreators.map((entry) => entry.creatorMethod))],
    );

    const fieldTypeSupport = {};
    for (const { fieldType, creatorMethod } of fieldTypeCreators) {
        fieldTypeSupport[fieldType] = {
            creator_method: creatorMethod,
            default_component: creatorComponents.get(creatorMethod),
        };
    }

    const sourceIndex = buildSourceIndex(browserRoot);
    const allKnownComponents = [...new Set([...registryNames, ...SUPPLEMENTAL_COMPONENTS])].sort();
    const componentLocations = Object.fromEntries(
        allKnownComponents.map((componentName) => {
            const sourceEntry = resolveSourceEntry(sourceIndex, componentName);
            return [
                componentName,
                sourceEntry
                    ? {
                        category: sourceEntry.category,
                        source_path: sourceEntry.relativePath,
                        docs_path: resolveDocsPath(browserRoot, sourceEntry),
                        source_text: fs.readFileSync(sourceEntry.absolutePath, 'utf8'),
                        option_keys: extractOptionKeys(fs.readFileSync(sourceEntry.absolutePath, 'utf8')),
                        listener_events: extractListenerEvents(fs.readFileSync(sourceEntry.absolutePath, 'utf8')),
                    }
                    : null,
            ];
        }),
    );

    const definitionExplicitNames = [...new Set([
        ...AvailableComponents.custom,
        ...AvailableComponents.packages,
    ])].filter((name) => allKnownComponents.includes(name));

    return {
        registryNames,
        supplementalNames: SUPPLEMENTAL_COMPONENTS,
        triggerActions,
        fieldTypeSupport,
        componentLocations,
        definitionExplicitNames,
    };
}
