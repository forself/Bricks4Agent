import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

import {
    buildMetadataArtifacts,
    validateManifestMap,
} from '../../ui_components/metadata/index.js';

const testDir = path.dirname(fileURLToPath(import.meta.url));
const browserRoot = path.resolve(testDir, '..', '..');
const metadataRoot = path.join(browserRoot, 'ui_components', 'metadata');

function readJson(relativePath) {
    return JSON.parse(readFileSync(path.join(browserRoot, relativePath), 'utf8'));
}

describe('component metadata', () => {
    it('covers every ComponentFactory registry entry with a valid manifest', () => {
        const artifacts = buildMetadataArtifacts(browserRoot);
        const validation = validateManifestMap(artifacts.manifestMap, artifacts.introspection, browserRoot);

        expect(artifacts.introspection.registryNames.length).toBeGreaterThan(0);
        expect(validation.missingRegistryEntries).toEqual([]);
        expect(validation.invalidManifests).toEqual([]);
        expect(Object.keys(artifacts.manifestMap).sort()).toEqual(
            [...artifacts.introspection.registryNames].sort(),
        );
    });

    it('renders deterministic component catalog and generator support matrix', () => {
        const artifacts = buildMetadataArtifacts(browserRoot);
        const checkedCatalog = readJson('ui_components/metadata/component-catalog.json');
        const checkedMatrix = readJson('ui_components/metadata/generator-support-matrix.json');

        expect(checkedCatalog).toEqual(artifacts.componentCatalog);
        expect(checkedMatrix).toEqual(artifacts.generatorSupportMatrix);
    });

    it('keeps generator metadata aligned with FieldResolver and TriggerEngine', () => {
        const artifacts = buildMetadataArtifacts(browserRoot);
        const { componentCatalog, generatorSupportMatrix } = artifacts;
        const fieldDirectComponents = Object.values(componentCatalog.by_registry_name)
            .filter((entry) => entry.generator.usage_mode === 'field_direct')
            .map((entry) => entry.registry_name);

        expect(generatorSupportMatrix.page_type_support.dashboard.status).toBe('partial');
        expect(Object.keys(generatorSupportMatrix.trigger_support).sort()).toEqual(
            [...artifacts.introspection.triggerActions].sort(),
        );

        for (const componentName of fieldDirectComponents) {
            const supportedByFieldType = Object.values(generatorSupportMatrix.field_type_support)
                .some((entry) => entry.default_component === componentName || entry.alternative_components.includes(componentName));
            expect(supportedByFieldType).toBe(true);
        }
    });

    it('stores generated metadata artifacts under the metadata root', () => {
        expect(path.join(metadataRoot, 'component-catalog.json')).toContain('ui_components');
        expect(path.join(metadataRoot, 'generator-support-matrix.json')).toContain('metadata');
    });
});
