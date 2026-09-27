/**
 * TimeGrid 字串（自行註冊到 Locale，命名空間 timeGrid）。
 * TimeGrid.js 以副作用方式 import 本檔；呼叫端可再用 Locale.register 覆寫任何鍵。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'timeGrid', {
    gridLabel: '時段表',
    slotHeader: '時段',
    timeRange: '{start}–{end}',
    untitled: '（未命名）',
    itemLabel: '{title}，{column}，{time}',
    itemLabelWithSubtitle: '{title}（{subtitle}），{column}，{time}',
    ghostLabel: '{label}（預覽）',
    selectionStarted: '開始選取 {column} {time}。按 Shift 加方向鍵延伸，Enter 確認，Escape 取消。',
    selectionUpdated: '已選取 {column} {time}',
    selectionConfirmed: '已確認選取 {column} {time}',
    selectionCancelled: '已取消選取',
    grabbed: '已拿起 {title}。用方向鍵移動，Enter 放下，Escape 取消。',
    moveTarget: '移到 {column} {time}',
    movePending: '正在移動 {title}…',
    moveAccepted: '已將 {title} 移到 {column} {time}',
    moveRejected: '無法移動 {title}，已放回原位',
    moveCancelled: '已取消移動 {title}'
});

Locale.register('en', 'timeGrid', {
    gridLabel: 'Time grid',
    slotHeader: 'Time',
    timeRange: '{start}–{end}',
    untitled: '(untitled)',
    itemLabel: '{title}, {column}, {time}',
    itemLabelWithSubtitle: '{title} ({subtitle}), {column}, {time}',
    ghostLabel: '{label} (preview)',
    selectionStarted: 'Selecting {column} {time}. Press Shift with arrow keys to extend, Enter to confirm, Escape to cancel.',
    selectionUpdated: 'Selected {column} {time}',
    selectionConfirmed: 'Selection confirmed: {column} {time}',
    selectionCancelled: 'Selection cancelled',
    grabbed: 'Picked up {title}. Use arrow keys to move, Enter to drop, Escape to cancel.',
    moveTarget: 'Move to {column} {time}',
    movePending: 'Moving {title}…',
    moveAccepted: 'Moved {title} to {column} {time}',
    moveRejected: 'Could not move {title}; it is back in its original place',
    moveCancelled: 'Cancelled moving {title}'
});
