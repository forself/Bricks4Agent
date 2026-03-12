/**
 * FormField Component
 * 表單欄位原子包裝器 - 為任何 form 元件提供 label、必填標記、錯誤/提示外殼
 * 不關心內部元件是什麼，只負責外殼結構與狀態管理
 */

export class FormField {
    /**
     * @param {Object} options
     * @param {string} options.fieldName - 欄位技術名稱
     * @param {string} options.label - 標籤文字
     * @param {boolean} options.required - 是否必填
     * @param {string} options.error - 錯誤訊息
     * @param {string} options.hint - 提示文字
     * @param {Object} options.component - 內部元件實例（需有 mount/destroy 方法）
     * @param {number} options.col - 欄寬 1-12（CSS grid span），null = 不設定
     */
    constructor(options = {}) {
        this.options = {
            fieldName: '',
            label: '',
            required: false,
            error: '',
            hint: '',
            component: null,
            col: null,
            ...options
        };

        this._visible = true;
        this._readonly = false;
        this.element = this._createElement();

        // 掛載內部元件
        if (this.options.component && this._slot) {
            this.options.component.mount(this._slot);
        }
    }

    _createElement() {
        const { fieldName, label, required, error, hint, col } = this.options;

        const container = document.createElement('div');
        container.className = 'form-field';
        container.dataset.field = fieldName;

        // col-N 支援
        if (col) {
            container.style.gridColumn = `span ${col}`;
        }

        // Label 區
        if (label) {
            const labelRow = document.createElement('div');
            labelRow.className = 'form-field__label';
            labelRow.style.cssText = 'display:flex;align-items:center;gap:4px;margin-bottom:6px;';

            const labelText = document.createElement('label');
            labelText.textContent = label;
            labelText.style.cssText = 'font-size:var(--cl-font-size-lg);font-weight:500;color:var(--cl-text);';
            labelRow.appendChild(labelText);
            this._labelText = labelText;

            // 必填標記
            const requiredMark = document.createElement('span');
            requiredMark.className = 'form-field__required';
            requiredMark.textContent = '*';
            requiredMark.style.cssText = `color:var(--cl-danger);font-weight:bold;display:${required ? 'inline' : 'none'};`;
            labelRow.appendChild(requiredMark);
            this._requiredMark = requiredMark;

            container.appendChild(labelRow);
        }

        // 元件插槽
        const slot = document.createElement('div');
        slot.className = 'form-field__slot';
        container.appendChild(slot);
        this._slot = slot;

        // 錯誤/提示區
        const messageEl = document.createElement('div');
        messageEl.className = 'form-field__message';
        messageEl.style.cssText = 'font-size:var(--cl-font-size-sm);min-height:18px;margin-top:4px;';
        container.appendChild(messageEl);
        this._messageEl = messageEl;

        // 初始狀態
        if (error) {
            this._showError(error);
        } else if (hint) {
            this._showHint(hint);
        }

        return container;
    }

    _showError(msg) {
        this._messageEl.textContent = msg;
        this._messageEl.style.color = 'var(--cl-danger)';
    }

    _showHint(msg) {
        this._messageEl.textContent = msg;
        this._messageEl.style.color = 'var(--cl-text-placeholder)';
    }

    // ─── 公開 API ───

    /** 設定錯誤訊息 */
    setError(msg) {
        this.options.error = msg;
        if (msg) {
            this._showError(msg);
        } else {
            this.clearError();
        }
    }

    /** 清除錯誤 */
    clearError() {
        this.options.error = '';
        if (this.options.hint) {
            this._showHint(this.options.hint);
        } else {
            this._messageEl.textContent = '';
        }
    }

    /** 設定必填狀態 */
    setRequired(required) {
        this.options.required = required;
        if (this._requiredMark) {
            this._requiredMark.style.display = required ? 'inline' : 'none';
        }
    }

    /** 設定標籤文字 */
    setLabel(text) {
        this.options.label = text;
        if (this._labelText) {
            this._labelText.textContent = text;
        }
    }

    /** 取得內部元件實例 */
    getComponent() {
        return this.options.component;
    }

    /** 設定欄寬 */
    setCol(n) {
        this.options.col = n;
        if (n) {
            this.element.style.gridColumn = `span ${n}`;
        } else {
            this.element.style.gridColumn = '';
        }
    }

    /** 顯示欄位 */
    show() {
        this._visible = true;
        this.element.style.display = '';
    }

    /** 隱藏欄位 */
    hide() {
        this._visible = false;
        this.element.style.display = 'none';
    }

    /** 是否可見 */
    isVisible() {
        return this._visible;
    }

    /** 設定唯讀（轉傳給內部元件） */
    setReadonly(readonly) {
        this._readonly = readonly;
        const comp = this.options.component;
        if (!comp) return;

        // 嘗試常見的 readonly/disabled API
        if (typeof comp.setReadonly === 'function') {
            comp.setReadonly(readonly);
        } else if (typeof comp.setDisabled === 'function') {
            comp.setDisabled(readonly);
        } else if (comp.element) {
            // 直接操作 DOM
            const inputs = comp.element.querySelectorAll('input,select,textarea');
            inputs.forEach(el => {
                el.readOnly = readonly;
                el.disabled = readonly;
            });
        }
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        if (this.options.component?.destroy) {
            this.options.component.destroy();
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default FormField;
