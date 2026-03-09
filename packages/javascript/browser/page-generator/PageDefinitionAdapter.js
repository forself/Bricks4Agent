/**
 * PageDefinitionAdapter - 頁面定義格式轉換器
 *
 * 在兩種頁面定義格式之間進行雙向轉換：
 * - 新格式（AI 生成格式，用於 DynamicPageRenderer，含 30 種 fieldType）
 * - 舊格式（用於 PageGenerator 靜態程式碼生成）
 *
 * 使用方式：
 *   PageDefinitionAdapter.toOldFormat(newDef)  // 新格式 → 舊格式
 *   PageDefinitionAdapter.toNewFormat(oldDef)  // 舊格式 → 新格式
 *
 * 元件映射直接使用 PageDefinition.js 的 ComponentMapping（17 種需要元件的 fieldType）
 *
 * @module PageDefinitionAdapter
 */

import { ComponentMapping } from './PageDefinition.js';

// ============================================================
// PageDefinitionAdapter 類別
// ============================================================

export class PageDefinitionAdapter {

    /**
     * 將新格式轉換為舊格式（供 PageGenerator 使用）
     *
     * @param {Object} newDef - 新格式頁面定義（含 page、fields）
     * @returns {Object} 舊格式頁面定義
     */
    static toOldFormat(newDef) {
        if (!newDef) return null;

        const page = newDef.page || {};
        const fields = newDef.fields || [];
        const entity = page.entity || '';
        const view = page.view || '';

        // 轉換頁面名稱：使用 entity 首字母大寫 + "Page"
        const name = entity
            ? PageDefinitionAdapter._capitalize(entity) + 'Page'
            : 'UnnamedPage';

        // 推斷頁面類型
        const type = PageDefinitionAdapter._derivePageType(view);

        // 轉換欄位
        const oldFields = fields.map(f => PageDefinitionAdapter._convertFieldToOld(f));

        // 從欄位推斷需要的元件
        const components = PageDefinitionAdapter._inferComponentsFromFields(fields);

        // 收集 fieldTriggers
        const fieldTriggers = {};
        for (const field of fields) {
            if (field.triggers && Array.isArray(field.triggers) && field.triggers.length > 0) {
                fieldTriggers[field.fieldName] = field.triggers;
            }
        }

        // 建立 API 端點（以 entity 為基底）
        const apiBase = entity ? `/api/${entity}` : '/api/data';

        return {
            name,
            type,
            description: page.pageName || '',
            components,
            services: [],
            fields: oldFields,
            api: {
                list: apiBase,
                get: apiBase,
                create: apiBase,
                update: apiBase,
                delete: apiBase
            },
            behaviors: {
                fieldTriggers: Object.keys(fieldTriggers).length > 0 ? fieldTriggers : {}
            },
            styles: {
                layout: 'single',
                theme: 'default'
            }
        };
    }

    /**
     * 將舊格式轉換為新格式（供 DynamicPageRenderer 使用）
     *
     * @param {Object} oldDef - 舊格式頁面定義
     * @returns {Object} 新格式頁面定義（含 page、fields）
     */
    static toNewFormat(oldDef) {
        if (!oldDef) return null;

        const name = oldDef.name || '';
        const entity = PageDefinitionAdapter._deriveEntity(name);
        const pageName = oldDef.description || name.replace(/Page$/, '');

        // 推斷 view
        let view = 'form';
        switch (oldDef.type) {
            case 'list':
                view = 'adminList';
                break;
            case 'detail':
                view = 'detail';
                break;
            case 'form':
            default:
                view = 'form';
                break;
        }

        // 取得 fieldTriggers 映射表
        const fieldTriggers = oldDef.behaviors?.fieldTriggers || {};

        // 轉換欄位
        const fields = (oldDef.fields || []).map((f, index) => {
            return PageDefinitionAdapter._convertFieldToNew(f, index, fieldTriggers);
        });

        return {
            page: {
                pageName,
                entity,
                view
            },
            fields
        };
    }

    // ============================================================
    // 輔助方法：欄位轉換
    // ============================================================

    /**
     * 將新格式欄位轉換為舊格式欄位
     * @private
     * @param {Object} field - 新格式欄位
     * @returns {Object} 舊格式欄位
     */
    static _convertFieldToOld(field) {
        if (!field) return {};

        const result = {
            name: field.fieldName || '',
            type: field.fieldType === 'multiselect' ? 'select' : (field.fieldType || 'text'),
            label: field.label || '',
            required: field.isRequired === true,
            default: PageDefinitionAdapter._parseDefaultValue(field.defaultValue, field.fieldType),
            validation: field.validation || null,
            dependsOn: field.dependsOn || null,
            component: field.component || null
        };

        // 處理 optionsSource → options
        if (field.optionsSource) {
            if (field.optionsSource.type === 'static') {
                result.options = field.optionsSource.items || [];
            }
            // API 來源的選項不直接映射到 options，存入 config
            if (field.optionsSource.type === 'api') {
                result.config = result.config || {};
                result.config.optionsApi = field.optionsSource;
            }
        } else {
            result.options = null;
        }

        // 處理 isReadonly → config.readonly
        if (field.isReadonly === true) {
            result.config = result.config || {};
            result.config.readonly = true;
        }

        return result;
    }

    /**
     * 將舊格式欄位轉換為新格式欄位
     * @private
     * @param {Object} field - 舊格式欄位
     * @param {number} index - 欄位索引（用於自動遞增值）
     * @param {Object} fieldTriggers - behaviors.fieldTriggers 映射表
     * @returns {Object} 新格式欄位
     */
    static _convertFieldToNew(field, index, fieldTriggers) {
        if (!field) return {};

        const fieldName = field.name || '';

        // 處理 options → optionsSource
        let optionsSource = null;
        if (field.options && Array.isArray(field.options) && field.options.length > 0) {
            optionsSource = {
                type: 'static',
                items: field.options
            };
        }
        // 如果有 API 選項來源（從 config 中恢復）
        if (field.config?.optionsApi) {
            optionsSource = field.config.optionsApi;
        }

        // 處理 triggers（從 fieldTriggers 中取回屬於此欄位的觸發器）
        const triggers = fieldTriggers[fieldName] || null;

        return {
            fieldName,
            label: field.label || '',
            fieldType: field.type || 'text',
            component: field.component || null,
            defaultValue: PageDefinitionAdapter._stringifyDefaultValue(field.default),
            formRow: index + 1,
            formCol: null,
            listOrder: index + 1,
            isRequired: field.required === true,
            isReadonly: field.config?.readonly === true,
            isSearchable: false,
            optionsSource,
            validation: field.validation || null,
            dependsOn: field.dependsOn || null,
            triggers
        };
    }

    // ============================================================
    // 輔助方法：頁面層級推斷
    // ============================================================

    /**
     * 從 view 名稱推斷頁面類型
     * @param {string} view - 視圖名稱（例如 "adminList"、"detail"、"form"）
     * @returns {string} 頁面類型（"list"、"detail"、"form"）
     */
    static _derivePageType(view) {
        if (!view) return 'form';

        const v = view.toLowerCase();
        if (v.includes('list')) return 'list';
        if (v.includes('detail')) return 'detail';
        return 'form';
    }

    /**
     * 從舊格式頁面名稱推斷 entity
     * @param {string} name - 頁面名稱（例如 "EmployeePage"）
     * @returns {string} entity 名稱（小寫，例如 "employee"）
     */
    static _deriveEntity(name) {
        if (!name) return '';
        // 移除 "Page" 後綴，轉為小寫
        return name.replace(/Page$/i, '').toLowerCase();
    }

    /**
     * 從欄位列表推斷需要的元件清單
     * @private
     * @param {Object[]} fields - 新格式欄位陣列
     * @returns {string[]} 元件名稱陣列
     */
    static _inferComponentsFromFields(fields) {
        const components = new Set();

        for (const field of fields) {
            const fieldType = field.fieldType || '';
            const component = ComponentMapping[fieldType];
            if (component) {
                components.add(component);
            }
            // 如果欄位明確指定了 component，也加入
            if (field.component) {
                components.add(field.component);
            }
        }

        return Array.from(components);
    }

    // ============================================================
    // 輔助方法：預設值轉換
    // ============================================================

    /**
     * 將字串型態的 defaultValue 解析為對應的 JavaScript 型別
     *
     * @param {string|null} value - 字串型態的預設值
     * @param {string} fieldType - 欄位類型（用於判斷轉換邏輯）
     * @returns {*} 解析後的值（boolean、number、string 或 null）
     */
    static _parseDefaultValue(value, fieldType) {
        if (value === null || value === undefined) return null;

        // 字串型態的布林值
        if (value === 'true') return true;
        if (value === 'false') return false;

        // 數值型態的欄位嘗試轉為數字
        if (typeof value === 'string' && value !== '') {
            const num = Number(value);
            if (!isNaN(num) && fieldType === 'number') {
                return num;
            }
        }

        return value;
    }

    /**
     * 將任意型別的預設值轉為字串表示
     *
     * @param {*} value - 預設值
     * @returns {string|null} 字串表示，或 null
     */
    static _stringifyDefaultValue(value) {
        if (value === null || value === undefined) return null;
        if (typeof value === 'string') return value;
        return String(value);
    }

    /**
     * 將字串首字母大寫
     * @param {string} str - 輸入字串
     * @returns {string} 首字母大寫的字串
     */
    static _capitalize(str) {
        if (!str) return '';
        return str.charAt(0).toUpperCase() + str.slice(1);
    }
}

export default PageDefinitionAdapter;
