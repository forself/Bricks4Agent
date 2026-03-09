/**
 * FormRow Component
 * 表單列佈局元件 - 12 欄 CSS Grid 容器
 * 管理同一列的多個 FormField，依 col-N 分配欄寬
 */

export class FormRow {
    /**
     * @param {Object} options
     * @param {Array} options.fields - FormField 實例陣列
     * @param {string} options.gap - 欄間距（預設 16px）
     */
    constructor(options = {}) {
        this.options = {
            fields: [],
            gap: '16px',
            ...options
        };

        this._fields = new Map(); // fieldName → FormField
        this.element = this._createElement();

        // 掛載初始 fields
        this.options.fields.forEach(f => this._mountField(f));
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'form-row';
        container.style.cssText = `
            display:grid;
            grid-template-columns:repeat(12, 1fr);
            gap:${this.options.gap};
            margin-bottom:16px;
        `;
        return container;
    }

    /**
     * 內部掛載單一 FormField
     * 無 col 的 FormField 不設定 gridColumn，由 CSS grid auto 分配
     */
    _mountField(formField) {
        const name = formField.options.fieldName;
        this._fields.set(name, formField);

        // 如果沒有指定 col，計算平均分配
        if (!formField.options.col) {
            this._autoDistribute();
        }

        this.element.appendChild(formField.element);
    }

    /**
     * 自動分配未指定 col 的欄位
     * 已指定 col 的佔固定寬度，剩餘空間平分給未指定的
     */
    _autoDistribute() {
        let usedCols = 0;
        let autoFields = [];

        this._fields.forEach(field => {
            if (field.options.col) {
                usedCols += field.options.col;
            } else {
                autoFields.push(field);
            }
        });

        if (autoFields.length > 0) {
            const remaining = Math.max(12 - usedCols, autoFields.length);
            const perField = Math.floor(remaining / autoFields.length);
            autoFields.forEach(field => {
                field.element.style.gridColumn = `span ${perField}`;
            });
        }
    }

    // ─── 公開 API ───

    /** 新增 FormField */
    addField(formField) {
        this._mountField(formField);
        return this;
    }

    /** 依 fieldName 取得 FormField */
    getField(fieldName) {
        return this._fields.get(fieldName) || null;
    }

    /** 取得所有 FormField */
    getFields() {
        return [...this._fields.values()];
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._fields.forEach(field => field.destroy());
        this._fields.clear();
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default FormRow;
