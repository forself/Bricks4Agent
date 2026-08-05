/**
 * DynamicFormRenderer
 * 動態表單渲染器 - 從頁面定義 JSON 組合 FormField + FormRow + TriggerEngine
 *
 * 組合元件：FormField（Layer 1）、FormRow（Layer 1）、TriggerEngine（Layer 2）、FieldResolver（Layer 3）
 */

import { FormRow } from '../ui_components/layout/FormRow/FormRow.js';
import { BasicButton } from '../ui_components/common/BasicButton/BasicButton.js';
import { FieldResolver } from './FieldResolver.js';
import { TriggerEngine } from './TriggerEngine.js';

export class DynamicFormRenderer {
    /**
     * @param {Object} options
     * @param {Object} options.definition - 頁面定義 JSON（包含 page + fields）
     * @param {Function} options.onSave - 儲存回調 (values) => void
     * @param {Function} options.onCancel - 取消回調 () => void
     * @param {boolean} options.showButtons - 是否顯示底部按鈕（預設 true）
     */
    constructor(options = {}) {
        this.options = {
            definition: null,
            onSave: null,
            onCancel: null,
            showButtons: true,
            fieldResolver: null,
            customComponentRegistry: null,
            ...options
        };

        this._fieldResolver = this.options.fieldResolver || new FieldResolver();
        this._triggerEngine = new TriggerEngine();

        /** @type {Map<string, { component, formField }>} */
        this._fieldInstances = new Map();

        /** @type {FormRow[]} */
        this._rows = [];
        this._buttonComponents = [];

        this.element = null;
    }

    /**
     * 初始化並渲染
     */
    async init() {
        await this._fieldResolver.preload();
        if (this.options.customComponentRegistry) {
            this.options.customComponentRegistry.installFieldResolver(this._fieldResolver);
        }
        this._build();
        return this;
    }

    _build() {
        const { definition } = this.options;
        if (!definition?.fields) return;

        const fields = definition.fields;

        // 解析所有欄位
        this._fieldInstances = this._fieldResolver.resolveAll(fields);

        // 依 formRow 分組
        const rowGroups = new Map();
        fields.forEach(def => {
            const row = def.formRow ?? 0;
            if (!rowGroups.has(row)) rowGroups.set(row, []);
            rowGroups.get(row).push(def);
        });

        // 建立容器
        this.element = document.createElement('div');
        this.element.className = 'dynamic-form';

        // 依 formRow 排序後逐列建立 FormRow
        const sortedRows = [...rowGroups.keys()].sort((a, b) => a - b);
        sortedRows.forEach(rowNum => {
            const defs = rowGroups.get(rowNum);
            const formFields = defs
                .map(def => this._fieldInstances.get(def.fieldName)?.formField)
                .filter(Boolean);

            if (formFields.length > 0) {
                const formRow = new FormRow({ fields: formFields });
                this._rows.push(formRow);
                this.element.appendChild(formRow.element);
            }
        });

        // 綁定 TriggerEngine
        this._triggerEngine.bind(fields, this._fieldInstances);

        // 底部按鈕
        if (this.options.showButtons) {
            this.element.appendChild(this._createButtons());
        }
    }

    _createButtons() {
        const footer = document.createElement('div');
        footer.className = 'dynamic-form__footer';
        footer.style.cssText = 'display:flex;justify-content:flex-end;gap:8px;margin-top:24px;padding-top:16px;border-top:1px solid var(--cl-border-light);';

        if (this.options.onCancel) {
            const cancel = new BasicButton({
                type: BasicButton.TYPES.CANCEL,
                variant: 'plain',
                showIcon: false,
                customLabel: '取消',
                onClick: () => this.options.onCancel(),
            });
            const cancelBtn = cancel.element;
            cancelBtn.style.cssText = 'padding:8px 20px;border: 1px solid var(--cl-border);background: var(--cl-bg);border-radius:var(--cl-radius-md);cursor:pointer;font-size:var(--cl-font-size-lg);';
            cancel.mount(footer);
            this._buttonComponents.push(cancel);
        }

        if (this.options.onSave) {
            const save = new BasicButton({
                type: BasicButton.TYPES.SAVE,
                variant: 'primary',
                showIcon: false,
                customLabel: '儲存',
                onClick: () => {
                    if (this.validate()) this.options.onSave(this.getValues());
                },
            });
            const saveBtn = save.element;
            saveBtn.style.cssText = 'padding:8px 20px;border:none;background: var(--cl-primary);color:var(--cl-text-inverse);border-radius:var(--cl-radius-md);cursor:pointer;font-size:var(--cl-font-size-lg);';
            save.mount(footer);
            this._buttonComponents.push(save);
        }

        return footer;
    }

    // ─── 公開 API ───

    /**
     * 取得所有欄位值
     * @returns {Object} { fieldName: value, ... }
     */
    getValues() {
        const values = {};
        this._fieldInstances.forEach(({ component }, fieldName) => {
            if (typeof component.getValue === 'function') {
                values[fieldName] = component.getValue();
            } else if (typeof component.getValues === 'function') {
                values[fieldName] = component.getValues();
            } else if (typeof component.isChecked === 'function') {
                values[fieldName] = component.isChecked();
            }
        });
        return values;
    }

    /**
     * 設定欄位值
     * @param {Object} data - { fieldName: value, ... }
     */
    setValues(data) {
        Object.entries(data).forEach(([fieldName, value]) => {
            const entry = this._fieldInstances.get(fieldName);
            if (!entry) return;

            const comp = entry.component;
            if (typeof comp.setValue === 'function') {
                comp.setValue(value);
            } else if (typeof comp.setValues === 'function') {
                comp.setValues(value);
            } else if (typeof comp.setChecked === 'function') {
                comp.setChecked(!!value);
            }
        });
    }

    /**
     * 驗證所有必填欄位
     * @returns {boolean} 是否通過
     */
    validate() {
        let valid = true;
        const { definition } = this.options;
        if (!definition?.fields) return true;

        definition.fields.forEach(def => {
            const entry = this._fieldInstances.get(def.fieldName);
            if (!entry) return;

            const { component, formField } = entry;

            // 隱藏的欄位跳過驗證
            if (!formField.isVisible()) return;

            let value;
            if (typeof component.getValue === 'function') value = component.getValue();
            else if (typeof component.getValues === 'function') value = component.getValues();
            else if (typeof component.isChecked === 'function') value = component.isChecked();

            const isEmpty = value === null || value === undefined || value === '' ||
                (Array.isArray(value) && value.length === 0);

            if (def.isRequired && isEmpty) {
                formField.setError(def.requiredMessage || `${def.label}為必填`);
                valid = false;
                return;
            }
            if (!isEmpty && def.pattern) {
                let matches = false;
                try {
                    matches = new RegExp(def.pattern).test(String(value));
                } catch {
                    matches = false;
                }
                if (!matches) {
                    formField.setError(def.validationMessage || `${def.label}格式不正確`);
                    valid = false;
                    return;
                }
            }
            formField.clearError();
        });

        return valid;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this.element) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._triggerEngine.destroy();
        this._buttonComponents.splice(0).forEach(button => button.destroy?.());
        this._rows.forEach(row => row.destroy());
        this._rows = [];
        this._fieldInstances.clear();
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default DynamicFormRenderer;
