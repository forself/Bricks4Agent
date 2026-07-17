import { describe, expect, it, vi } from 'vitest';

const toolRendererState = vi.hoisted(() => ({ instances: [] }));

vi.mock('../../page-generator/DynamicToolRenderer.js', () => ({
    DynamicToolRenderer: class DynamicToolRenderer {
        constructor(options) {
            this.options = options;
            this.init = vi.fn(async () => this);
            this.mount = vi.fn(() => this);
            this.destroy = vi.fn();
            toolRendererState.instances.push(this);
        }
    },
}));

vi.mock('../../page-generator/DynamicFormRenderer.js', () => ({ DynamicFormRenderer: class {} }));
vi.mock('../../page-generator/DynamicDetailRenderer.js', () => ({ DynamicDetailRenderer: class {} }));
vi.mock('../../page-generator/DynamicListRenderer.js', () => ({ DynamicListRenderer: class {} }));
vi.mock('../../custom_components/CustomComponentRegistry.js', () => ({ CustomComponentRegistry: class {} }));

import { DynamicPageRenderer } from '../../page-generator/DynamicPageRenderer.js';

describe('DynamicPageRenderer tool mode', () => {
    it('auto-selects tool mode and delegates lifecycle with injected runtime dependencies', async () => {
        const definition = {
            schema_version: 1,
            name: 'DelegatedToolPage',
            type: 'tool',
            root: { type: 'slot', id: 'workspace' },
        };
        const commandRegistry = new Map([['tool.run', vi.fn()]]);
        const state = { workspace: { active: true } };
        const factory = { create: vi.fn() };
        const controlRegistry = new Map();
        const facade = new DynamicPageRenderer({
            definition,
            commandRegistry,
            state,
            factory,
            controlRegistry,
        });

        await facade.init();
        const delegated = toolRendererState.instances.at(-1);
        expect(delegated.options).toEqual({
            definition,
            commandRegistry,
            state,
            factory,
            controlRegistry,
        });
        expect(delegated.init).toHaveBeenCalledOnce();

        const host = document.createElement('div');
        facade.mount(host);
        expect(delegated.mount).toHaveBeenCalledWith(host);

        facade.destroy();
        expect(delegated.destroy).toHaveBeenCalledOnce();
        expect(facade.getRenderer()).toBeNull();
    });
});
