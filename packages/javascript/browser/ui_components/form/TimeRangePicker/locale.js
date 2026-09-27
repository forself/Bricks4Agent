/**
 * TimeRangePicker 語系字串（自行註冊到 Locale 的 timeRangePicker 命名空間）。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'timeRangePicker', {
    groupLabel: '時間範圍',
    startPlaceholder: '開始時間',
    endPlaceholder: '結束時間',
    separator: '至',
    clear: '清除',
    nextDay: '隔日',
    nextDayLabel: '{label}（隔日）',
    unavailable: '此時間不在可選範圍內',
    units: {
        day: '{n} 天',
        hour: '{n} 小時',
        minute: '{n} 分鐘'
    },
    errors: {
        required: '請選擇時間範圍',
        startRequired: '請選擇開始時間',
        endRequired: '請選擇結束時間',
        order: '結束時間必須晚於開始時間',
        sameTime: '結束時間不可與開始時間相同',
        minDuration: '時間長度不可少於 {duration}',
        maxDuration: '時間長度不可超過 {duration}'
    }
});

Locale.register('en', 'timeRangePicker', {
    groupLabel: 'Time range',
    startPlaceholder: 'Start time',
    endPlaceholder: 'End time',
    separator: 'to',
    clear: 'Clear',
    nextDay: 'next day',
    nextDayLabel: '{label} (next day)',
    unavailable: 'This time is not available',
    units: {
        day: '{n} d',
        hour: '{n} h',
        minute: '{n} min'
    },
    errors: {
        required: 'Please select a time range',
        startRequired: 'Please select a start time',
        endRequired: 'Please select an end time',
        order: 'The end time must be after the start time',
        sameTime: 'The end time cannot be the same as the start time',
        minDuration: 'The duration must be at least {duration}',
        maxDuration: 'The duration cannot exceed {duration}'
    }
});
