/**
 * PersonnelDefinition - 人事系統頁面定義（新格式，複合輸入元件展示）
 *
 * 展示所有複合輸入元件的使用方式：
 * - address（地址輸入，含縣市/鄉鎮聯動）
 * - phonelist（電話列表）
 * - socialmedia（社群媒體列表）
 * - organization（組織層級輸入）
 * - student（學生資訊輸入）
 * - personinfo（人員資訊列表）
 * - addresslist（多筆地址列表）
 *
 * 此定義同時展示 triggers、validation、dependsOn 等進階功能。
 *
 * @module examples/PersonnelDefinition
 */

/**
 * 人事系統頁面定義（新格式）
 */
export const PersonnelDefinition = {
    page: {
        pageName: '人事資料管理',
        entity: 'personnel',
        view: 'form'
    },
    fields: [
        // ── 基本資料（Row 1）──
        {
            fieldName: 'employeeId',
            label: '員工編號',
            fieldType: 'text',
            formRow: 1,
            formCol: 4,
            listOrder: 1,
            isRequired: true,
            isReadonly: false,
            isSearchable: true,
            validation: { pattern: '^[A-Z]\\d{6}$', patternMessage: '格式：A000001' }
        },
        {
            fieldName: 'name',
            label: '姓名',
            fieldType: 'text',
            formRow: 1,
            formCol: 4,
            listOrder: 2,
            isRequired: true,
            isReadonly: false,
            isSearchable: true,
            validation: { maxLength: 50 }
        },
        {
            fieldName: 'idNumber',
            label: '身分證字號',
            fieldType: 'text',
            formRow: 1,
            formCol: 4,
            listOrder: 0,
            isRequired: true,
            isReadonly: false,
            isSearchable: false,
            validation: { pattern: '^[A-Z]\\d{9}$', patternMessage: '請輸入正確的身分證字號' }
        },

        // ── 日期與狀態（Row 2）──
        {
            fieldName: 'birthDate',
            label: '出生日期',
            fieldType: 'date',
            formRow: 2,
            formCol: 4,
            listOrder: 0,
            isRequired: true,
            isReadonly: false,
            isSearchable: false
        },
        {
            fieldName: 'hireDate',
            label: '到職日',
            fieldType: 'date',
            formRow: 2,
            formCol: 4,
            listOrder: 3,
            isRequired: true,
            isReadonly: false,
            isSearchable: true
        },
        {
            fieldName: 'isActive',
            label: '在職',
            fieldType: 'toggle',
            formRow: 2,
            formCol: 4,
            listOrder: 4,
            isRequired: false,
            isReadonly: false,
            isSearchable: true,
            defaultValue: 'true'
        },

        // ── 組織資訊（Row 3，複合輸入）──
        {
            fieldName: 'organization',
            label: '所屬組織',
            fieldType: 'organization',
            formRow: 3,
            formCol: null,
            listOrder: 5,
            isRequired: true,
            isReadonly: false,
            isSearchable: true
        },

        // ── 住家地址（Row 4，複合輸入）──
        {
            fieldName: 'homeAddress',
            label: '住家地址',
            fieldType: 'address',
            formRow: 4,
            formCol: null,
            listOrder: 0,
            isRequired: true,
            isReadonly: false,
            isSearchable: false
        },

        // ── 通訊地址列表（Row 5，複合輸入）──
        {
            fieldName: 'otherAddresses',
            label: '其他通訊地址',
            fieldType: 'addresslist',
            formRow: 5,
            formCol: null,
            listOrder: 0,
            isRequired: false,
            isReadonly: false,
            isSearchable: false,
            validation: { maxItems: 3 }
        },

        // ── 聯絡電話（Row 6，複合輸入）──
        {
            fieldName: 'phones',
            label: '聯絡電話',
            fieldType: 'phonelist',
            formRow: 6,
            formCol: null,
            listOrder: 0,
            isRequired: true,
            isReadonly: false,
            isSearchable: false,
            validation: { maxItems: 5 }
        },

        // ── 社群媒體（Row 7，複合輸入）──
        {
            fieldName: 'socialMedia',
            label: '社群帳號',
            fieldType: 'socialmedia',
            formRow: 7,
            formCol: null,
            listOrder: 0,
            isRequired: false,
            isReadonly: false,
            isSearchable: false,
            validation: { maxItems: 5 }
        },

        // ── 學生資訊（Row 8，複合輸入）──
        {
            fieldName: 'studentInfo',
            label: '學生身分',
            fieldType: 'student',
            formRow: 8,
            formCol: 6,
            listOrder: 0,
            isRequired: false,
            isReadonly: false,
            isSearchable: false
        },

        // ── 緊急聯絡人（Row 9，複合輸入）──
        {
            fieldName: 'emergencyContacts',
            label: '緊急聯絡人',
            fieldType: 'personinfo',
            formRow: 9,
            formCol: null,
            listOrder: 0,
            isRequired: true,
            isReadonly: false,
            isSearchable: false,
            validation: { maxItems: 3 }
        },

        // ── 照片（Row 10）──
        {
            fieldName: 'photo',
            label: '個人照片',
            fieldType: 'image',
            formRow: 10,
            formCol: 6,
            listOrder: 0,
            isRequired: false,
            isReadonly: false,
            isSearchable: false
        },

        // ── 備註（Row 11）──
        {
            fieldName: 'notes',
            label: '備註',
            fieldType: 'textarea',
            formRow: 11,
            formCol: null,
            listOrder: 0,
            isRequired: false,
            isReadonly: false,
            isSearchable: false
        }
    ]
};

export default PersonnelDefinition;
