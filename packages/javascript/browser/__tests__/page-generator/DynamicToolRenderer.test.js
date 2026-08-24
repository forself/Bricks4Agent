import { afterEach, describe, expect, it, vi } from 'vitest';

const officialFactoryCreate = vi.hoisted(() => vi.fn());
vi.mock('../../ui_components/binding/ComponentFactory.js', () => ({
    ComponentFactory: {
        create: officialFactoryCreate,
        registry: {
            BasicButton: class BasicButton {},
            DownloadButton: class DownloadButton {},
            TabContainer: class TabContainer {},
            TextInput: class TextInput {},
        },
    },
}));
vi.mock('../../ui_components/layout/TabContainer/TabContainer.js', () => {
    class TabContainer {
        constructor(options = {}) {
            this.options = options;
            this.containerId = options.containerId;
            this.activeTabId = options.tabs?.[0]?.id ?? null;
            this.element = document.createElement('div');
            for (const tab of options.tabs ?? []) {
                const button = document.createElement('button');
                button.type = 'button';
                button.textContent = tab.title;
                this.element.appendChild(button);
                if (tab.content instanceof Node) this.element.appendChild(tab.content);
            }
            document.getElementById(this.containerId)?.appendChild(this.element);
        }

        getActiveTabId() {
            return this.activeTabId;
        }

        destroy() {
            this.element.remove();
        }
    }
    return { TabContainer, default: TabContainer };
});

import { DynamicToolRenderer } from '../../page-generator/DynamicToolRenderer.js';
import { ComponentFactory } from '../../ui_components/binding/ComponentFactory.js';

const mountedRoots = [];

afterEach(() => {
    vi.restoreAllMocks();
    officialFactoryCreate.mockReset();
    for (const root of mountedRoots.splice(0)) root.remove();
});

function createDefinition(children = null) {
    return {
        schema_version: 1,
        name: 'DynamicToolContractPage',
        type: 'tool',
        root: {
            type: 'group',
            id: 'root',
            children: children ?? [
                {
                    type: 'component',
                    id: 'name-input',
                    component: 'TextInput',
                    options: { label: 'Name' },
                    bindings: { value: 'form.name' },
                    events: { onChange: 'form.name.changed' },
                },
                {
                    type: 'tabs',
                    id: 'workspace-tabs',
                    options: { closable: false },
                    events: { onTabChange: 'workspace.tab.changed' },
                    tabs: [
                        {
                            id: 'theme',
                            title: 'Theme',
                            content: {
                                type: 'component',
                                id: 'save-button',
                                component: 'BasicButton',
                                options: { customLabel: 'Save' },
                                events: { onClick: 'theme.save' },
                            },
                        },
                        {
                            id: 'components',
                            title: 'Components',
                            content: {
                                type: 'slot',
                                id: 'components-slot',
                                aria_label: 'Components workspace',
                            },
                        },
                    ],
                },
            ],
        },
    };
}

class FakeComponent {
    constructor(name, options, lifecycle) {
        this.name = name;
        this.options = options;
        this.lifecycle = lifecycle;
        this.element = document.createElement(name === 'BasicButton' ? 'button' : name === 'Link' ? 'a' : 'div');
        this.element.dataset.fakeComponent = name;
        if (name === 'Link') this.element.setAttribute('href', options.href || '#');
        if (name === 'TextInput') {
            this.input = document.createElement('input');
            this.element.appendChild(this.input);
        }
        if (name === 'TabContainer') {
            for (const tab of options.tabs ?? []) {
                const button = document.createElement('button');
                button.type = 'button';
                button.textContent = tab.title;
                this.element.appendChild(button);
                if (tab.content instanceof Node) this.element.appendChild(tab.content);
            }
        }
        if (name === 'List') this._renderListButton();
    }

    mount(host) {
        host.appendChild(this.element);
        this.lifecycle.push(`mount:${this.name}`);
        return this;
    }

    setValue(value) {
        this.value = value;
        if (this.input) this.input.value = String(value ?? '');
    }

    setItems(items) {
        this.options.items = items;
        this.element.replaceChildren();
        this._renderListButton();
    }

    _renderListButton() {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = String(this.options.items?.[0]?.label ?? 'item');
        this.element.appendChild(button);
    }

    destroy() {
        this.lifecycle.push(`destroy:${this.name}`);
        this.element.remove();
    }
}

function createFactory({ unknown = [] } = {}) {
    const lifecycle = [];
    const instances = [];
    const factory = {
        has: vi.fn((name) => !unknown.includes(name)),
        create: vi.fn((name, options = {}) => {
            if (unknown.includes(name)) return null;
            const instance = new FakeComponent(name, options, lifecycle);
            instances.push(instance);
            lifecycle.push(`create:${name}`);
            return instance;
        }),
    };
    return { factory, instances, lifecycle };
}

async function createMountedRenderer(options = {}) {
    const root = document.createElement('div');
    document.body.appendChild(root);
    mountedRoots.push(root);
    const renderer = new DynamicToolRenderer(options);
    await renderer.init();
    renderer.mount(root);
    return { renderer, root };
}

function findBareInteractive(root, records) {
    const selector = 'a[href],button,input,select,textarea,[role="button"],[role="switch"],[role="slider"]';
    return [...root.querySelectorAll(selector)].filter((element) => !records.has(element));
}

describe('DynamicToolRenderer', () => {
    it('uses the official ComponentFactory by default for components and TabContainer', async () => {
        const { factory } = createFactory();
        officialFactoryCreate.mockImplementation(factory.create);
        const commands = {
            'form.name.changed': () => {},
            'workspace.tab.changed': () => {},
            'theme.save': () => {},
        };

        const { renderer } = await createMountedRenderer({
            definition: createDefinition(),
            commandRegistry: commands,
            state: { form: { name: 'Ada' } },
        });

        expect(ComponentFactory.create.mock.calls.map(([name]) => name)).toEqual(expect.arrayContaining([
            'TextInput',
            'BasicButton',
            'TabContainer',
        ]));
        expect(renderer.getComponent('workspace-tabs')).toBeTruthy();
    });

    it('resolves bindings, updates mounted state, and dispatches allowlisted commands with context', async () => {
        const { factory } = createFactory();
        const changed = vi.fn();
        const tabChanged = vi.fn();
        const commandRegistry = new Map([
            ['form.name.changed', changed],
            ['workspace.tab.changed', tabChanged],
            ['theme.save', vi.fn()],
        ]);
        const { renderer } = await createMountedRenderer({
            definition: createDefinition(),
            commandRegistry,
            state: { form: { name: 'Ada' } },
            factory,
        });

        const initialInput = renderer.getComponent('name-input');
        const initialTabs = renderer.getComponent('workspace-tabs');
        const initialRootHost = renderer.getHost('root');
        const initialTabsHost = renderer.getHost('workspace-tabs');
        expect(initialInput.options.value).toBe('Ada');
        expect(renderer.getHost('name-input')).toBeInstanceOf(HTMLElement);

        renderer.setState('form.name', 'Grace');
        const input = renderer.getComponent('name-input');
        expect(input.options.value).toBe('Grace');
        expect(input).toBe(initialInput);
        expect(renderer.getComponent('workspace-tabs')).toBe(initialTabs);
        expect(renderer.getHost('root')).toBe(initialRootHost);
        expect(renderer.getHost('workspace-tabs')).toBe(initialTabsHost);

        input.options.onChange('Lin', { source: 'test' });
        expect(changed).toHaveBeenCalledTimes(1);
        expect(changed.mock.calls[0][0]).toEqual(expect.objectContaining({
            commandId: 'form.name.changed',
            nodeId: 'name-input',
            renderer,
            component: input,
        }));
        expect(changed.mock.calls[0].slice(1)).toEqual(['Lin', { source: 'test' }]);

        const tabs = renderer.getComponent('workspace-tabs');
        tabs.options.onTabChange({ tabId: 'components' });
        expect(tabChanged.mock.calls[0][0]).toEqual(expect.objectContaining({
            commandId: 'workspace.tab.changed',
            nodeId: 'workspace-tabs',
            renderer,
            component: tabs,
        }));
    });

    it('rerenders components whose bound option has no supported setter', async () => {
        const definition = createDefinition([{
            type: 'component',
            id: 'label-only',
            component: 'BasicButton',
            bindings: { customLabel: 'toolbar.label' },
        }]);
        const { factory } = createFactory();
        const { renderer } = await createMountedRenderer({
            definition,
            commandRegistry: {},
            state: { toolbar: { label: 'Before' } },
            factory,
        });
        const before = renderer.getComponent('label-only');
        const rootHost = renderer.getHost('root');

        renderer.setState('toolbar.label', 'After');

        const after = renderer.getComponent('label-only');
        expect(after).not.toBe(before);
        expect(after.options.customLabel).toBe('After');
        expect(renderer.getHost('root')).toBe(rootHost);
        expect(renderer.getHost('label-only')).toBeInstanceOf(HTMLElement);
    });

    it('accepts object command registries as well as Map registries', async () => {
        const { factory } = createFactory();
        const save = vi.fn();
        const { renderer } = await createMountedRenderer({
            definition: createDefinition(),
            commandRegistry: {
                'form.name.changed': vi.fn(),
                'workspace.tab.changed': vi.fn(),
                'theme.save': save,
            },
            state: { form: { name: 'Ada' } },
            factory,
        });

        renderer.getComponent('save-button').options.onClick('payload');
        expect(save).toHaveBeenCalledWith(
            expect.objectContaining({ commandId: 'theme.save', nodeId: 'save-button' }),
            'payload',
        );
    });

    it('records every renderer-owned interactive element with instance and command provenance', async () => {
        const { factory } = createFactory();
        const { renderer, root } = await createMountedRenderer({
            definition: createDefinition(),
            commandRegistry: {
                'form.name.changed': vi.fn(),
                'workspace.tab.changed': vi.fn(),
                'theme.save': vi.fn(),
            },
            state: { form: { name: 'Ada' } },
            factory,
        });

        expect(renderer.controlRecords).toBeInstanceOf(Map);
        expect(renderer.controlRecords.size).toBeGreaterThan(0);
        expect(findBareInteractive(root, renderer.controlRecords)).toEqual([]);

        for (const [element, record] of renderer.controlRecords) {
            expect(element).toBeInstanceOf(HTMLElement);
            expect(root.contains(element)).toBe(true);
            expect(record).toEqual(expect.objectContaining({
                instance: expect.any(Object),
                nodeId: expect.any(String),
                commandIds: expect.any(Array),
                renderer,
            }));
        }

        // Negative self-test: prove the detector is capable of rejecting a raw
        // control instead of merely returning an empty list for every page.
        const rawButton = document.createElement('button');
        root.appendChild(rawButton);
        expect(findBareInteractive(root, renderer.controlRecords)).toContain(rawButton);
        rawButton.remove();
        expect(findBareInteractive(root, renderer.controlRecords)).toEqual([]);
    });

    it('records official links as interactive controls with renderer provenance', async () => {
        const { factory } = createFactory();
        const definition = createDefinition([{
            type: 'component',
            id: 'docs-link',
            component: 'Link',
            options: { text: 'Docs', href: '/docs', scope: 'internal' },
        }]);
        const { renderer, root } = await createMountedRenderer({
            definition,
            commandRegistry: {},
            factory,
        });
        const link = root.querySelector('a[href="/docs"]');

        expect(link).toBeInstanceOf(HTMLAnchorElement);
        expect(renderer.controlRecords.get(link)).toEqual(expect.objectContaining({
            instance: renderer.getComponent('docs-link'),
            nodeId: 'docs-link',
            commandIds: [],
            renderer,
        }));
        expect(findBareInteractive(root, renderer.controlRecords)).toEqual([]);
    });

    it('refreshes provenance when a component setter replaces its internal controls', async () => {
        const { factory } = createFactory();
        const definition = createDefinition([{
            type: 'component',
            id: 'palette',
            component: 'List',
            bindings: { items: 'palette.items' },
        }]);
        const { renderer, root } = await createMountedRenderer({
            definition,
            commandRegistry: {},
            state: { palette: { items: [{ label: 'Before' }] } },
            factory,
        });
        const instance = renderer.getComponent('palette');
        const before = instance.element.querySelector('button');
        expect(renderer.controlRecords.get(before)?.instance).toBe(instance);

        renderer.setState('palette.items', [{ label: 'After' }]);

        const after = instance.element.querySelector('button');
        expect(after).not.toBe(before);
        expect(renderer.controlRecords.has(before)).toBe(false);
        expect(renderer.controlRecords.get(after)).toEqual(expect.objectContaining({
            instance,
            nodeId: 'palette',
            renderer,
        }));
        expect(findBareInteractive(root, renderer.controlRecords)).toEqual([]);
    });

    it('fails closed during init for unknown commands without creating components', async () => {
        const definition = createDefinition();
        definition.root.children[0].events.onChange = 'missing.command';
        const { factory } = createFactory();
        const renderer = new DynamicToolRenderer({
            definition,
            commandRegistry: {
                'workspace.tab.changed': vi.fn(),
                'theme.save': vi.fn(),
            },
            state: { form: { name: 'Ada' } },
            factory,
        });

        await expect(renderer.init()).rejects.toThrow(/missing\.command|command/i);
        expect(factory.create).not.toHaveBeenCalled();
        expect(renderer.controlRecords.size).toBe(0);
    });

    it('fails closed during init for unknown components before partial construction', async () => {
        const definition = createDefinition([
            { type: 'component', id: 'known', component: 'TextInput' },
            { type: 'component', id: 'unknown', component: 'DefinitelyNotRegistered' },
        ]);
        const { factory, instances, lifecycle } = createFactory({ unknown: ['DefinitelyNotRegistered'] });
        const renderer = new DynamicToolRenderer({ definition, commandRegistry: {}, factory });

        await expect(renderer.init()).rejects.toThrow(/DefinitelyNotRegistered|component/i);
        expect(instances).toHaveLength(0);
        expect(lifecycle).toEqual([]);
        expect(renderer.controlRecords.size).toBe(0);
    });

    it('destroys created component trees in strict reverse order and clears lookups', async () => {
        const definition = createDefinition([
            { type: 'component', id: 'first', component: 'TextInput' },
            { type: 'component', id: 'second', component: 'BasicButton' },
            { type: 'component', id: 'third', component: 'DownloadButton' },
        ]);
        const { factory, lifecycle } = createFactory();
        const { renderer, root } = await createMountedRenderer({
            definition,
            commandRegistry: {},
            factory,
        });

        renderer.destroy();
        expect(lifecycle.filter((entry) => entry.startsWith('destroy:'))).toEqual([
            'destroy:DownloadButton',
            'destroy:BasicButton',
            'destroy:TextInput',
        ]);
        expect(renderer.getComponent('first')).toBeNull();
        expect(renderer.getHost('first')).toBeNull();
        expect(renderer.controlRecords.size).toBe(0);
        expect(root.childElementCount).toBe(0);
    });
});
