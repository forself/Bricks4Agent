import fs from 'node:fs';
import path from 'node:path';

export const MANIFEST_SCHEMA_VERSION = 1;

export const COMPONENT_CATEGORIES = [
    'common',
    'form',
    'input',
    'layout',
    'social',
    'editor',
    'data',
    'viz',
    'utils',
];

export const COMPONENT_KINDS = [
    'atomic',
    'composite',
    'container',
    'visualizer',
    'service_bridge',
];

export const COMPONENT_MATURITY = [
    'stable',
    'beta',
    'legacy',
];

export const GENERATOR_USAGE_MODES = [
    'field_direct',
    'definition_explicit',
    'runtime_only',
    'manual_only',
];

function isStringArray(value) {
    return Array.isArray(value) && value.every((entry) => typeof entry === 'string');
}

function ensureRelativePath(browserRoot, relativePath) {
    if (!relativePath) {
        return true;
    }

    return fs.existsSync(path.join(browserRoot, relativePath));
}

export function validateManifest(manifest, context = {}) {
    const {
        browserRoot = '',
        triggerActions = [],
        registryNames = new Set(),
    } = context;

    const errors = [];

    if (manifest?.schema_version !== MANIFEST_SCHEMA_VERSION) {
        errors.push(`schema_version must be ${MANIFEST_SCHEMA_VERSION}`);
    }

    for (const field of ['component_id', 'registry_name', 'display_name', 'category', 'kind', 'source_path', 'maturity']) {
        if (typeof manifest?.[field] !== 'string' || manifest[field].trim() === '') {
            errors.push(`${field} is required`);
        }
    }

    if (manifest?.category && !COMPONENT_CATEGORIES.includes(manifest.category)) {
        errors.push(`unsupported category: ${manifest.category}`);
    }

    if (manifest?.kind && !COMPONENT_KINDS.includes(manifest.kind)) {
        errors.push(`unsupported kind: ${manifest.kind}`);
    }

    if (manifest?.maturity && !COMPONENT_MATURITY.includes(manifest.maturity)) {
        errors.push(`unsupported maturity: ${manifest.maturity}`);
    }

    if (!ensureRelativePath(browserRoot, manifest?.source_path)) {
        errors.push(`source_path not found: ${manifest?.source_path}`);
    }

    if (!ensureRelativePath(browserRoot, manifest?.docs_path)) {
        errors.push(`docs_path not found: ${manifest?.docs_path}`);
    }

    if (!manifest?.generator || typeof manifest.generator !== 'object') {
        errors.push('generator block is required');
    } else {
        if (typeof manifest.generator.usable !== 'boolean') {
            errors.push('generator.usable must be boolean');
        }

        if (!GENERATOR_USAGE_MODES.includes(manifest.generator.usage_mode)) {
            errors.push(`unsupported generator.usage_mode: ${manifest.generator.usage_mode}`);
        }

        if (!isStringArray(manifest.generator.supported_field_types)) {
            errors.push('generator.supported_field_types must be a string array');
        }

        if (!isStringArray(manifest.generator.supported_page_types)) {
            errors.push('generator.supported_page_types must be a string array');
        }

        if (typeof manifest.generator.definition_runtime !== 'boolean') {
            errors.push('generator.definition_runtime must be boolean');
        }
    }

    if (!manifest?.composition || typeof manifest.composition !== 'object') {
        errors.push('composition block is required');
    } else {
        for (const field of ['role', 'requires_form_field_wrapper', 'manual_only']) {
            if (!(field in manifest.composition)) {
                errors.push(`composition.${field} is required`);
            }
        }
        if (typeof manifest.composition.role !== 'string' || manifest.composition.role.trim() === '') {
            errors.push('composition.role must be a string');
        }
        if (typeof manifest.composition.requires_form_field_wrapper !== 'boolean') {
            errors.push('composition.requires_form_field_wrapper must be boolean');
        }
        if (typeof manifest.composition.manual_only !== 'boolean') {
            errors.push('composition.manual_only must be boolean');
        }
    }

    if (!manifest?.binding || typeof manifest.binding !== 'object') {
        errors.push('binding block is required');
    } else {
        if (typeof manifest.binding.value_io !== 'boolean') {
            errors.push('binding.value_io must be boolean');
        }
        if (!isStringArray(manifest.binding.listener_events)) {
            errors.push('binding.listener_events must be a string array');
        }
        if (!isStringArray(manifest.binding.target_actions)) {
            errors.push('binding.target_actions must be a string array');
        } else {
            for (const action of manifest.binding.target_actions) {
                if (!triggerActions.includes(action)) {
                    errors.push(`binding.target_actions includes unsupported action: ${action}`);
                }
            }
        }
    }

    if (!manifest?.styling || typeof manifest.styling !== 'object') {
        errors.push('styling block is required');
    } else {
        if (typeof manifest.styling.theme_token_only !== 'boolean') {
            errors.push('styling.theme_token_only must be boolean');
        }
        if (!isStringArray(manifest.styling.style_knobs)) {
            errors.push('styling.style_knobs must be a string array');
        }
    }

    if (
        manifest?.generator?.usage_mode === 'field_direct' &&
        (!Array.isArray(manifest?.generator?.supported_field_types) || manifest.generator.supported_field_types.length === 0)
    ) {
        errors.push('field_direct components must declare supported_field_types');
    }

    if (
        registryNames.size > 0 &&
        !registryNames.has(manifest?.registry_name) &&
        manifest?.generator?.usage_mode !== 'field_direct'
    ) {
        errors.push(`registry_name is not present in ComponentFactory registry: ${manifest?.registry_name}`);
    }

    return {
        valid: errors.length === 0,
        errors,
    };
}

export function validateManifestMap(manifestMap, introspection, browserRoot) {
    const missingRegistryEntries = introspection.registryNames
        .filter((registryName) => !manifestMap[registryName])
        .sort();

    const invalidManifests = Object.values(manifestMap)
        .map((manifest) => ({
            registry_name: manifest.registry_name,
            ...validateManifest(manifest, {
                browserRoot,
                triggerActions: introspection.triggerActions,
                registryNames: new Set(introspection.registryNames),
            }),
        }))
        .filter((entry) => !entry.valid)
        .map(({ registry_name, errors }) => ({ registry_name, errors }));

    return {
        missingRegistryEntries,
        invalidManifests,
    };
}
