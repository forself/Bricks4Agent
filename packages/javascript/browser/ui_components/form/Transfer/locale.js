/**
 * Transfer 元件字串（自行註冊到 Locale 的 transfer 命名空間）。
 * Transfer.js 以副作用方式 import 本檔。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'transfer', {
    sourceTitle: '可選項目',
    targetTitle: '已選項目',
    searchPlaceholder: '搜尋',
    searchLabel: '搜尋{title}',
    count: '{checked}/{total}',
    countLabel: '{title}：已勾選 {checked} 項，共 {total} 項',
    selectAll: '全選{title}中顯示的項目',
    moveToTarget: '將勾選的項目移到{title}',
    moveToSource: '將勾選的項目移回{title}',
    moveUp: '將勾選的項目上移',
    moveDown: '將勾選的項目下移',
    empty: '無資料',
    noMatch: '無符合項目',
    maxHint: '最多 {max} 項',
    maxReached: '已達上限，最多可選 {max} 項',
    movedToTarget: '已將 {count} 項移到{title}',
    movedToSource: '已將 {count} 項移回{title}',
    movedUp: '已上移 {count} 項',
    movedDown: '已下移 {count} 項',
    instructions: '方向鍵移動焦點，空白鍵勾選，Shift＋方向鍵延伸勾選，Ctrl＋A 全選，Enter 移動勾選的項目。'
});

Locale.register('en', 'transfer', {
    sourceTitle: 'Available',
    targetTitle: 'Selected',
    searchPlaceholder: 'Search',
    searchLabel: 'Search {title}',
    count: '{checked}/{total}',
    countLabel: '{title}: {checked} of {total} checked',
    selectAll: 'Select all shown items in {title}',
    moveToTarget: 'Move checked items to {title}',
    moveToSource: 'Move checked items back to {title}',
    moveUp: 'Move checked items up',
    moveDown: 'Move checked items down',
    empty: 'No data',
    noMatch: 'No matching items',
    maxHint: 'Max {max}',
    maxReached: 'Limit reached: at most {max} items can be selected',
    movedToTarget: 'Moved {count} item(s) to {title}',
    movedToSource: 'Moved {count} item(s) back to {title}',
    movedUp: 'Moved {count} item(s) up',
    movedDown: 'Moved {count} item(s) down',
    instructions: 'Arrow keys move focus, Space checks, Shift+Arrow extends, Ctrl+A checks all, Enter moves the checked items.'
});
