import { describe, expect, it } from 'vitest';

import { PageGenerator } from '../../page-generator/PageGenerator.js';

function definition() {
    return {
        schema_version: 1,
        name: 'GeneratedToolPage',
        type: 'tool',
        root: {
            type: 'component',
            id: 'run-button',
            component: 'BasicButton',
            options: { customLabel: 'Run' },
            events: { onClick: 'tool.run' },
        },
    };
}

describe('tool page generator integration', () => {
    it('generates a static wrapper that injects commands and state into DynamicToolRenderer', () => {
        const generator = new PageGenerator({ toolRendererImportPath: './DynamicToolRenderer.js' });
        const result = generator.generate(definition());

        expect(result.errors).toEqual([]);
        expect(result.code).toContain('import { DynamicToolRenderer } from "./DynamicToolRenderer.js";');
        expect(result.code).toContain('export class GeneratedToolPage');
        expect(result.code).toContain('commandRegistry: options.commandRegistry || options.commands');
        expect(result.code).toContain('state: options.state || {}');
        expect(result.code).toContain('await this.renderer.init()');
        expect(result.code).not.toContain('<button');
        expect(result.code).not.toContain('innerHTML');
    });

    it('rejects unsafe tool definitions before generating code', () => {
        const unsafe = definition();
        unsafe.root.events.onClick = 'tool.run;alert(1)';
        const result = new PageGenerator().generate(unsafe);

        expect(result.code).toBeNull();
        expect(result.errors.length).toBeGreaterThan(0);
    });

    it('keeps authored descriptions out of generated comments', () => {
        const source = definition();
        source.description = '*/\nglobalThis.injected = true;\n/*';
        const result = new PageGenerator({ toolRendererImportPath: './DynamicToolRenderer.js' }).generate(source);
        const generatedHeader = result.code.match(/^\/\*\*[\s\S]*?\*\//)?.[0] || '';

        expect(result.errors).toEqual([]);
        expect(generatedHeader).not.toContain('globalThis.injected');
        expect(result.code).toContain('"description": "*/\\nglobalThis.injected = true;\\n/*"');
    });

    it('rejects an accessor-backed page type without invoking it', () => {
        const source = definition();
        let invoked = false;
        Object.defineProperty(source, 'type', {
            enumerable: true,
            get() {
                invoked = true;
                return 'tool';
            },
        });

        const result = new PageGenerator().generate(source);
        expect(result.code).toBeNull();
        expect(result.errors.join(' ')).toMatch(/data property/i);
        expect(invoked).toBe(false);
    });
});
