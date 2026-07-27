/**
 * DynamicPageRenderer
 * 動態頁面渲染統一入口 - 依模式委派給對應渲染器
 *
 * 組合：DynamicFormRenderer、DynamicDetailRenderer、DynamicListRenderer
 */

import { DynamicFormRenderer } from './DynamicFormRenderer.js';
import { DynamicDetailRenderer } from './DynamicDetailRenderer.js';
import { DynamicListRenderer } from './DynamicListRenderer.js';
import { DynamicToolRenderer } from './DynamicToolRenderer.js';
import { isDeclarativeListDefinition, normalizeQueryDefinition } from './QueryDefinitionAdapter.js';

export class DynamicPageRenderer {
    /**
     * @param {Object} options
     * @param {Object} options.definition - 頁面定義 JSON
     * @param {string} options.mode - 渲染模式：'form' | 'detail' | 'list'
     * @param {Object} options.data - 資料（detail/form 編輯時使用）
     * @param {Function} options.onSave - 儲存回調（form 模式）
     * @param {Function} options.onCancel - 取消回調（form 模式）
     * @param {Function} options.onSearch - 搜尋回調（list 模式）
     * @param {Function} options.onAction - 操作回調（list 模式）
     * @param {Function} options.onBack - 返回回調（detail 模式）
     * @param {Function} options.onEdit - 編輯回調（detail 模式）
     * @param {Function} options.onPermissionCheck - 權限 UX gate (permissionKey, page, definition) => boolean|Promise<boolean>
     * @param {number} options.pageSize - 每頁筆數（list 模式）
     */
    constructor(options = {}) {
        this.options = {
            definition: null,
            mode: 'form',
            data: null,
            onSave: null,
            onCancel: null,
            onSearch: null,
            onAction: null,
            onBack: null,
            onEdit: null,
            onPermissionCheck: null,
            onDownload: null,
            confirmDownload: null,
            pageSize: 20,
            customComponents: null,
            customComponentRegistry: null,
            commandRegistry: null,
            state: {},
            factory: null,
            controlRegistry: null,
            ...options
        };

        if (options.mode === undefined && isDeclarativeListDefinition(options.definition)) {
            this.options.mode = 'list';
        } else if (options.mode === undefined && options.definition?.type === 'tool') {
            this.options.mode = 'tool';
        }

        /** @type {DynamicFormRenderer|DynamicDetailRenderer|DynamicListRenderer|DynamicToolRenderer|null} */
        this._renderer = null;
        this._customComponentRegistry = this.options.customComponentRegistry || null;
        this._ownsCustomComponentRegistry = false;
        this._permissionState = { checked: false, allowed: true, error: null };
    }

    /**
     * 初始化並建構渲染器
     */
    async init() {
        const allowed = await this._checkPermission();
        if (!allowed) return this;

        const definition = isDeclarativeListDefinition(this.options.definition)
            ? normalizeQueryDefinition(this.options.definition)
            : this.options.definition;
        const { mode, data } = this.options;
        await this._prepareCustomComponents();

        switch (mode) {
            case 'form': {
                this._renderer = new DynamicFormRenderer({
                    definition,
                    onSave: this.options.onSave,
                    onCancel: this.options.onCancel,
                    customComponentRegistry: this._customComponentRegistry,
                });
                await this._renderer.init();

                // 如果有資料（編輯模式），填入
                if (data) {
                    this._renderer.setValues(data);
                }
                break;
            }

            case 'detail': {
                this._renderer = new DynamicDetailRenderer({
                    definition,
                    data: data || {},
                    onBack: this.options.onBack,
                    onEdit: this.options.onEdit,
                });
                break;
            }

            case 'list': {
                this._renderer = new DynamicListRenderer({
                    definition,
                    onSearch: this.options.onSearch,
                    onAction: this.options.onAction,
                    onDownload: this.options.onDownload,
                    confirmDownload: this.options.confirmDownload,
                    pageSize: this.options.pageSize,
                });
                await this._renderer.init();
                break;
            }

            case 'tool': {
                this._renderer = new DynamicToolRenderer({
                    definition,
                    commandRegistry: this.options.commandRegistry,
                    state: this.options.state,
                    ...(this.options.factory ? { factory: this.options.factory } : {}),
                    controlRegistry: this.options.controlRegistry,
                });
                await this._renderer.init();
                break;
            }

            default:
                console.warn(`[DynamicPageRenderer] 未知的 mode: ${mode}`);
        }

        return this;
    }

    /**
     * 取得內部渲染器
     */
    /**
     * Load JSON-defined components before the synchronous field resolution step.
     * @private
     */
    async _prepareCustomComponents() {
        const source = this.options.customComponents;
        if (!source) return;

        const { CustomComponentRegistry } = await import('../custom_components/CustomComponentRegistry.js');
        const createsRegistry = !this._customComponentRegistry;
        const registry = this._customComponentRegistry || new CustomComponentRegistry({
            registerWithFactory: false,
        });
        const ownsRegistry = this._ownsCustomComponentRegistry || createsRegistry;
        const definitions = [];

        if (typeof source === 'string') {
            definitions.push(...await registry.fetchFolderDefinitions(source));
        } else if (Array.isArray(source)) {
            definitions.push(...source);
        } else if (typeof source === 'object') {
            if (Array.isArray(source.definitions) && source.definitions.length > 0) {
                definitions.push(...source.definitions);
            }
            if (source.folder) {
                definitions.push(...await registry.fetchFolderDefinitions(source.folder, {
                    manifest: source.manifest || 'registry.json',
                    additionalDefinitions: definitions,
                }));
            }
        } else {
            throw new TypeError('customComponents must be a folder URL, definition array, or options object.');
        }

        if (definitions.length > 0) registry.registerMany(definitions);
        this._customComponentRegistry = registry;
        this._ownsCustomComponentRegistry = ownsRegistry;
    }

    getCustomComponentRegistry() {
        return this._customComponentRegistry;
    }

    async _checkPermission() {
        const definition = this.options.definition || {};
        const permissionKey = definition.page?.permissionKey || definition.permissionKey || null;
        const hook = this.options.onPermissionCheck;

        if (!permissionKey || typeof hook !== 'function') {
            this._permissionState = { checked: false, allowed: true, error: null };
            return true;
        }

        try {
            const allowed = await hook(permissionKey, definition.page || {}, definition);
            this._permissionState = { checked: true, allowed: allowed !== false, error: null };
            return this._permissionState.allowed;
        } catch (error) {
            this._permissionState = { checked: true, allowed: false, error };
            return false;
        }
    }

    getPermissionState() {
        return { ...this._permissionState };
    }

    getRenderer() {
        return this._renderer;
    }

    /**
     * 切換模式（銷毀舊渲染器，建立新的）
     */
    async switchMode(mode, data = null) {
        const container = this._renderer?.element?.parentNode;
        this.destroy();
        this.options.mode = mode;
        this.options.data = data;
        await this.init();
        if (container) this.mount(container);
        return this;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this._renderer?.mount) {
            this._renderer.mount(target);
        } else if (target && this._renderer?.element) {
            target.appendChild(this._renderer.element);
        }
        return this;
    }

    destroy() {
        this._renderer?.destroy?.();
        this._renderer = null;
        if (this._ownsCustomComponentRegistry) {
            this._customComponentRegistry?.dispose?.();
            this._customComponentRegistry = null;
            this._ownsCustomComponentRegistry = false;
        }
    }
}

export default DynamicPageRenderer;
