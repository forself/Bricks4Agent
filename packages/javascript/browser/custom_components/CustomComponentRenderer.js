import { validateCustomComponentDefinition } from './CustomComponentDefinition.js';

const OPTION_BLOCKLIST = new Set([
    '__proto__',
    'prototype',
    'constructor',
    'container',
    'containerid',
    'target',
    'element',
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

const LAYOUT_CLASS_ALLOWLIST = new Set([
    'custom-component--stack',
    'custom-component--row',
    'custom-component--grid',
    'custom-component--gap-none',
    'custom-component--gap-xs',
    'custom-component--gap-sm',
    'custom-component--gap-md',
    'custom-component--gap-lg',
    'custom-component--gap-xl',
    'custom-component--align-start',
    'custom-component--align-center',
    'custom-component--align-end',
    'custom-component--align-stretch',
    ...Array.from({ length: 12 }, (_, index) => `custom-component--columns-${index + 1}`),
]);

let rendererSequence = 0;

function isRecord(value) {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
}

function isBlockedOptionKey(key) {
    return typeof key === 'string' && OPTION_BLOCKLIST.has(key.toLowerCase());
}

function cloneSafeValue(value, seen = new WeakMap()) {
    if (
        value === null ||
        typeof value === 'string' ||
        typeof value === 'boolean' ||
        typeof value === 'function'
    ) {
        return value;
    }
    if (typeof value === 'number') {
        return Number.isFinite(value) ? value : undefined;
    }
    if (typeof value !== 'object') return undefined;
    if (seen.has(value)) return seen.get(value);

    if (Array.isArray(value)) {
        const result = [];
        seen.set(value, result);
        for (const entry of value) {
            const cloned = cloneSafeValue(entry, seen);
            if (cloned !== undefined) result.push(cloned);
        }
        return result;
    }

    if (!isRecord(value)) return undefined;

    const result = {};
    seen.set(value, result);
    for (const key of Reflect.ownKeys(value)) {
        if (typeof key !== 'string' || isBlockedOptionKey(key)) continue;
        const descriptor = Object.getOwnPropertyDescriptor(value, key);
        if (!descriptor || descriptor.get || descriptor.set) continue;
        const cloned = cloneSafeValue(descriptor.value, seen);
        if (cloned !== undefined) result[key] = cloned;
    }
    return result;
}

function mergeSafeOptions(base, override) {
    const left = isRecord(base) ? cloneSafeValue(base) : {};
    const right = isRecord(override) ? override : {};

    for (const key of Reflect.ownKeys(right)) {
        if (typeof key !== 'string' || isBlockedOptionKey(key)) continue;
        const descriptor = Object.getOwnPropertyDescriptor(right, key);
        if (!descriptor || descriptor.get || descriptor.set) continue;

        const nextValue = descriptor.value;
        if (isRecord(left[key]) && isRecord(nextValue)) {
            left[key] = mergeSafeOptions(left[key], nextValue);
            continue;
        }

        const cloned = cloneSafeValue(nextValue);
        if (cloned !== undefined) left[key] = cloned;
    }
    return left;
}

function own(object, key) {
    return Object.prototype.hasOwnProperty.call(object, key);
}

function isDomNode(value) {
    return Boolean(value && typeof value === 'object' && Number.isInteger(value.nodeType));
}

function containsNode(container, node) {
    if (!container || !node) return false;
    if (typeof container.contains === 'function') return container.contains(node);
    let current = node;
    while (current) {
        if (current === container) return true;
        current = current.parentNode;
    }
    return false;
}

function childCount(element) {
    if (!element) return 0;
    if (element.childNodes && Number.isInteger(element.childNodes.length)) {
        return element.childNodes.length;
    }
    if (element.children && Number.isInteger(element.children.length)) {
        return element.children.length;
    }
    return 0;
}

function removeAllChildren(element) {
    if (!element) return;
    if (typeof element.replaceChildren === 'function') {
        element.replaceChildren();
        return;
    }
    while (element.firstChild && typeof element.removeChild === 'function') {
        element.removeChild(element.firstChild);
    }
}

function removeElement(element) {
    if (!element) return;
    if (typeof element.remove === 'function') {
        element.remove();
    } else if (element.parentNode && typeof element.parentNode.removeChild === 'function') {
        element.parentNode.removeChild(element);
    }
}

function factoryRegistryNames(factory) {
    const registry = factory?.registry;
    if (registry instanceof Map) return [...registry.keys()];
    if (registry && typeof registry === 'object') return Object.keys(registry);
    if (typeof factory?.list === 'function') {
        const listed = factory.list();
        if (Array.isArray(listed)) {
            return listed
                .map((entry) => typeof entry === 'string' ? entry : entry?.registry_name ?? entry?.name)
                .filter(Boolean);
        }
    }
    return [];
}

function formatValidationErrors(errors) {
    return (Array.isArray(errors) ? errors : [])
        .map((error) => {
            if (typeof error === 'string') return error;
            const location = error?.path ? ` at ${error.path}` : '';
            return `${error?.code ?? 'INVALID_DEFINITION'}${location}: ${error?.message ?? 'Invalid definition'}`;
        })
        .join('; ');
}

function normalizedNodeId(id) {
    return String(id || 'node').replace(/[^A-Za-z0-9_-]/g, '-');
}

/**
 * Render a validated custom-component definition without evaluating authored code.
 */
export class CustomComponentRenderer {
    constructor({
        definition,
        registry,
        factory = null,
        nodeOptions = undefined,
        node_options = undefined,
        values = undefined,
    } = {}) {
        if (!definition || typeof definition !== 'object') {
            throw new TypeError('CustomComponentRenderer requires a definition object.');
        }

        this.definition = definition;
        this.registry = registry ?? null;
        this.factory = factory ?? registry?.factory ?? null;
        this.nodeOptions = nodeOptions ?? node_options ?? {};
        this.values = values;

        this.element = null;
        this._container = null;
        this._instances = new Map();
        this._instanceOrder = [];
        this._mounted = false;
        this._destroyed = false;
        this._sequence = ++rendererSequence;
    }

    mount(container) {
        if (this._destroyed) {
            throw new Error('A destroyed CustomComponentRenderer cannot be mounted again.');
        }
        if (this._mounted) {
            if (container && container !== this._container) {
                throw new Error('CustomComponentRenderer is already mounted in another container.');
            }
            return this;
        }
        if (!container || typeof container.appendChild !== 'function') {
            throw new TypeError('mount(container) requires a DOM container.');
        }

        this._validateDefinition();
        const documentRef = container.ownerDocument ?? globalThis.document;
        if (!documentRef || typeof documentRef.createElement !== 'function') {
            throw new Error('A DOM document with createElement() is required.');
        }

        this._container = container;
        this.element = documentRef.createElement('div');
        this.element.classList.add('custom-component', 'custom-component__root');
        this.element.setAttribute('data-custom-component', this.definition.registry_name);
        container.appendChild(this.element);

        try {
            if (this.definition.root.type === 'group') {
                this._configureGroup(this.element, this.definition.root, true);
                this._renderChildren(this.definition.root.children, this.element, documentRef);
            } else {
                this._renderLeaf(this.definition.root, this.element, documentRef);
            }
            this._mounted = true;
            if (this.values !== undefined) this._applyValue(this.values);
            return this;
        } catch (error) {
            this._teardown();
            this._destroyed = true;
            throw error;
        }
    }

    getComponent(id) {
        return this._instances.get(id) ?? null;
    }

    getValue() {
        if (this._isAtomic()) {
            const record = this._leafRecords()[0];
            return record ? this._readInstanceValue(record.instance) : undefined;
        }

        const result = {};
        for (const record of this._leafRecords()) {
            result[record.id] = this._readInstanceValue(record.instance);
        }
        return result;
    }

    getValues() {
        return this.getValue();
    }

    setValue(value) {
        this.values = cloneSafeValue(value);
        if (this._mounted) this._applyValue(value);
        return this;
    }

    setValues(values) {
        return this.setValue(values);
    }

    destroy() {
        if (this._destroyed) return;
        this._teardown();
        this._destroyed = true;
    }

    _validateDefinition() {
        const resolveCustom = (reference) => {
            if (typeof this.registry?._resolveDefinition === 'function') {
                return this.registry._resolveDefinition(reference);
            }
            return typeof this.registry?.get === 'function' ? this.registry.get(reference) : null;
        };
        const builtinNames = typeof this.registry?._currentBuiltinNames === 'function'
            ? this.registry._currentBuiltinNames()
            : factoryRegistryNames(this.factory);
        const validation = validateCustomComponentDefinition(this.definition, {
            builtinNames,
            resolveCustom,
        });
        if (!validation?.valid) {
            const error = new Error(`Invalid custom component definition: ${formatValidationErrors(validation?.errors)}`);
            error.errors = validation?.errors ?? [];
            throw error;
        }
    }

    _renderChildren(children, parent, documentRef) {
        for (const node of children ?? []) {
            if (node.type === 'group') {
                const group = documentRef.createElement('div');
                group.classList.add('custom-component__group');
                group.setAttribute('data-custom-component-node', node.id);
                this._configureGroup(group, node, false);
                parent.appendChild(group);
                this._renderChildren(node.children, group, documentRef);
            } else {
                this._renderLeaf(node, parent, documentRef);
            }
        }
    }

    _configureGroup(element, node, isRoot) {
        if (!isRoot) element.classList.add('custom-component__group');
        if (node.aria_label) element.setAttribute('aria-label', node.aria_label);

        const layout = node.layout ?? {};
        const classes = [
            layout.mode ? `custom-component--${layout.mode}` : null,
            layout.gap ? `custom-component--gap-${layout.gap}` : null,
            layout.columns ? `custom-component--columns-${layout.columns}` : null,
            layout.align ? `custom-component--align-${layout.align}` : null,
        ];
        for (const className of classes) {
            if (className && LAYOUT_CLASS_ALLOWLIST.has(className)) {
                element.classList.add(className);
            }
        }
        for (const className of node.class_names ?? []) {
            element.classList.add(className);
        }
    }

    _renderLeaf(node, parent, documentRef) {
        const host = documentRef.createElement('div');
        host.classList.add('custom-component__host');
        host.setAttribute('data-custom-component-node', node.id);
        host.id = `custom-component-${this._sequence}-${normalizedNodeId(node.id)}`;
        parent.appendChild(host);

        const options = this._buildOptions(node, host);
        let instance;
        if (node.type === 'component') {
            if (!this.factory || typeof this.factory.create !== 'function') {
                throw new Error(`No component factory is available for built-in "${node.component}".`);
            }
            instance = this.factory.create(node.component, options);
        } else if (node.type === 'custom') {
            if (!this.registry || typeof this.registry.create !== 'function') {
                throw new Error(`No custom component registry is available for "${node.component}".`);
            }
            instance = this.registry.create(node.component, {
                factory: this.factory,
                nodeOptions: options,
                values: own(options, 'value') ? options.value : undefined,
            });
        }

        if (!instance) {
            throw new Error(`Component node "${node.id}" could not create "${node.component}".`);
        }

        const record = { id: node.id, instance, host };
        this._instances.set(node.id, instance);
        this._instanceOrder.push(record);
        this._attachInstance(instance, host);
    }

    _buildOptions(node, host) {
        const authored = isRecord(node.options) ? node.options : {};
        const runtime = this._runtimeOptionsFor(node.id);
        const options = mergeSafeOptions(authored, runtime);
        const initialValue = this._initialValueFor(node.id);
        if (initialValue.found) options.value = cloneSafeValue(initialValue.value);

        options.container = host;
        if (host.id) options.containerId = host.id;
        return options;
    }

    _runtimeOptionsFor(nodeId) {
        if (!isRecord(this.nodeOptions)) return {};
        const nestedMap = isRecord(this.nodeOptions.nodes) ? this.nodeOptions.nodes : null;
        if (nestedMap && isRecord(nestedMap[nodeId])) return nestedMap[nodeId];
        if (isRecord(this.nodeOptions[nodeId])) return this.nodeOptions[nodeId];
        return this._isAtomic() ? this.nodeOptions : {};
    }

    _initialValueFor(nodeId) {
        if (this.values === undefined) return { found: false, value: undefined };
        if (this._isAtomic()) return { found: true, value: this.values };
        if (isRecord(this.values) && own(this.values, nodeId)) {
            return { found: true, value: this.values[nodeId] };
        }
        return { found: false, value: undefined };
    }

    _attachInstance(instance, host) {
        const element = this._instanceElement(instance);
        if ((element && containsNode(host, element)) || childCount(host) > 0) return;

        if (typeof instance.mount === 'function') {
            const result = instance.mount(host);
            this._appendRenderResult(result, instance, host);
            return;
        }

        if (element) {
            if (!containsNode(host, element)) host.appendChild(element);
            return;
        }

        if (isDomNode(instance)) {
            if (!containsNode(host, instance)) host.appendChild(instance);
            return;
        }

        if (typeof instance.render === 'function') {
            const result = instance.render(host);
            this._appendRenderResult(result, instance, host);
            if (childCount(host) === 0) {
                throw new Error('render() did not return or append a DOM node.');
            }
            return;
        }

        throw new Error('Component instance does not support auto-mount, mount(), element, or render().');
    }

    _appendRenderResult(result, instance, host) {
        if (isDomNode(result) && !containsNode(host, result)) {
            host.appendChild(result);
            return;
        }
        const element = this._instanceElement(instance);
        if (element && !containsNode(host, element) && childCount(host) === 0) {
            host.appendChild(element);
        }
    }

    _instanceElement(instance) {
        try {
            return isDomNode(instance?.element) ? instance.element : null;
        } catch {
            return null;
        }
    }

    _leafRecords() {
        return this._instanceOrder.slice();
    }

    _readInstanceValue(instance) {
        if (typeof instance?.getValue === 'function') return instance.getValue();
        if (typeof instance?.getValues === 'function') return instance.getValues();
        if (typeof instance?.isChecked === 'function') return instance.isChecked();
        return undefined;
    }

    _writeInstanceValue(instance, value) {
        if (typeof instance?.setValue === 'function') {
            instance.setValue(value);
            return true;
        }
        if (typeof instance?.setValues === 'function') {
            instance.setValues(value);
            return true;
        }
        if (typeof instance?.setChecked === 'function') {
            instance.setChecked(Boolean(value));
            return true;
        }
        return false;
    }

    _applyValue(value) {
        if (this._isAtomic()) {
            const record = this._leafRecords()[0];
            if (record) this._writeInstanceValue(record.instance, value);
            return;
        }
        if (!isRecord(value)) return;
        for (const record of this._leafRecords()) {
            if (own(value, record.id)) this._writeInstanceValue(record.instance, value[record.id]);
        }
    }

    _isAtomic() {
        return this.definition.kind === 'atomic';
    }

    _teardown() {
        for (let index = this._instanceOrder.length - 1; index >= 0; index -= 1) {
            const instance = this._instanceOrder[index].instance;
            try {
                instance?.destroy?.();
            } catch (error) {
                console.error('[CustomComponentRenderer] Component destroy failed:', error);
            }
        }
        this._instanceOrder = [];
        this._instances.clear();
        removeAllChildren(this.element);
        removeElement(this.element);
        this.element = null;
        this._container = null;
        this._mounted = false;
    }
}

export default CustomComponentRenderer;
