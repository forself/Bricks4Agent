/**
 * EmployeeDefinition - 員工管理頁面定義（新格式）
 *
 * 展示新格式（AI 生成格式）的完整功能：
 * - formRow / formCol 佈局控制
 * - listOrder 列表欄位排序
 * - isSearchable 搜尋欄位
 * - optionsSource 靜態/API 選項
 * - triggers 聯動行為
 * - validation 驗證規則
 * - dependsOn 欄位依賴
 *
 * @module examples/EmployeeDefinition
 */

/**
 * 員工管理頁面定義（新格式）
 *
 * 此定義可直接傳給：
 * - DynamicPageRenderer（動態渲染）
 * - PageDefinitionAdapter.toOldFormat()（轉為舊格式後給 PageGenerator）
 * - page-gen CLI（命令列工具）
 */
export const EmployeeDefinition = {
    page: {
        pageName: '員工管理',
        entity: 'employee',
        view: 'adminList'
    },
    fields: [
        {
            fieldName: 'id',
            label: '編號',
            fieldType: 'number',
            formRow: 0,
            formCol: null,
            listOrder: 1,
            isRequired: false,
            isReadonly: true,
            isSearchable: false
        },
        {
            fieldName: 'name',
            label: '姓名',
            fieldType: 'text',
            formRow: 1,
            formCol: 6,
            listOrder: 2,
            isRequired: true,
            isReadonly: false,
            isSearchable: true,
            validation: { maxLength: 50 }
        },
        {
            fieldName: 'email',
            label: '電子郵件',
            fieldType: 'email',
            formRow: 1,
            formCol: 6,
            listOrder: 3,
            isRequired: true,
            isReadonly: false,
            isSearchable: true
        },
        {
            fieldName: 'department',
            label: '部門',
            fieldType: 'select',
            formRow: 2,
            formCol: 6,
            listOrder: 4,
            isRequired: true,
            isReadonly: false,
            isSearchable: true,
            optionsSource: {
                type: 'static',
                items: [
                    { value: 'hr', label: '人力資源部' },
                    { value: 'it', label: '資訊部' },
                    { value: 'sales', label: '業務部' },
                    { value: 'finance', label: '財務部' }
                ]
            },
            triggers: [
                { on: 'change', target: 'team', action: 'reloadOptions' },
                { on: 'change', target: 'team', action: 'clear' }
            ]
        },
        {
            fieldName: 'team',
            label: '小組',
            fieldType: 'select',
            formRow: 2,
            formCol: 6,
            listOrder: 5,
            isRequired: false,
            isReadonly: false,
            isSearchable: false,
            dependsOn: 'department',
            optionsSource: {
                type: 'static',
                items: [
                    { value: 'dev', label: '開發組' },
                    { value: 'qa', label: '測試組' },
                    { value: 'ops', label: '維運組' }
                ]
            }
        },
        {
            fieldName: 'hireDate',
            label: '到職日',
            fieldType: 'date',
            formRow: 3,
            formCol: 6,
            listOrder: 6,
            isRequired: true,
            isReadonly: false,
            isSearchable: true
        },
        {
            fieldName: 'isActive',
            label: '在職',
            fieldType: 'toggle',
            formRow: 3,
            formCol: 6,
            listOrder: 7,
            isRequired: false,
            isReadonly: false,
            isSearchable: true,
            defaultValue: 'true'
        },
        {
            fieldName: 'notes',
            label: '備註',
            fieldType: 'textarea',
            formRow: 4,
            formCol: null,
            listOrder: 0,
            isRequired: false,
            isReadonly: false,
            isSearchable: false
        }
    ]
};

export default EmployeeDefinition;
