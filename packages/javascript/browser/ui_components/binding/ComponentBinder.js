import { ComponentFactory } from './ComponentFactory.js';
import { escapeHtml } from '../utils/security.js';

/**
 * ComponentBinder
 * 負責解析 JSON 設定檔並生成元件，處理資料綁定與生命週期。
 */
export class ComponentBinder {
    /**
     * @param {Object} context - 綁定環境的上下文 (通常是 Page 或 Controller instance)
     * 用于在生命週期回調中綁定 'this'
     */
    constructor(context = {}) {
        this.context = context;
        // 儲存已生成的元件實例: { fieldName: componentInstance }
        this.instances = {};
    }

    /**
     * 根據 JSON 設定渲染元件
     * @param {Array<Object>} configList - 元件設定列表
     * @param {HTMLElement|string} container - 容器
     */
    render(configList, container) {
        const targetContainer = typeof container === 'string' ? document.querySelector(container) : container;
        if (!targetContainer) {
            console.error('[ComponentBinder] Container not found:', container);
            return;
        }

        configList.forEach(config => {
            const wrapper = this._createWrapper(config);
            const component = this._createComponent(config);

            if (component) {
                // 1. 綁定生命週期
                this._bindLifecycle(component, config);

                // 2. 儲存實例
                if (config.fieldName) {
                    this.instances[config.fieldName] = component;
                }

                // 3. 掛載
                // 大多數元件有 mount 方法，若無則嘗試直接 append element
                if (typeof component.mount === 'function') {
                    // 若有 fieldset，先掛載到 wrapper 的內容區
                    const mountTarget = wrapper.querySelector('.binding-content') || wrapper;
                    component.mount(mountTarget);
                } else if (component.element instanceof HTMLElement) {
                    const mountTarget = wrapper.querySelector('.binding-content') || wrapper;
                    mountTarget.appendChild(component.element);
                }

                // 4. 初次觸發 onInit
                if (config.lifecycle && config.lifecycle.onInit) {
                    this._executeCallback(config.lifecycle.onInit, component);
                }
            }
            
            targetContainer.appendChild(wrapper);
        });
    }

    /**
     * 建立包裝容器 (處理 Fieldset/Legend, Classname, Validation Message, Styles)
     */
    _createWrapper(config) {
        const wrapper = document.createElement('div');
        wrapper.className = `binding-wrapper ${config.className || ''}`;
        
        // 支援直接樣式設定 (Width, Height, Z-Index etc.)
        if (config.style) {
            Object.assign(wrapper.style, config.style);
        }

        // 處理 Fieldset & Legend
        if (config.fieldset) {
            const fieldset = document.createElement('fieldset');
            fieldset.className = 'binding-fieldset';
            if (config.legend) {
                const legend = document.createElement('legend');
                legend.textContent = config.legend;
                fieldset.appendChild(legend);
            }
            const content = document.createElement('div');
            content.className = 'binding-content';
            fieldset.appendChild(content);
            wrapper.appendChild(fieldset);
        } else {
             // 若無 fieldset，直接作為容器
             wrapper.classList.add('binding-simple-container');
        }

        return wrapper;
    }

    /**
     * 建立元件實例
     */
    _createComponent(config) {
        const options = { ...(config.attrs || {}) };

        // 處理通用屬性對映到各元件的 options
        if (config.fieldName) options.name = config.fieldName; // 許多 input 用 name
        if (config.displayName) options.label = config.displayName;
        if (config.required) options.required = config.required;
        if (config.url) options.src = config.url; // Image/Frame 類
        if (config.target) options.target = config.target;
        
        // 處理 List 類別
        if (config.type === 'list' && config['obj-list']) {
            options.items = this.context[config['obj-list']] || [];
        }

        // 處理 Validation 規則傳遞 (若元件支援)
        if (config.validation) {
            options.validationRule = config.validation;
        }

        return ComponentFactory.create(config.component, options);
    }

    /**
     * 綁定生命週期
     */
    _bindLifecycle(component, config) {
        if (!config.lifecycle) return;

        // OnChange: 大多數 Form 元件支援 options.onChange
        if (config.lifecycle.onChange) {
            // 如果元件支持直接傳入 onChange
            if (component.options) {
                const originalOnChange = component.options.onChange;
                component.options.onChange = (value) => {
                    // 執行原始回調(若有)
                    if (originalOnChange) originalOnChange(value);
                    // 執行綁定回調
                    // 這裡遵守需求: "觸發函式一律傳值this，將要傳的值存放在屬性中"
                    // 但通常 onChange 帶的是 value。
                    // 為了同時滿足，我們將 value 存入 component.value (或類似)，然後傳 component (this)
                    
                    // 嘗試更新 component 內部 value (若尚未更新)
                    if (typeof component.setValue === 'function') {
                        // component.setValue(value); // 通常內部已更新，避免遞迴
                    }
                    
                    // 呼叫 context 中的 method
                    this._executeCallback(config.lifecycle.onChange, component, value);
                };
            }
        }
    }

    /**
     * 執行回調
     * @param {string} methodName - Context 中的方法名
     * @param {Object} component - 元件實例 (作為 this 傳遞)
     * @param {*} value - 額外參數 (如變更的值)
     */
    _executeCallback(methodName, component, value) {
        if (this.context && typeof this.context[methodName] === 'function') {
            // 這裡將 component 作為第一個參數傳遞，模擬 "傳值this" 的需求
            // 或者使用 .call(component) 讓函式內的 this 指向 component?
            // 需求說: "觸發函式一律傳值this" => 可能是指 argument 傳入 this
            this.context[methodName].call(this.context, component, value);
        } else {
            console.warn(`[ComponentBinder] Method "${methodName}" not found in context.`);
        }
    }

    /**
     * 取得特定元件實例
     */
    getComponent(fieldName) {
        return this.instances[fieldName];
    }
}
