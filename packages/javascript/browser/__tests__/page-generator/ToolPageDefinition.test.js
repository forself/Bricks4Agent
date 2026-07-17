import { describe, expect, it } from 'vitest';

import { validateToolPageDefinition } from '../../page-generator/ToolPageDefinition.js';

function createDefinition() {
    return {
        schema_version: 1,
        name: 'SelfHostContractPage',
        type: 'tool',
        description: 'JSON-only renderer contract fixture',
        root: {
            type: 'group',
            id: 'root',
            children: [
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
                                type: 'slot',
                                id: 'theme-slot',
                                class_names: ['theme-workspace'],
                                aria_label: 'Theme workspace',
                            },
                        },
                        {
                            id: 'components',
                            title: 'Components',
                            content: {
                                type: 'component',
                                id: 'export-button',
                                component: 'DownloadButton',
                                options: { type: 'json' },
                                events: { onClick: 'components.export' },
                            },
                        },
                    ],
                },
            ],
        },
    };
}

function containsFunction(value, seen = new Set()) {
    if (typeof value === 'function') return true;
    if (value === null || typeof value !== 'object' || seen.has(value)) return false;
    seen.add(value);
    return Reflect.ownKeys(value).some((key) => containsFunction(value[key], seen));
}

describe('validateToolPageDefinition', () => {
    it('accepts the JSON-only group/component/tabs/slot contract', () => {
        const source = createDefinition();
        const parsed = JSON.parse(JSON.stringify(source));
        const result = validateToolPageDefinition(parsed);

        expect(result).toEqual(expect.objectContaining({ valid: true, errors: [] }));
        expect(containsFunction(parsed)).toBe(false);
    });

    it('requires the fixed tool-page envelope', () => {
        const cases = [
            { key: 'schema_version', value: 2 },
            { key: 'name', value: 'not PascalCase' },
            { key: 'type', value: 'page' },
        ];

        for (const testCase of cases) {
            const definition = createDefinition();
            definition[testCase.key] = testCase.value;
            const result = validateToolPageDefinition(definition);
            expect(result.valid, `${testCase.key} should fail`).toBe(false);
            expect(result.errors.length).toBeGreaterThan(0);
        }
    });

    it('rejects executable values anywhere in options, bindings, or events', () => {
        const executableOptions = createDefinition();
        executableOptions.root.children[0].options.formatter = () => 'unsafe';

        const executableEvent = createDefinition();
        executableEvent.root.children[0].events.onChange = () => {};

        const executableBinding = createDefinition();
        executableBinding.root.children[0].bindings.value = { read: () => 'unsafe' };

        for (const definition of [executableOptions, executableEvent, executableBinding]) {
            const result = validateToolPageDefinition(definition);
            expect(result.valid).toBe(false);
            expect(result.errors.length).toBeGreaterThan(0);
        }
    });

    it('rejects accessors without invoking them', () => {
        const definition = createDefinition();
        let invoked = false;
        Object.defineProperty(definition.root.children[0].options, 'label', {
            enumerable: true,
            get() {
                invoked = true;
                return 'Unsafe';
            },
        });

        const result = validateToolPageDefinition(definition);
        expect(result.valid).toBe(false);
        expect(result.errors.join(' ')).toMatch(/data property/i);
        expect(invoked).toBe(false);
    });

    it('rejects raw HTML strings even under otherwise ordinary option keys', () => {
        const definition = createDefinition();
        definition.root.children[0].options.label = '<strong>Name</strong>';

        const result = validateToolPageDefinition(definition);
        expect(result.valid).toBe(false);
        expect(result.errors.join(' ')).toMatch(/raw HTML/i);
    });

    it('rejects sparse arrays instead of silently skipping missing nodes', () => {
        const definition = createDefinition();
        definition.root.children = new Array(1);

        const result = validateToolPageDefinition(definition);
        expect(result.valid).toBe(false);
        expect(result.errors.join(' ')).toMatch(/data property/i);
    });

    it('allows only renderer-supported event names and command identifiers', () => {
        const unsupportedEvent = createDefinition();
        unsupportedEvent.root.children[0].events.onMouseOver = 'unsafe.hover';

        const unsafeCommandId = createDefinition();
        unsafeCommandId.root.children[0].events.onChange = 'commands[0];alert(1)';

        for (const definition of [unsupportedEvent, unsafeCommandId]) {
            const result = validateToolPageDefinition(definition);
            expect(result.valid).toBe(false);
            expect(result.errors.length).toBeGreaterThan(0);
        }
    });

    it('rejects malformed tabs and duplicate node ids', () => {
        const contentInOptions = createDefinition();
        contentInOptions.root.children[1].options.content = '<button>raw</button>';

        const duplicateId = createDefinition();
        duplicateId.root.children[1].tabs[0].content.id = 'name-input';

        for (const definition of [contentInOptions, duplicateId]) {
            const result = validateToolPageDefinition(definition);
            expect(result.valid).toBe(false);
            expect(result.errors.length).toBeGreaterThan(0);
        }
    });

    it('fails closed for unknown node types instead of treating them as HTML', () => {
        const definition = createDefinition();
        definition.root.children.push({
            type: 'html',
            id: 'raw-html',
            content: '<input oninput="run()">',
        });

        const result = validateToolPageDefinition(definition);
        expect(result.valid).toBe(false);
        expect(result.errors.length).toBeGreaterThan(0);
    });
});
