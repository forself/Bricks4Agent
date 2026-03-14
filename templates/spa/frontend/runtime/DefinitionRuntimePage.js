import { BasePage } from '../core/BasePage.js';
import { PageDefinitionAdapter } from './page-generator/PageDefinitionAdapter.js';
import { DynamicPageRenderer } from './page-generator/DynamicPageRenderer.js';

const SUPPORTED_RUNTIME_MODES = new Set(['form', 'detail', 'list']);

export class DefinitionRuntimePage extends BasePage {
    constructor(options = {}) {
        super(options);
        this._renderer = null;
        this._runtimeState = null;
    }

    async onInit() {
        const definition = this._resolveDefinition();
        const runtimeDefinition = PageDefinitionAdapter.toNewFormat(definition);
        const mode = this._resolveRuntimeMode(definition);

        if (!runtimeDefinition?.page || !Array.isArray(runtimeDefinition.fields)) {
            throw new Error('Failed to normalize page definition for runtime rendering');
        }

        this._runtimeState = {
            definition,
            runtimeDefinition,
            mode,
            title: this.meta?.title || definition.description || definition.name || this.constructor.name
        };
    }

    template() {
        const title = this.esc(this._runtimeState?.title || '');
        const pageId = this.esc(this.getPageId());

        return `
            <div class="definition-runtime-page">
                <header class="page-header">
                    <h1>${title}</h1>
                    <p>Rendered by DefinitionRuntimePage (${pageId})</p>
                </header>
                <section class="card">
                    <div class="card-body">
                        <div data-definition-runtime-host></div>
                    </div>
                </section>
            </div>
        `;
    }

    async onMounted() {
        const host = this.$('[data-definition-runtime-host]');
        if (!host) {
            throw new Error('Definition runtime host container was not found');
        }

        this._renderer = new DynamicPageRenderer(this._buildRendererOptions());
        await this._renderer.init();
        this._renderer.mount(host);
    }

    async onDestroy() {
        this._renderer?.destroy();
        this._renderer = null;
    }

    getPageId() {
        return this.constructor.pageId || this.constructor.name;
    }

    getInitialData() {
        return null;
    }

    async handleSave() {}

    async handleSearch() {}

    async handleAction() {}

    handleCancel() {
        this.router?.back?.();
    }

    handleBack() {
        this.router?.back?.();
    }

    handleEdit() {}

    _resolveDefinition() {
        const definition = this.constructor.definition;
        if (!definition || typeof definition !== 'object') {
            throw new Error(`${this.constructor.name} must declare a static definition object`);
        }

        return definition;
    }

    _resolveRuntimeMode(definition) {
        const explicitMode = this.constructor.mode;
        if (explicitMode) {
            if (!SUPPORTED_RUNTIME_MODES.has(explicitMode)) {
                throw new Error(`Unsupported runtime mode: ${explicitMode}`);
            }

            return explicitMode;
        }

        switch (definition.type) {
            case 'list':
                return 'list';
            case 'detail':
                return 'detail';
            case 'form':
                return 'form';
            default:
                throw new Error(`DefinitionRuntimePage does not support page type: ${definition.type}`);
        }
    }

    _buildRendererOptions() {
        return {
            definition: this._runtimeState.runtimeDefinition,
            mode: this._runtimeState.mode,
            data: this.getInitialData(),
            onSave: (values) => this.handleSave(values),
            onCancel: () => this.handleCancel(),
            onSearch: (filters, page, pageSize) => this.handleSearch(filters, page, pageSize),
            onAction: (action, row) => this.handleAction(action, row),
            onBack: () => this.handleBack(),
            onEdit: () => this.handleEdit()
        };
    }
}

export default DefinitionRuntimePage;
