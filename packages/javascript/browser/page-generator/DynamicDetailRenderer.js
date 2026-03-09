/**
 * DynamicDetailRenderer
 * 動態明細渲染器 - 從頁面定義 JSON 產生唯讀明細頁
 *
 * 不實例化 form 元件，直接渲染 label + formatted value。
 * 支援 30 種 fieldType 的格式化顯示，包含：
 * - 基本型別：text, email, number, textarea, password（遮罩）
 * - 日期時間：date（YYYY/MM/DD）, time, datetime（YYYY/MM/DD HH:MM）
 * - 選擇型別：select/radio（label 查找）, multiselect（標籤列）, checkbox/toggle（是/否）
 * - 進階型別：color（色塊）, image（縮圖）, richtext（HTML 截斷）, canvas（文字提示）
 * - 服務型別：geolocation（地址/座標）, weather（圖示+溫度）
 * - 複合輸入：address, addresslist, phonelist, socialmedia, personinfo,
 *             organization（層級路徑）, student（學生/非學生）, chained, list
 */

export class DynamicDetailRenderer {
    /**
     * @param {Object} options
     * @param {Object} options.definition - 頁面定義 JSON
     * @param {Object} options.data - 資料物件 { fieldName: value }
     * @param {Function} options.onBack - 返回按鈕回調
     * @param {Function} options.onEdit - 編輯按鈕回調
     */
    constructor(options = {}) {
        this.options = {
            definition: null,
            data: {},
            onBack: null,
            onEdit: null,
            ...options
        };

        this.element = null;
        this._build();
    }

    _build() {
        const { definition, data } = this.options;
        if (!definition?.fields) return;

        this.element = document.createElement('div');
        this.element.className = 'dynamic-detail';

        // 依 formRow 分組
        const rowGroups = new Map();
        definition.fields.forEach(def => {
            if (def.fieldType === 'hidden') return;
            const row = def.formRow ?? 0;
            if (!rowGroups.has(row)) rowGroups.set(row, []);
            rowGroups.get(row).push(def);
        });

        const sortedRows = [...rowGroups.keys()].sort((a, b) => a - b);
        sortedRows.forEach(rowNum => {
            const defs = rowGroups.get(rowNum);
            const rowEl = document.createElement('div');
            rowEl.className = 'dynamic-detail__row';
            rowEl.style.cssText = `
                display:grid;grid-template-columns:repeat(12, 1fr);gap:16px;margin-bottom:16px;
            `;

            defs.forEach(def => {
                const fieldEl = this._createDetailField(def, data[def.fieldName]);
                if (def.formCol) {
                    fieldEl.style.gridColumn = `span ${def.formCol}`;
                } else {
                    // 平均分配
                    const colSpan = Math.floor(12 / defs.length);
                    fieldEl.style.gridColumn = `span ${colSpan}`;
                }
                rowEl.appendChild(fieldEl);
            });

            this.element.appendChild(rowEl);
        });

        // 底部按鈕
        if (this.options.onBack || this.options.onEdit) {
            this.element.appendChild(this._createButtons());
        }
    }

    _createDetailField(def, value) {
        const container = document.createElement('div');
        container.className = 'dynamic-detail__field';

        // Label
        const label = document.createElement('div');
        label.className = 'dynamic-detail__label';
        label.textContent = def.label;
        label.style.cssText = 'font-size:12px;color:#888;margin-bottom:4px;font-weight:500;';

        // Value
        const valueEl = document.createElement('div');
        valueEl.className = 'dynamic-detail__value';
        valueEl.style.cssText = 'font-size:14px;color: var(--cl-text);min-height:20px;';

        // 格式化
        valueEl.innerHTML = this._formatValue(def, value);

        container.appendChild(label);
        container.appendChild(valueEl);
        return container;
    }

    /**
     * 依 fieldType 格式化顯示值
     */
    _formatValue(def, value) {
        if (value === null || value === undefined || value === '') {
            return '<span style="color:#ccc;">—</span>';
        }

        switch (def.fieldType) {
            case 'date':
                return this._formatDate(value);

            case 'time':
                return String(value);

            case 'checkbox':
            case 'toggle':
                return this._formatBoolean(value);

            case 'select':
            case 'radio':
                return this._formatOption(def, value);

            case 'multiselect':
                return this._formatMultiOption(def, value);

            case 'color':
                return `<span style="display:inline-flex;align-items:center;gap:6px;">
                    <span style="display:inline-block;width:16px;height:16px;border-radius:3px;background:${value};border: 1px solid var(--cl-border);"></span>
                    ${value}
                </span>`;

            case 'image':
                return `<img src="${value}" style="max-width:120px;max-height:80px;border-radius:4px;border: 1px solid var(--cl-border-light);" />`;

            case 'password':
                return '••••••••';

            case 'datetime':
                return this._formatDateTime(value);

            case 'richtext':
                // HTML 內容截斷顯示
                return `<div style="max-height:80px;overflow:hidden;border: 1px solid var(--cl-border-light);padding:4px 8px;border-radius:4px;font-size:13px;">${value}</div>`;

            case 'canvas':
                return `<div style="color:#888;font-size:12px;">（繪圖內容）</div>`;

            case 'geolocation':
                if (typeof value === 'object') {
                    return this._escapeHtml(value.address?.shortName || `${value.lat}, ${value.lng}`);
                }
                return this._escapeHtml(String(value));

            case 'weather':
                if (typeof value === 'object') {
                    return `<span>${value.icon || ''} ${this._escapeHtml(value.temperature || '')}${this._escapeHtml(value.unit || '')} ${this._escapeHtml(value.description || '')}</span>`;
                }
                return this._escapeHtml(String(value));

            case 'address':
                if (typeof value === 'object') {
                    return this._escapeHtml([value.city, value.district, value.address].filter(Boolean).join(''));
                }
                return this._escapeHtml(String(value));

            case 'addresslist':
            case 'phonelist':
            case 'socialmedia':
            case 'personinfo':
                return this._formatListValue(value);

            case 'organization':
                if (typeof value === 'object') {
                    return this._escapeHtml([value.level1, value.level2, value.level3, value.level4].filter(Boolean).join(' / '));
                }
                return this._escapeHtml(String(value));

            case 'student':
                if (typeof value === 'object') {
                    return value.isStudent ? `學生 — ${this._escapeHtml(value.schoolName || '')}` : '非學生';
                }
                return this._escapeHtml(String(value));

            case 'chained':
            case 'list':
                return this._formatListValue(value);

            default:
                return this._escapeHtml(String(value));
        }
    }

    _formatDate(value) {
        try {
            const d = value instanceof Date ? value : new Date(value);
            if (isNaN(d.getTime())) return String(value);
            return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')}`;
        } catch {
            return String(value);
        }
    }

    _formatBoolean(value) {
        const isTrue = value === true || value === 'true' || value === 1;
        const bgColor = isTrue ? 'var(--cl-success-light)' : 'var(--cl-bg-secondary)';
        const fgColor = isTrue ? 'var(--cl-success)' : 'var(--cl-grey)';
        const text = isTrue ? '是' : '否';
        return `<span style="display:inline-block;padding:2px 8px;border-radius:4px;font-size:12px;background:${bgColor};color:${fgColor};font-weight:500;">${text}</span>`;
    }

    _formatOption(def, value) {
        if (def.optionsSource?.type === 'static') {
            const item = def.optionsSource.items.find(i => i.value === value);
            if (item) return this._escapeHtml(item.label);
        }
        return this._escapeHtml(String(value));
    }

    _formatMultiOption(def, value) {
        const values = Array.isArray(value) ? value : [];
        if (values.length === 0) return '<span style="color:#ccc;">—</span>';

        if (def.optionsSource?.type === 'static') {
            return values.map(v => {
                const item = def.optionsSource.items.find(i => i.value === v);
                const label = item ? item.label : v;
                return `<span style="display:inline-block;padding:2px 8px;margin:2px;border-radius:4px;font-size:12px;background: var(--cl-bg-active);color:#1976D2;">${this._escapeHtml(label)}</span>`;
            }).join('');
        }

        return values.map(v =>
            `<span style="display:inline-block;padding:2px 8px;margin:2px;border-radius:4px;font-size:12px;background: var(--cl-bg-active);color:#1976D2;">${this._escapeHtml(String(v))}</span>`
        ).join('');
    }

    _formatDateTime(value) {
        try {
            const d = value instanceof Date ? value : new Date(value);
            if (isNaN(d.getTime())) return String(value);
            return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
        } catch {
            return String(value);
        }
    }

    _formatListValue(value) {
        if (Array.isArray(value)) {
            if (value.length === 0) return '<span style="color:#ccc;">—</span>';
            return value.map((item, i) => {
                const text = typeof item === 'object' ? Object.values(item).filter(Boolean).join(' / ') : String(item);
                return `<div style="padding:2px 0;font-size:13px;">${i + 1}. ${this._escapeHtml(text)}</div>`;
            }).join('');
        }
        if (typeof value === 'object') {
            return this._escapeHtml(Object.values(value).filter(Boolean).join(' / '));
        }
        return this._escapeHtml(String(value));
    }

    _escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    _createButtons() {
        const footer = document.createElement('div');
        footer.className = 'dynamic-detail__footer';
        footer.style.cssText = 'display:flex;justify-content:flex-end;gap:8px;margin-top:24px;padding-top:16px;border-top:1px solid var(--cl-border-light);';

        if (this.options.onBack) {
            const backBtn = document.createElement('button');
            backBtn.type = 'button';
            backBtn.textContent = '返回列表';
            backBtn.style.cssText = 'padding:8px 20px;border: 1px solid var(--cl-border);background: var(--cl-bg);border-radius:6px;cursor:pointer;font-size:14px;';
            backBtn.addEventListener('click', () => this.options.onBack());
            footer.appendChild(backBtn);
        }

        if (this.options.onEdit) {
            const editBtn = document.createElement('button');
            editBtn.type = 'button';
            editBtn.textContent = '編輯';
            editBtn.style.cssText = 'padding:8px 20px;border:none;background: var(--cl-primary);color:white;border-radius:6px;cursor:pointer;font-size:14px;';
            editBtn.addEventListener('click', () => this.options.onEdit());
            footer.appendChild(editBtn);
        }

        return footer;
    }

    // ─── 公開 API ───

    /**
     * 設定/更新資料
     */
    setData(data) {
        this.options.data = data;
        if (this.element?.parentNode) {
            const parent = this.element.parentNode;
            this.element.remove();
            this._build();
            parent.appendChild(this.element);
        } else {
            this._build();
        }
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this.element) target.appendChild(this.element);
        return this;
    }

    destroy() {
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default DynamicDetailRenderer;
