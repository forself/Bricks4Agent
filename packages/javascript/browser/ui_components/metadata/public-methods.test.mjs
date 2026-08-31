import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import {
    createManifestSkeleton,
    extractPublicMethodNames,
    introspectBrowserMetadata,
    validateManifest,
} from './index.js';

const metadataRoot = path.dirname(fileURLToPath(import.meta.url));
const browserRoot = path.resolve(metadataRoot, '..', '..');

test('public method extraction ignores child component calls', () => {
    const source = `export class Example {
    clear() { return this; }
    apply(value) {
        this.child.setValue(value);
        this.items.clear();
    }
}`;

    assert.deepEqual(extractPublicMethodNames(source), ['apply', 'clear']);
});

test('public method extraction supports multiline declarations', () => {
    const source = `class Example {
    async reload(
        options = {},
    ) {
        return options;
    }
}`;

    assert.deepEqual(extractPublicMethodNames(source), ['reload']);
});

test('EditableTable binding reflects its own public API only', () => {
    const introspection = introspectBrowserMetadata(browserRoot);
    const methods = introspection.componentLocations.EditableTable.public_methods;
    const manifest = createManifestSkeleton(introspection, 'EditableTable');

    assert.deepEqual(methods, ['destroy', 'getRows', 'mount', 'send', 'snapshot']);
    assert.equal(manifest.binding.value_io, false);
    assert.deepEqual(manifest.binding.target_actions, []);
});

test('manifest validation rejects binding capabilities absent from the component', () => {
    const result = validateManifest({
        schema_version: 1,
        component_id: 'layout.EditableTable',
        registry_name: 'EditableTable',
        display_name: 'EditableTable',
        category: 'layout',
        kind: 'composite',
        source_path: 'ui_components/layout/EditableTable/EditableTable.js',
        docs_path: '',
        maturity: 'stable',
        generator: {
            usable: false,
            usage_mode: 'manual_only',
            supported_field_types: [],
            supported_page_types: [],
            definition_runtime: false,
        },
        composition: {
            role: 'data_view',
            requires_form_field_wrapper: false,
            manual_only: true,
        },
        binding: {
            value_io: true,
            listener_events: [],
            target_actions: ['clear', 'setValue'],
        },
        styling: { theme_token_only: true, style_knobs: [] },
    }, {
        browserRoot,
        triggerActions: ['clear', 'setValue'],
        registryNames: new Set(['EditableTable']),
        publicMethods: ['destroy', 'getRows', 'mount', 'send', 'snapshot'],
    });

    assert.equal(result.valid, false);
    assert(result.errors.includes('binding.value_io requires public getValue and setValue methods'));
    assert(result.errors.includes('binding.target_actions clear has no matching public method'));
    assert(result.errors.includes('binding.target_actions setValue has no matching public method'));
});
