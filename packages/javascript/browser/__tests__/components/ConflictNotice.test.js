import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { ConflictNotice } from '../../ui_components/common/ConflictNotice/index.js';
import ConflictNoticeDefault from '../../ui_components/common/ConflictNotice/ConflictNotice.js';

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

const actionButtons = (root) => [...root.querySelectorAll('.cl-conflict-notice__actions button')];
const button = (root, action) => root.querySelector(`button[data-action="${action}"]`);
const cells = (row) => [...row.children].map((cell) => cell.textContent);

function escape(target) {
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
}

function track(promise) {
    const state = { settled: false, value: undefined };
    promise.then((value) => {
        state.settled = true;
        state.value = value;
    });
    return state;
}

function openDialog() {
    const backdrop = document.querySelector('.modal-backdrop');
    const dialog = backdrop?.querySelector('.panel');
    return { backdrop, dialog, root: dialog?.querySelector('.cl-conflict-notice') };
}

describe('ConflictNotice', () => {
    let host;

    beforeEach(() => {
        Locale.setLang('zh-TW');
        host = document.createElement('div');
        host.id = 'conflict-host';
        document.body.appendChild(host);
    });

    afterEach(async () => {
        host.remove();
        await tick();
        document.querySelectorAll('.modal-backdrop').forEach((node) => node.remove());
        document.body.style.overflow = '';
        Locale.setLang('zh-TW');
        vi.restoreAllMocks();
    });

    describe('inline variant defaults', () => {
        it('renders an alert region with Locale title, message and the default actions', () => {
            expect(ConflictNoticeDefault).toBe(ConflictNotice);
            expect(ConflictNotice.ACTIONS).toEqual({ RELOAD: 'reload', OVERWRITE: 'overwrite', CANCEL: 'cancel' });

            const notice = new ConflictNotice().mount('#conflict-host');
            const root = notice.element;
            expect(host.contains(root)).toBe(true);
            expect(root.getAttribute('role')).toBe('alert');
            expect(root.dataset.variant).toBe('inline');

            const title = root.querySelector('.cl-conflict-notice__title');
            expect(title.textContent).toBe('資料已被其他人更新');
            expect(root.getAttribute('aria-labelledby')).toBe(title.id);
            const message = root.querySelector('.cl-conflict-notice__message');
            expect(message.textContent).toBe(Locale.t('conflictNotice.message'));
            expect(root.getAttribute('aria-describedby')).toBe(message.id);

            const buttons = actionButtons(root);
            expect(buttons.map((item) => item.dataset.action)).toEqual(['reload', 'overwrite', 'cancel']);
            expect(buttons.map((item) => item.textContent)).toEqual(['載入最新資料', '以我的版本覆寫', '取消']);
            expect(buttons.every((item) => item.type === 'button')).toBe(true);
            expect(root.querySelector('.cl-conflict-notice__actions').getAttribute('role')).toBe('group');

            expect(button(root, 'overwrite').style.cssText).toContain('var(--cl-danger)');
            expect(button(root, 'reload').style.cssText).toContain('var(--cl-primary)');
            expect(root.querySelector('.cl-conflict-notice__confirm').hidden).toBe(true);
            expect(root.querySelector('table')).toBeNull();
            expect(root.querySelector('.cl-conflict-notice__meta')).toBeNull();
            expect(notice.snapshot()).toEqual({ lifecycle: 'mounted', step: 'choose', resolved: null });
            notice.destroy();
        });

        it('honours title/message options, labels overrides, action subsets and danger: false', () => {
            const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
            const notice = new ConflictNotice({
                title: 'Booking changed',
                message: '',
                actions: ['cancel', 'overwrite', 'delete', 'cancel'],
                danger: false,
                labels: { overwrite: 'Keep mine' }
            }).mount(host);
            const root = notice.element;
            expect(root.querySelector('.cl-conflict-notice__title').textContent).toBe('Booking changed');
            expect(root.querySelector('.cl-conflict-notice__message')).toBeNull();
            expect(root.hasAttribute('aria-describedby')).toBe(false);
            expect(actionButtons(root).map((item) => item.dataset.action)).toEqual(['cancel', 'overwrite']);
            expect(button(root, 'overwrite').textContent).toBe('Keep mine');
            expect(button(root, 'overwrite').style.cssText).not.toContain('var(--cl-danger)');
            expect(warn).toHaveBeenCalledTimes(1);

            const fallback = new ConflictNotice({ actions: [] }).mount(host);
            expect(actionButtons(fallback.element).map((item) => item.dataset.action)).toEqual(['reload', 'overwrite', 'cancel']);
            notice.destroy();
            fallback.destroy();
        });

        it('warns when the mount target is missing', () => {
            const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
            const notice = new ConflictNotice();
            expect(notice.mount('#does-not-exist')).toBe(notice);
            expect(warn).toHaveBeenCalled();
            expect(notice.snapshot().lifecycle).toBe('created');
            notice.destroy();
        });
    });

    describe('diffs and metadata', () => {
        it('renders a three-column text table; HTML in values stays inert', () => {
            const payload = '<img src="x" onerror="window.__conflictXss = true">';
            const notice = new ConflictNotice({
                diffs: [
                    { field: 'title', label: 'Title', local: 'Quarterly review', server: 'Quarterly planning' },
                    { field: 'room', label: 'Room', local: null, server: 'Room B' },
                    { field: 'remote', label: 'Remote', local: true, server: false },
                    { field: 'notes', local: payload, server: undefined },
                    { field: 'tags', label: 'Tags', local: ['design', 'review'], server: [] },
                    { field: 'meta', label: '<b>Meta</b>', local: { owner: 'Sam' }, server: '' },
                    null,
                    'bad row'
                ]
            }).mount(host);
            const table = notice.element.querySelector('table');
            expect(table.querySelector('caption').textContent).toBe('您的內容與目前資料的差異');
            const headers = [...table.querySelectorAll('thead th')];
            expect(headers.map((cell) => cell.textContent)).toEqual(['欄位', '您的值', '目前的值']);
            expect(headers.every((cell) => cell.getAttribute('scope') === 'col')).toBe(true);

            const rows = [...table.querySelectorAll('tbody tr')];
            expect(rows).toHaveLength(6);
            expect(rows.every((row) => row.firstElementChild.tagName === 'TH' && row.firstElementChild.getAttribute('scope') === 'row')).toBe(true);
            expect(cells(rows[0])).toEqual(['Title', 'Quarterly review', 'Quarterly planning']);
            expect(cells(rows[1])).toEqual(['Room', '—', 'Room B']);
            expect(cells(rows[2])).toEqual(['Remote', '是', '否']);
            expect(cells(rows[3])).toEqual(['notes', payload, '—']);
            expect(cells(rows[4])).toEqual(['Tags', 'design, review', '—']);
            expect(cells(rows[5])).toEqual(['<b>Meta</b>', '{"owner":"Sam"}', '—']);
            expect(rows[0].dataset.field).toBe('title');

            expect(table.querySelector('img')).toBeNull();
            expect(table.querySelector('b')).toBeNull();
            expect(window.__conflictXss).toBeUndefined();
            notice.destroy();
        });

        it('formats dates with Intl and supports a custom formatValue', () => {
            const error = vi.spyOn(console, 'error').mockImplementation(() => {});
            const due = new Date('2026-04-05T06:07:00Z');
            const expected = new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(due);
            const labels = { 1: 'Open', 2: 'Closed' };
            const notice = new ConflictNotice({
                diffs: [
                    { field: 'due', local: due, server: new Date('invalid') },
                    { field: 'status', local: 1, server: 2 },
                    { field: 'count', local: 3, server: 4 },
                    { field: 'owner', local: 'Sam', server: 'Alex' }
                ],
                formatValue: (value, { diff, side }) => {
                    if (diff.field === 'status') return labels[value];
                    if (diff.field === 'count') return side === 'server' ? null : undefined;
                    if (diff.field === 'owner') throw new Error('formatter bug');
                    return undefined;
                }
            }).mount(host);
            const rows = [...notice.element.querySelectorAll('tbody tr')];
            expect(cells(rows[0])).toEqual(['due', expected, '—']);
            expect(cells(rows[1])).toEqual(['status', 'Open', 'Closed']);
            expect(cells(rows[2])).toEqual(['count', '3', '—']);
            expect(cells(rows[3])).toEqual(['owner', 'Sam', 'Alex']);
            expect(error).toHaveBeenCalled();
            notice.destroy();
        });

        it('shows who changed the record and when, as plain text', () => {
            const at = new Date('2026-03-04T05:06:07Z');
            const expected = new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(at);
            const both = new ConflictNotice({ serverUpdatedBy: 'Morgan <ops>', serverUpdatedAt: at }).mount(host);
            const meta = both.element.querySelector('.cl-conflict-notice__meta');
            expect(meta.textContent).toBe(`最後由 Morgan <ops> 於 ${expected} 更新`);
            expect(meta.children).toHaveLength(0);

            const userOnly = new ConflictNotice({ serverUpdatedBy: 'Morgan' }).mount(host);
            expect(userOnly.element.querySelector('.cl-conflict-notice__meta').textContent).toBe('最後由 Morgan 更新');

            const iso = '2026-03-04T05:06:07Z';
            const timeOnly = new ConflictNotice({ serverUpdatedAt: iso, dateTimeFormat: { year: 'numeric' } }).mount(host);
            const year = new Intl.DateTimeFormat('zh-TW', { year: 'numeric' }).format(new Date(iso));
            expect(timeOnly.element.querySelector('.cl-conflict-notice__meta').textContent).toBe(`最後更新於 ${year}`);

            const raw = new ConflictNotice({ serverUpdatedAt: 'shortly before noon' }).mount(host);
            expect(raw.element.querySelector('.cl-conflict-notice__meta').textContent).toBe('最後更新於 shortly before noon');
            [both, userOnly, timeOnly, raw].forEach((notice) => notice.destroy());
        });
    });

    describe('inline resolution', () => {
        it('calls onResolve once, resolves result and disables the buttons', async () => {
            const onResolve = vi.fn();
            const notice = new ConflictNotice({ onResolve }).mount(host);
            button(notice.element, 'reload').click();
            expect(onResolve).toHaveBeenCalledWith('reload');
            await expect(notice.result).resolves.toBe('reload');
            expect(notice.snapshot()).toMatchObject({ step: 'done', resolved: 'reload' });
            expect(actionButtons(notice.element).every((item) => item.disabled)).toBe(true);
            button(notice.element, 'cancel').click();
            expect(onResolve).toHaveBeenCalledTimes(1);
            notice.destroy();
        });

        it('asks for a second confirmation before overwrite, with keyboard and focus handling', () => {
            const onResolve = vi.fn();
            const notice = new ConflictNotice({ onResolve }).mount(host);
            const root = notice.element;
            notice.focus();
            expect(document.activeElement).toBe(button(root, 'reload'));

            button(root, 'overwrite').click();
            expect(onResolve).not.toHaveBeenCalled();
            expect(notice.snapshot().step).toBe('confirm');
            const confirmBox = root.querySelector('.cl-conflict-notice__confirm');
            expect(confirmBox.hidden).toBe(false);
            expect(confirmBox.style.display).toBe('flex');
            expect(root.querySelector('.cl-conflict-notice__actions').hidden).toBe(true);
            expect(document.getElementById(confirmBox.getAttribute('aria-labelledby')).textContent)
                .toBe(Locale.t('conflictNotice.confirmOverwriteMessage'));
            expect(document.activeElement).toBe(button(root, 'back'));
            notice.focus();
            expect(document.activeElement).toBe(button(root, 'back'));

            const esc = escape(document.activeElement);
            expect(esc.defaultPrevented).toBe(true);
            expect(notice.snapshot().step).toBe('choose');
            expect(confirmBox.hidden).toBe(true);
            expect(document.activeElement).toBe(button(root, 'overwrite'));
            expect(escape(document.activeElement).defaultPrevented).toBe(false);

            button(root, 'overwrite').click();
            button(root, 'back').click();
            expect(notice.snapshot().step).toBe('choose');
            button(root, 'overwrite').click();
            expect(button(root, 'confirm-overwrite').style.cssText).toContain('var(--cl-danger)');
            button(root, 'confirm-overwrite').click();
            expect(onResolve).toHaveBeenCalledTimes(1);
            expect(onResolve).toHaveBeenCalledWith('overwrite');
            notice.destroy();
        });

        it('resolves overwrite directly when confirmOverwrite is false', () => {
            const onResolve = vi.fn();
            const notice = new ConflictNotice({ onResolve, confirmOverwrite: false }).mount(host);
            expect(notice.element.querySelector('.cl-conflict-notice__confirm')).toBeNull();
            button(notice.element, 'overwrite').click();
            expect(onResolve).toHaveBeenCalledWith('overwrite');
            notice.destroy();
        });

        it('isolates onResolve failures', async () => {
            const error = vi.spyOn(console, 'error').mockImplementation(() => {});
            const notice = new ConflictNotice({ onResolve: () => { throw new Error('handler bug'); } }).mount(host);
            button(notice.element, 'cancel').click();
            await expect(notice.result).resolves.toBe('cancel');
            expect(error).toHaveBeenCalled();
            notice.destroy();
        });
    });

    describe('ConflictNotice.show()', () => {
        it('opens an alertdialog with focus on the safest action and resolves the chosen action', async () => {
            const docAdd = vi.spyOn(document, 'addEventListener');
            const docRemove = vi.spyOn(document, 'removeEventListener');
            const trigger = document.createElement('button');
            host.appendChild(trigger);
            trigger.focus();
            const onResolve = vi.fn();

            const promise = ConflictNotice.show({
                diffs: [{ field: 'title', label: 'Title', local: 'A', server: 'B' }],
                serverUpdatedBy: 'Morgan',
                onResolve
            });
            const { backdrop, dialog, root } = openDialog();
            expect(backdrop).not.toBeNull();
            expect(dialog.getAttribute('role')).toBe('alertdialog');
            expect(dialog.getAttribute('aria-modal')).toBe('true');
            expect(document.getElementById(dialog.getAttribute('aria-labelledby')).textContent).toBe('資料已被其他人更新');
            expect(document.getElementById(dialog.getAttribute('aria-describedby')).textContent).toBe(Locale.t('conflictNotice.message'));
            expect(root.dataset.variant).toBe('modal');
            expect(root.hasAttribute('role')).toBe(false);
            expect(root.querySelector('.cl-conflict-notice__title')).toBeNull();
            expect(root.querySelectorAll('tbody tr')).toHaveLength(1);
            expect(document.activeElement).toBe(button(root, 'reload'));

            button(root, 'reload').click();
            await expect(promise).resolves.toBe('reload');
            expect(onResolve).toHaveBeenCalledWith('reload');
            await tick();
            expect(document.querySelector('.modal-backdrop')).toBeNull();
            expect(document.activeElement).toBe(trigger);
            const keydownDelta = docAdd.mock.calls.filter(([type]) => type === 'keydown').length
                - docRemove.mock.calls.filter(([type]) => type === 'keydown').length;
            expect(keydownDelta).toBe(0);
        });

        it('resolves cancel for the cancel button, Escape and the close button', async () => {
            const viaButton = ConflictNotice.show();
            button(openDialog().root, 'cancel').click();
            await expect(viaButton).resolves.toBe('cancel');
            await tick();

            const onResolve = vi.fn();
            const viaEscape = ConflictNotice.show({ actions: ['reload', 'overwrite'], onResolve });
            escape(document.activeElement);
            await expect(viaEscape).resolves.toBe('cancel');
            expect(onResolve).toHaveBeenCalledWith('cancel');
            await tick();

            const viaClose = ConflictNotice.show();
            openDialog().dialog.querySelector('.panel__close').click();
            await expect(viaClose).resolves.toBe('cancel');
            await tick();
            expect(document.querySelector('.modal-backdrop')).toBeNull();
        });

        it('requires the second confirmation for overwrite; Escape there only steps back', async () => {
            const promise = ConflictNotice.show({ diffs: [{ field: 'room', local: 'A', server: 'B' }] });
            const state = track(promise);
            const { root } = openDialog();

            button(root, 'overwrite').click();
            await tick();
            expect(state.settled).toBe(false);
            expect(document.activeElement).toBe(button(root, 'back'));

            const esc = escape(document.activeElement);
            expect(esc.defaultPrevented).toBe(true);
            await tick();
            expect(state.settled).toBe(false);
            expect(document.querySelector('.modal-backdrop')).not.toBeNull();
            expect(document.activeElement).toBe(button(root, 'overwrite'));

            button(root, 'overwrite').click();
            button(root, 'confirm-overwrite').click();
            await expect(promise).resolves.toBe('overwrite');
        });

        it('resolves overwrite immediately without the confirmation step', async () => {
            const promise = ConflictNotice.show({ confirmOverwrite: false });
            button(openDialog().root, 'overwrite').click();
            await expect(promise).resolves.toBe('overwrite');
        });

        it('keeps Tab focus inside the dialog', async () => {
            const promise = ConflictNotice.show();
            const { dialog, root } = openDialog();
            const cancel = button(root, 'cancel');
            cancel.focus();
            const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
            cancel.dispatchEvent(tab);
            expect(tab.defaultPrevented).toBe(true);
            expect(document.activeElement).toBe(dialog.querySelector('.panel__close'));

            const back = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true });
            document.activeElement.dispatchEvent(back);
            expect(document.activeElement).toBe(cancel);
            cancel.click();
            await promise;
        });

        it('uses the active Locale for the dialog', async () => {
            Locale.setLang('en');
            const promise = ConflictNotice.show();
            const { dialog, root } = openDialog();
            expect(dialog.querySelector('.panel__title').textContent).toBe('This record was changed by someone else');
            expect(actionButtons(root).map((item) => item.textContent)).toEqual(['Load latest', 'Overwrite with mine', 'Cancel']);
            escape(document.activeElement);
            await expect(promise).resolves.toBe('cancel');
        });
    });

    describe('destroy()', () => {
        it('removes the DOM and its own listeners, adds no document/window listeners and is idempotent', async () => {
            const add = vi.spyOn(EventTarget.prototype, 'addEventListener');
            const remove = vi.spyOn(EventTarget.prototype, 'removeEventListener');
            const notice = new ConflictNotice({ diffs: [{ field: 'a', local: 1, server: 2 }] }).mount(host);
            const root = notice.element;
            notice.destroy();

            const typesFor = (spy, target) => spy.mock.calls
                .filter((_, index) => spy.mock.contexts[index] === target)
                .map(([type]) => type)
                .sort();
            expect(typesFor(add, root)).toEqual(['click', 'keydown']);
            expect(typesFor(remove, root)).toEqual(['click', 'keydown']);
            expect(add.mock.contexts.some((context) => context === document || context === window)).toBe(false);

            expect(host.children).toHaveLength(0);
            expect(notice.element).toBeNull();
            expect(notice.snapshot().lifecycle).toBe('destroyed');
            await expect(notice.result).resolves.toBe('cancel');
            expect(() => notice.destroy()).not.toThrow();
            expect(notice.mount(host)).toBe(notice);
            expect(notice.focus()).toBe(notice);
            expect(host.children).toHaveLength(0);
        });
    });

    describe('Locale', () => {
        it('renders English strings after switching language', () => {
            Locale.setLang('en');
            const notice = new ConflictNotice({ diffs: [{ field: 'remote', local: null, server: true }] }).mount(host);
            const root = notice.element;
            expect(root.querySelector('.cl-conflict-notice__title').textContent).toBe('This record was changed by someone else');
            expect(actionButtons(root).map((item) => item.textContent)).toEqual(['Load latest', 'Overwrite with mine', 'Cancel']);
            expect([...root.querySelectorAll('thead th')].map((cell) => cell.textContent)).toEqual(['Field', 'Your value', 'Current value']);
            expect(cells(root.querySelector('tbody tr'))).toEqual(['remote', '—', 'Yes']);
            button(root, 'overwrite').click();
            expect(button(root, 'confirm-overwrite').textContent).toBe('Overwrite anyway');
            expect(button(root, 'back').textContent).toBe('Back');
            notice.destroy();
        });
    });
});
