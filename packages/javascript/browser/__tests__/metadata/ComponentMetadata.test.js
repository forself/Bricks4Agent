import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { beforeAll, describe, expect, it } from 'vitest';

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

// 三個測試共用同一份 artifacts，避免重複掃描整個元件庫
let artifacts;

describe('component metadata', () => {
    beforeAll(() => {
        artifacts = buildMetadataArtifacts(browserRoot);
    });

    it('covers every ComponentFactory registry entry with a valid manifest', () => {
        const validation = validateManifestMap(artifacts.manifestMap, artifacts.introspection, browserRoot);

        expect(artifacts.introspection.registryNames.length).toBeGreaterThan(0);
        expect(validation.missingRegistryEntries).toEqual([]);
        expect(validation.invalidManifests).toEqual([]);
        expect(Object.keys(artifacts.manifestMap).sort()).toEqual(
            [...artifacts.introspection.registryNames].sort(),
        );
    });

    it('renders deterministic component catalog and generator support matrix', () => {
        const checkedCatalog = readJson('ui_components/metadata/component-catalog.json');
        const checkedMatrix = readJson('ui_components/metadata/generator-support-matrix.json');

        expect(checkedCatalog).toEqual(artifacts.componentCatalog);
        expect(checkedMatrix).toEqual(artifacts.generatorSupportMatrix);
    });

    it('keeps generator metadata aligned with FieldResolver and TriggerEngine', () => {
        const { componentCatalog, generatorSupportMatrix } = artifacts;
        // by_registry_name 存索引，需經 components[] 還原 manifest
        for (const [registryName, index] of Object.entries(componentCatalog.by_registry_name)) {
            expect(componentCatalog.components[index].registry_name).toBe(registryName);
        }
        const fieldDirectComponents = Object.values(componentCatalog.by_registry_name)
            .map((index) => componentCatalog.components[index])
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
