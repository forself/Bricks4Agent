/**
 * DateRangePicker 語系字串（自行註冊到 Locale 的 dateRangePicker 命名空間）。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'dateRangePicker', {
    groupLabel: '日期範圍',
    startPlaceholder: '開始日期',
    endPlaceholder: '結束日期',
    separator: '至',
    clear: '清除',
    presetsLabel: '快速選擇',
    errors: {
        required: '請選擇日期範圍',
        startRequired: '請選擇開始日期',
        endRequired: '請選擇結束日期',
        order: '結束日期不可早於開始日期',
        sameDay: '結束日期必須晚於開始日期',
        beforeMin: '日期不可早於 {min}',
        afterMax: '日期不可晚於 {max}',
        maxSpan: '日期範圍不可超過 {days} 天'
    }
});

Locale.register('en', 'dateRangePicker', {
    groupLabel: 'Date range',
    startPlaceholder: 'Start date',
    endPlaceholder: 'End date',
    separator: 'to',
    clear: 'Clear',
    presetsLabel: 'Quick select',
    errors: {
        required: 'Please select a date range',
        startRequired: 'Please select a start date',
        endRequired: 'Please select an end date',
        order: 'The end date cannot be before the start date',
        sameDay: 'The end date must be after the start date',
        beforeMin: 'Dates cannot be earlier than {min}',
        afterMax: 'Dates cannot be later than {max}',
        maxSpan: 'The range cannot exceed {days} days'
    }
});
