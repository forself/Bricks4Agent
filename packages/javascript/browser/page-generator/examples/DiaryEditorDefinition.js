/**
 * DiaryEditorDefinition - 日記編輯器頁面定義
 *
 * 此為範例頁面定義，展示如何使用 PageDefinition Schema
 * 定義一個完整的日記編輯頁面
 *
 * @module examples/DiaryEditorDefinition
 */

import { FieldTypes, PageTypes } from '../PageDefinition.js';

/**
 * 日記編輯器頁面定義
 * @type {import('../PageDefinition.js').PageDefinition}
 */
export const DiaryEditorDefinition = {
    name: 'DiaryEditorPage',
    type: PageTypes.FORM,
    description: '日記編輯',

    // 需要的元件
    components: [
        'DatePicker',
        'ColorPicker',
        'GeolocationService',
        'WeatherService',
        'ToastPanel',
        'ModalPanel'
    ],

    // 欄位定義
    fields: [
        {
            name: 'title',
            type: FieldTypes.TEXT,
            label: '標題',
            required: true,
            validation: {
                maxLength: 100
            }
        },
        {
            name: 'date',
            type: FieldTypes.DATE,
            label: '日期',
            required: true,
            default: 'today' // 由 runtime 解析為當日日期，避免 codegen 時固化
        },
        {
            name: 'mood',
            type: FieldTypes.SELECT,
            label: '心情',
            options: [
                { value: 'happy', label: '開心 😊' },
                { value: 'neutral', label: '普通 😐' },
                { value: 'sad', label: '難過 😢' },
                { value: 'angry', label: '生氣 😠' },
                { value: 'excited', label: '興奮 🤩' }
            ]
        },
        {
            name: 'content',
            type: FieldTypes.TEXTAREA,
            label: '內容',
            required: true,
            config: {
                rows: 10
            }
        },
        {
            name: 'location',
            type: FieldTypes.GEOLOCATION,
            label: '位置'
        },
        {
            name: 'weather',
            type: FieldTypes.WEATHER,
            label: '天氣',
            dependsOn: 'location'
        },
        {
            name: 'backgroundColor',
            type: FieldTypes.COLOR,
            label: '背景顏色',
            default: '#ffffff'
        },
        {
            name: 'isPrivate',
            type: FieldTypes.TOGGLE,
            label: '設為私密',
            default: false
        }
    ],

    // API 端點
    api: {
        get: '/api/diary',
        create: '/api/diary',
        update: '/api/diary',
        delete: '/api/diary'
    },

    // 行為定義
    behaviors: {
        onInit: 'loadDiaryIfEditing',
        onSave: 'navigateToList',
        fieldTriggers: {
            location: 'fetchWeatherOnLocationChange'
        }
    },

    // 樣式定義
    styles: {
        layout: 'single',
        theme: 'default'
    }
};

export default DiaryEditorDefinition;
