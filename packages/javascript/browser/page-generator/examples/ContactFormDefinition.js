/**
 * ContactFormDefinition - 聯絡表單頁面定義
 *
 * 簡單的聯絡表單範例
 *
 * @module examples/ContactFormDefinition
 */

import { FieldTypes, PageTypes } from '../PageDefinition.js';

/**
 * 聯絡表單頁面定義
 * @type {import('../PageDefinition.js').PageDefinition}
 */
export const ContactFormDefinition = {
    name: 'ContactFormPage',
    type: PageTypes.FORM,
    description: '聯絡我們',

    // 需要的元件（僅使用基本元件）
    components: [
        'ToastPanel',
        'ModalPanel'
    ],

    // 欄位定義
    fields: [
        {
            name: 'name',
            type: FieldTypes.TEXT,
            label: '姓名',
            required: true,
            validation: {
                maxLength: 50
            }
        },
        {
            name: 'email',
            type: FieldTypes.EMAIL,
            label: '電子郵件',
            required: true
        },
        {
            name: 'phone',
            type: FieldTypes.TEXT,
            label: '電話',
            validation: {
                pattern: '^[0-9-]+$'
            }
        },
        {
            name: 'subject',
            type: FieldTypes.SELECT,
            label: '主題',
            required: true,
            options: [
                { value: 'general', label: '一般詢問' },
                { value: 'support', label: '技術支援' },
                { value: 'feedback', label: '意見回饋' },
                { value: 'other', label: '其他' }
            ]
        },
        {
            name: 'message',
            type: FieldTypes.TEXTAREA,
            label: '訊息內容',
            required: true,
            config: {
                rows: 6
            }
        },
        {
            name: 'subscribe',
            type: FieldTypes.CHECKBOX,
            label: '訂閱電子報',
            default: false
        }
    ],

    // API 端點
    api: {
        create: '/api/contact'
    },

    // 行為定義
    behaviors: {
        onSave: 'showThankYouMessage'
    },

    // 樣式定義
    styles: {
        layout: 'single',
        theme: 'default'
    }
};

export default ContactFormDefinition;
