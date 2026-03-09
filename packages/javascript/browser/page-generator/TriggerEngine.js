/**
 * TriggerEngine
 * 聯動行為執行引擎 - 原子行為註冊 + 組合執行
 *
 * 設計原則：
 * 1. 所有行為必須預先註冊，不允許未知 action
 * 2. 每個 action 只做一件事（原子化）
 * 3. 複雜聯動 = 多個原子 action 的組合
 * 4. 未知 action 忽略並 console.warn
 */

export class TriggerEngine {
    constructor() {
        /** @type {Map<string, Function>} 原子行為註冊表 */
        this._actions = new Map();

        /** @type {Map<string, Object>} fieldName → { formField, component, definition } */
        this._fieldMap = new Map();

        /** @type {Array} 已綁定的事件清理函式 */
        this._cleanups = [];

        // 註冊內建原子行為
        this._registerBuiltins();
    }

    /**
     * 註冊內建的 8 個原子行為
     */
    _registerBuiltins() {
        // clear: 清空目標值
        this.registerAction('clear', (source, target) => {
            const comp = target.component;
            if (typeof comp.clear === 'function') {
                comp.clear();
            } else if (typeof comp.setValue === 'function') {
                comp.setValue(comp.options?.defaultValue ?? '');
            }
        });

        // setValue: 設定目標值
        this.registerAction('setValue', (source, target, params = {}) => {
            const comp = target.component;
            if (typeof comp.setValue !== 'function') return;

            if ('value' in params) {
                comp.setValue(params.value);
            } else if (params.fromField) {
                const fromEntry = this._fieldMap.get(params.fromField);
                if (fromEntry?.component?.getValue) {
                    comp.setValue(fromEntry.component.getValue());
                }
            }
        });

        // show: 顯示目標欄位
        this.registerAction('show', (source, target) => {
            if (target.formField) {
                target.formField.show();
            }
        });

        // hide: 隱藏目標欄位
        this.registerAction('hide', (source, target) => {
            if (target.formField) {
                target.formField.hide();
            }
        });

        // setReadonly: 設定唯讀狀態
        this.registerAction('setReadonly', (source, target, params = {}) => {
            if (target.formField) {
                target.formField.setReadonly(!!params.value);
            }
        });

        // setRequired: 設定必填狀態
        this.registerAction('setRequired', (source, target, params = {}) => {
            if (target.formField) {
                target.formField.setRequired(!!params.value);
            }
        });

        // reload: 觸發目標元件重新載入
        this.registerAction('reload', (source, target) => {
            const comp = target.component;
            if (typeof comp.reload === 'function') {
                comp.reload();
            } else if (typeof comp.refresh === 'function') {
                comp.refresh();
            }
        });

        // reloadOptions: 重新載入目標欄位的選項
        this.registerAction('reloadOptions', (source, target) => {
            const comp = target.component;
            const def = target.definition;

            if (!def?.optionsSource || def.optionsSource.type !== 'api') return;
            if (typeof comp.setItems !== 'function') return;

            const { endpoint, params, parentField } = def.optionsSource;
            const fetchParams = { ...params };

            // 帶入來源欄位的值作為父欄位參數
            if (parentField) {
                const parentEntry = this._fieldMap.get(parentField);
                if (parentEntry?.component?.getValue) {
                    fetchParams[parentField] = parentEntry.component.getValue();
                }
            }

            // 發送 API 請求載入選項
            fetch(endpoint, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(fetchParams)
            })
            .then(res => res.json())
            .then(data => {
                // 預期回傳格式：[{ value, label }] 或 { items: [...] }
                const items = Array.isArray(data) ? data : (data.items || []);
                comp.setItems(items);
            })
            .catch(err => {
                console.warn(`[TriggerEngine] reloadOptions 失敗:`, err);
            });
        });
    }

    // ─── 公開 API ───

    /**
     * 註冊原子行為
     * @param {string} name - 行為名稱
     * @param {Function} handler - (sourceEntry, targetEntry, params) => void
     */
    registerAction(name, handler) {
        this._actions.set(name, handler);
    }

    /**
     * 綁定欄位定義與元件實例
     * 讀取每個欄位的 triggers，綁定對應的 DOM 事件
     *
     * @param {Array} fieldDefinitions - 頁面定義 JSON 中的 fields 陣列
     * @param {Map<string, Object>} fieldInstances - fieldName → { formField, component }
     */
    bind(fieldDefinitions, fieldInstances) {
        // 清理舊綁定
        this.unbind();

        // 建立 fieldMap
        fieldDefinitions.forEach(def => {
            const instance = fieldInstances.get(def.fieldName);
            if (instance) {
                this._fieldMap.set(def.fieldName, {
                    formField: instance.formField,
                    component: instance.component,
                    definition: def
                });
            }
        });

        // 綁定 triggers
        fieldDefinitions.forEach(def => {
            if (!def.triggers || !Array.isArray(def.triggers)) return;

            const sourceEntry = this._fieldMap.get(def.fieldName);
            if (!sourceEntry) return;

            def.triggers.forEach(trigger => {
                this._bindTrigger(def.fieldName, sourceEntry, trigger);
            });
        });
    }

    /**
     * 綁定單一觸發規則
     */
    _bindTrigger(sourceFieldName, sourceEntry, trigger) {
        const { on, target: targetFieldName, action, params } = trigger;

        const targetEntry = this._fieldMap.get(targetFieldName);
        if (!targetEntry) {
            console.warn(`[TriggerEngine] 找不到目標欄位: ${targetFieldName}`);
            return;
        }

        if (!this._actions.has(action)) {
            console.warn(`[TriggerEngine] 未知的 action: ${action}，已忽略`);
            return;
        }

        const handler = this._actions.get(action);
        const comp = sourceEntry.component;

        // 依 on 值綁定不同事件
        switch (on) {
            case 'change': {
                // 統一透過 onChange 回調
                const origOnChange = comp.options?.onChange;
                const wrappedOnChange = (...args) => {
                    if (origOnChange) origOnChange(...args);
                    handler(sourceEntry, targetEntry, params);
                };

                if (comp.options) {
                    comp.options.onChange = wrappedOnChange;
                }

                this._cleanups.push(() => {
                    if (comp.options) comp.options.onChange = origOnChange;
                });
                break;
            }

            case 'check':
            case 'uncheck': {
                // checkbox/toggle 的 onChange，依 checked 狀態判斷
                const origOnChange = comp.options?.onChange;
                const wrappedOnChange = (...args) => {
                    if (origOnChange) origOnChange(...args);
                    const isChecked = typeof comp.isChecked === 'function'
                        ? comp.isChecked()
                        : (typeof comp.getValue === 'function' ? !!comp.getValue() : false);

                    if ((on === 'check' && isChecked) || (on === 'uncheck' && !isChecked)) {
                        handler(sourceEntry, targetEntry, params);
                    }
                };

                if (comp.options) {
                    comp.options.onChange = wrappedOnChange;
                }

                this._cleanups.push(() => {
                    if (comp.options) comp.options.onChange = origOnChange;
                });
                break;
            }

            case 'upload': {
                // BatchUploader 的 onComplete 回調
                const origOnComplete = comp.options?.onComplete;
                const wrappedOnComplete = (...args) => {
                    if (origOnComplete) origOnComplete(...args);
                    handler(sourceEntry, targetEntry, params);
                };

                if (comp.options) {
                    comp.options.onComplete = wrappedOnComplete;
                }

                this._cleanups.push(() => {
                    if (comp.options) comp.options.onComplete = origOnComplete;
                });
                break;
            }

            default:
                console.warn(`[TriggerEngine] 未知的觸發時機: ${on}，已忽略`);
        }
    }

    /**
     * 手動執行單一原子行為
     */
    execute(actionName, sourceFieldName, targetFieldName, params = {}) {
        const handler = this._actions.get(actionName);
        if (!handler) {
            console.warn(`[TriggerEngine] 未知的 action: ${actionName}`);
            return;
        }

        const sourceEntry = this._fieldMap.get(sourceFieldName);
        const targetEntry = this._fieldMap.get(targetFieldName);

        if (!targetEntry) {
            console.warn(`[TriggerEngine] 找不到目標欄位: ${targetFieldName}`);
            return;
        }

        handler(sourceEntry, targetEntry, params);
    }

    /**
     * 解除所有綁定
     */
    unbind() {
        this._cleanups.forEach(cleanup => cleanup());
        this._cleanups = [];
        this._fieldMap.clear();
    }

    /**
     * 銷毀引擎
     */
    destroy() {
        this.unbind();
        this._actions.clear();
    }
}

export default TriggerEngine;
