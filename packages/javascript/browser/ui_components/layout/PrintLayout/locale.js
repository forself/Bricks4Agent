/**
 * PrintLayout 語系字串（自我註冊；PrintLayout.js 以副作用方式匯入）
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'printLayout', {
    print: '列印',
    printedAt: '列印時間：{time}'
});

Locale.register('en', 'printLayout', {
    print: 'Print',
    printedAt: 'Printed: {time}'
});
