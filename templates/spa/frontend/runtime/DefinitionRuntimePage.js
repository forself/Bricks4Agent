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
        await this._hydrateRuntimeData();
    }

    async onDestroy() {
        this._renderer?.destroy();
        this._renderer = null;
    }

    getPageId() {
        return this.constructor.pageId || this.constructor.name;
    }

    getInitialData() {
        return this._runtimeState?.record || null;
    }

    async handleSave(values) {
        const { definition } = this._runtimeState;
        const recordId = this._getRecordId();
        const canUpdate = Boolean(recordId && definition.api?.update);
        const endpoint = canUpdate
            ? this._resolveResourceEndpoint(definition.api?.update, recordId)
            : this._resolveCollectionEndpoint(definition.api?.create);

        if (!this.api || !endpoint) {
            this.showMessage('Save endpoint is not configured for this page', 'warning');
            return;
        }

        this.showLoading();
        try {
            const response = canUpdate
                ? await this.api.put(endpoint, values)
                : await this.api.post(endpoint, values);
            const record = this._normalizeRecordResult(response, values);
            this._runtimeState.record = record;
            this._applyRecordToRenderer(record);
            this.showMessage(canUpdate ? 'Saved changes' : 'Created record', 'success');
        } catch (error) {
            this.showMessage(error.message || 'Save failed', 'error');
            throw error;
        } finally {
            this.hideLoading();
        }
    }

    async handleSearch(filters = {}, page = 1, pageSize = 20) {
        await this._loadListData(filters, page, pageSize);
    }

    async handleAction(action, row) {
        if (action === 'delete') {
            await this._deleteRow(row);
            return;
        }

        this._navigateRowAction(action, row);
    }

    handleCancel() {
        this.router?.back?.();
    }

    handleBack() {
        this.router?.back?.();
    }

    handleEdit() {
        const actionRoutes = this.meta?.actionRoutes || {};
        const recordId = this._getRecordId();
        if (!recordId || !actionRoutes.edit) {
            this.showMessage('Edit route is not configured for this page', 'warning');
            return;
        }

        this.navigate(this._interpolateRoute(actionRoutes.edit, recordId));
    }

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

    async _hydrateRuntimeData() {
        if (!this.api) {
            return;
        }

        if (this._runtimeState.mode === 'list') {
            await this._loadListData({}, 1, 20);
            return;
        }

        if ((this._runtimeState.mode === 'form' || this._runtimeState.mode === 'detail') && this._getRecordId()) {
            await this._loadRecord();
        }
    }

    async _loadRecord() {
        const endpoint = this._resolveResourceEndpoint(
            this._runtimeState.definition.api?.get,
            this._getRecordId()
        );
        if (!endpoint) {
            return;
        }

        this.showLoading();
        try {
            const response = await this.api.get(endpoint);
            const record = this._normalizeRecordResult(response, {});
            this._runtimeState.record = record;
            this._applyRecordToRenderer(record);
        } catch (error) {
            this.showMessage(error.message || 'Failed to load record', 'error');
            throw error;
        } finally {
            this.hideLoading();
        }
    }

    async _loadListData(filters = {}, page = 1, pageSize = 20) {
        const endpoint = this._resolveCollectionEndpoint(this._runtimeState.definition.api?.list);
        if (!endpoint) {
            return;
        }

        this._runtimeState.listState = {
            filters,
            page,
            pageSize
        };

        this.showLoading();
        try {
            const response = await this.api.get(
                this._appendQuery(endpoint, {
                    ...filters,
                    page,
                    pageSize
                })
            );
            const normalized = this._normalizeListResult(response);
            this._runtimeState.listState.total = normalized.total;
            this._renderer?.getRenderer?.()?.setData?.(normalized.items, normalized.total);
        } catch (error) {
            this.showMessage(error.message || 'Failed to load list data', 'error');
            throw error;
        } finally {
            this.hideLoading();
        }
    }

    async _deleteRow(row) {
        const endpoint = this._resolveResourceEndpoint(
            this._runtimeState.definition.api?.delete,
            this._extractRowId(row)
        );
        if (!this.api || !endpoint) {
            this.showMessage('Delete endpoint is not configured for this page', 'warning');
            return;
        }

        this.showLoading();
        try {
            await this.api.delete(endpoint);
            this.showMessage('Deleted record', 'success');

            const listState = this._runtimeState.listState || {
                filters: {},
                page: 1,
                pageSize: 20
            };
            await this._loadListData(listState.filters, listState.page, listState.pageSize);
        } catch (error) {
            this.showMessage(error.message || 'Delete failed', 'error');
            throw error;
        } finally {
            this.hideLoading();
        }
    }

    _applyRecordToRenderer(record) {
        const renderer = this._renderer?.getRenderer?.();
        if (!renderer || !record) {
            return;
        }

        if (typeof renderer.setValues === 'function') {
            renderer.setValues(record);
            return;
        }

        if (typeof renderer.setData === 'function') {
            renderer.setData(record);
        }
    }

    _normalizeRecordResult(response, fallback) {
        if (response && typeof response === 'object' && !Array.isArray(response)) {
            if (response.data && typeof response.data === 'object' && !Array.isArray(response.data)) {
                return response.data;
            }

            if (response.item && typeof response.item === 'object' && !Array.isArray(response.item)) {
                return response.item;
            }

            return response;
        }

        return fallback;
    }

    _normalizeListResult(response) {
        if (Array.isArray(response)) {
            return {
                items: response,
                total: response.length
            };
        }

        if (response && typeof response === 'object') {
            const candidates = ['items', 'rows', 'results', 'data'];
            for (const key of candidates) {
                if (Array.isArray(response[key])) {
                    return {
                        items: response[key],
                        total: Number(response.total) || response[key].length
                    };
                }
            }
        }

        return {
            items: [],
            total: 0
        };
    }

    _appendQuery(endpoint, params) {
        const [path, queryString] = String(endpoint).split('?');
        const search = new URLSearchParams(queryString || '');

        for (const [key, value] of Object.entries(params || {})) {
            if (value === null || value === undefined || value === '') {
                continue;
            }

            search.set(key, String(value));
        }

        const nextQuery = search.toString();
        return nextQuery ? `${path}?${nextQuery}` : path;
    }

    _resolveCollectionEndpoint(endpoint) {
        return typeof endpoint === 'string' && endpoint.trim() !== '' ? endpoint.trim() : null;
    }

    _resolveResourceEndpoint(endpoint, recordId) {
        if (recordId === null || recordId === undefined || recordId === '') {
            return this._resolveCollectionEndpoint(endpoint);
        }

        const resolved = this._resolveCollectionEndpoint(endpoint);
        if (!resolved) {
            return null;
        }

        if (resolved.includes(':id')) {
            return resolved.replace(':id', encodeURIComponent(String(recordId)));
        }

        return `${resolved.replace(/\/+$/, '')}/${encodeURIComponent(String(recordId))}`;
    }

    _getRecordId() {
        return this.params?.id ?? this.query?.id ?? null;
    }

    _extractRowId(row) {
        return row?.id ?? row?.Id ?? row?.ID ?? null;
    }

    _navigateRowAction(action, row) {
        const actionRoutes = this.meta?.actionRoutes || {};
        const recordId = this._extractRowId(row);
        const routeTemplate = actionRoutes[action];

        if (!recordId || !routeTemplate) {
            this.showMessage(`${action} route is not configured for this page`, 'warning');
            return;
        }

        this.navigate(this._interpolateRoute(routeTemplate, recordId));
    }

    _interpolateRoute(routeTemplate, recordId) {
        return String(routeTemplate).replace(':id', encodeURIComponent(String(recordId)));
    }
}

export default DefinitionRuntimePage;
