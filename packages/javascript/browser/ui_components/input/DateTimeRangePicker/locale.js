/**
 * DateTimeRangePicker 語系字串（自行註冊到 Locale 的 dateTimeRangePicker 命名空間）。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'dateTimeRangePicker', {
    groupLabel: '日期時間範圍',
    startDate: '開始日期',
    startTime: '開始時間',
    endDate: '結束日期',
    endTime: '結束時間',
    separator: '至',
    clear: '清除',
    units: {
        day: '{n} 天',
        hour: '{n} 小時',
        minute: '{n} 分鐘'
    },
    errors: {
        required: '請選擇日期時間範圍',
        startRequired: '請選擇開始日期與時間',
        endRequired: '請選擇結束日期與時間',
        startDateRequired: '請選擇開始日期',
        startTimeRequired: '請選擇開始時間',
        endDateRequired: '請選擇結束日期',
        endTimeRequired: '請選擇結束時間',
        order: '結束必須晚於開始',
        beforeMin: '不可早於 {min}',
        afterMax: '不可晚於 {max}',
        maxSpan: '範圍不可超過 {duration}'
    }
});

Locale.register('en', 'dateTimeRangePicker', {
    groupLabel: 'Date and time range',
    startDate: 'Start date',
    startTime: 'Start time',
    endDate: 'End date',
    endTime: 'End time',
    separator: 'to',
    clear: 'Clear',
    units: {
        day: '{n} d',
        hour: '{n} h',
        minute: '{n} min'
    },
    errors: {
        required: 'Please select a date and time range',
        startRequired: 'Please select the start date and time',
        endRequired: 'Please select the end date and time',
        startDateRequired: 'Please select the start date',
        startTimeRequired: 'Please select the start time',
        endDateRequired: 'Please select the end date',
        endTimeRequired: 'Please select the end time',
        order: 'The end must be after the start',
        beforeMin: 'Cannot be earlier than {min}',
        afterMax: 'Cannot be later than {max}',
        maxSpan: 'The range cannot exceed {duration}'
    }
});
