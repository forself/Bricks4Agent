/**
 * PageDefinition - 頁面定義規格
 *
 * 定義頁面的結構、元件、服務、欄位、API 等
 * 此定義將被 PageGenerator 讀取並生成實際頁面
 *
 * @module PageDefinition
 */

/**
 * 頁面定義 Schema
 * @typedef {Object} PageDefinition
 * @property {string} name - 頁面名稱 (PascalCase)
 * @property {string} type - 頁面類型 (form|list|detail|dashboard)
 * @property {string} [description] - 頁面描述
 * @property {ComponentDef[]} components - 需要的元件
 * @property {ServiceDef[]} services - 需要的服務
 * @property {FieldDef[]} fields - 欄位定義
 * @property {ApiDef} api - API 端點定義
 * @property {BehaviorDef} behaviors - 頁面行為定義
 * @property {StyleDef} styles - 樣式定義
 */

/**
 * 元件定義
 * @typedef {Object} ComponentDef
 * @property {string} name - 元件名稱
 * @property {string} [variant] - 元件變體 (full|lite)
 * @property {Object} [config] - 元件配置
 */

/**
 * 服務定義
 * @typedef {Object} ServiceDef
 * @property {string} name - 服務名稱
 * @property {Object} [config] - 服務配置
 */

/**
 * 欄位定義
 * @typedef {Object} FieldDef
 * @property {string} name - 欄位名稱
 * @property {string} type - 欄位類型
 * @property {string} [label] - 顯示標籤
 * @property {boolean} [required] - 是否必填
 * @property {*} [default] - 預設值
 * @property {Object} [validation] - 驗證規則
 * @property {string} [dependsOn] - 依賴欄位
 * @property {string} [component] - 使用的元件
 */

/**
 * API 定義
 * @typedef {Object} ApiDef
 * @property {string} [list] - 列表端點
 * @property {string} [get] - 取得單筆端點
 * @property {string} [create] - 新增端點
 * @property {string} [update] - 更新端點
 * @property {string} [delete] - 刪除端點
 */

/**
 * 行為定義
 * @typedef {Object} BehaviorDef
 * @property {string} [onInit] - 初始化行為
 * @property {string} [onSave] - 儲存後行為
 * @property {string} [onDelete] - 刪除後行為
 * @property {Object} [fieldTriggers] - 欄位觸發器
 */

/**
 * 樣式定義
 * @typedef {Object} StyleDef
 * @property {string} [layout] - 佈局 (single|two-column|tabs)
 * @property {string} [theme] - 主題
 * @property {Object} [custom] - 自訂樣式
 */

// ============================================================
// 欄位類型定義
// ============================================================

export const FieldTypes = {
    // 基本類型
    TEXT: 'text',
    TEXTAREA: 'textarea',
    NUMBER: 'number',
    EMAIL: 'email',
    PASSWORD: 'password',
    TEL: 'tel',
    URL: 'url',

    // 選擇類型
    SELECT: 'select',
    MULTISELECT: 'multiselect',
    RADIO: 'radio',
    CHECKBOX: 'checkbox',
    TOGGLE: 'toggle',

    // 日期時間
    DATE: 'date',
    TIME: 'time',
    DATETIME: 'datetime',

    // 進階類型
    RICHTEXT: 'richtext',       // 需要 WebTextEditor
    CANVAS: 'canvas',           // 需要 DrawingBoard
    COLOR: 'color',             // 需要 ColorPicker
    IMAGE: 'image',             // 需要 ImageViewer
    FILE: 'file',

    // 服務類型
    GEOLOCATION: 'geolocation', // 需要 GeolocationService
    WEATHER: 'weather',         // 需要 WeatherService

    // 複合輸入類型
    ADDRESS: 'address',             // 需要 AddressInput
    ADDRESSLIST: 'addresslist',     // 需要 AddressListInput
    CHAINED: 'chained',             // 需要 ChainedInput
    LIST: 'list',                   // 需要 ListInput
    PERSONINFO: 'personinfo',       // 需要 PersonInfoList
    PHONELIST: 'phonelist',         // 需要 PhoneListInput
    SOCIALMEDIA: 'socialmedia',     // 需要 SocialMediaList
    ORGANIZATION: 'organization',   // 需要 OrganizationInput
    STUDENT: 'student',             // 需要 StudentInput

    // 評分 / 標籤
    RATING: 'rating',
    TAGS: 'tags',

    // 隱藏類型
    HIDDEN: 'hidden'
};

// ============================================================
// 頁面類型定義
// ============================================================

export const PageTypes = {
    FORM: 'form',           // 表單頁面（新增/編輯）
    LIST: 'list',           // 列表頁面
    DETAIL: 'detail',       // 詳情頁面（唯讀）
    DASHBOARD: 'dashboard'  // 儀表板
};

// ============================================================
// 元件映射表
// ============================================================

export const ComponentMapping = {
    // 欄位類型 -> 元件名稱
    [FieldTypes.DATE]: 'DatePicker',
    [FieldTypes.DATETIME]: 'DateTimeInput',
    [FieldTypes.COLOR]: 'ColorPicker',
    [FieldTypes.IMAGE]: 'ImageViewer',
    [FieldTypes.RICHTEXT]: 'WebTextEditor',
    [FieldTypes.CANVAS]: 'DrawingBoard',
    [FieldTypes.GEOLOCATION]: 'GeolocationService',
    [FieldTypes.WEATHER]: 'WeatherService',
    // 複合輸入元件
    [FieldTypes.ADDRESS]: 'AddressInput',
    [FieldTypes.ADDRESSLIST]: 'AddressListInput',
    [FieldTypes.CHAINED]: 'ChainedInput',
    [FieldTypes.LIST]: 'ListInput',
    [FieldTypes.PERSONINFO]: 'PersonInfoList',
    [FieldTypes.PHONELIST]: 'PhoneListInput',
    [FieldTypes.SOCIALMEDIA]: 'SocialMediaList',
    [FieldTypes.ORGANIZATION]: 'OrganizationInput',
    [FieldTypes.STUDENT]: 'StudentInput'
};

// ============================================================
// 元件庫清單
// ============================================================

export const AvailableComponents = {
    // SPA 範本內建 (templates/spa/frontend/components/)
    custom: [
        'DatePicker',
        'ColorPicker',
        'ImageViewer',
        'ToastPanel',
        'ModalPanel',
        'GeolocationService',
        'WeatherService',
        'WebTextEditor',
        'DrawingBoard',
        'WebPainter',
        'BasicButton',
        'EditorButton',
        'ButtonGroup',
        'DateTimeInput',
        'AddressInput',
        'AddressListInput',
        'ChainedInput',
        'ListInput',
        'PersonInfoList',
        'PhoneListInput',
        'SocialMediaList',
        'OrganizationInput',
        'StudentInput'
    ],

    // Packages 進階元件 (packages/javascript/browser/ui_components/)
    packages: [
        'DatePicker',
        'ColorPicker',
        'ImageViewer',
        'ToastPanel',
        'ModalPanel',
        'GeolocationService',
        'WeatherService',
        'WebTextEditor',
        'DrawingBoard',
        'WebPainter',
        'BasicButton',
        'EditorButton',
        'ButtonGroup',
        // 複合輸入元件
        'DateTimeInput',
        'AddressInput',
        'AddressListInput',
        'ChainedInput',
        'ListInput',
        'PersonInfoList',
        'PhoneListInput',
        'SocialMediaList',
        'OrganizationInput',
        'StudentInput'
    ]
};

// ============================================================
// 驗證函數
// ============================================================

/**
 * 驗證頁面定義是否有效
 * @param {PageDefinition} definition
 * @returns {{ valid: boolean, errors: string[] }}
 */
export function validateDefinition(definition) {
    const errors = [];

    // 必要欄位檢查
    if (!definition.name) {
        errors.push('缺少頁面名稱 (name)');
    } else if (!/^[A-Z][a-zA-Z0-9]*Page$/.test(definition.name)) {
        errors.push('頁面名稱必須是 PascalCase 且以 Page 結尾');
    }

    if (!definition.type) {
        errors.push('缺少頁面類型 (type)');
    } else if (!Object.values(PageTypes).includes(definition.type)) {
        errors.push(`無效的頁面類型: ${definition.type}`);
    }

    // 元件檢查
    if (definition.components) {
        const allComponents = AvailableComponents.custom;

        for (const comp of definition.components) {
            const compName = typeof comp === 'string' ? comp : comp.name;
            if (!allComponents.includes(compName)) {
                errors.push(`未知的元件: ${compName}，請先加入元件庫`);
            }
        }
    }

    // 欄位檢查
    if (definition.fields) {
        for (const field of definition.fields) {
            if (!field.name) {
                errors.push('欄位缺少名稱');
            }
            if (!field.type) {
                errors.push(`欄位 ${field.name} 缺少類型`);
            } else if (!Object.values(FieldTypes).includes(field.type)) {
                errors.push(`欄位 ${field.name} 的類型無效: ${field.type}`);
            }

            // 檢查依賴欄位
            if (field.dependsOn) {
                const depField = definition.fields.find(f => f.name === field.dependsOn);
                if (!depField) {
                    errors.push(`欄位 ${field.name} 依賴的欄位 ${field.dependsOn} 不存在`);
                }
            }
        }
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

/**
 * 從欄位定義推斷需要的元件
 * @param {FieldDef[]} fields
 * @returns {string[]}
 */
export function inferComponents(fields) {
    const components = new Set();

    for (const field of fields) {
        const component = ComponentMapping[field.type];
        if (component) {
            components.add(component);
        }
    }

    return Array.from(components);
}

/**
 * 建立預設頁面定義
 * @param {string} name
 * @param {string} type
 * @returns {PageDefinition}
 */
export function createDefaultDefinition(name, type = PageTypes.FORM) {
    return {
        name,
        type,
        description: '',
        components: [],
        services: [],
        fields: [],
        api: {},
        behaviors: {},
        styles: {
            layout: 'single',
            theme: 'default'
        }
    };
}

export default {
    FieldTypes,
    PageTypes,
    ComponentMapping,
    AvailableComponents,
    validateDefinition,
    inferComponents,
    createDefaultDefinition
};
