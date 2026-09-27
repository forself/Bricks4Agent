/**
 * Countdown 語系字串（自我註冊；Countdown.js 以副作用方式匯入）。
 * 命名空間：countdown
 *
 * units：畫面顯示用的短單位；long：給螢幕閱讀器朗讀的完整單位（區分單複數）。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'countdown', {
    completed: '時間到',
    noTarget: '—',
    remaining: '剩餘 {time}',
    separator: ' ',
    units: {
        days: '{value} 天',
        hours: '{value} 小時',
        minutes: '{value} 分',
        seconds: '{value} 秒'
    },
    long: {
        day: '{value} 天',
        days: '{value} 天',
        hour: '{value} 小時',
        hours: '{value} 小時',
        minute: '{value} 分鐘',
        minutes: '{value} 分鐘',
        second: '{value} 秒',
        seconds: '{value} 秒'
    }
});

Locale.register('en', 'countdown', {
    completed: "Time's up",
    noTarget: '—',
    remaining: '{time} remaining',
    separator: ' ',
    units: {
        days: '{value}d',
        hours: '{value}h',
        minutes: '{value}m',
        seconds: '{value}s'
    },
    long: {
        day: '{value} day',
        days: '{value} days',
        hour: '{value} hour',
        hours: '{value} hours',
        minute: '{value} minute',
        minutes: '{value} minutes',
        second: '{value} second',
        seconds: '{value} seconds'
    }
});
