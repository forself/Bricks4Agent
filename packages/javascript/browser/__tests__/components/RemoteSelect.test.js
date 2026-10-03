import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { RemoteSelect as BaseRemoteSelect } from '../../ui_components/form/RemoteSelect/RemoteSelect.js';
import { FIELD_ERROR_CONTRACT } from '../../ui_components/utils/field-error.js';

// 記下每個實例，測試失敗提早結束時仍在 afterEach 銷毀，避免殘留的監聽影響下一個測試
const live = new Set();
class RemoteSelect extends BaseRemoteSelect {
    constructor(options) {
        super(options);
        live.add(this);
    }
}

const STAFF = Array.from({ length: 45 }, (_, index) => ({
    value: index + 1,
    label: `Staff ${index + 1}`,
}));

/** 依頁碼回傳 STAFF 中符合查詢字串的項目 */
function pagedSource() {
    return vi.fn(async (query, { page, pageSize }) => {
        const matches = STAFF.filter((item) => item.label.toLowerCase().includes(query.toLowerCase()));
        const start = (page - 1) * pageSize;
        return { items: matches.slice(start, start + pageSize), hasMore: start + pageSize < matches.length };
    });
}

function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((res, rej) => {
        resolve = res;
        reject = rej;
    });
    return { promise, resolve, reject };
}

/** 回傳可逐一手動完成的請求；calls[i] 帶 query、page、signal、resolve、reject */
function manualSource() {
    const calls = [];
    const fetchOptions = vi.fn((query, { page, signal }) => {
        const pending = deferred();
        calls.push({ query, page, signal, ...pending });
        return pending.promise;
    });
    return { fetchOptions, calls };
}

async function flush() {
    for (let i = 0; i < 10; i += 1) await Promise.resolve();
}

const inputOf = (select) => select.element.querySelector('.remote-select__input');
const listboxOf = (select) => select.element.querySelector('[role="listbox"]');
const optionsOf = (select) => [...select.element.querySelectorAll('.remote-select__option:not(.remote-select__load-more)')];
const statusOf = (select) => select.element.querySelector('.remote-select__status').textContent;
const tagLabelsOf = (select) => [...select.element.querySelectorAll('.remote-select__tag-label')].map((el) => el.textContent);

function type(select, text) {
    const input = inputOf(select);
    input.value = text;
    input.dispatchEvent(new Event('input', { bubbles: true }));
}

function press(select, key, init = {}) {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    inputOf(select).dispatchEvent(event);
    return event;
}

function netListeners(addSpy, removeSpy, type) {
    const added = addSpy.mock.calls.filter(([name]) => name === type).length;
    const removed = removeSpy.mock.calls.filter(([name]) => name === type).length;
    return added - removed;
}

describe('RemoteSelect', () => {
    let host;

    beforeEach(() => {
        vi.useFakeTimers();
        Locale.setLang('zh-TW');
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        live.forEach((select) => select.destroy());
        live.clear();
        host.remove();
        Locale.setLang('zh-TW');
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    it('renders an ARIA combobox wired to its listbox, with documented defaults', () => {
        const select = new RemoteSelect({ fetchOptions: pagedSource() }).mount(host);
        const input = inputOf(select);
        const listbox = listboxOf(select);

        expect(select.options).toMatchObject({
            debounce: 300,
            minQueryLength: 1,
            pageSize: 20,
            multiple: false,
            clearable: true,
            disabled: false,
            width: '100%',
            maxSelected: null,
            cacheResults: true,
        });
        expect(input.getAttribute('role')).toBe('combobox');
        expect(input.getAttribute('aria-autocomplete')).toBe('list');
        expect(input.getAttribute('aria-expanded')).toBe('false');
        expect(input.getAttribute('aria-controls')).toBe(listbox.id);
        expect(listbox.getAttribute('role')).toBe('listbox');
        expect(listbox.hasAttribute('aria-multiselectable')).toBe(false);
        expect(input.placeholder).toBe('輸入關鍵字搜尋');
        expect(select.getValue()).toBeNull();
        expect(select.getSelectedItems()).toEqual([]);
        select.destroy();
    });

    it('applies width, ariaLabel, placeholder and initial disabled; focus() and snapshot() work', () => {
        const select = new RemoteSelect({
            fetchOptions: pagedSource(),
            width: 320,
            ariaLabel: 'Meeting room',
            placeholder: 'Search rooms',
            disabled: true,
        }).mount(host);
        expect(select.element.style.width).toBe('320px');
        expect(inputOf(select).getAttribute('aria-label')).toBe('Meeting room');
        expect(listboxOf(select).getAttribute('aria-label')).toBe('Meeting room');
        expect(inputOf(select).placeholder).toBe('Search rooms');
        expect(inputOf(select).disabled).toBe(true);
        expect(select.snapshot()).toMatchObject({ availability: 'disabled', open: false, lifecycle: 'mounted' });

        select.setDisabled(false);
        select.focus();
        expect(document.activeElement).toBe(inputOf(select));
        select.destroy();

        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const noSource = new RemoteSelect({ debounce: 0 }).mount(host);
        expect(warn).toHaveBeenCalled();
        type(noSource, 'abc');
        return flush().then(() => {
            expect(statusOf(noSource)).toBe('查無符合的項目');
            noSource.destroy();
        });
    });

    it('debounces typing into one request for the latest query', async () => {
        const fetchOptions = pagedSource();
        const select = new RemoteSelect({ fetchOptions }).mount(host);

        type(select, 's');
        await vi.advanceTimersByTimeAsync(100);
        type(select, 'st');
        await vi.advanceTimersByTimeAsync(100);
        type(select, 'sta');
        await vi.advanceTimersByTimeAsync(299);
        expect(fetchOptions).not.toHaveBeenCalled();

        await vi.advanceTimersByTimeAsync(1);
        expect(fetchOptions).toHaveBeenCalledTimes(1);
        const [query, context] = fetchOptions.mock.calls[0];
        expect(query).toBe('sta');
        expect(context).toMatchObject({ page: 1, pageSize: 20 });
        expect(context.signal.aborted).toBe(false);

        await flush();
        expect(optionsOf(select)).toHaveLength(20);
        expect(inputOf(select).getAttribute('aria-expanded')).toBe('true');
        select.destroy();
    });

    it('skips queries shorter than minQueryLength and shows a hint', async () => {
        const fetchOptions = pagedSource();
        const select = new RemoteSelect({ fetchOptions, minQueryLength: 3 }).mount(host);
        type(select, 'st');
        await vi.advanceTimersByTimeAsync(1000);
        expect(fetchOptions).not.toHaveBeenCalled();
        expect(statusOf(select)).toBe('請至少輸入 3 個字元');
        select.destroy();

        const eager = pagedSource();
        const openOnEmpty = new RemoteSelect({ fetchOptions: eager, minQueryLength: 0 }).mount(host);
        openOnEmpty.open();
        expect(eager).toHaveBeenCalledWith('', expect.objectContaining({ page: 1 }));
        await flush();
        expect(optionsOf(openOnEmpty)).toHaveLength(20);
        openOnEmpty.destroy();
    });

    it('aborts superseded requests and ignores stale responses', async () => {
        const { fetchOptions, calls } = manualSource();
        const select = new RemoteSelect({ fetchOptions, debounce: 0 }).mount(host);

        type(select, 'a');
        type(select, 'ab');
        expect(calls.map((call) => call.query)).toEqual(['a', 'ab']);
        expect(calls[0].signal.aborted).toBe(true);
        expect(calls[1].signal.aborted).toBe(false);

        calls[1].resolve({ items: [{ value: 'b', label: 'Beta' }] });
        await flush();
        // 呼叫端忽略 signal、舊請求晚到：序號守衛仍要丟棄
        calls[0].resolve({ items: [{ value: 'a', label: 'Alpha' }] });
        await flush();
        expect(optionsOf(select).map((el) => el.textContent)).toEqual(['Beta']);
        select.destroy();
    });

    it('shows loading with aria-busy and a visible indicator, then the result count for screen readers', async () => {
        const pending = deferred();
        const select = new RemoteSelect({ fetchOptions: () => pending.promise, debounce: 0 }).mount(host);
        const spinner = select.element.querySelector('.remote-select__spinner');
        const statusRow = select.element.querySelector('.remote-select__status-row');

        type(select, 'on');
        expect(listboxOf(select).getAttribute('aria-busy')).toBe('true');
        expect(spinner.style.display).not.toBe('none');
        expect(statusOf(select)).toBe('載入中…');
        expect(statusRow.dataset.mode).toBe('visible');

        pending.resolve({ items: [{ value: 1, label: 'One' }], hasMore: false });
        await flush();
        expect(listboxOf(select).getAttribute('aria-busy')).toBe('false');
        expect(spinner.style.display).toBe('none');
        expect(statusOf(select)).toBe('共 1 筆結果');
        expect(statusRow.dataset.mode).toBe('hidden');
        expect(select.element.querySelector('[role="status"]')).not.toBeNull();
        select.destroy();
    });

    it('shows an empty state', async () => {
        const select = new RemoteSelect({ fetchOptions: async () => ({ items: [], hasMore: false }), debounce: 0 }).mount(host);
        type(select, 'zz');
        await flush();
        expect(optionsOf(select)).toHaveLength(0);
        expect(statusOf(select)).toBe('查無符合的項目');
        expect(select.element.querySelector('.remote-select__status-row').dataset.mode).toBe('visible');
        select.destroy();
    });

    it('reports errors, offers retry and recovers', async () => {
        let fail = true;
        const onError = vi.fn();
        const fetchOptions = vi.fn(async () => {
            if (fail) throw new Error('boom');
            return { items: [{ value: 1, label: 'One' }] };
        });
        const select = new RemoteSelect({ fetchOptions, onError, debounce: 0 }).mount(host);
        const retry = select.element.querySelector('.remote-select__retry');

        type(select, 'on');
        await flush();
        expect(onError).toHaveBeenCalledWith(expect.objectContaining({ message: 'boom' }));
        expect(statusOf(select)).toBe('載入失敗');
        expect(retry.style.display).not.toBe('none');
        expect(retry.textContent).toBe('重試');

        retry.click();
        await flush();
        expect(fetchOptions).toHaveBeenCalledTimes(2);
        expect(statusOf(select)).toBe('載入失敗');

        // Enter 在沒有高亮項目時也會重試
        fail = false;
        press(select, 'Enter');
        await flush();
        expect(fetchOptions).toHaveBeenCalledTimes(3);
        expect(optionsOf(select)).toHaveLength(1);
        expect(retry.style.display).toBe('none');
        select.destroy();
    });

    it('pages through results with a keyboard-reachable load-more item and on scroll', async () => {
        const fetchOptions = pagedSource();
        const select = new RemoteSelect({ fetchOptions, debounce: 0 }).mount(host);
        const input = inputOf(select);

        type(select, 'staff');
        await flush();
        expect(optionsOf(select)).toHaveLength(20);
        const loadMore = select.element.querySelector('.remote-select__load-more');
        expect(loadMore.getAttribute('role')).toBe('option');
        expect(loadMore.textContent).toBe('載入更多');

        press(select, 'End');
        expect(input.getAttribute('aria-activedescendant')).toBe(loadMore.id);
        press(select, 'Enter');
        expect(fetchOptions).toHaveBeenLastCalledWith('staff', expect.objectContaining({ page: 2 }));
        expect(loadMore.textContent).toBe('載入更多中…');
        expect(listboxOf(select).getAttribute('aria-busy')).toBe('true');

        await flush();
        const options = optionsOf(select);
        expect(options).toHaveLength(40);
        // 以鍵盤載入更多後，高亮落在第一筆新項目
        expect(input.getAttribute('aria-activedescendant')).toBe(options[20].id);

        // 捲到底載入最後一頁（jsdom 版面皆為 0，任何捲動都視為到底）
        listboxOf(select).dispatchEvent(new Event('scroll'));
        expect(fetchOptions).toHaveBeenLastCalledWith('staff', expect.objectContaining({ page: 3 }));
        await flush();
        expect(optionsOf(select)).toHaveLength(45);
        expect(select.element.querySelector('.remote-select__load-more')).toBeNull();
        select.destroy();
    });

    it('caches results per query for the life of the component, and refresh() bypasses the cache', async () => {
        const fetchOptions = pagedSource();
        const select = new RemoteSelect({ fetchOptions, debounce: 0 }).mount(host);
        type(select, 'staff 1');
        await flush();
        type(select, 'staff 2');
        await flush();
        type(select, 'staff 1');
        expect(fetchOptions).toHaveBeenCalledTimes(2);
        expect(optionsOf(select)).toHaveLength(11);

        await select.refresh();
        expect(fetchOptions).toHaveBeenCalledTimes(3);
        expect(fetchOptions).toHaveBeenLastCalledWith('staff 1', expect.objectContaining({ page: 1 }));
        select.destroy();

        const uncached = pagedSource();
        const plain = new RemoteSelect({ fetchOptions: uncached, debounce: 0, cacheResults: false }).mount(host);
        type(plain, 'staff 1');
        await flush();
        type(plain, 'staff 2');
        await flush();
        type(plain, 'staff 1');
        await flush();
        expect(uncached).toHaveBeenCalledTimes(3);
        plain.destroy();
    });

    it('follows the combobox keyboard pattern in single mode', async () => {
        const onChange = vi.fn();
        const select = new RemoteSelect({ fetchOptions: pagedSource(), debounce: 0, onChange }).mount(host);
        const input = inputOf(select);

        type(select, 'staff 1');
        await flush();
        const options = optionsOf(select);
        expect(options).toHaveLength(11);

        press(select, 'ArrowDown');
        expect(input.getAttribute('aria-activedescendant')).toBe(options[0].id);
        press(select, 'ArrowDown');
        expect(input.getAttribute('aria-activedescendant')).toBe(options[1].id);
        press(select, 'ArrowUp');
        expect(input.getAttribute('aria-activedescendant')).toBe(options[0].id);
        press(select, 'End');
        expect(input.getAttribute('aria-activedescendant')).toBe(options[10].id);
        press(select, 'Home');
        expect(input.getAttribute('aria-activedescendant')).toBe(options[0].id);

        const enter = press(select, 'Enter');
        expect(enter.defaultPrevented).toBe(true);
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledWith(1, expect.objectContaining({ value: 1, label: 'Staff 1' }));
        expect(select.getValue()).toBe(1);
        expect(input.value).toBe('Staff 1');
        expect(input.getAttribute('aria-expanded')).toBe('false');
        expect(input.hasAttribute('aria-activedescendant')).toBe(false);

        // Enter 在清單收合時不攔截（讓表單照常送出）
        expect(press(select, 'Enter').defaultPrevented).toBe(false);

        // 第一次 Escape 收合並保留字串，第二次清除字串；兩次都不往外傳
        const outer = vi.fn();
        document.addEventListener('keydown', outer);
        type(select, 'staff 2');
        await flush();
        const first = press(select, 'Escape');
        expect(first.defaultPrevented).toBe(true);
        expect(input.getAttribute('aria-expanded')).toBe('false');
        expect(input.value).toBe('staff 2');
        press(select, 'Escape');
        expect(input.value).toBe('Staff 1');
        expect(outer).not.toHaveBeenCalled();
        // 沒有東西可收時 Escape 照常往外傳（例如關閉外層對話框）
        expect(press(select, 'Escape').defaultPrevented).toBe(false);
        expect(outer).toHaveBeenCalledTimes(1);
        document.removeEventListener('keydown', outer);

        // ArrowDown 在收合時展開；已選項目標示 aria-selected
        type(select, 'staff 1');
        await flush();
        expect(optionsOf(select)[0].getAttribute('aria-selected')).toBe('true');
        expect(optionsOf(select)[1].getAttribute('aria-selected')).toBe('false');
        press(select, 'Escape');
        press(select, 'ArrowDown');
        expect(input.getAttribute('aria-expanded')).toBe('true');
        expect(input.getAttribute('aria-activedescendant')).toBe(optionsOf(select)[0].id);
        select.destroy();
    });

    it('supports multiple selection with tags, maxSelected and keyboard removal', async () => {
        const onChange = vi.fn();
        const select = new RemoteSelect({
            fetchOptions: pagedSource(),
            debounce: 0,
            multiple: true,
            maxSelected: 2,
            onChange,
        }).mount(host);
        const input = inputOf(select);
        expect(listboxOf(select).getAttribute('aria-multiselectable')).toBe('true');
        expect(select.getValue()).toEqual([]);

        type(select, 'staff 1');
        await flush();
        optionsOf(select)[0].click();
        press(select, 'ArrowDown');
        press(select, 'ArrowDown');
        press(select, 'Enter');
        expect(select.getValue()).toEqual([1, 10]);
        expect(onChange).toHaveBeenLastCalledWith(
            [1, 10],
            [expect.objectContaining({ value: 1 }), expect.objectContaining({ value: 10 })],
        );
        // 多選時清單保持展開、保留查詢字串，方便繼續挑選
        expect(input.getAttribute('aria-expanded')).toBe('true');
        expect(input.value).toBe('staff 1');

        let options = optionsOf(select);
        expect(options[0].getAttribute('aria-selected')).toBe('true');
        expect(options[2].getAttribute('aria-disabled')).toBe('true');
        options[2].click();
        expect(select.getValue()).toEqual([1, 10]);
        expect(statusOf(select)).toBe('最多可選 2 項');

        // 已選項目再選一次即取消
        optionsOf(select)[1].click();
        expect(select.getValue()).toEqual([1]);
        optionsOf(select)[2].click();
        expect(select.getValue()).toEqual([1, 11]);

        expect(tagLabelsOf(select)).toEqual(['Staff 1', 'Staff 11']);
        const removeButtons = [...select.element.querySelectorAll('.remote-select__tag-remove')];
        expect(removeButtons[0].tagName).toBe('BUTTON');
        expect(removeButtons[0].type).toBe('button');
        expect(removeButtons[0].tabIndex).toBe(0);
        expect(removeButtons[0].getAttribute('aria-label')).toBe('移除 Staff 1');
        removeButtons[0].click();
        expect(select.getValue()).toEqual([11]);
        expect(document.activeElement).toBe(input);

        // Backspace 在空白輸入框移除最後一個標籤
        press(select, 'Escape');
        press(select, 'Escape');
        expect(input.value).toBe('');
        press(select, 'Backspace');
        expect(select.getValue()).toEqual([]);
        expect(onChange).toHaveBeenLastCalledWith([], []);
        expect(tagLabelsOf(select)).toEqual([]);
        select.destroy();
    });

    it('labels preset values from initialItems and resolves unknown ones with resolveLabels', async () => {
        const single = new RemoteSelect({
            fetchOptions: pagedSource(),
            value: 7,
            initialItems: [{ value: 7, label: 'Staff 7' }],
        }).mount(host);
        expect(inputOf(single).value).toBe('Staff 7');
        expect(single.getSelectedItems()).toEqual([{ value: 7, label: 'Staff 7' }]);
        single.destroy();

        const onChange = vi.fn();
        const resolveLabels = vi.fn(async (values) => values.map((value) => ({ value, label: `Staff ${value}` })));
        const multi = new RemoteSelect({
            fetchOptions: pagedSource(),
            multiple: true,
            values: [3, 4],
            initialItems: [{ value: 3, label: 'Staff 3' }],
            resolveLabels,
            onChange,
        }).mount(host);
        expect(resolveLabels).toHaveBeenCalledWith([4]);
        expect(tagLabelsOf(multi)).toEqual(['Staff 3', '載入中…']);
        // 鍵盤焦點停在標籤的移除按鈕上時，標籤重繪後焦點仍在同一個標籤
        multi.element.querySelector('.remote-select__tag-remove').focus();
        await flush();
        expect(tagLabelsOf(multi)).toEqual(['Staff 3', 'Staff 4']);
        expect(document.activeElement.getAttribute('aria-label')).toBe('移除 Staff 3');
        expect(multi.element.contains(document.activeElement)).toBe(true);

        multi.setValue([5, 3]);
        expect(resolveLabels).toHaveBeenLastCalledWith([5]);
        await flush();
        expect(tagLabelsOf(multi)).toEqual(['Staff 5', 'Staff 3']);
        expect(onChange).not.toHaveBeenCalled();
        multi.destroy();

        const onError = vi.fn();
        const failing = new RemoteSelect({
            fetchOptions: pagedSource(),
            value: 42,
            resolveLabels: async () => { throw new Error('lookup failed'); },
            onError,
        }).mount(host);
        await flush();
        expect(onError).toHaveBeenCalledWith(expect.objectContaining({ message: 'lookup failed' }));
        expect(inputOf(failing).value).toBe('42');
        failing.destroy();
    });

    it('setValue and clear() stay silent; the clear button fires onChange', () => {
        const onChange = vi.fn();
        const select = new RemoteSelect({ fetchOptions: pagedSource(), onChange }).mount(host);
        const clearButton = select.element.querySelector('.remote-select__clear');
        expect(clearButton.style.display).toBe('none');

        select.setValue(9, { value: 9, label: 'Staff 9' });
        expect(select.getValue()).toBe(9);
        expect(select.getSelectedItems()).toEqual([{ value: 9, label: 'Staff 9' }]);
        expect(inputOf(select).value).toBe('Staff 9');
        expect(onChange).not.toHaveBeenCalled();

        expect(clearButton.style.display).not.toBe('none');
        expect(clearButton.getAttribute('aria-label')).toBe('清除選取');
        clearButton.click();
        expect(select.getValue()).toBeNull();
        expect(onChange).toHaveBeenCalledWith(null, null);

        select.setValue(9);
        select.clear();
        expect(select.getValue()).toBeNull();
        expect(inputOf(select).value).toBe('');
        expect(onChange).toHaveBeenCalledTimes(1);
        select.destroy();

        const notClearable = new RemoteSelect({ fetchOptions: pagedSource(), clearable: false, value: 1 }).mount(host);
        expect(notClearable.element.querySelector('.remote-select__clear').style.display).toBe('none');
        notClearable.destroy();
    });

    it('setDisabled blocks opening, typing and tag removal', () => {
        const select = new RemoteSelect({
            fetchOptions: pagedSource(),
            multiple: true,
            values: [1],
            initialItems: [{ value: 1, label: 'Staff 1' }],
        }).mount(host);
        expect(select.element.querySelector('.remote-select__tag-remove')).not.toBeNull();

        select.setDisabled(true);
        expect(inputOf(select).disabled).toBe(true);
        expect(select.element.querySelector('.remote-select__tag-remove')).toBeNull();
        expect(select.element.querySelector('.remote-select__clear').style.display).toBe('none');
        select.open();
        expect(inputOf(select).getAttribute('aria-expanded')).toBe('false');
        press(select, 'Backspace');
        expect(select.getValue()).toEqual([1]);

        select.setDisabled(false);
        expect(inputOf(select).disabled).toBe(false);
        expect(select.element.querySelector('.remote-select__tag-remove')).not.toBeNull();
        select.destroy();
    });

    it('implements the field-error contract through utils/field-error.js', () => {
        const select = new RemoteSelect({ fetchOptions: pagedSource() }).mount(host);
        const input = inputOf(select);
        const control = select.element.querySelector('.remote-select__control');
        expect(select[FIELD_ERROR_CONTRACT]).toBe(true);

        expect(select.setError('必填')).toBe(select);
        const message = select.element.querySelector('.b4a-field-error');
        expect(message.textContent).toBe('必填');
        expect(message.getAttribute('role')).toBe('alert');
        expect(input.getAttribute('aria-invalid')).toBe('true');
        expect(input.getAttribute('aria-describedby')).toContain(message.id);
        expect(control.style.outline).toContain('var(--cl-danger)');

        select.clearError();
        expect(select.element.querySelector('.b4a-field-error')).toBeNull();
        expect(input.hasAttribute('aria-invalid')).toBe(false);
        expect(control.style.outline).toBe('');

        select.setError('格式不正確', { display: false });
        expect(select.element.querySelector('.b4a-field-error')).toBeNull();
        expect(input.getAttribute('aria-invalid')).toBe('true');
        select.setError('');
        expect(input.hasAttribute('aria-invalid')).toBe(false);
        select.destroy();
    });

    it('floats the panel with fixed positioning, flips above when needed, and restores it on close', async () => {
        const select = new RemoteSelect({ fetchOptions: pagedSource(), minQueryLength: 0 }).mount(host);
        const control = select.element.querySelector('.remote-select__control');
        const panel = select.element.querySelector('.remote-select__panel');
        vi.spyOn(control, 'getBoundingClientRect').mockReturnValue({ top: 700, bottom: 736, left: 40, right: 340, width: 300, height: 36, x: 40, y: 700 });
        vi.spyOn(panel, 'getBoundingClientRect').mockReturnValue({ top: 0, bottom: 200, left: 0, right: 300, width: 300, height: 200, x: 0, y: 0 });

        select.open();
        await flush();
        expect(panel.style.position).toBe('fixed');
        expect(panel.dataset.floating).toBe('fixed');
        expect(panel.dataset.placement).toBe('top');
        expect(panel.style.top).toBe('496px');
        expect(select.element.contains(panel)).toBe(true);

        select.close();
        expect(panel.style.position).toBe('absolute');
        expect(panel.dataset.floating).toBeUndefined();
        select.destroy();
    });

    it('attaches document and window listeners only while the list is open', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const select = new RemoteSelect({ fetchOptions: pagedSource(), minQueryLength: 0 }).mount(host);
        expect(docAdd).not.toHaveBeenCalled();
        expect(winAdd).not.toHaveBeenCalled();

        select.open();
        expect(netListeners(docAdd, docRemove, 'mousedown')).toBe(1);
        expect(netListeners(winAdd, winRemove, 'scroll')).toBe(1);
        expect(netListeners(winAdd, winRemove, 'resize')).toBe(1);

        // 點在清單內不收合；點在外面收合並卸下監聽
        select.element.querySelector('.remote-select__panel').dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        expect(inputOf(select).getAttribute('aria-expanded')).toBe('true');
        document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        expect(inputOf(select).getAttribute('aria-expanded')).toBe('false');
        expect(netListeners(docAdd, docRemove, 'mousedown')).toBe(0);
        expect(netListeners(winAdd, winRemove, 'scroll')).toBe(0);
        expect(netListeners(winAdd, winRemove, 'resize')).toBe(0);
        select.destroy();
    });

    it('destroy() aborts in-flight requests, clears timers and leaves no DOM or listeners', async () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const { fetchOptions, calls } = manualSource();
        const onError = vi.fn();
        const onChange = vi.fn();
        const select = new RemoteSelect({ fetchOptions, onError, onChange }).mount(host);

        type(select, 'st');
        await vi.advanceTimersByTimeAsync(300);
        expect(calls).toHaveLength(1);
        expect(calls[0].signal.aborted).toBe(false);

        select.destroy();
        expect(calls[0].signal.aborted).toBe(true);
        expect(host.childElementCount).toBe(0);
        expect(document.querySelector('.remote-select')).toBeNull();
        calls[0].resolve({ items: [{ value: 1, label: 'late' }] });
        await flush();
        expect(onError).not.toHaveBeenCalled();
        expect(onChange).not.toHaveBeenCalled();

        for (const type of ['mousedown', 'click', 'keydown']) expect(netListeners(docAdd, docRemove, type)).toBe(0);
        for (const type of ['scroll', 'resize']) expect(netListeners(winAdd, winRemove, type)).toBe(0);
        expect(vi.getTimerCount()).toBe(0);

        expect(() => {
            select.destroy();
            select.setValue(1);
            select.open();
            select.close();
            select.clear();
            select.setDisabled(true);
            select.setError('x');
            select.clearError();
            select.focus();
        }).not.toThrow();
        await expect(select.refresh()).resolves.toBeUndefined();

        // 防抖中的查詢也會被 destroy 取消
        const pendingSource = pagedSource();
        const debounced = new RemoteSelect({ fetchOptions: pendingSource }).mount(host);
        type(debounced, 'staff');
        debounced.destroy();
        await vi.advanceTimersByTimeAsync(1000);
        expect(pendingSource).not.toHaveBeenCalled();
    });

    it('follows Locale switching for its strings', async () => {
        Locale.setLang('en');
        const select = new RemoteSelect({ fetchOptions: async () => ({ items: [] }), debounce: 0 }).mount(host);
        expect(inputOf(select).placeholder).toBe('Type to search');
        type(select, 'zz');
        await flush();
        expect(statusOf(select)).toBe('No matching results');

        Locale.setLang('zh-TW');
        type(select, 'zzz');
        await flush();
        expect(statusOf(select)).toBe('查無符合的項目');
        select.destroy();
    });
});
