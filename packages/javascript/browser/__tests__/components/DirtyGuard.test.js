import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import createDirtyGuardDefault, { createDirtyGuard, DirtyGuard } from '../../ui_components/utils/dirty-guard.js';
import { TextInput } from '../../ui_components/form/TextInput/TextInput.js';

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

function type(control, value, eventType = 'input') {
    control.value = value;
    control.dispatchEvent(new Event(eventType, { bubbles: true }));
}

function fakeComponent(value) {
    return { value, getValue() { return this.value; } };
}

/** 依 add/remove 呼叫紀錄計算目前仍掛著的監聽數。 */
function activeListeners(addSpy, removeSpy, type, capture) {
    const matches = ([eventType, , options]) => eventType === type
        && (capture === undefined || Boolean(typeof options === 'object' ? options?.capture : options) === capture);
    return addSpy.mock.calls.filter(matches).length - removeSpy.mock.calls.filter(matches).length;
}

function openDialog() {
    const backdrop = document.querySelector('.modal-backdrop');
    const dialog = backdrop?.querySelector('.panel');
    const buttons = dialog ? [...dialog.querySelectorAll('button')] : [];
    const byText = (text) => buttons.find((button) => button.textContent === text);
    return { backdrop, dialog, buttons, byText };
}

describe('DirtyGuard', () => {
    let host;
    let guard;

    beforeEach(() => {
        Locale.setLang('zh-TW');
        host = document.createElement('div');
        document.body.appendChild(host);
        guard = null;
    });

    afterEach(async () => {
        vi.useRealTimers();
        guard?.destroy();
        host.remove();
        await tick();
        document.querySelectorAll('.modal-backdrop').forEach((node) => node.remove());
        document.body.style.overflow = '';
        Locale.setLang('zh-TW');
        vi.restoreAllMocks();
    });

    function buildForm() {
        host.innerHTML = [
            '<form id="meeting">',
            '<input name="title" value="Weekly sync">',
            '<input type="checkbox" name="remote">',
            '<select name="room"><option value="a" selected>Room A</option><option value="b">Room B</option></select>',
            '<textarea name="agenda">Status</textarea>',
            '<button type="submit">Save</button>',
            '<div data-dirty-ignore><input name="filter" value=""></div>',
            '</form>'
        ].join('');
        return host.querySelector('form');
    }

    describe('construction', () => {
        it('exports the factory as default and validates confirm', () => {
            expect(createDirtyGuardDefault).toBe(createDirtyGuard);
            expect(createDirtyGuard()).toBeInstanceOf(DirtyGuard);
            expect(() => createDirtyGuard({ confirm: 'yes' })).toThrow(TypeError);
        });

        it('starts clean and rejects unsupported sources', () => {
            guard = createDirtyGuard();
            expect(guard.isDirty()).toBe(false);
            expect(guard.getDirtyKeys()).toEqual([]);
            expect(() => guard.track(null)).toThrow(TypeError);
            expect(() => guard.track({ value: 1 })).toThrow(TypeError);
            expect(() => guard.track('form')).toThrow(TypeError);
        });
    });

    describe('element sources', () => {
        it('compares a snapshot of form control values on input/change and notices a revert', () => {
            const form = buildForm();
            guard = createDirtyGuard();
            const listener = vi.fn();
            guard.onChange(listener);
            guard.track(form, { key: 'meeting' });
            expect(guard.isDirty()).toBe(false);

            const title = form.querySelector('[name="title"]');
            type(title, 'Planning');
            expect(listener).toHaveBeenLastCalledWith(true, ['meeting']);
            expect(guard.getDirtyKeys()).toEqual(['meeting']);

            type(title, 'Weekly sync');
            expect(listener).toHaveBeenLastCalledWith(false, []);
            expect(guard.isDirty()).toBe(false);

            const remote = form.querySelector('[name="remote"]');
            remote.checked = true;
            remote.dispatchEvent(new Event('change', { bubbles: true }));
            expect(guard.isDirty()).toBe(true);
            remote.checked = false;
            remote.dispatchEvent(new Event('change', { bubbles: true }));

            type(form.querySelector('[name="room"]'), 'b', 'change');
            expect(guard.getDirtyKeys()).toEqual(['meeting']);
            type(form.querySelector('[name="room"]'), 'a', 'change');

            type(form.querySelector('[name="agenda"]'), 'Status and risks');
            expect(guard.isDirty()).toBe(true);
            // 每次切換 dirty/clean 各通知一次：title×2、remote×2、room×2、agenda×1
            expect(listener).toHaveBeenCalledTimes(7);
        });

        it('ignores buttons and controls inside [data-dirty-ignore]', () => {
            const form = buildForm();
            guard = createDirtyGuard();
            guard.track(form);
            type(form.querySelector('[name="filter"]'), 'room');
            form.querySelector('button').value = 'changed';
            expect(guard.isDirty()).toBe(false);
        });

        it('re-checks after clicks (for example an added row) and after form reset', async () => {
            const form = buildForm();
            guard = createDirtyGuard();
            const listener = vi.fn();
            guard.onChange(listener);
            guard.track(form, { key: 'meeting' });

            const addRow = document.createElement('button');
            addRow.type = 'button';
            addRow.addEventListener('click', () => {
                const extra = document.createElement('input');
                extra.name = 'attendee';
                extra.value = 'Alex';
                form.appendChild(extra);
            });
            form.appendChild(addRow);
            addRow.click();
            expect(listener).not.toHaveBeenCalled();
            await tick();
            expect(listener).toHaveBeenLastCalledWith(true, ['meeting']);

            form.querySelector('[name="attendee"]').remove();
            type(form.querySelector('[name="title"]'), 'Changed');
            form.reset();
            await tick();
            expect(listener).toHaveBeenLastCalledWith(false, []);
        });

        it('does not attach document listeners for element-only tracking', () => {
            const docAdd = vi.spyOn(document, 'addEventListener');
            guard = createDirtyGuard();
            guard.track(buildForm());
            expect(docAdd).not.toHaveBeenCalled();
        });
    });

    describe('component and isDirty() sources', () => {
        it('takes the baseline at track time and deep-compares with a stable key order', () => {
            const source = fakeComponent({ title: 'Kickoff', tags: ['a', 'b'], meta: { owner: 'Sam', size: 3 } });
            guard = createDirtyGuard();
            guard.track(source, { key: 'task' });
            expect(guard.isDirty()).toBe(false);

            source.value = { meta: { size: 3, owner: 'Sam' }, tags: ['a', 'b'], title: 'Kickoff', note: undefined };
            expect(guard.isDirty()).toBe(false);

            source.value.tags.reverse();
            expect(guard.isDirty()).toBe(true);
            source.value.tags.reverse();
            expect(guard.isDirty()).toBe(false);

            // 基準是快照：直接改動同一個物件也算變更
            source.value.meta.size = 4;
            expect(guard.getDirtyKeys()).toEqual(['task']);
        });

        it('handles dates, maps, sets, NaN and circular values without throwing', () => {
            const cyclic = { name: 'loop' };
            cyclic.self = cyclic;
            const source = fakeComponent({
                when: new Date('2026-01-02T03:04:05Z'),
                seats: new Map([['b', 2], ['a', 1]]),
                labels: new Set(['x', 'y']),
                ratio: NaN,
                cyclic
            });
            guard = createDirtyGuard();
            guard.track(source);
            source.value = {
                cyclic,
                ratio: NaN,
                labels: new Set(['y', 'x']),
                seats: new Map([['a', 1], ['b', 2]]),
                when: new Date('2026-01-02T03:04:05Z')
            };
            expect(guard.isDirty()).toBe(false);
            source.value = { ...source.value, when: new Date('2026-01-03T00:00:00Z') };
            expect(guard.isDirty()).toBe(true);
        });

        it('detects user edits in a real component through the document-level activity check', async () => {
            const input = new TextInput({ value: 'Room A' }).mount(host);
            guard = createDirtyGuard();
            const listener = vi.fn();
            guard.onChange(listener);
            guard.track(input, { key: 'room' });

            type(input.input, 'Room B');
            expect(listener).not.toHaveBeenCalled();
            await tick();
            expect(listener).toHaveBeenCalledWith(true, ['room']);
            input.destroy();
        });

        it('picks up changes made from a popup rendered outside the component', async () => {
            const picker = fakeComponent('2026-05-01');
            const popupButton = document.createElement('button');
            popupButton.addEventListener('click', (event) => {
                event.stopPropagation();
                picker.value = '2026-05-02';
            });
            document.body.appendChild(popupButton);
            guard = createDirtyGuard();
            const listener = vi.fn();
            guard.onChange(listener);
            guard.track(picker, { key: 'date' });

            popupButton.click();
            await tick();
            expect(listener).toHaveBeenCalledWith(true, ['date']);
            popupButton.remove();
        });

        it('check() picks up programmatic changes immediately', () => {
            const input = new TextInput({ value: 'a' }).mount(host);
            guard = createDirtyGuard();
            guard.track(input, { key: 'field' });
            input.setValue('b');
            expect(guard.check()).toBe(true);
            input.destroy();
        });

        it('lets isDirty() objects own their state and forwards markClean()', () => {
            const editor = { dirty: false, isDirty() { return this.dirty; }, markClean: vi.fn(function markClean() { this.dirty = false; }) };
            guard = createDirtyGuard();
            guard.track(editor, { key: 'editor' });
            expect(guard.isDirty()).toBe(false);
            editor.dirty = true;
            expect(guard.getDirtyKeys()).toEqual(['editor']);
            guard.markClean('editor');
            expect(editor.markClean).toHaveBeenCalledTimes(1);
            expect(guard.isDirty()).toBe(false);
        });

        it('attaches capture listeners on document only while such sources are tracked', () => {
            const docAdd = vi.spyOn(document, 'addEventListener');
            const docRemove = vi.spyOn(document, 'removeEventListener');
            guard = createDirtyGuard();
            const untrackA = guard.track(fakeComponent(1), { key: 'a' });
            const untrackB = guard.track({ isDirty: () => false }, { key: 'b' });
            for (const type of ['input', 'change', 'click', 'keyup', 'pointerup']) {
                expect(activeListeners(docAdd, docRemove, type, true)).toBe(1);
            }
            untrackA();
            expect(activeListeners(docAdd, docRemove, 'click', true)).toBe(1);
            untrackB();
            for (const type of ['input', 'change', 'click', 'keyup', 'pointerup']) {
                expect(activeListeners(docAdd, docRemove, type, true)).toBe(0);
            }
        });
    });

    describe('keys, markClean and onChange', () => {
        it('reports dirty keys in tracking order and re-baselines one or all sources', () => {
            const a = fakeComponent('a');
            const b = fakeComponent('b');
            guard = createDirtyGuard();
            guard.track(a, { key: 'first' });
            guard.track(b, { key: 'second' });
            a.value = 'a2';
            b.value = 'b2';
            expect(guard.getDirtyKeys()).toEqual(['first', 'second']);
            guard.markClean('second');
            expect(guard.getDirtyKeys()).toEqual(['first']);
            b.value = 'b';
            expect(guard.getDirtyKeys()).toEqual(['first', 'second']);
            guard.markClean();
            expect(guard.isDirty()).toBe(false);
            guard.markClean('missing');
            expect(guard.isDirty()).toBe(false);
        });

        it('auto-generates keys, replaces a re-tracked key and untracks', () => {
            guard = createDirtyGuard();
            const source = fakeComponent(1);
            const untrack = guard.track(source);
            source.value = 2;
            expect(guard.getDirtyKeys()).toEqual(['source-1']);

            const form = buildForm();
            const removeSpy = vi.spyOn(form, 'removeEventListener');
            guard.track(form, { key: 'x' });
            guard.track(fakeComponent('z'), { key: 'x' });
            expect(removeSpy.mock.calls.map(([type]) => type).sort()).toEqual(['change', 'click', 'input', 'reset']);

            untrack();
            expect(guard.getDirtyKeys()).toEqual([]);
            expect(() => untrack()).not.toThrow();
        });

        it('isolates listener failures and supports unsubscribe', () => {
            const error = vi.spyOn(console, 'error').mockImplementation(() => {});
            guard = createDirtyGuard();
            const bad = vi.fn(() => { throw new Error('listener bug'); });
            const good = vi.fn();
            guard.onChange(bad);
            const off = guard.onChange(good);
            const source = fakeComponent(1);
            guard.track(source);
            source.value = 2;
            guard.check();
            expect(good).toHaveBeenCalledWith(true, ['source-1']);
            expect(error).toHaveBeenCalled();
            off();
            source.value = 1;
            guard.check();
            expect(good).toHaveBeenCalledTimes(1);
            expect(() => guard.onChange(null)).toThrow(TypeError);
        });
    });

    describe('beforeunload', () => {
        it('attaches only while dirty and prevents unload with a message', () => {
            const winAdd = vi.spyOn(window, 'addEventListener');
            const winRemove = vi.spyOn(window, 'removeEventListener');
            guard = createDirtyGuard();
            const source = fakeComponent('draft');
            guard.track(source);
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(0);

            source.value = 'draft 2';
            guard.check();
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(1);
            guard.check();
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(1);

            const event = new Event('beforeunload', { cancelable: true });
            window.dispatchEvent(event);
            expect(event.defaultPrevented).toBe(true);

            source.value = 'draft';
            guard.check();
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(0);
            const clean = new Event('beforeunload', { cancelable: true });
            window.dispatchEvent(clean);
            expect(clean.defaultPrevented).toBe(false);
        });

        it('never attaches with beforeUnload: false', () => {
            const winAdd = vi.spyOn(window, 'addEventListener');
            guard = createDirtyGuard({ beforeUnload: false });
            const source = fakeComponent(1);
            guard.track(source);
            source.value = 2;
            expect(guard.check()).toBe(true);
            expect(winAdd.mock.calls.filter(([type]) => type === 'beforeunload')).toHaveLength(0);
        });

        it('pauses after the user confirmed leaving until the content changes again', async () => {
            const winAdd = vi.spyOn(window, 'addEventListener');
            const winRemove = vi.spyOn(window, 'removeEventListener');
            guard = createDirtyGuard({ confirm: () => true });
            const source = fakeComponent('a');
            guard.track(source);
            source.value = 'b';
            guard.check();
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(1);

            await expect(guard.confirmLeave()).resolves.toBe(true);
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(0);
            guard.check();
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(0);

            source.value = 'c';
            guard.check();
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(1);
        });
    });

    describe('confirmLeave() and wrap()', () => {
        it('resolves true without asking when clean', async () => {
            const confirm = vi.fn(() => false);
            guard = createDirtyGuard({ confirm });
            guard.track(fakeComponent(1));
            await expect(guard.confirmLeave()).resolves.toBe(true);
            expect(confirm).not.toHaveBeenCalled();
        });

        it('passes the message and dirty keys to an injected confirm and shares one prompt', async () => {
            let answer;
            const confirm = vi.fn(() => new Promise((resolve) => { answer = resolve; }));
            guard = createDirtyGuard({ confirm, message: 'Discard the booking draft?' });
            const source = fakeComponent(1);
            guard.track(source, { key: 'booking' });
            source.value = 2;

            const first = guard.confirmLeave();
            const second = guard.confirmLeave();
            expect(confirm).toHaveBeenCalledTimes(1);
            expect(confirm).toHaveBeenCalledWith('Discard the booking draft?', { dirtyKeys: ['booking'] });
            answer(false);
            await expect(first).resolves.toBe(false);
            await expect(second).resolves.toBe(false);

            confirm.mockImplementation(() => true);
            await expect(guard.confirmLeave()).resolves.toBe(true);
            expect(confirm).toHaveBeenCalledTimes(2);
        });

        it('accepts a message function and treats only true as consent', async () => {
            const confirm = vi.fn(() => 'yes');
            guard = createDirtyGuard({ confirm, message: (keys) => `Unsaved: ${keys.join(', ')}` });
            const source = fakeComponent(1);
            guard.track(source, { key: 'order' });
            source.value = 2;
            await expect(guard.confirmLeave()).resolves.toBe(false);
            expect(confirm).toHaveBeenCalledWith('Unsaved: order', { dirtyKeys: ['order'] });
        });

        it('treats a throwing or rejecting confirm as "stay"', async () => {
            vi.spyOn(console, 'error').mockImplementation(() => {});
            const source = fakeComponent(1);
            guard = createDirtyGuard({ confirm: () => { throw new Error('dialog failed'); } });
            guard.track(source);
            source.value = 2;
            await expect(guard.confirmLeave()).resolves.toBe(false);
            guard.destroy();

            guard = createDirtyGuard({ confirm: () => Promise.reject(new Error('dialog failed')) });
            guard.track(source);
            source.value = 3;
            await expect(guard.confirmLeave()).resolves.toBe(false);
        });

        it('wrap(fn) runs fn only after confirmation and keeps this/arguments/return value', async () => {
            const answers = [false, true];
            guard = createDirtyGuard({ confirm: () => answers.shift() });
            const source = fakeComponent(1);
            guard.track(source);
            const fn = vi.fn(function close(reason) { return `${this.name}:${reason}`; });
            const wrapped = guard.wrap(fn);
            const owner = { name: 'drawer', close: wrapped };

            await expect(owner.close('clean')).resolves.toBe('drawer:clean');
            source.value = 2;
            await expect(owner.close('first')).resolves.toBeUndefined();
            await expect(owner.close('second')).resolves.toBe('drawer:second');
            expect(fn).toHaveBeenCalledTimes(2);
            expect(() => guard.wrap('nope')).toThrow(TypeError);
        });
    });

    describe('default ModalPanel confirm', () => {
        function dirtyGuard() {
            guard = createDirtyGuard();
            const source = fakeComponent('a');
            guard.track(source, { key: 'notes' });
            source.value = 'b';
            return source;
        }

        it('shows ModalPanel.confirm with Locale text, dialog semantics and focus on "stay"; leaving resolves true', async () => {
            const trigger = document.createElement('button');
            host.appendChild(trigger);
            trigger.focus();
            dirtyGuard();

            const result = guard.confirmLeave();
            const { backdrop, dialog, byText } = openDialog();
            expect(backdrop).not.toBeNull();
            expect(dialog.getAttribute('role')).toBe('alertdialog');
            expect(dialog.getAttribute('aria-modal')).toBe('true');
            const title = document.getElementById(dialog.getAttribute('aria-labelledby'));
            expect(title.textContent).toBe('尚未儲存的變更');
            const message = document.getElementById(dialog.getAttribute('aria-describedby'));
            expect(message.textContent).toBe(Locale.t('dirtyGuard.message'));
            const stay = byText('留在此頁');
            const leave = byText('離開');
            expect(document.activeElement).toBe(stay);

            // Tab 在對話框內循環
            leave.focus();
            const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
            leave.dispatchEvent(tab);
            expect(tab.defaultPrevented).toBe(true);
            expect(dialog.contains(document.activeElement)).toBe(true);
            expect(document.activeElement).not.toBe(leave);

            leave.click();
            await expect(result).resolves.toBe(true);
            await tick();
            expect(document.querySelector('.modal-backdrop')).toBeNull();
            expect(document.activeElement).toBe(trigger);
        });

        it('resolves false for the stay button and for Escape', async () => {
            dirtyGuard();
            const first = guard.confirmLeave();
            openDialog().byText('留在此頁').click();
            await expect(first).resolves.toBe(false);
            await tick();
            expect(document.querySelector('.modal-backdrop')).toBeNull();

            const second = guard.confirmLeave();
            expect(document.querySelector('.modal-backdrop')).not.toBeNull();
            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
            await expect(second).resolves.toBe(false);
            await tick();
            expect(document.querySelector('.modal-backdrop')).toBeNull();
        });

        it('switches dialog text with the Locale', async () => {
            Locale.setLang('en');
            dirtyGuard();
            const result = guard.confirmLeave();
            const { dialog, byText } = openDialog();
            expect(dialog.querySelector('.panel__title').textContent).toBe('Unsaved changes');
            expect(byText('Leave')).toBeTruthy();
            const stay = byText('Stay on this page');
            expect(document.activeElement).toBe(stay);
            stay.click();
            await expect(result).resolves.toBe(false);
        });

        it('closes an open dialog and resolves false when destroyed', async () => {
            dirtyGuard();
            const result = guard.confirmLeave();
            expect(document.querySelector('.modal-backdrop')).not.toBeNull();
            guard.destroy();
            await expect(result).resolves.toBe(false);
            await tick();
            expect(document.querySelector('.modal-backdrop')).toBeNull();
        });
    });

    describe('destroy()', () => {
        it('removes element, document, window listeners and timers; later calls do not throw', async () => {
            vi.useFakeTimers();
            const docAdd = vi.spyOn(document, 'addEventListener');
            const docRemove = vi.spyOn(document, 'removeEventListener');
            const winAdd = vi.spyOn(window, 'addEventListener');
            const winRemove = vi.spyOn(window, 'removeEventListener');
            const form = buildForm();
            const formAdd = vi.spyOn(form, 'addEventListener');
            const formRemove = vi.spyOn(form, 'removeEventListener');

            guard = createDirtyGuard();
            const listener = vi.fn();
            guard.onChange(listener);
            guard.track(form, { key: 'form' });
            const source = fakeComponent(1);
            guard.track(source, { key: 'component' });
            source.value = 2;
            guard.check();
            document.body.click();
            expect(vi.getTimerCount()).toBe(1);

            guard.destroy();
            expect(vi.getTimerCount()).toBe(0);
            for (const type of ['input', 'change', 'click', 'keyup', 'pointerup']) {
                expect(activeListeners(docAdd, docRemove, type)).toBe(0);
            }
            expect(activeListeners(winAdd, winRemove, 'beforeunload')).toBe(0);
            expect(formRemove.mock.calls.length).toBe(formAdd.mock.calls.length);
            listener.mockClear();

            expect(() => guard.destroy()).not.toThrow();
            expect(guard.track(form)).toBeTypeOf('function');
            expect(guard.isDirty()).toBe(false);
            expect(guard.getDirtyKeys()).toEqual([]);
            expect(() => guard.markClean()).not.toThrow();
            expect(guard.onChange(() => {})).toBeTypeOf('function');
            await expect(guard.confirmLeave()).resolves.toBe(true);
            type(form.querySelector('[name="title"]'), 'after destroy');
            expect(listener).not.toHaveBeenCalled();
            expect(formAdd.mock.calls.length).toBe(4);
        });
    });
});
