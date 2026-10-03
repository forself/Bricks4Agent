import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import createPermissionGateDefault, { createPermissionGate, PermissionGate } from '../../ui_components/utils/permission-gate.js';
import { TextInput } from '../../ui_components/form/TextInput/TextInput.js';
import { BasicButton } from '../../ui_components/common/BasicButton/BasicButton.js';

function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((res, rej) => {
        resolve = res;
        reject = rej;
    });
    return { promise, resolve, reject };
}

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

function grantedGate(granted, options = {}) {
    const set = new Set(granted);
    const can = vi.fn((capability) => set.has(capability));
    return { gate: createPermissionGate({ can, ...options }), can, set };
}

function makeButton(html = '<button type="button" class="btn" title="Remove item" data-id="7">Remove</button>') {
    const wrapper = document.createElement('div');
    wrapper.innerHTML = html;
    const element = wrapper.firstElementChild;
    return element;
}

describe('PermissionGate', () => {
    let host;

    beforeEach(() => {
        Locale.setLang('zh-TW');
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        host.remove();
        Locale.setLang('zh-TW');
        vi.restoreAllMocks();
    });

    describe('construction and validation', () => {
        it('exposes the factory as the default export and returns PermissionGate instances', () => {
            expect(createPermissionGateDefault).toBe(createPermissionGate);
            expect(createPermissionGate({ can: () => true })).toBeInstanceOf(PermissionGate);
        });

        it('requires a can() function and a known mode', () => {
            expect(() => createPermissionGate({})).toThrow(TypeError);
            expect(() => createPermissionGate({ can: () => true, mode: 'remove' })).toThrow(TypeError);
            const gate = createPermissionGate({ can: () => true });
            expect(() => gate.apply(makeButton(), 'x', { mode: 'fade' })).toThrow(TypeError);
        });

        it('rejects malformed requirements instead of guessing', () => {
            const { gate } = grantedGate([]);
            const target = makeButton();
            for (const bad of ['', '   ', [], ['a'], { anyOf: [] }, { allOf: [] }, { anyOf: 'a' }, { allOf: [1] }, { anyOf: [''] }, null, 42]) {
                expect(() => gate.apply(target, bad)).toThrow(TypeError);
            }
            expect(target.hasAttribute('hidden')).toBe(false);
        });

        it('rejects targets that are neither elements nor usable components', () => {
            const { gate } = grantedGate([]);
            expect(() => gate.apply(null, 'x')).toThrow(TypeError);
            expect(() => gate.apply({}, 'x')).toThrow(TypeError);
            expect(() => gate.apply({ setDisabled() {} }, 'x', { mode: 'hide' })).toThrow(TypeError);
        });
    });

    describe('hide mode on elements', () => {
        it('sets only the hidden attribute when that is enough, and release restores the exact markup', () => {
            const button = makeButton();
            host.appendChild(button);
            const before = button.outerHTML;
            const { gate } = grantedGate([]);

            const handle = gate.apply(button, 'orders.delete');
            expect(handle.allowed).toBe(false);
            expect(button.hasAttribute('hidden')).toBe(true);
            expect(button.hasAttribute('aria-hidden')).toBe(false);
            expect(button.style.display).toBe('');

            handle.release();
            expect(button.outerHTML).toBe(before);
            expect(handle.released).toBe(true);
        });

        it('leaves allowed targets untouched', () => {
            const button = makeButton();
            host.appendChild(button);
            const before = button.outerHTML;
            const { gate, can } = grantedGate(['orders.delete']);
            const handle = gate.apply(button, 'orders.delete');
            expect(handle.allowed).toBe(true);
            expect(button.outerHTML).toBe(before);
            expect(can).toHaveBeenCalledWith('orders.delete', null);
        });

        it('forces display:none and aria-hidden only when an inline display defeats [hidden]', () => {
            const panel = document.createElement('div');
            panel.style.cssText = 'display: flex; gap: 4px;';
            panel.textContent = 'Budget';
            host.appendChild(panel);
            const before = panel.outerHTML;
            const { gate } = grantedGate([]);

            const handle = gate.apply(panel, 'projects.budget.view');
            expect(panel.hasAttribute('hidden')).toBe(true);
            expect(panel.style.getPropertyValue('display')).toBe('none');
            expect(panel.style.getPropertyPriority('display')).toBe('important');
            expect(panel.getAttribute('aria-hidden')).toBe('true');
            expect(getComputedStyle(panel).display).toBe('none');

            handle.release();
            expect(panel.outerHTML).toBe(before);
            expect(panel.style.display).toBe('flex');
        });

        it('keeps an already hidden element hidden after release', () => {
            const note = document.createElement('p');
            note.setAttribute('hidden', 'until-found');
            host.appendChild(note);
            const before = note.outerHTML;
            const { gate } = grantedGate([]);
            const handle = gate.apply(note, 'notes.read');
            expect(note.getAttribute('hidden')).toBe('');
            handle.release();
            expect(note.outerHTML).toBe(before);
        });
    });

    describe('disable mode on elements', () => {
        it('disables native controls with aria-disabled plus a Locale reason, and restores title exactly', () => {
            const button = makeButton();
            host.appendChild(button);
            const before = button.outerHTML;
            const { gate } = grantedGate([], { mode: 'disable' });

            const handle = gate.apply(button, 'orders.delete');
            const reason = Locale.t('permissionGate.deniedReason');
            expect(reason).toBe('您沒有執行此操作的權限');
            expect(button.disabled).toBe(true);
            expect(button.getAttribute('aria-disabled')).toBe('true');
            expect(button.getAttribute('title')).toBe(reason);
            expect(button.getAttribute('aria-description')).toBe(reason);
            expect(button.hasAttribute('hidden')).toBe(false);

            handle.release();
            expect(button.outerHTML).toBe(before);
            expect(button.getAttribute('title')).toBe('Remove item');
            expect(button.disabled).toBe(false);
        });

        it('keeps a pre-existing disabled attribute value on release', () => {
            const button = makeButton('<button type="button" disabled="disabled" aria-disabled="false">Archive</button>');
            host.appendChild(button);
            const before = button.outerHTML;
            const { gate } = grantedGate([]);
            const handle = gate.apply(button, 'projects.archive', { mode: 'disable' });
            expect(button.getAttribute('aria-disabled')).toBe('true');
            handle.release();
            expect(button.outerHTML).toBe(before);
            expect(button.disabled).toBe(true);
        });

        it('blocks click and Enter/Space activation on elements without native disabled', () => {
            const link = makeButton('<a href="#/reports/export" class="action">Export</a>');
            host.appendChild(link);
            const before = link.outerHTML;
            const onClick = vi.fn();
            link.addEventListener('click', onClick);
            const addSpy = vi.spyOn(link, 'addEventListener');
            const removeSpy = vi.spyOn(link, 'removeEventListener');
            const { gate } = grantedGate([]);

            const handle = gate.apply(link, 'reports.export', { mode: 'disable' });
            expect(link.getAttribute('aria-disabled')).toBe('true');
            expect(link.hasAttribute('disabled')).toBe(false);

            const click = new MouseEvent('click', { bubbles: true, cancelable: true });
            link.dispatchEvent(click);
            expect(click.defaultPrevented).toBe(true);
            expect(onClick).not.toHaveBeenCalled();

            const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
            link.dispatchEvent(enter);
            expect(enter.defaultPrevented).toBe(true);
            const space = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
            link.dispatchEvent(space);
            expect(space.defaultPrevented).toBe(true);
            const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
            link.dispatchEvent(tab);
            expect(tab.defaultPrevented).toBe(false);

            handle.release();
            expect(link.outerHTML).toBe(before);
            const added = addSpy.mock.calls.map(([type, fn, capture]) => `${type}:${capture}:${fn.name}`);
            const removed = removeSpy.mock.calls.map(([type, fn, capture]) => `${type}:${capture}:${fn.name}`);
            expect(added).toHaveLength(3);
            expect(removed.sort()).toEqual(added.sort());

            const after = new MouseEvent('click', { bubbles: true, cancelable: true });
            link.dispatchEvent(after);
            expect(onClick).toHaveBeenCalledTimes(1);
        });

        it('uses reason overrides in order: apply reason, gate deniedReason, Locale default', () => {
            const a = makeButton();
            const b = makeButton();
            const c = makeButton();
            host.append(a, b, c);
            const reasonFn = vi.fn((requirement, context) => `${context.role} cannot ${requirement.allOf[0]}`);
            const { gate } = grantedGate([], { mode: 'disable', deniedReason: reasonFn, context: { role: 'viewer' } });

            gate.apply(a, 'tasks.assign', { reason: 'Only project leads can assign tasks' });
            gate.apply(b, 'tasks.assign');
            gate.apply(c, 'tasks.assign', { reason: '' });

            expect(a.getAttribute('title')).toBe('Only project leads can assign tasks');
            expect(b.getAttribute('title')).toBe('viewer cannot tasks.assign');
            expect(reasonFn).toHaveBeenCalledWith(expect.objectContaining({ allOf: ['tasks.assign'] }), { role: 'viewer' });
            // explicit empty reason: no tooltip, original title kept
            expect(c.getAttribute('title')).toBe('Remove item');
            expect(c.hasAttribute('aria-description')).toBe(false);
            expect(c.disabled).toBe(true);
        });

        it('accepts a string deniedReason and a function reason per apply', () => {
            const a = makeButton();
            const b = makeButton();
            host.append(a, b);
            const { gate } = grantedGate([], { mode: 'disable', deniedReason: 'Ask an administrator' });
            gate.apply(a, 'rooms.book');
            gate.apply(b, { anyOf: ['rooms.book', 'rooms.admin'] }, {
                reason: (requirement) => `Needs one of: ${requirement.anyOf.join(' / ')}`
            });
            expect(a.getAttribute('title')).toBe('Ask an administrator');
            expect(b.getAttribute('aria-description')).toBe('Needs one of: rooms.book / rooms.admin');
        });
    });

    describe('capability combinations', () => {
        it('treats capability strings as opaque (no operator parsing)', () => {
            const { gate, can } = grantedGate(['a || b']);
            const handle = gate.apply(makeButton(), 'a || b');
            expect(handle.allowed).toBe(true);
            expect(can).toHaveBeenCalledTimes(1);
            expect(can).toHaveBeenCalledWith('a || b', null);
        });

        it('supports anyOf, allOf and both together', () => {
            const { gate } = grantedGate(['rooms.book', 'rooms.view']);
            expect(gate.apply(makeButton(), { anyOf: ['rooms.admin', 'rooms.book'] }).allowed).toBe(true);
            expect(gate.apply(makeButton(), { anyOf: ['rooms.admin', 'rooms.delete'] }).allowed).toBe(false);
            expect(gate.apply(makeButton(), { allOf: ['rooms.book', 'rooms.view'] }).allowed).toBe(true);
            expect(gate.apply(makeButton(), { allOf: ['rooms.book', 'rooms.admin'] }).allowed).toBe(false);
            expect(gate.apply(makeButton(), { allOf: ['rooms.view'], anyOf: ['rooms.admin', 'rooms.book'] }).allowed).toBe(true);
            expect(gate.apply(makeButton(), { allOf: ['rooms.view'], anyOf: ['rooms.admin'] }).allowed).toBe(false);
        });

        it('allows only a literal true; errors and rejections fail closed', async () => {
            const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
            const results = { yes: 'yes', one: 1, boom: () => { throw new Error('boom'); } };
            const gate = createPermissionGate({
                can: (capability) => {
                    if (capability === 'boom') return results.boom();
                    if (capability === 'reject') return Promise.reject(new Error('offline'));
                    if (capability === 'async-true') return Promise.resolve(true);
                    return results[capability];
                }
            });
            expect(gate.apply(makeButton(), 'yes').allowed).toBe(false);
            expect(gate.apply(makeButton(), 'one').allowed).toBe(false);
            expect(gate.apply(makeButton(), 'boom').allowed).toBe(false);
            const rejected = gate.apply(makeButton(), 'reject');
            const ok = gate.apply(makeButton(), 'async-true');
            await Promise.all([rejected.ready, ok.ready]);
            expect(rejected.allowed).toBe(false);
            expect(ok.allowed).toBe(true);
            expect(warn).toHaveBeenCalled();
        });
    });

    describe('component targets', () => {
        it('uses hide()/show() and restores the original visibility', () => {
            const input = new TextInput({ placeholder: 'Room name' }).mount(host);
            const { gate } = grantedGate([]);
            const handle = gate.apply(input, 'rooms.rename');
            expect(input.snapshot().visibility).toBe('hidden');
            expect(input.element.style.display).toBe('none');
            handle.release();
            expect(input.snapshot().visibility).toBe('visible');
            expect(input.element.style.display).toBe('');
            input.destroy();
        });

        it('does not show a component that was hidden before gating', () => {
            const input = new TextInput({}).mount(host);
            input.hide();
            const { gate } = grantedGate([]);
            gate.apply(input, 'rooms.rename').release();
            expect(input.snapshot().visibility).toBe('hidden');
            input.destroy();
        });

        it('uses setDisabled() and keeps components that were already disabled disabled', () => {
            const enabled = new TextInput({}).mount(host);
            const disabled = new TextInput({ disabled: true }).mount(host);
            const { gate } = grantedGate([], { mode: 'disable' });

            const h1 = gate.apply(enabled, 'rooms.rename');
            const h2 = gate.apply(disabled, 'rooms.rename');
            expect(enabled.snapshot().availability).toBe('disabled');
            expect(enabled.element.getAttribute('title')).toBe(Locale.t('permissionGate.deniedReason'));

            h1.release();
            h2.release();
            expect(enabled.snapshot().availability).toBe('enabled');
            expect(enabled.element.hasAttribute('title')).toBe(false);
            expect(disabled.snapshot().availability).toBe('disabled');
            enabled.destroy();
            disabled.destroy();
        });

        it('falls back to the component element when the method is missing', () => {
            const button = new BasicButton({ type: 'delete' }).mount(host);
            const before = button.element.outerHTML;
            const { gate } = grantedGate([]);
            const hidden = gate.apply(button, 'orders.delete', { mode: 'hide' });
            expect(button.element.hasAttribute('hidden')).toBe(true);
            hidden.release();
            expect(button.element.outerHTML).toBe(before);

            const disabled = gate.apply(button, 'orders.delete', { mode: 'disable' });
            expect(button.options.disabled).toBe(true);
            expect(button.button.disabled).toBe(true);
            disabled.release();
            expect(button.options.disabled).toBe(false);
            expect(button.button.disabled).toBe(false);
            button.destroy();
        });
    });

    describe('scan()', () => {
        it('processes data-permission declarations, including the root, asking can() once per capability', () => {
            host.setAttribute('data-permission', 'workspace.open');
            host.innerHTML = [
                '<button type="button" id="edit" data-permission="tasks.edit">Edit</button>',
                '<button type="button" id="assign" data-permission-any="tasks.assign, tasks.admin" data-permission-mode="disable">Assign</button>',
                '<button type="button" id="close" data-permission-all="tasks.edit,tasks.close" data-permission-mode="disable" data-permission-reason="Needs close rights">Close</button>',
                '<button type="button" id="plain">Plain</button>'
            ].join('');
            const { gate, can } = grantedGate(['workspace.open', 'tasks.edit', 'tasks.admin']);

            const handles = gate.scan(host);
            expect(handles).toHaveLength(4);
            expect(host.hasAttribute('hidden')).toBe(false);
            expect(host.querySelector('#edit').hasAttribute('hidden')).toBe(false);
            expect(host.querySelector('#assign').disabled).toBe(false);
            const close = host.querySelector('#close');
            expect(close.disabled).toBe(true);
            expect(close.getAttribute('title')).toBe('Needs close rights');
            expect(host.querySelector('#plain').outerHTML).toBe('<button type="button" id="plain">Plain</button>');

            const asked = can.mock.calls.map(([capability]) => capability).sort();
            expect(asked).toEqual(['tasks.admin', 'tasks.assign', 'tasks.close', 'tasks.edit', 'workspace.open']);
            host.removeAttribute('data-permission');
        });

        it('reuses handles on re-scan and rebuilds them when the declaration changes', () => {
            host.innerHTML = '<button type="button" data-permission="rooms.book">Book</button>';
            const button = host.firstElementChild;
            const { gate, set } = grantedGate([]);

            const [first] = gate.scan(host);
            expect(button.hasAttribute('hidden')).toBe(true);
            const [again] = gate.scan(host);
            expect(again).toBe(first);

            set.add('rooms.view');
            button.setAttribute('data-permission', 'rooms.view');
            const [rebuilt] = gate.scan(host);
            expect(rebuilt).not.toBe(first);
            expect(first.released).toBe(true);
            expect(button.hasAttribute('hidden')).toBe(false);
        });

        it('keeps the current state while a changed declaration is re-evaluated asynchronously', async () => {
            let pending = deferred();
            const gate = createPermissionGate({ can: () => pending.promise });
            host.innerHTML = '<button type="button" data-permission="rooms.book">Book</button>';
            const button = host.firstElementChild;
            const [first] = gate.scan(host);
            pending.resolve(false);
            await first.ready;
            expect(button.hidden).toBe(true);

            pending = deferred();
            button.setAttribute('data-permission', 'rooms.view');
            const [rebuilt] = gate.scan(host);
            expect(rebuilt.pending).toBe(true);
            expect(rebuilt.allowed).toBe(false);
            expect(button.hidden).toBe(true);

            pending.resolve(true);
            await rebuilt.ready;
            expect(button.hidden).toBe(false);
            expect(button.hasAttribute('hidden')).toBe(false);
        });

        it('fails closed for empty declarations and falls back to the gate mode for unknown modes', () => {
            const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
            host.innerHTML = '<button type="button" id="empty" data-permission=" , ">Empty</button>'
                + '<button type="button" id="odd" data-permission="rooms.book" data-permission-mode="blur">Odd</button>';
            const { gate } = grantedGate([]);
            gate.scan(host);
            expect(host.querySelector('#empty').hasAttribute('hidden')).toBe(true);
            expect(host.querySelector('#odd').hasAttribute('hidden')).toBe(true);
            expect(warn).toHaveBeenCalledTimes(2);
        });

        it('defaults to document and rejects invalid roots', () => {
            host.innerHTML = '<span data-permission="rooms.view">Rooms</span>';
            const { gate } = grantedGate([]);
            const handles = gate.scan();
            expect(handles.some((handle) => handle.target === host.firstElementChild)).toBe(true);
            expect(() => gate.scan({})).toThrow(TypeError);
            gate.destroy();
        });
    });

    describe('refresh() and context', () => {
        it('re-evaluates every applied target after the context changes', async () => {
            const gate = createPermissionGate({
                can: (capability, user) => Boolean(user && user.permissions.includes(capability)),
                context: { permissions: [] }
            });
            const edit = makeButton();
            const remove = makeButton();
            host.append(edit, remove);
            gate.apply(edit, 'staff.edit');
            gate.apply(remove, 'staff.remove', { mode: 'disable' });
            expect(edit.hidden).toBe(true);
            expect(remove.disabled).toBe(true);

            await gate.setContext({ permissions: ['staff.edit', 'staff.remove'] });
            expect(gate.getContext()).toEqual({ permissions: ['staff.edit', 'staff.remove'] });
            expect(edit.hidden).toBe(false);
            expect(remove.disabled).toBe(false);

            await gate.setContext({ permissions: [] }, { refresh: false });
            expect(edit.hidden).toBe(false);
            await gate.refresh();
            expect(edit.hidden).toBe(true);
        });

        it('keeps the current state while an async result is pending (no flicker)', async () => {
            let pending = deferred();
            const gate = createPermissionGate({ can: () => pending.promise });
            const button = makeButton();
            host.appendChild(button);
            const before = button.outerHTML;

            const handle = gate.apply(button, 'orders.approve');
            expect(handle.pending).toBe(true);
            expect(handle.allowed).toBe(null);
            expect(button.outerHTML).toBe(before);

            pending.resolve(false);
            await handle.ready;
            expect(handle.allowed).toBe(false);
            expect(button.hidden).toBe(true);

            pending = deferred();
            const refreshing = gate.refresh();
            expect(handle.pending).toBe(true);
            expect(button.hidden).toBe(true);
            await tick();
            expect(button.hidden).toBe(true);

            pending.resolve(true);
            await refreshing;
            expect(handle.allowed).toBe(true);
            expect(button.outerHTML).toBe(before);
        });

        it('ignores stale evaluations that resolve after a newer one', async () => {
            const queue = [];
            const gate = createPermissionGate({
                can: () => {
                    const next = deferred();
                    queue.push(next);
                    return next.promise;
                }
            });
            const button = makeButton();
            host.appendChild(button);
            const handle = gate.apply(button, 'orders.approve');
            queue[0].resolve(false);
            await handle.ready;
            expect(button.hidden).toBe(true);

            const slow = gate.refresh();
            const fast = gate.refresh();
            expect(queue).toHaveLength(3);
            queue[2].resolve(true);
            await fast;
            expect(button.hidden).toBe(false);

            queue[1].resolve(false);
            await slow;
            await tick();
            expect(button.hidden).toBe(false);
            expect(handle.allowed).toBe(true);
        });

        it('drops pending results after release() or destroy()', async () => {
            const pendingA = deferred();
            const pendingB = deferred();
            const gate = createPermissionGate({ can: (capability) => (capability === 'a' ? pendingA.promise : pendingB.promise) });
            const a = makeButton();
            const b = makeButton();
            host.append(a, b);
            const beforeA = a.outerHTML;
            const beforeB = b.outerHTML;

            const handleA = gate.apply(a, 'a');
            gate.apply(b, 'b');
            handleA.release();
            gate.destroy();
            pendingA.resolve(false);
            pendingB.resolve(false);
            await tick();
            expect(a.outerHTML).toBe(beforeA);
            expect(b.outerHTML).toBe(beforeB);
        });

        it('lets a single handle refresh itself', async () => {
            const { gate, set } = grantedGate([]);
            const button = makeButton();
            host.appendChild(button);
            const handle = gate.apply(button, 'rooms.book');
            expect(button.hidden).toBe(true);
            set.add('rooms.book');
            await expect(handle.refresh()).resolves.toBe(true);
            expect(button.hidden).toBe(false);
        });
    });

    describe('overlapping handles', () => {
        it('stays denied while any handle denies and restores the pristine state when all are released', async () => {
            const button = makeButton();
            host.appendChild(button);
            const before = button.outerHTML;
            const { gate, set } = grantedGate([]);
            const other = createPermissionGate({ can: () => false });

            const first = gate.apply(button, 'orders.cancel');
            const second = other.apply(button, 'orders.cancel');
            first.release();
            expect(button.hidden).toBe(true);
            second.release();
            expect(button.outerHTML).toBe(before);

            const hide = gate.apply(button, 'orders.cancel');
            const disable = gate.apply(button, 'orders.cancel', { mode: 'disable' });
            expect(button.hidden).toBe(true);
            expect(button.disabled).toBe(true);
            set.add('orders.cancel');
            await gate.refresh();
            expect(button.outerHTML).toBe(before);
            hide.release();
            disable.release();
            expect(button.outerHTML).toBe(before);
        });
    });

    describe('destroy()', () => {
        it('restores every target, never touches document/window listeners, and is idempotent', () => {
            const docAdd = vi.spyOn(document, 'addEventListener');
            const winAdd = vi.spyOn(window, 'addEventListener');
            host.innerHTML = '<button type="button" data-permission="a">A</button><a href="#/b" data-permission="b" data-permission-mode="disable">B</a>';
            const before = host.innerHTML;
            const { gate } = grantedGate([]);
            gate.scan(host);
            gate.apply(host, 'c', { mode: 'disable' });
            expect(host.innerHTML).not.toBe(before);

            gate.destroy();
            expect(host.innerHTML).toBe(before);
            expect(host.hasAttribute('aria-disabled')).toBe(false);
            expect(docAdd).not.toHaveBeenCalled();
            expect(winAdd).not.toHaveBeenCalled();

            expect(() => gate.destroy()).not.toThrow();
            const inert = gate.apply(host, 'c');
            expect(inert.released).toBe(true);
            expect(() => inert.release()).not.toThrow();
            expect(gate.scan(host)).toEqual([]);
            expect(host.hasAttribute('hidden')).toBe(false);
            return Promise.all([gate.refresh(), gate.setContext({}), inert.ready]);
        });
    });

    describe('Locale', () => {
        it('uses the active language for the default reason and refresh() picks up a switch', async () => {
            const button = makeButton();
            host.appendChild(button);
            const { gate } = grantedGate([], { mode: 'disable' });
            gate.apply(button, 'orders.delete');
            expect(button.getAttribute('title')).toBe('您沒有執行此操作的權限');

            Locale.setLang('en');
            await gate.refresh();
            expect(button.getAttribute('title')).toBe('You do not have permission to perform this action');
            expect(button.getAttribute('aria-description')).toBe('You do not have permission to perform this action');
            gate.destroy();
            expect(button.getAttribute('title')).toBe('Remove item');
        });
    });
});
