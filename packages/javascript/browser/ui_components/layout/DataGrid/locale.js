/**
 * DataGrid 語系字串（命名空間 dataGrid）。
 * DataGrid.js 以副作用方式匯入本檔完成註冊；呼叫端可再以 Locale.register 覆寫任一鍵值。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'dataGrid', {
    gridLabel: '資料表格',
    rowNumber: '列號',
    empty: '沒有資料',
    modified: '已修改',
    requiredHint: '必填',
    emptyOption: '（空白）',
    editorLabel: '{column}，第 {row} 列',
    cellMessage: '第 {row} 列・{column}：{message}',
    pasted: '已貼上 {rows} 列 × {cols} 欄',
    pasteAdded: '，新增 {count} 列',
    pasteTruncated: '，{count} 列超出表格未貼上',
    copied: '已複製 {rows} 列 × {cols} 欄',
    cleared: '已清除 {count} 個儲存格',
    selectedAll: '已選取全部 {rows} 列 × {cols} 欄',
    errors: {
        required: '此欄為必填',
        number: '請輸入數字',
        min: '不可小於 {min}',
        max: '不可大於 {max}',
        dateMin: '不可早於 {min}',
        dateMax: '不可晚於 {max}',
        maxLength: '不可超過 {maxLength} 個字',
        date: '請輸入有效日期（YYYY-MM-DD）',
        option: '不是可選的項目',
        boolean: '請輸入 TRUE 或 FALSE',
    },
});

Locale.register('en', 'dataGrid', {
    gridLabel: 'Data grid',
    rowNumber: 'Row number',
    empty: 'No rows',
    modified: 'Modified',
    requiredHint: 'required',
    emptyOption: '(blank)',
    editorLabel: '{column}, row {row}',
    cellMessage: 'Row {row}, {column}: {message}',
    pasted: 'Pasted {rows} × {cols} cells',
    pasteAdded: '; added {count} new row(s)',
    pasteTruncated: '; {count} row(s) did not fit',
    copied: 'Copied {rows} × {cols} cells',
    cleared: 'Cleared {count} cell(s)',
    selectedAll: 'Selected all {rows} × {cols} cells',
    errors: {
        required: 'This field is required',
        number: 'Enter a number',
        min: 'Must be at least {min}',
        max: 'Must be at most {max}',
        dateMin: 'Must be on or after {min}',
        dateMax: 'Must be on or before {max}',
        maxLength: 'Must be at most {maxLength} characters',
        date: 'Enter a valid date (YYYY-MM-DD)',
        option: 'Not an available option',
        boolean: 'Enter TRUE or FALSE',
    },
});
