/**
 * DynamicPageRenderer
 * 動態頁面渲染統一入口 - 依模式委派給對應渲染器
 *
 * 組合：DynamicFormRenderer、DynamicDetailRenderer、DynamicListRenderer
 */

import { DynamicFormRenderer } from './DynamicFormRenderer.js';
import { DynamicDetailRenderer } from './DynamicDetailRenderer.js';
import { DynamicListRenderer } from './DynamicListRenderer.js';

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
            pageSize: 20,
            ...options
        };

        /** @type {DynamicFormRenderer|DynamicDetailRenderer|DynamicListRenderer|null} */
        this._renderer = null;
    }

    /**
     * 初始化並建構渲染器
     */
    async init() {
        const { definition, mode, data } = this.options;

        switch (mode) {
            case 'form': {
                this._renderer = new DynamicFormRenderer({
                    definition,
                    onSave: this.options.onSave,
                    onCancel: this.options.onCancel,
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
                    pageSize: this.options.pageSize,
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
        if (target && this._renderer?.element) {
            target.appendChild(this._renderer.element);
        } else if (target && this._renderer?.mount) {
            this._renderer.mount(target);
        }
        return this;
    }

    destroy() {
        this._renderer?.destroy?.();
        this._renderer = null;
    }
}

export default DynamicPageRenderer;
