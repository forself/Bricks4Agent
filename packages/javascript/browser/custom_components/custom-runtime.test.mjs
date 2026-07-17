import assert from 'node:assert/strict';
import { CustomComponentRegistry } from './CustomComponentRegistry.js';
import { CustomComponentRenderer } from './CustomComponentRenderer.js';
import { DynamicPageRenderer } from '../page-generator/DynamicPageRenderer.js';

class FakeClassList {
    constructor() {
        this.values = new Set();
    }

    add(...names) {
        names.forEach((name) => this.values.add(name));
    }

    contains(name) {
        return this.values.has(name);
    }
}

class FakeElement {
    constructor(tagName, ownerDocument) {
        this.tagName = tagName;
        this.ownerDocument = ownerDocument;
        this.nodeType = 1;
        this.parentNode = null;
        this.childNodes = [];
        this.children = this.childNodes;
        this.classList = new FakeClassList();
        this.attributes = {};
        this.id = '';
    }

    appendChild(node) {
        node.parentNode?.removeChild?.(node);
        this.childNodes.push(node);
        node.parentNode = this;
        return node;
    }

    removeChild(node) {
        const index = this.childNodes.indexOf(node);
        if (index >= 0) this.childNodes.splice(index, 1);
        node.parentNode = null;
        return node;
    }

    contains(node) {
        for (let current = node; current; current = current.parentNode) {
            if (current === this) return true;
        }
        return false;
    }

    setAttribute(name, value) {
        this.attributes[name] = String(value);
    }

    replaceChildren() {
        this.childNodes.forEach((node) => { node.parentNode = null; });
        this.childNodes = [];
        this.children = this.childNodes;
    }

    remove() {
        this.parentNode?.removeChild?.(this);
    }

    get firstChild() {
        return this.childNodes[0] ?? null;
    }
}

class FakeDocument {
    createElement(tagName) {
        return new FakeElement(tagName, this);
    }
}

const documentRef = new FakeDocument();
globalThis.document = documentRef;

function makeFactory(destroyed, { failName = null } = {}) {
    class AutoMountComponent {
        constructor(options) {
            this.options = options;
            this.value = options.value ?? '';
            this.element = documentRef.createElement('input');
            options.container.appendChild(this.element);
        }
        mount() { throw new Error('auto-mounted component must not be mounted twice'); }
        getValue() { return this.value; }
        setValue(value) { this.value = value; }
        destroy() { destroyed.push('auto'); this.element.remove(); }
    }

    class MountComponent {
        constructor(options) {
            this.options = options;
            this.checked = Boolean(options.value);
            this.element = documentRef.createElement('button');
        }
        mount(container) { container.appendChild(this.element); return this; }
        isChecked() { return this.checked; }
        setChecked(value) { this.checked = Boolean(value); }
        destroy() { destroyed.push('mount'); this.element.remove(); }
    }

    class ElementComponent {
        constructor(options) {
            this.options = options;
            this.values = options.value ?? null;
            this.element = documentRef.createElement('output');
        }
        getValues() { return this.values; }
        setValues(value) { this.values = value; }
        destroy() { destroyed.push('element'); this.element.remove(); }
    }

    class RenderComponent {
        constructor(options) {
            this.options = options;
            this.value = options.value ?? null;
            this.rendered = null;
        }
        render() { this.rendered = documentRef.createElement('canvas'); return this.rendered; }
        getValue() { return this.value; }
        setValue(value) { this.value = value; }
        destroy() { destroyed.push('render'); this.rendered?.remove(); }
    }

    return {
        registry: {
            AutoMountComponent,
            MountComponent,
            ElementComponent,
            RenderComponent,
        },
        register(name, componentClass) {
            if (name === failName) throw new Error('factory registration failed');
            this.registry[name] = componentClass;
        },
        create(name, options) {
            const ComponentClass = this.registry[name];
            return ComponentClass ? new ComponentClass(options) : null;
        },
    };
}

const atomicDefinition = {
    schema_version: 1,
    component_id: 'custom.text_field',
    registry_name: 'CustomTextField',
    display_name: 'Custom Text Field',
    version: '1.0.0',
    kind: 'atomic',
    root: {
        type: 'component',
        id: 'text',
        component: 'AutoMountComponent',
        options: {
            placeholder: 'Authored placeholder',
            target: 'must be removed',
        },
    },
};

const compositeDefinition = {
    schema_version: 1,
    component_id: 'custom.profile',
    registry_name: 'CustomProfile',
    display_name: 'Custom Profile',
    version: '1.0.0',
    kind: 'composite',
    root: {
        type: 'group',
        id: 'root',
        layout: { mode: 'grid', gap: 'md', columns: 2, align: 'stretch' },
        class_names: ['profile-grid'],
        children: [
            { type: 'custom', id: 'custom_leaf', component: 'CustomTextField' },
            { type: 'component', id: 'check', component: 'MountComponent' },
            { type: 'component', id: 'details', component: 'ElementComponent' },
            { type: 'component', id: 'preview', component: 'RenderComponent' },
        ],
    },
};

function response(body, status = 200) {
    return {
        ok: status >= 200 && status < 300,
        status,
        async json() { return body; },
    };
}

async function run() {
    const destroyed = [];
    const factory = makeFactory(destroyed);
    const registry = new CustomComponentRegistry({ factory });

    // Forward custom references are valid because the whole batch is analyzed first.
    registry.registerMany([compositeDefinition, atomicDefinition]);
    assert.equal(registry.has('CustomProfile'), true);
    assert.equal(registry.has('custom.text_field'), true);

    const factoryInstance = factory.create('CustomProfile', {});
    assert.equal(factoryInstance instanceof CustomComponentRenderer, true);

    const container = documentRef.createElement('main');
    const renderer = registry.create('CustomProfile', {
        nodeOptions: {
            custom_leaf: {
                placeholder: 'Runtime placeholder',
                container: 'untrusted',
                target: 'untrusted',
            },
        },
        values: {
            custom_leaf: 'Ada',
            check: true,
            details: { role: 'admin' },
            preview: 'ready',
        },
    }).mount(container);

    assert.deepEqual(renderer.getValue(), {
        custom_leaf: 'Ada',
        check: true,
        details: { role: 'admin' },
        preview: 'ready',
    });
    assert.equal(renderer.element.classList.contains('custom-component--grid'), true);
    assert.equal(renderer.element.classList.contains('profile-grid'), true);

    const nested = renderer.getComponent('custom_leaf');
    const nestedLeaf = nested.getComponent('text');
    assert.equal(nestedLeaf.options.placeholder, 'Runtime placeholder');
    assert.equal(nestedLeaf.options.target, undefined);
    assert.equal(nestedLeaf.options.container.nodeType, 1);
    assert.equal(nestedLeaf.options.containerId, nestedLeaf.options.container.id);

    renderer.setValue({
        custom_leaf: 'Grace',
        check: false,
        details: { role: 'reader' },
        preview: 'updated',
    });
    assert.deepEqual(renderer.getValues(), {
        custom_leaf: 'Grace',
        check: false,
        details: { role: 'reader' },
        preview: 'updated',
    });

    renderer.destroy();
    renderer.destroy();
    assert.deepEqual(destroyed, ['render', 'element', 'mount', 'auto']);
    assert.equal(container.childNodes.length, 0);

    const resolverRegistrations = new Map();
    registry.installFieldResolver({
        registerComponent(name, create) { resolverRegistrations.set(name, create); },
    });
    const fromResolver = resolverRegistrations.get('CustomTextField')({
        componentOptions: { placeholder: 'Field resolver override' },
        nodeOptions: { placeholder: 'lower priority' },
        defaultValue: 'initial',
    });
    const resolverContainer = documentRef.createElement('main');
    fromResolver.mount(resolverContainer);
    assert.equal(fromResolver.getComponent('text').options.placeholder, 'Field resolver override');
    assert.equal(fromResolver.getValue(), 'initial');
    fromResolver.destroy();

    // Factory failure rolls back both the external factory and internal maps.
    const rollbackFactory = makeFactory([], { failName: 'Broken' });
    const rollbackRegistry = new CustomComponentRegistry({ factory: rollbackFactory });
    const brokenDefinition = {
        ...atomicDefinition,
        component_id: 'custom.broken',
        registry_name: 'Broken',
        display_name: 'Broken',
    };
    assert.throws(() => rollbackRegistry.register(brokenDefinition), /factory registration failed/);
    assert.equal(rollbackRegistry.has('Broken'), false);
    assert.equal(Object.hasOwn(rollbackFactory.registry, 'Broken'), false);

    // Updating one definition revalidates existing reverse dependants against the next graph.
    const dependencyRegistry = new CustomComponentRegistry({
        factory: makeFactory([]),
        registerWithFactory: false,
    });
    const atomicAlias = {
        ...atomicDefinition,
        component_id: 'custom.atomic_alias',
        registry_name: 'AtomicAlias',
        display_name: 'Atomic Alias',
        root: { type: 'custom', id: 'aliased', component: 'CustomTextField' },
    };
    dependencyRegistry.registerMany([atomicAlias, atomicDefinition]);
    const templateReplacement = {
        ...atomicDefinition,
        kind: 'template',
        root: {
            type: 'group',
            id: 'depth-4',
            children: [{
                type: 'group',
                id: 'depth-3',
                children: [{
                    type: 'group',
                    id: 'depth-2',
                    children: [{
                        type: 'group',
                        id: 'depth-1',
                        children: [{ type: 'component', id: 'leaf', component: 'AutoMountComponent' }],
                    }],
                }],
            }],
        },
    };
    assert.throws(() => dependencyRegistry.register(templateReplacement), /KIND_MISMATCH/);
    assert.equal(dependencyRegistry.get('CustomTextField').kind, 'atomic');
    assert.equal(dependencyRegistry.get('AtomicAlias').kind, 'atomic');

    // Disposal removes only factory classes still owned by this registry and is idempotent.
    const ownershipFactory = makeFactory([]);
    const ownershipRegistry = new CustomComponentRegistry({ factory: ownershipFactory });
    ownershipRegistry.register(atomicDefinition);
    const externalReplacement = class ExternalReplacement {};
    ownershipFactory.registry.CustomTextField = externalReplacement;
    ownershipRegistry.dispose();
    ownershipRegistry.dispose();
    assert.equal(ownershipFactory.registry.CustomTextField, externalReplacement);
    assert.equal(ownershipRegistry.has('CustomTextField'), false);
    assert.throws(() => ownershipRegistry.register(atomicDefinition), /disposed/);

    const removalFactory = makeFactory([]);
    const removalRegistry = new CustomComponentRegistry({ factory: removalFactory });
    removalRegistry.register(atomicDefinition);
    removalRegistry.dispose();
    removalRegistry.dispose();
    assert.equal(Object.hasOwn(removalFactory.registry, 'CustomTextField'), false);

    const manifest = {
        components: [
            { component_id: 'custom.profile', registry_name: 'CustomProfile', path: 'definitions/profile.json' },
            { component_id: 'custom.text_field', registry_name: 'CustomTextField', path: 'definitions/text.json' },
        ],
    };
    const payloads = new Map([
        ['https://example.test/components/registry.json', response(manifest)],
        ['https://example.test/components/definitions/profile.json', response(compositeDefinition)],
        ['https://example.test/components/definitions/text.json', response(atomicDefinition)],
    ]);
    const fetchCalls = [];
    const fetchReceivers = [];
    const folderRegistry = new CustomComponentRegistry({
        factory: makeFactory([]),
        fetchImpl: async function fetchForTest(url) {
            fetchReceivers.push(this);
            const key = String(url);
            fetchCalls.push(key);
            return payloads.get(key) ?? response({}, 404);
        },
    });

    // Folder prefetch validates and returns definitions without mutating registry or factory state.
    const prefetchFactory = makeFactory([]);
    const prefetchRegistry = new CustomComponentRegistry({
        factory: prefetchFactory,
        fetchImpl: async (url) => payloads.get(String(url)) ?? response({}, 404),
    });
    const prefetched = await prefetchRegistry.fetchFolderDefinitions('https://example.test/components');
    assert.equal(prefetched.length, 2);
    assert.equal(prefetchRegistry.list().length, 0);
    assert.equal(Object.hasOwn(prefetchFactory.registry, 'CustomProfile'), false);
    assert.equal(Object.hasOwn(prefetchFactory.registry, 'CustomTextField'), false);

    const loaded = await folderRegistry.loadFolder('https://example.test/components');
    assert.equal(loaded.length, 2);
    assert.equal(folderRegistry.has('CustomProfile'), true);
    assert.deepEqual(fetchCalls, [
        'https://example.test/components/registry.json',
        'https://example.test/components/definitions/profile.json',
        'https://example.test/components/definitions/text.json',
    ]);
    assert.equal(fetchReceivers.every((receiver) => receiver === globalThis), true);
    await assert.rejects(
        () => folderRegistry.loadFolder('https://example.test/components', { manifest: '../registry.json' }),
        /safe relative|cannot contain/,
    );

    const beforeFailedFetch = folderRegistry.list().length;
    await assert.rejects(
        () => folderRegistry.loadDefinition('https://example.test/components/missing.json'),
        /HTTP 404/,
    );
    assert.equal(folderRegistry.list().length, beforeFailedFetch);

    // Dynamic inline + folder setup performs one transaction after every fetch succeeds.
    const partialFactory = makeFactory([]);
    const partialRegistry = new CustomComponentRegistry({
        factory: partialFactory,
        fetchImpl: async () => response({}, 404),
    });
    const dynamicWithExternalRegistry = new DynamicPageRenderer({
        customComponentRegistry: partialRegistry,
        customComponents: {
            definitions: [atomicDefinition],
            folder: 'https://example.test/missing-components',
        },
    });
    await assert.rejects(
        () => dynamicWithExternalRegistry._prepareCustomComponents(),
        /HTTP 404/,
    );
    assert.equal(partialRegistry.has('CustomTextField'), false);
    assert.equal(Object.hasOwn(partialFactory.registry, 'CustomTextField'), false);
    dynamicWithExternalRegistry.destroy();
    partialRegistry.register(atomicDefinition);
    assert.equal(partialRegistry.has('CustomTextField'), true);
    partialRegistry.dispose();

    const transactionalFactory = makeFactory([]);
    const transactionalRegistry = new CustomComponentRegistry({
        factory: transactionalFactory,
        fetchImpl: async (url) => payloads.get(String(url)) ?? response({}, 404),
    });
    const originalRegisterMany = transactionalRegistry.registerMany.bind(transactionalRegistry);
    let registerManyCalls = 0;
    transactionalRegistry.registerMany = (definitions) => {
        registerManyCalls += 1;
        return originalRegisterMany(definitions);
    };
    const inlineDynamicDefinition = {
        ...atomicDefinition,
        component_id: 'custom.inline_dynamic',
        registry_name: 'InlineDynamic',
        display_name: 'Inline Dynamic',
    };
    const successfulDynamicPage = new DynamicPageRenderer({
        customComponentRegistry: transactionalRegistry,
        customComponents: {
            definitions: [inlineDynamicDefinition],
            folder: 'https://example.test/components',
        },
    });
    await successfulDynamicPage._prepareCustomComponents();
    assert.equal(registerManyCalls, 1);
    assert.equal(transactionalRegistry.list().length, 3);
    successfulDynamicPage.destroy();
    assert.equal(transactionalRegistry.has('InlineDynamic'), true);
    transactionalRegistry.dispose();

    // Dynamic-owned registries stay local to the page and are disposed with it.
    const dynamicDefinition = {
        ...atomicDefinition,
        component_id: 'custom.dynamic_owned',
        registry_name: 'DynamicOwned',
        display_name: 'Dynamic Owned',
        root: { type: 'component', id: 'button', component: 'BasicButton' },
    };
    const dynamicWithOwnedRegistry = new DynamicPageRenderer({
        customComponents: [dynamicDefinition],
    });
    await dynamicWithOwnedRegistry._prepareCustomComponents();
    const ownedRegistry = dynamicWithOwnedRegistry.getCustomComponentRegistry();
    assert.equal(ownedRegistry.registerWithFactory, false);
    assert.equal(ownedRegistry.has('DynamicOwned'), true);
    assert.equal(Object.hasOwn(ownedRegistry.factory.registry, 'DynamicOwned'), false);
    dynamicWithOwnedRegistry.destroy();
    dynamicWithOwnedRegistry.destroy();
    assert.equal(ownedRegistry.has('DynamicOwned'), false);
    assert.throws(() => ownedRegistry.register(dynamicDefinition), /disposed/);
}

await run();
console.log('custom runtime tests: PASS');
