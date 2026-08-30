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
 * - DynamicPageRenderer（動態渲染，mode: 'form' 或 'list'）
 * - page-gen CLI（命令列工具，--mode static|dynamic|both）
 * - PageDefinitionAdapter.toOldFormat()（轉為舊格式）
 *
 * 注意：toOldFormat() 會把 field.triggers 原樣放進 behaviors.fieldTriggers，
 * 而 PageGenerator 要求該值是「方法名識別字字串」，因此本定義（含 triggers）
 * 轉檔後不能直接餵給 PageGenerator.generate()；靜態生成請走 page-gen CLI，
 * 它會另外把 triggers 轉成 handle_<fieldName>_trigger 方法名。
 * 詳見 examples/test-all.js「New-format sample definitions」段落。
 */
export const EmployeeDefinition = {
    page: {
        // pageName 是輸出的頁面類別名稱（PascalCase 且以 Page 結尾），
        // 給人看的標題放 title；page-gen CLI 會直接把 pageName 當類別名。
        pageName: 'EmployeePage',
        title: '員工管理',
        entity: 'employee',
        // 'adminList' 已保留給宣告式（ext-v2）列表定義，會要求 columns[] 等區塊；
        // 一般新格式列表頁請用 'list'。
        view: 'list'
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
