/**
 * ImportWizard 元件字串（自行註冊到 Locale 的 importWizard 命名空間）。
 * ImportWizard.js 以副作用方式 import 本檔。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'importWizard', {
    label: '資料匯入',
    stepStatus: '第 {current} 步，共 {total} 步：{title}',
    steps: {
        upload: '上傳檔案',
        mapping: '欄位對應',
        preview: '預覽與驗證',
        confirm: '確認匯入'
    },
    buttons: {
        back: '上一步',
        next: '下一步',
        cancel: '取消'
    },
    upload: {
        heading: '選擇要匯入的檔案',
        dropHint: '將檔案拖放到這裡，或選擇檔案。',
        fileLabel: '選擇檔案',
        acceptHint: '支援格式：{accept}；大小上限 {size}',
        pasteLabel: '或直接貼上資料（第一列為標題）',
        pasteLabelNoHeader: '或直接貼上資料',
        pastePlaceholder: '把 CSV 或以 Tab 分隔的資料貼在這裡',
        usePaste: '使用貼上的資料',
        pastedName: '貼上的資料',
        reading: '讀取中…',
        loaded: '已讀取「{name}」：{rows} 列、{columns} 欄'
    },
    mapping: {
        heading: '設定欄位對應',
        hint: '為每個目標欄位選擇來源欄位；標示 * 的欄位必須對應。',
        notMapped: '（不匯入）',
        column: '第 {index} 欄',
        columnWithIndex: '{header}（第 {index} 欄）',
        fieldLabel: '{field} 的來源欄位',
        required: '必填',
        requiredMark: '*',
        sample: '範例：{value}',
        missingRequired: '以下必填欄位尚未對應：{fields}',
        fieldSeparator: '、'
    },
    preview: {
        heading: '預覽與驗證',
        summary: '共 {total} 列：有效 {valid} 列，無效 {invalid} 列',
        caption: '預覽前 {count} 列',
        rowHeader: '列',
        errorsHeading: '錯誤清單',
        errorItem: '第 {row} 列，{field}：{message}',
        rowLevel: '整列',
        moreErrors: '另有 {count} 項錯誤未列出',
        noErrors: '所有資料列都通過驗證。',
        blocked: '仍有 {count} 列資料無效，請修正檔案後重新上傳。',
        nothingToImport: '沒有可匯入的資料列。'
    },
    confirm: {
        heading: '確認匯入',
        summary: '即將匯入 {valid} 列資料。',
        skipped: '將略過 {invalid} 列無效資料。',
        start: '開始匯入',
        busy: '匯入中，請稍候…',
        success: '匯入完成：成功 {imported} 列，失敗 {failed} 列。',
        failure: '匯入失敗，請稍後再試。',
        failureDetail: '匯入失敗：{detail}',
        retry: '重試',
        startOver: '重新開始'
    },
    errors: {
        fileType: '不支援的檔案類型：{name}',
        fileSize: '檔案過大（{size}），上限為 {max}',
        readFailed: '無法讀取檔案。',
        encoding: '不支援的文字編碼：{encoding}',
        decodeWarning: '有部分字元無法以 {encoding} 解碼，請確認檔案的編碼。',
        empty: '沒有任何資料。',
        noDataRows: '只有標題列，沒有資料列。',
        tooManyRows: '資料列數超過上限 {max} 列。',
        headerInvalid: '標題列的引號格式錯誤（第 {line} 行）。',
        pasteEmpty: '請先貼上資料。',
        required: '必填',
        number: '不是有效的數字',
        date: '日期格式應為 YYYY-MM-DD',
        boolean: '不是有效的是／否值',
        columnCount: '欄位數不符（應為 {expected} 欄，實際 {actual} 欄）',
        unterminatedQuote: '引號未關閉',
        malformedQuote: '引號格式錯誤',
        validateFailed: '驗證失敗',
        transformFailed: '轉換失敗'
    },
    size: {
        bytes: '{value} B',
        kilobytes: '{value} KB',
        megabytes: '{value} MB'
    }
});

Locale.register('en', 'importWizard', {
    label: 'Data import',
    stepStatus: 'Step {current} of {total}: {title}',
    steps: {
        upload: 'Upload',
        mapping: 'Map columns',
        preview: 'Preview & validate',
        confirm: 'Confirm'
    },
    buttons: {
        back: 'Back',
        next: 'Next',
        cancel: 'Cancel'
    },
    upload: {
        heading: 'Choose a file to import',
        dropHint: 'Drop a file here, or choose one.',
        fileLabel: 'Choose file',
        acceptHint: 'Supported: {accept}; up to {size}',
        pasteLabel: 'Or paste data (first row is the header)',
        pasteLabelNoHeader: 'Or paste data',
        pastePlaceholder: 'Paste CSV or tab-separated data here',
        usePaste: 'Use pasted data',
        pastedName: 'Pasted data',
        reading: 'Reading…',
        loaded: 'Read "{name}": {rows} rows, {columns} columns'
    },
    mapping: {
        heading: 'Map columns',
        hint: 'Choose a source column for each target field; fields marked * are required.',
        notMapped: '(Do not import)',
        column: 'Column {index}',
        columnWithIndex: '{header} (column {index})',
        fieldLabel: 'Source column for {field}',
        required: 'Required',
        requiredMark: '*',
        sample: 'Sample: {value}',
        missingRequired: 'Map these required fields first: {fields}',
        fieldSeparator: ', '
    },
    preview: {
        heading: 'Preview & validate',
        summary: '{total} rows: {valid} valid, {invalid} invalid',
        caption: 'First {count} rows',
        rowHeader: 'Row',
        errorsHeading: 'Errors',
        errorItem: 'Row {row}, {field}: {message}',
        rowLevel: 'whole row',
        moreErrors: '{count} more errors not listed',
        noErrors: 'All rows passed validation.',
        blocked: '{count} rows are invalid. Fix the file and upload it again.',
        nothingToImport: 'There are no rows to import.'
    },
    confirm: {
        heading: 'Confirm import',
        summary: '{valid} rows will be imported.',
        skipped: '{invalid} invalid rows will be skipped.',
        start: 'Start import',
        busy: 'Importing, please wait…',
        success: 'Import finished: {imported} imported, {failed} failed.',
        failure: 'The import failed. Please try again later.',
        failureDetail: 'The import failed: {detail}',
        retry: 'Retry',
        startOver: 'Start over'
    },
    errors: {
        fileType: 'Unsupported file type: {name}',
        fileSize: 'The file is too large ({size}); the limit is {max}',
        readFailed: 'The file could not be read.',
        encoding: 'Unsupported text encoding: {encoding}',
        decodeWarning: 'Some characters could not be decoded as {encoding}. Check the file encoding.',
        empty: 'There is no data.',
        noDataRows: 'There is a header row but no data rows.',
        tooManyRows: 'More than {max} data rows.',
        headerInvalid: 'The header row has a quoting error (line {line}).',
        pasteEmpty: 'Paste some data first.',
        required: 'required',
        number: 'not a valid number',
        date: 'dates must look like YYYY-MM-DD',
        boolean: 'not a valid yes/no value',
        columnCount: 'wrong number of columns (expected {expected}, got {actual})',
        unterminatedQuote: 'unclosed quote',
        malformedQuote: 'malformed quote',
        validateFailed: 'validation failed',
        transformFailed: 'conversion failed'
    },
    size: {
        bytes: '{value} B',
        kilobytes: '{value} KB',
        megabytes: '{value} MB'
    }
});
