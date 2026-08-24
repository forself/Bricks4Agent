/**
 * Fail-closed schema validation for declarative tool pages.
 *
 * Tool definitions describe structure and trusted command identifiers only.
 * They never contain executable callbacks, raw HTML, DOM targets, or imports.
 */

export const TOOL_EVENT_OPTIONS = Object.freeze([
    'onClick',
    'onChange',
    'onInput',
    'onSelect',
    'onItemClick',
    'onTabChange',
]);

const TOOL_EVENT_OPTION_SET = new Set(TOOL_EVENT_OPTIONS);
const NODE_TYPES = new Set(['group', 'component', 'tabs', 'slot']);
const LAYOUT_MODES = new Set(['stack', 'row', 'grid']);
const LAYOUT_GAPS = new Set(['none', 'xs', 'sm', 'md', 'lg', 'xl']);
const LAYOUT_ALIGNS = new Set(['start', 'center', 'end', 'stretch']);
const DANGEROUS_KEYS = new Set([
    '__proto__',
    'prototype',
    'constructor',
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
const RESERVED_OPTION_KEYS = new Set(['container', 'containerid', 'target', 'element', 'tabs', 'content']);
const TOP_LEVEL_KEYS = new Set(['schema_version', 'name', 'type', 'description', 'root']);
const GROUP_KEYS = new Set(['type', 'id', 'layout', 'class_names', 'aria_label', 'children']);
const COMPONENT_KEYS = new Set(['type', 'id', 'component', 'options', 'bindings', 'events']);
const TABS_KEYS = new Set(['type', 'id', 'options', 'events', 'class_names', 'aria_label', 'tabs']);
const SLOT_KEYS = new Set(['type', 'id', 'class_names', 'aria_label']);
const TAB_KEYS = new Set(['id', 'title', 'content']);
const LAYOUT_KEYS = new Set(['mode', 'gap', 'columns', 'align']);
const PAGE_NAME_PATTERN = /^[A-Z][A-Za-z0-9]*Page$/;
const NODE_ID_PATTERN = /^[a-z][a-z0-9_-]*$/;
const COMPONENT_NAME_PATTERN = /^[A-Z][A-Za-z0-9]*$/;
const CLASS_NAME_PATTERN = /^-?[_a-zA-Z]+[_a-zA-Z0-9-]*$/;
const COMMAND_ID_PATTERN = /^[A-Za-z][A-Za-z0-9]*(?:[._:-][A-Za-z0-9][A-Za-z0-9_-]*)*$/;
const STATE_PATH_PATTERN = /^[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)*$/;
const RAW_HTML_PATTERN = /<\/?[A-Za-z][A-Za-z0-9-]*(?:\s[^<>]*?)?\/?>|<!--|<!doctype\b|<\?xml\b/i;
const MAX_DEPTH = 32;
const MAX_NODES = 1000;
const MAX_ARRAY_LENGTH = 10000;

function isRecord(value) {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    try {
        const prototype = Object.getPrototypeOf(value);
        return prototype === Object.prototype || prototype === null;
    } catch {
        return false;
    }
}

function ownKeys(value, path, errors) {
    try {
        return Reflect.ownKeys(value);
    } catch {
        errors.push(`${path} cannot be inspected safely.`);
        return [];
    }
}

function isDangerousKey(key) {
    return typeof key === 'string' && DANGEROUS_KEYS.has(key.toLowerCase());
}

function validateAllowedKeys(value, allowed, path, errors) {
    if (!isRecord(value)) {
        errors.push(`${path} must be an object.`);
        return false;
    }
    let valid = true;
    for (const key of ownKeys(value, path, errors)) {
        if (typeof key !== 'string') {
            errors.push(`${path} must not contain symbol keys.`);
            valid = false;
            continue;
        }
        if (isDangerousKey(key)) {
            errors.push(`${path}.${key} is not allowed.`);
            valid = false;
            continue;
        }
        const descriptor = Object.getOwnPropertyDescriptor(value, key);
        if (!descriptor || descriptor.get || descriptor.set) {
            errors.push(`${path}.${key} must be a data property.`);
            valid = false;
            continue;
        }
        if (!allowed.has(key)) {
            errors.push(`${path}.${key} is not a supported property.`);
            valid = false;
        }
    }
    return valid;
}

function validateJsonValue(value, path, errors, active = new WeakSet()) {
    if (value === null || typeof value === 'boolean') return;
    if (typeof value === 'string') {
        if (RAW_HTML_PATTERN.test(value)) errors.push(`${path} must not contain raw HTML.`);
        return;
    }
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) errors.push(`${path} must contain finite numbers.`);
        return;
    }
    if (typeof value === 'function') {
        errors.push(`${path} must not contain functions.`);
        return;
    }
    if (typeof value !== 'object') {
        errors.push(`${path} must be JSON-compatible.`);
        return;
    }
    if (active.has(value)) {
        errors.push(`${path} must not contain cycles.`);
        return;
    }

    active.add(value);
    try {
        if (Array.isArray(value)) {
            if (value.length > MAX_ARRAY_LENGTH) {
                errors.push(`${path} exceeds the maximum array length of ${MAX_ARRAY_LENGTH}.`);
                return;
            }
            for (const key of ownKeys(value, path, errors)) {
                if (key === 'length') continue;
                if (typeof key !== 'string' || !/^(0|[1-9]\d*)$/.test(key) || Number(key) >= value.length) {
                    errors.push(`${path} arrays must only contain indexed entries.`);
                }
            }
            for (let index = 0; index < value.length; index += 1) {
                const descriptor = Object.getOwnPropertyDescriptor(value, String(index));
                if (!descriptor || descriptor.get || descriptor.set) {
                    errors.push(`${path}[${index}] must be a data property.`);
                    continue;
                }
                validateJsonValue(descriptor.value, `${path}[${index}]`, errors, active);
            }
            return;
        }

        if (!isRecord(value)) {
            errors.push(`${path} must use a plain object prototype.`);
            return;
        }
        for (const key of ownKeys(value, path, errors)) {
            if (typeof key !== 'string') {
                errors.push(`${path} must not contain symbol keys.`);
                continue;
            }
            if (isDangerousKey(key)) {
                errors.push(`${path}.${key} is not allowed.`);
                continue;
            }
            const descriptor = Object.getOwnPropertyDescriptor(value, key);
            if (!descriptor || descriptor.get || descriptor.set) {
                errors.push(`${path}.${key} must be a data property.`);
                continue;
            }
            validateJsonValue(descriptor.value, `${path}.${key}`, errors, active);
        }
    } finally {
        active.delete(value);
    }
}

function validateClasses(value, path, errors) {
    if (value === undefined) return;
    if (!Array.isArray(value)) {
        errors.push(`${path} must be an array.`);
        return;
    }
    for (let index = 0; index < value.length; index += 1) {
        if (typeof value[index] !== 'string' || !CLASS_NAME_PATTERN.test(value[index])) {
            errors.push(`${path}[${index}] must be a safe CSS class name.`);
        }
    }
}

function validateAriaLabel(value, path, errors) {
    if (value !== undefined && (typeof value !== 'string' || value.trim() === '')) {
        errors.push(`${path} must be a non-empty string.`);
    }
}

function validateLayout(layout, path, errors) {
    if (layout === undefined) return;
    if (!validateAllowedKeys(layout, LAYOUT_KEYS, path, errors)) return;
    if (layout.mode !== undefined && !LAYOUT_MODES.has(layout.mode)) {
        errors.push(`${path}.mode is invalid.`);
    }
    if (layout.gap !== undefined && !LAYOUT_GAPS.has(layout.gap)) {
        errors.push(`${path}.gap is invalid.`);
    }
    if (layout.align !== undefined && !LAYOUT_ALIGNS.has(layout.align)) {
        errors.push(`${path}.align is invalid.`);
    }
    if (layout.columns !== undefined && (!Number.isInteger(layout.columns) || layout.columns < 1 || layout.columns > 12)) {
        errors.push(`${path}.columns must be an integer from 1 to 12.`);
    }
}

function validateEvents(events, path, errors) {
    if (events === undefined) return;
    if (!isRecord(events)) {
        errors.push(`${path} must be an object.`);
        return;
    }
    for (const key of ownKeys(events, path, errors)) {
        if (typeof key !== 'string') {
            errors.push(`${path} must not contain symbol keys.`);
            continue;
        }
        if (!TOOL_EVENT_OPTION_SET.has(key)) {
            errors.push(`${path}.${key} is not an allowed event option.`);
            continue;
        }
        const descriptor = Object.getOwnPropertyDescriptor(events, key);
        if (!descriptor || descriptor.get || descriptor.set) {
            errors.push(`${path}.${key} must be a data property.`);
            continue;
        }
        if (typeof descriptor.value !== 'string' || !COMMAND_ID_PATTERN.test(descriptor.value)) {
            errors.push(`${path}.${key} must be a safe command id.`);
        }
    }
}

function validateOptions(options, path, errors) {
    if (options === undefined) return;
    if (!isRecord(options)) {
        errors.push(`${path} must be an object.`);
        return;
    }
    validateJsonValue(options, path, errors);
    for (const key of ownKeys(options, path, errors)) {
        if (typeof key !== 'string') continue;
        const normalized = key.toLowerCase();
        if (/^on[A-Z]/.test(key)) errors.push(`${path}.${key} must be declared in events.`);
        if (RESERVED_OPTION_KEYS.has(normalized)) {
            errors.push(`${path}.${key} is renderer-owned and is not allowed.`);
        }
    }
}

function validateBindings(bindings, path, errors) {
    if (bindings === undefined) return;
    if (!isRecord(bindings)) {
        errors.push(`${path} must be an object.`);
        return;
    }
    for (const key of ownKeys(bindings, path, errors)) {
        if (typeof key !== 'string') {
            errors.push(`${path} must not contain symbol keys.`);
            continue;
        }
        if (isDangerousKey(key) || /^on[A-Z]/.test(key) || RESERVED_OPTION_KEYS.has(key.toLowerCase())) {
            errors.push(`${path}.${key} is not an allowed binding target.`);
            continue;
        }
        const descriptor = Object.getOwnPropertyDescriptor(bindings, key);
        if (!descriptor || descriptor.get || descriptor.set) {
            errors.push(`${path}.${key} must be a data property.`);
            continue;
        }
        if (typeof descriptor.value !== 'string' || !STATE_PATH_PATTERN.test(descriptor.value)) {
            errors.push(`${path}.${key} must be a safe state path.`);
        }
    }
}

function validateNode(node, path, errors, context, depth = 0) {
    if (depth > MAX_DEPTH) {
        errors.push(`${path} exceeds the maximum nesting depth of ${MAX_DEPTH}.`);
        return;
    }
    context.count += 1;
    if (context.count > MAX_NODES) {
        if (!context.tooManyReported) errors.push(`Tool definition exceeds the maximum of ${MAX_NODES} nodes.`);
        context.tooManyReported = true;
        return;
    }
    if (!isRecord(node)) {
        errors.push(`${path} must be a node object.`);
        return;
    }
    if (!NODE_TYPES.has(node.type)) {
        errors.push(`${path}.type must be group, component, tabs, or slot.`);
        return;
    }

    const allowedKeys = {
        group: GROUP_KEYS,
        component: COMPONENT_KEYS,
        tabs: TABS_KEYS,
        slot: SLOT_KEYS,
    }[node.type];
    validateAllowedKeys(node, allowedKeys, path, errors);

    if (typeof node.id !== 'string' || !NODE_ID_PATTERN.test(node.id)) {
        errors.push(`${path}.id must be a safe lowercase node id.`);
    } else if (context.nodeIds.has(node.id)) {
        errors.push(`${path}.id duplicates node id "${node.id}".`);
    } else {
        context.nodeIds.add(node.id);
    }

    if (node.type === 'group') {
        validateLayout(node.layout, `${path}.layout`, errors);
        validateClasses(node.class_names, `${path}.class_names`, errors);
        validateAriaLabel(node.aria_label, `${path}.aria_label`, errors);
        if (!Array.isArray(node.children) || node.children.length === 0) {
            errors.push(`${path}.children must be a non-empty array.`);
            return;
        }
        node.children.forEach((child, index) => validateNode(child, `${path}.children[${index}]`, errors, context, depth + 1));
        return;
    }

    if (node.type === 'component') {
        if (typeof node.component !== 'string' || !COMPONENT_NAME_PATTERN.test(node.component)) {
            errors.push(`${path}.component must be a safe registered component name.`);
        }
        validateOptions(node.options, `${path}.options`, errors);
        validateBindings(node.bindings, `${path}.bindings`, errors);
        validateEvents(node.events, `${path}.events`, errors);
        return;
    }

    if (node.type === 'tabs') {
        validateOptions(node.options, `${path}.options`, errors);
        validateEvents(node.events, `${path}.events`, errors);
        if (node.events && Object.keys(node.events).some((eventName) => eventName !== 'onTabChange')) {
            errors.push(`${path}.events only supports onTabChange.`);
        }
        validateClasses(node.class_names, `${path}.class_names`, errors);
        validateAriaLabel(node.aria_label, `${path}.aria_label`, errors);
        if (!Array.isArray(node.tabs) || node.tabs.length === 0) {
            errors.push(`${path}.tabs must be a non-empty array.`);
            return;
        }
        const tabIds = new Set();
        node.tabs.forEach((tab, index) => {
            const tabPath = `${path}.tabs[${index}]`;
            if (!validateAllowedKeys(tab, TAB_KEYS, tabPath, errors)) return;
            if (typeof tab.id !== 'string' || !NODE_ID_PATTERN.test(tab.id)) {
                errors.push(`${tabPath}.id must be a safe lowercase tab id.`);
            } else if (tabIds.has(tab.id)) {
                errors.push(`${tabPath}.id duplicates tab id "${tab.id}".`);
            } else {
                tabIds.add(tab.id);
            }
            if (typeof tab.title !== 'string' || tab.title.trim() === '') {
                errors.push(`${tabPath}.title must be a non-empty string.`);
            }
            validateNode(tab.content, `${tabPath}.content`, errors, context, depth + 1);
        });
        return;
    }

    validateClasses(node.class_names, `${path}.class_names`, errors);
    validateAriaLabel(node.aria_label, `${path}.aria_label`, errors);
}

/**
 * Validate a ToolPageDefinition without executing getters or authored code.
 *
 * @param {unknown} definition
 * @returns {{valid: boolean, errors: string[]}}
 */
export function validateToolPageDefinition(definition) {
    const errors = [];
    validateJsonValue(definition, 'definition', errors);
    if (errors.length > 0) return { valid: false, errors };
    if (!validateAllowedKeys(definition, TOP_LEVEL_KEYS, 'definition', errors)) {
        return { valid: false, errors };
    }

    if (definition.schema_version !== 1) errors.push('definition.schema_version must equal 1.');
    if (typeof definition.name !== 'string' || !PAGE_NAME_PATTERN.test(definition.name)) {
        errors.push('definition.name must be PascalCase and end with Page.');
    }
    if (definition.type !== 'tool') errors.push('definition.type must equal "tool".');
    if (definition.description !== undefined && typeof definition.description !== 'string') {
        errors.push('definition.description must be a string.');
    }

    const context = { count: 0, nodeIds: new Set(), tooManyReported: false };
    validateNode(definition.root, 'definition.root', errors, context);
    return { valid: errors.length === 0, errors };
}

export default {
    TOOL_EVENT_OPTIONS,
    validateToolPageDefinition,
};
