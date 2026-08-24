const TOP_LEVEL_KEYS = new Set([
    '$schema',
    'schema_version',
    'component_id',
    'registry_name',
    'display_name',
    'version',
    'kind',
    'description',
    'root',
]);

const LEAF_NODE_KEYS = new Set(['type', 'id', 'component', 'options']);
const GROUP_NODE_KEYS = new Set([
    'type',
    'id',
    'layout',
    'class_names',
    'aria_label',
    'children',
]);
const LAYOUT_KEYS = new Set(['mode', 'gap', 'columns', 'align']);
const KINDS = new Set(['atomic', 'composite', 'template']);
const LAYOUT_MODES = new Set(['stack', 'row', 'grid']);
const LAYOUT_GAPS = new Set(['none', 'xs', 'sm', 'md', 'lg', 'xl']);
const LAYOUT_ALIGNS = new Set(['start', 'center', 'end', 'stretch']);
const DANGEROUS_KEYS = new Set(['__proto__', 'prototype', 'constructor']);
const UNSAFE_HTML_KEYS = new Set([
    '__html',
    'dangerouslysetinnerhtml',
    'documentwrite',
    'html',
    'innerhtml',
    'insertadjacenthtml',
    'outerhtml',
    'rawhtml',
    'srcdoc',
]);

const COMPONENT_ID_PATTERN = /^custom\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*$/;
const REGISTRY_NAME_PATTERN = /^[A-Z][A-Za-z0-9]*$/;
const NODE_ID_PATTERN = /^[a-z][a-z0-9_-]*$/;
const CLASS_NAME_PATTERN = /^-?[_a-zA-Z]+[_a-zA-Z0-9-]*$/;
const SEMVER_PATTERN = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$/;

function isRecord(value) {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) {
        return false;
    }

    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
}

function addError(errors, code, path, message, detail = undefined) {
    const error = { code, path, message };
    if (detail !== undefined) {
        error.detail = detail;
    }
    errors.push(error);
}

function checkAllowedKeys(value, allowedKeys, path, errors) {
    if (!isRecord(value)) return;

    for (const key of Object.keys(value)) {
        if (!allowedKeys.has(key)) {
            addError(
                errors,
                'ADDITIONAL_PROPERTY',
                `${path}.${key}`,
                `Property "${key}" is not allowed.`,
            );
        }
    }
}

function inspectObjectSafety(value, path, errors, seen = new WeakSet()) {
    if (value === null || typeof value !== 'object') return;
    if (seen.has(value)) {
        addError(errors, 'NON_JSON_CYCLE', path, 'Definition values must not contain object cycles.');
        return;
    }
    seen.add(value);

    if (Array.isArray(value)) {
        for (let index = 0; index < value.length; index += 1) {
            inspectObjectSafety(value[index], `${path}[${index}]`, errors, seen);
        }
        seen.delete(value);
        return;
    }

    if (!isRecord(value)) {
        addError(errors, 'UNSAFE_PROTOTYPE', path, 'Objects must use the standard or null prototype.');
        seen.delete(value);
        return;
    }

    for (const key of Reflect.ownKeys(value)) {
        if (typeof key !== 'string') {
            addError(errors, 'UNSUPPORTED_KEY', path, 'Symbol keys are not supported.');
            continue;
        }

        const keyPath = `${path}.${key}`;
        if (DANGEROUS_KEYS.has(key)) {
            addError(errors, 'DANGEROUS_KEY', keyPath, `Prototype-sensitive key "${key}" is forbidden.`);
            continue;
        }
        if (UNSAFE_HTML_KEYS.has(key.toLowerCase())) {
            addError(errors, 'UNSAFE_HTML_KEY', keyPath, `Raw HTML key "${key}" is forbidden.`);
            continue;
        }

        const descriptor = Object.getOwnPropertyDescriptor(value, key);
        if (!descriptor || descriptor.get || descriptor.set) {
            addError(errors, 'ACCESSOR_PROPERTY', keyPath, 'Accessor properties are not supported.');
            continue;
        }
        inspectObjectSafety(descriptor.value, keyPath, errors, seen);
    }
    seen.delete(value);
}

function validateJsonValue(value, path, errors, seen = new WeakSet()) {
    if (value === null || typeof value === 'string' || typeof value === 'boolean') return;
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) {
            addError(errors, 'NON_JSON_VALUE', path, 'Numbers must be finite.');
        }
        return;
    }
    if (typeof value !== 'object') {
        addError(errors, 'NON_JSON_VALUE', path, 'Only JSON-compatible option values are supported.');
        return;
    }
    if (seen.has(value)) return;
    seen.add(value);

    if (Array.isArray(value)) {
        value.forEach((entry, index) => validateJsonValue(entry, `${path}[${index}]`, errors, seen));
    } else if (isRecord(value)) {
        for (const key of Object.keys(value)) {
            validateJsonValue(value[key], `${path}.${key}`, errors, seen);
        }
    }
    seen.delete(value);
}

function requireNonEmptyString(value, path, errors) {
    if (typeof value !== 'string' || value.trim() === '') {
        addError(errors, 'INVALID_STRING', path, `${path} must be a non-empty string.`);
        return false;
    }
    return true;
}

function validateLayout(layout, path, errors) {
    if (!isRecord(layout)) {
        addError(errors, 'INVALID_LAYOUT', path, 'layout must be an object.');
        return;
    }

    checkAllowedKeys(layout, LAYOUT_KEYS, path, errors);
    if ('mode' in layout && !LAYOUT_MODES.has(layout.mode)) {
        addError(errors, 'INVALID_LAYOUT_MODE', `${path}.mode`, 'layout.mode is unsupported.');
    }
    if ('gap' in layout && !LAYOUT_GAPS.has(layout.gap)) {
        addError(errors, 'INVALID_LAYOUT_GAP', `${path}.gap`, 'layout.gap is unsupported.');
    }
    if ('columns' in layout && (!Number.isInteger(layout.columns) || layout.columns < 1 || layout.columns > 12)) {
        addError(errors, 'INVALID_LAYOUT_COLUMNS', `${path}.columns`, 'layout.columns must be an integer from 1 to 12.');
    }
    if ('align' in layout && !LAYOUT_ALIGNS.has(layout.align)) {
        addError(errors, 'INVALID_LAYOUT_ALIGN', `${path}.align`, 'layout.align is unsupported.');
    }
}

function validateNode(node, path, errors, nodeIds) {
    if (!isRecord(node)) {
        addError(errors, 'INVALID_NODE', path, 'A node must be an object.');
        return;
    }

    if (!['component', 'custom', 'group'].includes(node.type)) {
        addError(errors, 'INVALID_NODE_TYPE', `${path}.type`, 'Node type must be component, custom, or group.');
        return;
    }

    const allowedKeys = node.type === 'group' ? GROUP_NODE_KEYS : LEAF_NODE_KEYS;
    checkAllowedKeys(node, allowedKeys, path, errors);

    if (typeof node.id !== 'string' || !NODE_ID_PATTERN.test(node.id)) {
        addError(errors, 'INVALID_NODE_ID', `${path}.id`, 'Node id must be lower-case and identifier-safe.');
    } else if (nodeIds.has(node.id)) {
        addError(errors, 'DUPLICATE_NODE_ID', `${path}.id`, `Duplicate node id "${node.id}".`);
    } else {
        nodeIds.add(node.id);
    }

    if (node.type === 'component' || node.type === 'custom') {
        if (!requireNonEmptyString(node.component, `${path}.component`, errors)) return;
        if (node.type === 'component' && !REGISTRY_NAME_PATTERN.test(node.component)) {
            addError(errors, 'INVALID_COMPONENT_REFERENCE', `${path}.component`, 'Built-in component references must use PascalCase registry names.');
        }
        if (
            node.type === 'custom' &&
            !REGISTRY_NAME_PATTERN.test(node.component) &&
            !COMPONENT_ID_PATTERN.test(node.component)
        ) {
            addError(errors, 'INVALID_CUSTOM_REFERENCE', `${path}.component`, 'Custom references must use a registry name or custom component id.');
        }
        if ('options' in node) {
            if (!isRecord(node.options)) {
                addError(errors, 'INVALID_OPTIONS', `${path}.options`, 'options must be an object.');
            } else {
                validateJsonValue(node.options, `${path}.options`, errors);
            }
        }
        return;
    }

    if ('layout' in node) validateLayout(node.layout, `${path}.layout`, errors);
    if ('class_names' in node) {
        if (!Array.isArray(node.class_names) || !node.class_names.every((entry) => typeof entry === 'string' && CLASS_NAME_PATTERN.test(entry))) {
            addError(errors, 'INVALID_CLASS_NAMES', `${path}.class_names`, 'class_names must contain safe CSS class names.');
        }
    }
    if ('aria_label' in node) requireNonEmptyString(node.aria_label, `${path}.aria_label`, errors);
    if (!Array.isArray(node.children) || node.children.length === 0) {
        addError(errors, 'INVALID_GROUP_CHILDREN', `${path}.children`, 'A group must contain at least one child.');
        return;
    }

    node.children.forEach((child, index) => validateNode(child, `${path}.children[${index}]`, errors, nodeIds));
}

function validateDefinitionShape(definition) {
    const errors = [];
    if (!isRecord(definition)) {
        addError(errors, 'INVALID_DEFINITION', '$', 'Custom component definition must be an object.');
        return errors;
    }

    inspectObjectSafety(definition, '$', errors);
    checkAllowedKeys(definition, TOP_LEVEL_KEYS, '$', errors);

    if ('$schema' in definition) requireNonEmptyString(definition.$schema, '$.$schema', errors);
    if (definition.schema_version !== 1) {
        addError(errors, 'INVALID_SCHEMA_VERSION', '$.schema_version', 'schema_version must be 1.');
    }
    if (typeof definition.component_id !== 'string' || !COMPONENT_ID_PATTERN.test(definition.component_id)) {
        addError(errors, 'INVALID_COMPONENT_ID', '$.component_id', 'component_id must use the custom.lower_case form.');
    }
    if (typeof definition.registry_name !== 'string' || !REGISTRY_NAME_PATTERN.test(definition.registry_name)) {
        addError(errors, 'INVALID_REGISTRY_NAME', '$.registry_name', 'registry_name must be PascalCase.');
    }
    requireNonEmptyString(definition.display_name, '$.display_name', errors);
    if (typeof definition.version !== 'string' || !SEMVER_PATTERN.test(definition.version)) {
        addError(errors, 'INVALID_VERSION', '$.version', 'version must be a valid semantic version.');
    }
    if (!KINDS.has(definition.kind)) {
        addError(errors, 'INVALID_KIND', '$.kind', 'kind must be atomic, composite, or template.');
    }
    if ('description' in definition) requireNonEmptyString(definition.description, '$.description', errors);

    validateNode(definition.root, '$.root', errors, new Set());
    return errors;
}

function definitionLabel(definition) {
    if (!isRecord(definition)) return '(invalid definition)';
    if (typeof definition.registry_name === 'string') return definition.registry_name;
    if (typeof definition.component_id === 'string') return definition.component_id;
    return '(anonymous definition)';
}

function definitionIdentity(definition) {
    if (!isRecord(definition)) return definition;
    if (typeof definition.component_id === 'string' && definition.component_id !== '') {
        return `component_id:${definition.component_id}`;
    }
    if (typeof definition.registry_name === 'string' && definition.registry_name !== '') {
        return `registry_name:${definition.registry_name}`;
    }
    return definition;
}

function emptyNodeMetrics() {
    return {
        maxDepth: 0,
        atomicLeafCount: 0,
        customReferenceCount: 0,
        compositeReferenceCount: 0,
        hasTemplateReference: false,
        referencedTier: null,
    };
}

function mergeChildMetrics(children) {
    return {
        maxDepth: 1 + Math.max(...children.map((child) => child.maxDepth)),
        atomicLeafCount: children.reduce((sum, child) => sum + child.atomicLeafCount, 0),
        customReferenceCount: children.reduce((sum, child) => sum + child.customReferenceCount, 0),
        compositeReferenceCount: children.reduce((sum, child) => sum + child.compositeReferenceCount, 0),
        hasTemplateReference: children.some((child) => child.hasTemplateReference),
        referencedTier: null,
    };
}

function recordUnresolved(state, reference, path, kind) {
    state.unresolved.add(reference);
    addError(
        state.errors,
        'UNRESOLVED_REFERENCE',
        path,
        `Unknown ${kind} component reference "${reference}".`,
        { reference, kind },
    );
}

function analyzeNode(node, path, state) {
    if (!isRecord(node) || !['component', 'custom', 'group'].includes(node.type)) {
        return emptyNodeMetrics();
    }

    if (node.type === 'component') {
        if (!state.builtinNames.has(node.component)) {
            recordUnresolved(state, node.component, `${path}.component`, 'built-in');
        }
        return {
            ...emptyNodeMetrics(),
            atomicLeafCount: 1,
            referencedTier: 'atomic',
        };
    }

    if (node.type === 'custom') {
        let dependency = null;
        try {
            dependency = state.resolveCustom(node.component);
        } catch (error) {
            addError(
                state.errors,
                'CUSTOM_RESOLVER_FAILED',
                `${path}.component`,
                `Custom resolver failed for "${node.component}".`,
                { message: error instanceof Error ? error.message : String(error) },
            );
        }

        if (!dependency) {
            recordUnresolved(state, node.component, `${path}.component`, 'custom');
            return {
                ...emptyNodeMetrics(),
                customReferenceCount: 1,
            };
        }

        const dependencyResult = analyzeDefinition(dependency, state);
        return {
            maxDepth: dependencyResult.maxDepth,
            atomicLeafCount: dependencyResult.atomicLeafCount,
            customReferenceCount: 1 + dependencyResult.customReferenceCount,
            compositeReferenceCount:
                dependencyResult.compositeReferenceCount +
                (dependencyResult.derivedKind === 'composite' ? 1 : 0),
            hasTemplateReference:
                dependencyResult.derivedKind === 'template' || dependencyResult.hasTemplateReference,
            referencedTier: dependencyResult.derivedKind,
        };
    }

    if (!Array.isArray(node.children) || node.children.length === 0) {
        return emptyNodeMetrics();
    }
    return mergeChildMetrics(
        node.children.map((child, index) => analyzeNode(child, `${path}.children[${index}]`, state)),
    );
}

function analyzeDefinition(definition, state) {
    if (!isRecord(definition)) {
        return {
            ...emptyNodeMetrics(),
            derivedKind: null,
        };
    }

    if (state.cache.has(definition)) {
        return state.cache.get(definition);
    }

    const identity = definitionIdentity(definition);
    const activeIndex = state.activeIdentities.indexOf(identity);
    if (activeIndex >= 0) {
        const cycle = [
            ...state.activeLabels.slice(activeIndex),
            definitionLabel(definition),
        ];
        const cycleKey = cycle.join(' -> ');
        if (!state.cycleKeys.has(cycleKey)) {
            state.cycleKeys.add(cycleKey);
            state.cycles.push(cycle);
            addError(state.errors, 'CUSTOM_REFERENCE_CYCLE', '$.root', `Custom component cycle detected: ${cycleKey}.`, { cycle });
        }
        return {
            ...emptyNodeMetrics(),
            derivedKind: null,
        };
    }

    if (!state.shapeValidated.has(definition)) {
        state.shapeValidated.add(definition);
        const label = definitionLabel(definition);
        for (const error of validateDefinitionShape(definition)) {
            state.errors.push({ ...error, definition: label });
        }
    }

    state.activeIdentities.push(identity);
    state.activeLabels.push(definitionLabel(definition));
    const metrics = analyzeNode(definition.root, '$.root', state);

    let derivedKind = null;
    if (definition.root?.type === 'component') {
        derivedKind = 'atomic';
    } else if (definition.root?.type === 'custom') {
        derivedKind = metrics.referencedTier;
    } else if (definition.root?.type === 'group') {
        derivedKind = (
            metrics.hasTemplateReference ||
            metrics.maxDepth > 3 ||
            metrics.compositeReferenceCount >= 2
        ) ? 'template' : 'composite';
    }

    if (derivedKind && KINDS.has(definition.kind) && definition.kind !== derivedKind) {
        addError(
            state.errors,
            'KIND_MISMATCH',
            '$.kind',
            `Declared kind "${definition.kind}" does not match derived kind "${derivedKind}".`,
            { declared: definition.kind, derived: derivedKind, definition: definitionLabel(definition) },
        );
    }

    state.activeIdentities.pop();
    state.activeLabels.pop();

    const result = {
        ...metrics,
        derivedKind,
    };
    state.cache.set(definition, result);
    return result;
}

/**
 * Validate and analyze one custom component definition.
 *
 * @param {object} definition
 * @param {{builtinNames?: Iterable<string>, resolveCustom?: (reference: string) => object|null}} options
 * @returns {{valid: boolean, errors: object[], derived_kind: string|null, max_depth: number,
 * atomic_leaf_count: number, custom_reference_count: number, composite_reference_count: number,
 * unresolved: string[], cycles: string[][]}}
 */
export function analyzeCustomComponentDefinition(definition, options = {}) {
    const state = {
        builtinNames: new Set(options.builtinNames ?? []),
        resolveCustom: typeof options.resolveCustom === 'function' ? options.resolveCustom : () => null,
        errors: [],
        unresolved: new Set(),
        cycles: [],
        cycleKeys: new Set(),
        cache: new WeakMap(),
        shapeValidated: new WeakSet(),
        activeIdentities: [],
        activeLabels: [],
    };

    const result = analyzeDefinition(definition, state);
    const unresolved = [...state.unresolved].sort();
    return {
        valid: state.errors.length === 0,
        errors: state.errors,
        derived_kind: result.derivedKind,
        max_depth: result.maxDepth,
        atomic_leaf_count: result.atomicLeafCount,
        custom_reference_count: result.customReferenceCount,
        composite_reference_count: result.compositeReferenceCount,
        unresolved,
        cycles: state.cycles,
    };
}

export function validateCustomComponentDefinition(definition, options = {}) {
    return analyzeCustomComponentDefinition(definition, options);
}

export const CustomComponentKinds = Object.freeze([...KINDS]);
