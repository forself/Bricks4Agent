import { LazyComponentFactory } from '../ui_components/binding/LazyComponentFactory.js';
import { validateToolPageDefinition } from './ToolPageDefinition.js';

const INTERACTIVE_SELECTOR = [
    'a[href]',
    'button',
    'input',
    'select',
    'textarea',
    '[role="button"]',
    '[role="tab"]',
    '[role="checkbox"]',
    '[tabindex]',
].join(',');
const SAFE_PATH_PATTERN = /^[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)*$/;
const BLOCKED_PATH_SEGMENTS = new Set(['__proto__', 'prototype', 'constructor']);
let rendererSequence = 0;

function isRecord(value) {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
}

function cloneData(value, path = 'value', active = new WeakSet()) {
    if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) throw new TypeError(`${path} must contain finite numbers.`);
        return value;
    }
    if (typeof value === 'function') throw new TypeError(`${path} must not contain functions.`);
    if (typeof value !== 'object') throw new TypeError(`${path} must be JSON-compatible.`);
    if (active.has(value)) throw new TypeError(`${path} must not contain cycles.`);

    active.add(value);
    try {
        if (Array.isArray(value)) {
            const result = [];
            for (const key of Reflect.ownKeys(value)) {
                if (key === 'length') continue;
                if (typeof key !== 'string' || !/^(0|[1-9]\d*)$/.test(key) || Number(key) >= value.length) {
                    throw new TypeError(`${path} arrays must only contain indexed entries.`);
                }
            }
            for (let index = 0; index < value.length; index += 1) {
                const descriptor = Object.getOwnPropertyDescriptor(value, String(index));
                if (!descriptor || descriptor.get || descriptor.set) {
                    throw new TypeError(`${path}[${index}] must be a data property.`);
                }
                result.push(cloneData(descriptor.value, `${path}[${index}]`, active));
            }
            return result;
        }

        if (!isRecord(value)) throw new TypeError(`${path} must use a plain object prototype.`);
        const result = {};
        for (const key of Reflect.ownKeys(value)) {
            if (typeof key !== 'string' || BLOCKED_PATH_SEGMENTS.has(key)) {
                throw new TypeError(`${path} contains an unsafe key.`);
            }
            const descriptor = Object.getOwnPropertyDescriptor(value, key);
            if (!descriptor || descriptor.get || descriptor.set) {
                throw new TypeError(`${path}.${key} must be a data property.`);
            }
            result[key] = cloneData(descriptor.value, `${path}.${key}`, active);
        }
        return result;
    } finally {
        active.delete(value);
    }
}

function pathSegments(path) {
    if (typeof path !== 'string' || !SAFE_PATH_PATTERN.test(path)) {
        throw new TypeError('State path must be a safe dotted identifier.');
    }
    const segments = path.split('.');
    if (segments.some((segment) => BLOCKED_PATH_SEGMENTS.has(segment))) {
        throw new TypeError('State path contains a blocked segment.');
    }
    return segments;
}

/**
 * 綁定值是否與上次推給元件的相同。值域與 cloneData 一致（JSON 相容、無函式、
 * 無迴圈、數值有限），因此不必處理 Date/Map/Set，也不會遇到 NaN。
 * 物件連鍵的順序一起比：順序變了就當作變了。寧可多推一次，也不要漏掉
 * 「元件依 Object.entries 順序渲染」這種情形。
 */
function sameData(a, b) {
    if (a === b) return true;
    if (a === null || b === null) return false;
    if (typeof a !== 'object' || typeof b !== 'object') return false;

    const aIsArray = Array.isArray(a);
    if (aIsArray !== Array.isArray(b)) return false;
    if (aIsArray) {
        if (a.length !== b.length) return false;
        for (let i = 0; i < a.length; i += 1) {
            if (!sameData(a[i], b[i])) return false;
        }
        return true;
    }

    const aKeys = Object.keys(a);
    const bKeys = Object.keys(b);
    if (aKeys.length !== bKeys.length) return false;
    for (let i = 0; i < aKeys.length; i += 1) {
        if (aKeys[i] !== bKeys[i]) return false;
        if (!sameData(a[aKeys[i]], b[bKeys[i]])) return false;
    }
    return true;
}

function readPath(source, path) {
    let current = source;
    for (const segment of pathSegments(path)) {
        if (!isRecord(current) || !Object.prototype.hasOwnProperty.call(current, segment)) {
            return { found: false, value: undefined };
        }
        current = current[segment];
    }
    return { found: true, value: current };
}

function writePath(target, path, value) {
    const segments = pathSegments(path);
    let current = target;
    for (let index = 0; index < segments.length - 1; index += 1) {
        const segment = segments[index];
        if (!Object.prototype.hasOwnProperty.call(current, segment)) current[segment] = {};
        if (!isRecord(current[segment])) {
            throw new TypeError(`State path segment "${segment}" is not an object.`);
        }
        current = current[segment];
    }
    current[segments.at(-1)] = value;
}

function pathsOverlap(left, right) {
    return left === right
        || left.startsWith(`${right}.`)
        || right.startsWith(`${left}.`);
}

function commandFromRegistry(registry, commandId) {
    if (registry instanceof Map) return registry.get(commandId);
    if (isRecord(registry) && Object.prototype.hasOwnProperty.call(registry, commandId)) {
        return registry[commandId];
    }
    if (registry && typeof registry.get === 'function') return registry.get(commandId);
    return undefined;
}

function resolveControlRecords(controlRegistry) {
    if (controlRegistry instanceof Map) return controlRegistry;
    if (controlRegistry?.records instanceof Map) return controlRegistry.records;
    return new Map();
}

// 收集定義樹會實例化的所有元件名稱(含 _replaceComponent 重建用——其節點必來自定義樹)
function collectComponentNames(node, names = new Set()) {
    if (!node || typeof node !== 'object') return names;
    if (node.type === 'component') {
        names.add(node.component);
    } else if (node.type === 'tabs') {
        names.add('TabContainer');
        for (const tab of node.tabs || []) collectComponentNames(tab.content, names);
    } else if (node.type === 'group') {
        for (const child of node.children || []) collectComponentNames(child, names);
    }
    return names;
}

function instanceElement(instance) {
    const element = instance?.element;
    return element && Number.isInteger(element.nodeType) ? element : null;
}

function contains(container, node) {
    return Boolean(container && node && (container === node || container.contains?.(node)));
}

/**
 * Render a validated ToolPageDefinition through ComponentFactory and trusted commands.
 */
export class DynamicToolRenderer {
    constructor(options = {}) {
        this.definition = options.definition ?? null;
        this.commandRegistry = options.commandRegistry ?? new Map();
        this.state = cloneData(options.state ?? {}, 'state');
        this.factory = options.factory || LazyComponentFactory;
        this.controlRecords = resolveControlRecords(options.controlRegistry);

        this.element = null;
        this._container = null;
        this._document = null;
        this._components = new Map();
        this._hosts = new Map();
        this._instanceOrder = [];
        this._preparedInstances = new Map();
        this._bindingRecords = [];
        this._initialized = false;
        this._mounted = false;
        this._destroyed = false;
        this._sequence = ++rendererSequence;
    }

    async init() {
        if (this._destroyed) throw new Error('A destroyed DynamicToolRenderer cannot be initialized.');
        if (this._initialized) return this;
        const validation = validateToolPageDefinition(this.definition);
        if (!validation.valid) {
            const error = new Error(`Invalid ToolPageDefinition: ${validation.errors.join('; ')}`);
            error.errors = validation.errors;
            throw error;
        }
        if (!this.factory || typeof this.factory.create !== 'function') {
            throw new TypeError('DynamicToolRenderer requires a ComponentFactory-compatible factory.');
        }

        this.definition = cloneData(this.definition, 'definition');
        // 先把定義用到的元件模組載入快取;自訂 factory 若無 preload 則保持原行為
        await this.factory.preload?.([...collectComponentNames(this.definition.root)]);
        // preload 等待期間可能已被 destroy，不得將已銷毀的渲染器標記為 initialized
        if (this._destroyed) throw new Error('A destroyed DynamicToolRenderer cannot be initialized.');
        try {
            this._preflightNode(this.definition.root);
            this._initialized = true;
            return this;
        } catch (error) {
            this._destroyPreparedInstances();
            throw error;
        }
    }

    _preflightNode(node) {
        if (node.type === 'component') {
            const factoryStatus = this._factoryHas(node.component);
            if (factoryStatus === false) {
                throw new Error(`Unknown tool component: ${node.component}`);
            }
            for (const statePath of Object.values(node.bindings || {})) {
                if (!readPath(this.state, statePath).found) {
                    throw new Error(`Missing bound state path "${statePath}" for node "${node.id}".`);
                }
            }
            this._preflightEvents(node);
            if (factoryStatus === null) this._prepareComponent(node);
            return;
        }
        if (node.type === 'tabs') {
            const factoryStatus = this._factoryHas('TabContainer');
            if (factoryStatus === false) throw new Error('Unknown tool component: TabContainer');
            this._preflightEvents(node);
            node.tabs.forEach((tab) => this._preflightNode(tab.content));
            return;
        }
        if (node.type === 'group') node.children.forEach((child) => this._preflightNode(child));
    }

    _preflightEvents(node) {
        for (const commandId of Object.values(node.events || {})) {
            if (typeof commandFromRegistry(this.commandRegistry, commandId) !== 'function') {
                throw new Error(`Missing trusted command "${commandId}" for node "${node.id}".`);
            }
        }
    }

    _factoryHas(name) {
        if (typeof this.factory.has === 'function') return this.factory.has(name) === true;
        const registry = this.factory.registry;
        if (registry instanceof Map) return registry.has(name);
        if (registry && typeof registry === 'object') return Object.prototype.hasOwnProperty.call(registry, name);
        if (typeof this.factory.getComponentClass === 'function') return Boolean(this.factory.getComponentClass(name));
        return null;
    }

    _prepareComponent(node) {
        const optionsRecord = this._createComponentOptions(node);
        const instance = this.factory.create(node.component, optionsRecord.options);
        optionsRecord.setInstance(instance);
        if (!instance) throw new Error(`Unknown tool component: ${node.component}`);
        this._preparedInstances.set(node.id, instance);
    }

    _destroyPreparedInstances() {
        const instances = [...this._preparedInstances.values()];
        for (let index = instances.length - 1; index >= 0; index -= 1) {
            try {
                instances[index]?.destroy?.();
            } catch (error) {
                console.error('[DynamicToolRenderer] Prepared component destroy failed:', error);
            }
        }
        this._preparedInstances.clear();
    }

    mount(container) {
        if (!this._initialized) throw new Error('Call await init() before mount().');
        if (this._destroyed) throw new Error('A destroyed DynamicToolRenderer cannot be mounted.');
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target || typeof target.appendChild !== 'function') {
            throw new TypeError('mount(container) requires a DOM container.');
        }
        if (this._mounted) {
            if (target !== this._container) throw new Error('DynamicToolRenderer is already mounted in another container.');
            return this;
        }

        this._container = target;
        this._document = target.ownerDocument || document;
        this.element = this._document.createElement('div');
        this.element.className = 'dynamic-tool';
        this.element.setAttribute('data-tool-page', this.definition.name);
        target.appendChild(this.element);
        try {
            this._renderNode(this.definition.root, this.element);
            this._mounted = true;
            return this;
        } catch (error) {
            this._teardownRendered();
            this._destroyPreparedInstances();
            this.element.remove();
            this.element = null;
            this._container = null;
            this._document = null;
            throw error;
        }
    }

    _renderNode(node, parent) {
        switch (node.type) {
            case 'group':
                return this._renderGroup(node, parent);
            case 'component':
                return this._renderComponent(node, parent);
            case 'tabs':
                return this._renderTabs(node, parent);
            case 'slot':
                return this._renderSlot(node, parent);
            default:
                throw new Error(`Unsupported tool node type: ${node.type}`);
        }
    }

    _createHost(node, className, parent) {
        const host = this._document.createElement('div');
        host.classList.add(className);
        host.setAttribute('data-tool-node', node.id);
        host.id = `dynamic-tool-${this._sequence}-${node.id}`;
        for (const customClass of node.class_names || []) host.classList.add(customClass);
        if (node.aria_label) host.setAttribute('aria-label', node.aria_label);
        parent.appendChild(host);
        this._hosts.set(node.id, host);
        return host;
    }

    _renderGroup(node, parent) {
        const host = this._createHost(node, 'dynamic-tool__group', parent);
        const layout = node.layout || {};
        if (layout.mode) host.classList.add(`dynamic-tool--${layout.mode}`);
        if (layout.gap) host.classList.add(`dynamic-tool--gap-${layout.gap}`);
        if (layout.columns) host.classList.add(`dynamic-tool--columns-${layout.columns}`);
        if (layout.align) host.classList.add(`dynamic-tool--align-${layout.align}`);
        node.children.forEach((child) => this._renderNode(child, host));
        return host;
    }

    _renderComponent(node, parent) {
        const host = this._createHost(node, 'dynamic-tool__component', parent);
        const optionsRecord = this._createComponentOptions(node);
        const options = optionsRecord.options;
        options.container = host;
        options.containerId = host.id;

        let instance = this._preparedInstances.get(node.id) || null;
        const reusedPrepared = instance !== null;
        if (instance) {
            this._preparedInstances.delete(node.id);
        } else {
            instance = this.factory.create(node.component, options);
            optionsRecord.setInstance(instance);
        }
        if (!instance) throw new Error(`ComponentFactory could not create "${node.component}" for node "${node.id}".`);
        this._components.set(node.id, instance);
        this._instanceOrder.push(instance);
        // 重用預先建立的實例時，上面新算出的 options 會被丟棄，該實例持有的是
        // _prepareComponent 當時的值，因此不能當成「已套用」。
        this._trackBindings(instance, node, reusedPrepared ? null : options);
        this._mountInstance(instance, host);
        this._registerControls(instance, host, node);
        return host;
    }

    _createComponentOptions(node) {
        const options = cloneData(node.options || {}, `definition node ${node.id} options`);
        for (const [optionName, statePath] of Object.entries(node.bindings || {})) {
            const bound = readPath(this.state, statePath);
            if (!bound.found) throw new Error(`Missing bound state path "${statePath}".`);
            options[optionName] = cloneData(bound.value, `state.${statePath}`);
        }

        let instance = null;
        for (const [eventName, commandId] of Object.entries(node.events || {})) {
            const handler = commandFromRegistry(this.commandRegistry, commandId);
            options[eventName] = (...eventArgs) => handler(Object.freeze({
                commandId,
                nodeId: node.id,
                node,
                renderer: this,
                component: instance,
            }), ...eventArgs);
        }
        return {
            options,
            setInstance(value) { instance = value; },
        };
    }

    _renderTabs(node, parent) {
        const host = this._createHost(node, 'dynamic-tool__tabs', parent);
        const contentHosts = node.tabs.map((tab) => {
            const content = this._document.createElement('div');
            content.className = 'dynamic-tool__tab-content';
            content.setAttribute('data-tool-tab-content', tab.id);
            return { tab, content };
        });
        const options = cloneData(node.options || {}, `definition node ${node.id} options`);
        let instance = null;
        const commandId = node.events?.onTabChange;
        if (commandId) {
            const handler = commandFromRegistry(this.commandRegistry, commandId);
            options.onTabChange = (...eventArgs) => handler(Object.freeze({
                commandId,
                nodeId: node.id,
                node,
                renderer: this,
                component: instance,
            }), ...eventArgs);
        }
        options.containerId = host.id;
        options.tabs = contentHosts.map(({ tab, content }) => ({
            id: tab.id,
            title: tab.title,
            content,
        }));

        instance = this.factory.create('TabContainer', options);
        if (!instance) throw new Error('ComponentFactory could not create "TabContainer".');
        this._components.set(node.id, instance);
        this._instanceOrder.push(instance);
        this._mountInstance(instance, host);
        this._registerControls(instance, host, node);
        contentHosts.forEach(({ tab, content }) => this._renderNode(tab.content, content));
        return host;
    }

    _renderSlot(node, parent) {
        return this._createHost(node, 'dynamic-tool__slot', parent);
    }

    _mountInstance(instance, host) {
        const element = instanceElement(instance);
        if ((element && contains(host, element)) || host.childNodes.length > 0) return;
        if (typeof instance.mount === 'function') {
            const result = instance.mount(host);
            if (result && Number.isInteger(result.nodeType) && !contains(host, result)) host.appendChild(result);
            const mountedElement = instanceElement(instance);
            if (mountedElement && !contains(host, mountedElement) && host.childNodes.length === 0) {
                host.appendChild(mountedElement);
            }
            return;
        }
        if (element) {
            host.appendChild(element);
            return;
        }
        if (Number.isInteger(instance?.nodeType)) {
            host.appendChild(instance);
            return;
        }
        throw new Error('Tool component does not expose mount(), element, or a DOM node.');
    }

    _registerControls(instance, host, node) {
        const controls = new Set();
        if (host.matches?.(INTERACTIVE_SELECTOR)) controls.add(host);
        host.querySelectorAll?.(INTERACTIVE_SELECTOR).forEach((element) => controls.add(element));
        for (const candidate of [instance?.button, instance?.input, instance?.textarea, instance?.select, instance?.selector, instance?.fileInput]) {
            if (candidate && Number.isInteger(candidate.nodeType)) controls.add(candidate);
        }
        const commandIds = Object.freeze([...new Set(Object.values(node.events || {}))]);
        controls.forEach((element) => {
            this.controlRecords.set(element, {
                instance,
                nodeId: node.id,
                commandIds,
                renderer: this,
            });
        });
    }

    _clearControlRecords(instance, host = null) {
        for (const [element, record] of this.controlRecords) {
            if (record?.renderer === this && (record.instance === instance || contains(host, element))) {
                this.controlRecords.delete(element);
            }
        }
    }

    getComponent(id) {
        return this._components.get(id) || null;
    }

    getHost(id) {
        return this._hosts.get(id) || null;
    }

    setState(path, value) {
        if (this._destroyed) throw new Error('A destroyed DynamicToolRenderer cannot update state.');
        const cloned = cloneData(value, `state.${path}`);
        writePath(this.state, path, cloned);
        if (this._mounted) this._applyBindings(path);
        return this;
    }

    /**
     * 登記節點的綁定。傳入 options 表示元件正是用這批值建構的，可直接視為已套用，
     * 之後同值的 setState 就不必再推一次；傳 null 則等第一次 setState 才套用。
     */
    _trackBindings(instance, node, options = null) {
        for (const [optionName, statePath] of Object.entries(node.bindings || {})) {
            const record = { instance, node, optionName, statePath, applied: false, lastValue: undefined };
            if (options) {
                record.applied = true;
                record.lastValue = options[optionName];
            }
            this._bindingRecords.push(record);
        }
    }

    _applyBindings(changedPath = null) {
        const records = changedPath === null
            ? this._bindingRecords
            : this._bindingRecords.filter(({ statePath }) => pathsOverlap(statePath, changedPath));
        const updates = [];
        for (const record of records) {
            const { instance, node, optionName, statePath } = record;
            const bound = readPath(this.state, statePath);
            if (!bound.found) throw new Error(`Missing bound state path "${statePath}".`);
            const value = cloneData(bound.value, `state.${statePath}`);
            // 值沒變就不推。消費端常在每次同步對每個葉路徑呼叫 setState，而沒有對應
            // setter 的綁定會走 _replaceComponent——那是整個元件銷毀重建。
            // 比較對象是「上次推給元件的值」而非元件現值：拿現值比，會在每次同步
            // 把使用者尚未提交的輸入蓋回去。
            if (record.applied && sameData(record.lastValue, value)) continue;
            const setterName = `set${optionName.charAt(0).toUpperCase()}${optionName.slice(1)}`;
            const setter = typeof instance[setterName] === 'function'
                ? instance[setterName].bind(instance)
                : optionName === 'value' && typeof instance.setValue === 'function'
                    ? instance.setValue.bind(instance)
                    : null;
            updates.push({ record, instance, node, optionName, value, setter });
        }

        const replacements = new Map();
        for (const update of updates) {
            if (update.setter === null) replacements.set(update.instance, update.node);
        }
        for (const [instance, node] of replacements) this._replaceComponent(node, instance);

        for (const { record, instance, node, optionName, value, setter } of updates) {
            if (replacements.has(instance)) continue;
            if (instance.options && typeof instance.options === 'object') instance.options[optionName] = value;
            const host = this._hosts.get(node.id);
            this._clearControlRecords(instance, host);
            setter(value);
            this._registerControls(instance, host, node);
            record.applied = true;
            record.lastValue = value;
        }
    }

    _replaceComponent(node, previousInstance) {
        const host = this._hosts.get(node.id);
        if (!host) throw new Error(`Cannot replace missing tool component host "${node.id}".`);

        const orderIndex = this._instanceOrder.indexOf(previousInstance);
        if (orderIndex < 0) throw new Error(`Cannot replace untracked tool component "${node.id}".`);

        try {
            previousInstance?.destroy?.();
        } catch (error) {
            console.error('[DynamicToolRenderer] Component replacement destroy failed:', error);
        }
        this._instanceOrder.splice(orderIndex, 1);
        this._components.delete(node.id);
        this._bindingRecords = this._bindingRecords.filter(({ instance }) => instance !== previousInstance);
        this._clearControlRecords(previousInstance, host);
        host.replaceChildren();

        const optionsRecord = this._createComponentOptions(node);
        const options = optionsRecord.options;
        options.container = host;
        options.containerId = host.id;
        const instance = this.factory.create(node.component, options);
        optionsRecord.setInstance(instance);
        if (!instance) throw new Error(`ComponentFactory could not recreate "${node.component}" for node "${node.id}".`);

        this._components.set(node.id, instance);
        this._instanceOrder.splice(orderIndex, 0, instance);
        // 新實例就是用這批 options 建構的，等同已套用；不種下去的話每次同步都會
        // 再重建一次（沒有 setter 的綁定永遠走這條路）。
        this._trackBindings(instance, node, options);
        this._mountInstance(instance, host);
        this._registerControls(instance, host, node);
    }

    _rerender() {
        const root = this.element;
        this._teardownRendered();
        root.replaceChildren();
        try {
            this._renderNode(this.definition.root, root);
        } catch (error) {
            this._teardownRendered();
            root.replaceChildren();
            throw error;
        }
    }

    _teardownRendered() {
        for (let index = this._instanceOrder.length - 1; index >= 0; index -= 1) {
            try {
                this._instanceOrder[index]?.destroy?.();
            } catch (error) {
                console.error('[DynamicToolRenderer] Component destroy failed:', error);
            }
        }
        this._instanceOrder = [];
        this._bindingRecords = [];
        this._components.clear();
        this._hosts.clear();
        for (const [element, record] of this.controlRecords) {
            if (record?.renderer === this) this.controlRecords.delete(element);
        }
    }

    destroy() {
        if (this._destroyed) return;
        this._teardownRendered();
        this._destroyPreparedInstances();
        this.element?.remove();
        this.element = null;
        this._container = null;
        this._document = null;
        this._mounted = false;
        this._initialized = false;
        this._destroyed = true;
    }
}

export default DynamicToolRenderer;
