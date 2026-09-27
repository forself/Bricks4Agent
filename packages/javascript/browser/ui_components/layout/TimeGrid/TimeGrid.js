/**
 * TimeGrid — 時段格（排程檢視）
 *
 * 列＝時段（slot），欄＝日期或資源（column），項目（item）佔同一欄內一或多個連續時段。
 * 適用會議室預約、人員排班或任何時間表；元件本身不帶任何業務語意。
 *
 * - DOM + CSS grid 版面，樣式全走 CSSOM 與 var(--cl-*) token（嚴格 CSP 相容，不注入 <style>）。
 * - 項目位置以時段索引算術求得（不讀版面），整張表在離線片段中一次建好再掛上。
 * - 同欄重疊的項目依重疊群組（cluster）分道（lane）並排；ghost 項目另成一層疊在上方。
 * - 鍵盤：roving tabindex、方向鍵/Home/End/PageUp/PageDown、Enter 選取、Space 拿起移動。
 *
 * @example
 * const grid = new TimeGrid({
 *     columns: [{ key: 'roomA', label: '會議室 A' }, { key: 'roomB', label: '會議室 B' }],
 *     timeRange: { start: '08:00', end: '18:00', step: 30 },
 *     items: [{ id: 1, column: 'roomA', start: '09:00', end: '10:30', title: '週會' }],
 *     selectable: true,
 *     onSelect: (range) => console.log(range)
 * }).mount('#host');
 */
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';
import './locale.js';

const VARIANT_TOKENS = {
    primary: { accent: 'var(--cl-primary)', fill: 'var(--cl-primary-light)' },
    success: { accent: 'var(--cl-success)', fill: 'var(--cl-success-light)' },
    warning: { accent: 'var(--cl-warning)', fill: 'var(--cl-warning-light)' },
    danger: { accent: 'var(--cl-danger)', fill: 'var(--cl-danger-light)' },
    info: { accent: 'var(--cl-info)', fill: 'var(--cl-info-light)' },
    neutral: { accent: 'var(--cl-grey)', fill: 'var(--cl-bg-secondary)' }
};

const DEFAULT_RANGE = Object.freeze({ start: '08:00', end: '18:00', step: 30 });
const TIME_PATTERN = /^(\d{1,2}):(\d{2})$/;
const PAGE_STEP = 5;
const DRAG_THRESHOLD = 4;
const ANNOUNCE_INTERVAL = 250;
const NOW_INTERVAL = 60000;
const MAX_SLOTS = 1440;
const EDGE_ZONE = 24;
const EDGE_SPEED = 12;
const NAV_KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown']);
// 疊放層級（皆在根元素 isolation 形成的堆疊脈絡內）
const Z = { item: 1, ghost: 2, now: 3, drop: 4, focus: 5, rowHeader: 6, header: 7 };

const VISUALLY_HIDDEN = 'position:absolute; width:1px; height:1px; margin:-1px; padding:0; border:0; overflow:hidden; clip:rect(0 0 0 0); clip-path:inset(50%); white-space:nowrap;';
const CELL_CSS = 'position:relative; box-sizing:border-box; min-width:0; border-right:1px solid var(--cl-border-light); border-bottom:1px solid var(--cl-border-light); background:var(--cl-bg); outline:none;';
const ROW_HEADER_CSS = `position:sticky; left:0; z-index:${Z.rowHeader}; box-sizing:border-box; min-width:0; padding:2px 6px; border-right:1px solid var(--cl-border); border-bottom:1px solid var(--cl-border-light); background:var(--cl-bg-secondary); font-size:var(--cl-font-size-xs); color:var(--cl-text-secondary); white-space:nowrap; overflow:hidden; text-overflow:ellipsis; font-variant-numeric:tabular-nums;`;
const COLUMN_HEADER_CSS = 'display:flex; flex-direction:column; align-items:center; justify-content:center; gap:1px; min-width:0; padding:6px 4px; box-sizing:border-box; border-right:1px solid var(--cl-border-light); border-bottom:1px solid var(--cl-border); background:var(--cl-bg-secondary); font-size:var(--cl-font-size-sm); font-weight:600; color:var(--cl-text); text-align:center;';
const CORNER_CSS = 'position:sticky; left:0; z-index:1; display:flex; align-items:flex-end; justify-content:flex-end; min-width:0; padding:6px; box-sizing:border-box; border-right:1px solid var(--cl-border); border-bottom:1px solid var(--cl-border); background:var(--cl-bg-secondary); font-size:var(--cl-font-size-xs); font-weight:600; color:var(--cl-text-secondary);';
const ELLIPSIS_CSS = 'display:block; max-width:100%; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;';

export class TimeGrid {
    constructor(options = {}) {
        this.options = {
            columns: [],                // 欄定義 [{ key, label, sublabel?, date?: 'YYYY-MM-DD' }]
            slots: null,                // 明確時段 [{ key, label?, start: 'HH:mm', end: 'HH:mm' }]；null 時改用 timeRange 產生
            timeRange: { start: '08:00', end: '18:00', step: 30 }, // slots 為 null 時依此產生等長時段（step 單位：分鐘）
            items: [],                  // 項目 [{ id, column, start, end, title, subtitle?, variant?, ghost?, draggable?, data? }]
            selectable: false,          // 允許點擊或拖曳空白格選取同欄連續時段 → onSelect
            editable: false,            // 允許拖放（指標或鍵盤）把項目移到其他欄/時段 → onItemMove
            readonly: false,            // true 時停用選取與移動（不論 selectable/editable）
            slotHeight: 40,             // 每個時段列的高度（px）
            height: null,               // 捲動容器高度（數字＝px，或 CSS 長度字串）；null＝不限高
            columnMinWidth: 120,        // 每欄最小寬度（px），總寬不足時水平捲動
            slotLabelWidth: 72,         // 左側時段標籤欄寬度（px）
            nowIndicator: false,        // 在 columns[].date 為今天的欄畫出目前時間線（每分鐘更新）
            now: () => new Date(),      // 取得目前時間的函式（可注入以便測試）
            ariaLabel: '',              // 表格的無障礙名稱；空字串時用 Locale 的 timeGrid.gridLabel
            onSelect: null,             // ({ column, start, end, startTime, endTime, slots }) => void
            onItemMove: null,           // ({ item, from, to }) => boolean | Promise<boolean>；回傳 false 表示拒絕
            onItemClick: null,          // (item, event) => void
            onCellClick: null,          // ({ column, slot, startTime, endTime }, event) => void
            ...options
        };

        this.element = null;
        this._destroyed = false;
        this._mounted = false;
        this._localeBound = false;

        this._columns = [];
        this._colIndex = new Map();
        this._slots = [];
        this._slotIndex = new Map();
        this._items = new Map();
        this._orderOf = new Map();
        this._orderSeq = 0;
        this._autoId = 0;
        this._resolved = new Map();
        this._byColumn = [];
        this._placements = new Map();
        this._colPlaced = [];
        this._cover = [];
        this._cells = [];
        this._rowEls = [];
        this._headerCells = [];
        this._itemEls = new Map();
        this._warned = new Set();
        this._active = { row: 0, col: 0 };
        this._itemAnchor = null;
        this._slotHeight = 40;

        this._press = null;
        this._docListening = false;
        this._edge = null;
        this._raf = 0;
        this._lastPointer = null;
        this._suppressClick = false;
        this._clickGuardTimer = null;
        this._pendingMove = null;
        this._moveToken = 0;
        this._restructuring = false;
        this._announceTimer = null;
        this._announcePending = '';
        this._nowTimer = null;
        this._nowEls = [];
        this._ring = null;
        this._dropEl = null;

        this._state = createComponentState({
            lifecycle: 'created',
            mode: 'idle',       // idle | dragging | grabbed | pending
            selection: null,    // { col, anchor, focus, confirmed, via }
            move: null          // { key, via, source: { col, s, e }, target: { col, s }, pending }
        }, {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed', mode: 'idle', selection: null, move: null }),
            SELECT: (s, p) => ({ ...s, selection: p?.selection ?? null }),
            MOVE_BEGIN: (s, p) => ({ ...s, mode: p.via === 'keyboard' ? 'grabbed' : 'dragging', move: p.move }),
            MOVE_SYNC: (s, p) => (s.move ? { ...s, move: { ...s.move, ...p } } : s),
            MOVE_PENDING: (s) => (s.move ? { ...s, mode: 'pending', move: { ...s.move, pending: true } } : s),
            MOVE_END: (s) => ({ ...s, mode: 'idle', move: null })
        });

        this._onKeyDown = this._handleKeyDown.bind(this);
        this._onKeyUp = this._handleKeyUp.bind(this);
        this._onPointerDown = this._handlePointerDown.bind(this);
        this._onGridClick = this._handleClick.bind(this);
        this._onFocusIn = this._handleFocusIn.bind(this);
        this._onFocusOut = this._handleFocusOut.bind(this);
        this._onDocPointerMove = this._handleDocPointerMove.bind(this);
        this._onDocPointerUp = this._handleDocPointerUp.bind(this);
        this._onDocPointerCancel = this._handleDocPointerCancel.bind(this);
        this._onDocKeyDown = this._handleDocKeyDown.bind(this);
        this._onEdgeFrame = this._edgeFrame.bind(this);
        this._onNowTick = () => this._updateNow();
        this._onLocaleChanged = () => { if (!this._destroyed) this._render(); };

        this._buildShell();
        this._report(this._loadItems(this.options.items));
        this._render();
    }

    // ── 公開 API ──────────────────────────────────────────────

    mount(container) {
        if (this._destroyed) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) {
            console.warn('[TimeGrid] mount target not found:', container);
            return this;
        }
        target.appendChild(this.element);
        if (!this._mounted) {
            this._mounted = true;
            this._state.send('MOUNT');
        }
        if (!this._localeBound && typeof window !== 'undefined') {
            window.addEventListener('locale-changed', this._onLocaleChanged);
            this._localeBound = true;
        }
        this._startNowTimer();
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._endPress();
        this._stopNowTimer();
        clearTimeout(this._announceTimer);
        clearTimeout(this._clickGuardTimer);
        this._announceTimer = null;
        this._clickGuardTimer = null;
        this._destroyed = true;
        this._pendingMove = null;
        if (this._localeBound && typeof window !== 'undefined') {
            window.removeEventListener('locale-changed', this._onLocaleChanged);
            this._localeBound = false;
        }
        if (this._grid) {
            this._grid.removeEventListener('keydown', this._onKeyDown);
            this._grid.removeEventListener('keyup', this._onKeyUp);
            this._grid.removeEventListener('pointerdown', this._onPointerDown);
            this._grid.removeEventListener('click', this._onGridClick);
            this._grid.removeEventListener('focusin', this._onFocusIn);
            this._grid.removeEventListener('focusout', this._onFocusOut);
        }
        if (this.element && this.element.parentNode) this.element.remove();
        this._state.send('DESTROY');
        this._items.clear();
        this._itemEls.clear();
        this._placements.clear();
        this._resolved.clear();
        this._cells = [];
        this._rowEls = [];
        this._headerCells = [];
        this._cover = [];
        this._nowEls = [];
        this._ring = null;
        this._dropEl = null;
        this._grid = null;
        this._scroller = null;
        this._live = null;
        this.element = null;
    }

    setItems(items) {
        if (this._destroyed) return this;
        this._touchPending(null);
        const problems = this._loadItems(items);
        this._resolveAll(problems);
        this._report(problems);
        this._preserveFocus(() => this._layoutColumns(this._allColumnIndexes()));
        return this;
    }

    addItem(item) {
        if (this._destroyed || !item || typeof item !== 'object') return this;
        const rec = { ...item };
        if (rec.id === undefined || rec.id === null || rec.id === '') rec.id = `tg-item-${++this._autoId}`;
        const key = String(rec.id);
        if (this._items.has(key)) {
            this._report([{ key: `item-dup:${key}`, text: `項目 id "${key}" 已存在，addItem 已略過（要修改請用 updateItem）` }]);
            return this;
        }
        this._items.set(key, rec);
        this._orderOf.set(key, this._orderSeq++);
        this._refreshItems([key], []);
        return this;
    }

    updateItem(id, patch = {}) {
        if (this._destroyed || id === undefined || id === null) return this;
        const key = String(id);
        const rec = this._items.get(key);
        if (!rec) return this;
        const oldCol = this._resolved.get(key)?.col;
        const changes = { ...(patch || {}) };
        delete changes.id;
        Object.assign(rec, changes);
        this._touchPending(key);
        this._refreshItems([key], oldCol === undefined ? [] : [oldCol]);
        return this;
    }

    removeItem(id) {
        if (this._destroyed || id === undefined || id === null) return this;
        const key = String(id);
        if (!this._items.has(key)) return this;
        const oldCol = this._resolved.get(key)?.col;
        this._items.delete(key);
        this._orderOf.delete(key);
        this._touchPending(key);
        this._refreshItems([key], oldCol === undefined ? [] : [oldCol]);
        return this;
    }

    getItems() {
        return Array.from(this._items.values(), (rec) => ({ ...rec }));
    }

    setColumns(columns) {
        if (this._destroyed) return this;
        this.options.columns = Array.isArray(columns) ? columns : [];
        this._render();
        return this;
    }

    setSlots(slots) {
        if (this._destroyed) return this;
        this.options.slots = Array.isArray(slots) ? slots : null;
        this._render();
        return this;
    }

    setTimeRange(range) {
        if (this._destroyed) return this;
        this.options.timeRange = range && typeof range === 'object' ? { ...range } : { ...DEFAULT_RANGE };
        this.options.slots = null;
        this._render();
        return this;
    }

    getSelection() {
        if (this._destroyed) return null;
        const sel = this._state.snapshot().selection;
        if (!sel) return null;
        const [lo, hi] = TimeGrid._span(sel);
        return { ...this._describe(sel.col, lo, hi + 1), confirmed: !!sel.confirmed };
    }

    clearSelection() {
        if (this._destroyed) return this;
        this._setSelection(null);
        return this;
    }

    focusCell(column, slot) {
        if (this._destroyed) return this;
        const col = this._colIndex.get(String(column));
        const row = this._slotIndex.get(String(slot));
        if (col === undefined || row === undefined) return this;
        this._focusCellAt(row, col);
        return this;
    }

    scrollToSlot(slotKey) {
        if (this._destroyed) return this;
        const row = this._slotIndex.get(String(slotKey));
        if (row === undefined || !this._scroller) return this;
        if (this._hasFixedHeight()) {
            this._scroller.scrollTop = row * this._slotHeight;
        } else {
            const rowEl = this._rowEls[row];
            if (rowEl && typeof rowEl.scrollIntoView === 'function') rowEl.scrollIntoView({ block: 'start' });
        }
        return this;
    }

    snapshot() {
        return this._state.snapshot();
    }

    // ── 模型 ──────────────────────────────────────────────────

    _loadItems(items) {
        const problems = [];
        this._items = new Map();
        this._orderOf = new Map();
        this._orderSeq = 0;
        for (const raw of Array.isArray(items) ? items : []) {
            if (!raw || typeof raw !== 'object') continue;
            const rec = { ...raw };
            if (rec.id === undefined || rec.id === null || rec.id === '') rec.id = `tg-item-${++this._autoId}`;
            const key = String(rec.id);
            if (this._items.has(key)) {
                problems.push({ key: `item-dup:${key}`, text: `項目 id "${key}" 重複，已略過後者` });
                continue;
            }
            this._items.set(key, rec);
            this._orderOf.set(key, this._orderSeq++);
        }
        return problems;
    }

    _rebuildColumns() {
        const problems = [];
        const list = [];
        const index = new Map();
        const source = Array.isArray(this.options.columns) ? this.options.columns : [];
        source.forEach((raw, i) => {
            const id = raw && typeof raw === 'object' ? raw.key : undefined;
            if (id === undefined || id === null || id === '') {
                problems.push({ key: `column-nokey:${i}`, text: `第 ${i + 1} 個欄缺少 key，已略過` });
                return;
            }
            const lookup = String(id);
            if (index.has(lookup)) {
                problems.push({ key: `column-dup:${lookup}`, text: `欄 key "${lookup}" 重複，已略過後者` });
                return;
            }
            index.set(lookup, list.length);
            list.push({
                id,
                lookup,
                label: raw.label === undefined || raw.label === null ? lookup : String(raw.label),
                sublabel: raw.sublabel === undefined || raw.sublabel === null ? '' : String(raw.sublabel),
                date: typeof raw.date === 'string' ? raw.date.trim() : ''
            });
        });
        this._columns = list;
        this._colIndex = index;
        return problems;
    }

    _rebuildSlots() {
        const problems = [];
        let list = [];
        if (Array.isArray(this.options.slots)) {
            const candidates = [];
            this.options.slots.forEach((raw, i) => {
                const start = TimeGrid._parseTime(raw?.start);
                const end = TimeGrid._parseTime(raw?.end);
                if (start === null || end === null || end <= start) {
                    problems.push({ key: `slot-invalid:${i}:${raw?.start}-${raw?.end}`, text: `第 ${i + 1} 個時段的 start/end 無效，已略過` });
                    return;
                }
                const id = raw.key === undefined || raw.key === null || raw.key === '' ? TimeGrid._formatTime(start) : raw.key;
                candidates.push({
                    id,
                    lookup: String(id),
                    label: raw.label === undefined || raw.label === null ? '' : String(raw.label),
                    start,
                    end,
                    order: i
                });
            });
            candidates.sort((a, b) => (a.start - b.start) || (a.order - b.order));
            const seen = new Set();
            for (const slot of candidates) {
                if (seen.has(slot.lookup)) {
                    problems.push({ key: `slot-dup:${slot.lookup}`, text: `時段 key "${slot.lookup}" 重複，已略過後者` });
                    continue;
                }
                const prev = list[list.length - 1];
                if (prev && slot.start < prev.end) {
                    problems.push({ key: `slot-overlap:${slot.lookup}`, text: `時段 "${slot.lookup}" 與前一時段重疊，已略過` });
                    continue;
                }
                seen.add(slot.lookup);
                list.push(slot);
            }
        } else {
            const range = this.options.timeRange && typeof this.options.timeRange === 'object' ? this.options.timeRange : DEFAULT_RANGE;
            let start = TimeGrid._parseTime(range.start);
            let end = TimeGrid._parseTime(range.end);
            let step = Math.round(Number(range.step));
            if (start === null || end === null || end <= start || !(step >= 1)) {
                problems.push({ key: `range-invalid:${range.start}-${range.end}-${range.step}`, text: `timeRange 無效，改用預設 ${DEFAULT_RANGE.start}–${DEFAULT_RANGE.end}／${DEFAULT_RANGE.step} 分鐘` });
                start = TimeGrid._parseTime(DEFAULT_RANGE.start);
                end = TimeGrid._parseTime(DEFAULT_RANGE.end);
                step = DEFAULT_RANGE.step;
            }
            for (let t = start; t < end && list.length < MAX_SLOTS; t += step) {
                const key = TimeGrid._formatTime(t);
                list.push({ id: key, lookup: key, label: '', start: t, end: Math.min(t + step, end) });
            }
        }
        this._slots = list;
        this._slotIndex = new Map(list.map((slot, i) => [slot.lookup, i]));
        return problems;
    }

    _resolveAll(problems) {
        this._resolved = new Map();
        this._byColumn = this._columns.map(() => []);
        for (const [key, rec] of this._items) {
            const result = this._resolve(rec);
            if (result.error) {
                problems.push(result.error);
                continue;
            }
            this._resolved.set(key, result);
            this._byColumn[result.col].push(key);
        }
    }

    _refreshItems(keys, extraCols) {
        const problems = [];
        const cols = new Set(extraCols);
        for (const key of keys) {
            const before = this._resolved.get(key);
            if (before) {
                cols.add(before.col);
                const list = this._byColumn[before.col];
                const at = list ? list.indexOf(key) : -1;
                if (at >= 0) list.splice(at, 1);
                this._resolved.delete(key);
            }
            const rec = this._items.get(key);
            if (!rec) continue;
            const result = this._resolve(rec);
            if (result.error) {
                problems.push(result.error);
                continue;
            }
            this._resolved.set(key, result);
            this._byColumn[result.col].push(key);
            cols.add(result.col);
        }
        this._report(problems);
        this._preserveFocus(() => this._layoutColumns([...cols]));
    }

    _resolve(rec) {
        const id = String(rec.id);
        const col = this._colIndex.get(String(rec.column));
        if (col === undefined) {
            return { error: { key: `column:${String(rec.column)}`, text: `欄 "${String(rec.column)}" 不存在`, item: id } };
        }
        const total = this._slots.length;
        if (!total) return { error: { key: 'no-slots', text: '沒有任何時段可放置項目', item: id } };
        const start = this._boundary(rec.start, 'start');
        if (start.error) return { error: { key: `${start.error}:${String(rec.start)}`, text: TimeGrid._boundaryText(start.error, rec.start), item: id } };
        const s = start.index;
        let e = s + 1;
        if (!(rec.end === undefined || rec.end === null || rec.end === '')) {
            const end = this._boundary(rec.end, 'end');
            if (end.error) return { error: { key: `${end.error}:${String(rec.end)}`, text: TimeGrid._boundaryText(end.error, rec.end), item: id } };
            e = end.index;
            if (e <= s) {
                const startTime = TimeGrid._parseTime(rec.start);
                const endTime = TimeGrid._parseTime(rec.end);
                const slots = this._slots;
                // 起訖相同（同一個 key 或零長度時間）視為佔一個時段
                if (e === s && (String(rec.end) === String(rec.start) || (startTime !== null && startTime === endTime))) {
                    e = s + 1;
                } else if (startTime !== null && endTime !== null && endTime > startTime) {
                    // 區間與任何時段都不重疊：在最前面之前（或最後面之後）算超出範圍，否則落在空檔
                    if (endTime <= slots[0].start || startTime >= slots[slots.length - 1].end) {
                        return { error: { key: `outside:${String(rec.end)}`, text: TimeGrid._boundaryText('outside', `${rec.start}–${rec.end}`), item: id } };
                    }
                    return { error: { key: `gap:${id}`, text: `項目 "${id}" 的 ${rec.start}–${rec.end} 落在時段之間的空檔`, item: id } };
                } else {
                    return { error: { key: `range:${id}`, text: `項目 "${id}" 的結束（${rec.end}）不晚於開始（${rec.start}）`, item: id } };
                }
            }
        }
        return { col, s, e: Math.min(e, total) };
    }

    _boundary(value, kind) {
        if (value === undefined || value === null || value === '') return { error: 'slot' };
        const byKey = this._slotIndex.get(String(value));
        if (byKey !== undefined) return { index: byKey };
        const t = TimeGrid._parseTime(value);
        if (t === null) return { error: 'slot' };
        const slots = this._slots;
        let lo = 0;
        let hi = slots.length;
        if (kind === 'start') {
            // 第一個 end > t 的時段（t 落在空檔時取下一個時段）
            while (lo < hi) {
                const mid = (lo + hi) >> 1;
                if (slots[mid].end > t) hi = mid; else lo = mid + 1;
            }
            return lo < slots.length ? { index: lo } : { error: 'outside' };
        }
        // 結束（不含）：start < t 的時段數；0 代表在第一個時段之前結束，由 _resolve 判斷是否為零長度
        while (lo < hi) {
            const mid = (lo + hi) >> 1;
            if (slots[mid].start < t) lo = mid + 1; else hi = mid;
        }
        return { index: lo };
    }

    _report(problems) {
        if (!problems || !problems.length) return;
        const groups = new Map();
        for (const problem of problems) {
            if (this._warned.has(problem.key)) continue;
            if (!groups.has(problem.key)) groups.set(problem.key, { text: problem.text, items: [] });
            if (problem.item !== undefined) groups.get(problem.key).items.push(problem.item);
        }
        if (!groups.size) return;
        const parts = [];
        for (const [key, group] of groups) {
            this._warned.add(key);
            const ids = group.items;
            const shown = ids.slice(0, 5).map((id) => `"${id}"`).join('、');
            parts.push(ids.length ? `${group.text}（項目 ${shown}${ids.length > 5 ? ` 等 ${ids.length} 筆` : ''}）` : group.text);
        }
        console.warn(`[TimeGrid] 已略過無法使用的資料：${parts.join('；')}`);
    }

    // ── 渲染 ──────────────────────────────────────────────────

    _buildShell() {
        const root = document.createElement('div');
        root.className = 'b4a-timegrid';
        root.style.cssText = 'position:relative; isolation:isolate; box-sizing:border-box; font-family:var(--cl-font-family); font-size:var(--cl-font-size-md); color:var(--cl-text); background:var(--cl-bg); border:1px solid var(--cl-border); border-radius:var(--cl-radius-md); overflow:hidden;';

        const scroller = document.createElement('div');
        scroller.className = 'b4a-timegrid__scroller';
        scroller.style.cssText = 'position:relative;';

        const grid = document.createElement('div');
        grid.className = 'b4a-timegrid__grid';
        grid.setAttribute('role', 'grid');
        grid.style.cssText = 'position:relative; user-select:none; -webkit-user-select:none;';
        grid.addEventListener('keydown', this._onKeyDown);
        grid.addEventListener('keyup', this._onKeyUp);
        grid.addEventListener('pointerdown', this._onPointerDown);
        grid.addEventListener('click', this._onGridClick);
        grid.addEventListener('focusin', this._onFocusIn);
        grid.addEventListener('focusout', this._onFocusOut);
        scroller.appendChild(grid);

        const live = document.createElement('div');
        live.className = 'b4a-timegrid__live';
        live.setAttribute('aria-live', 'polite');
        live.setAttribute('aria-atomic', 'true');
        live.style.cssText = VISUALLY_HIDDEN;

        root.append(scroller, live);
        this.element = root;
        this._scroller = scroller;
        this._grid = grid;
        this._live = live;
    }

    _render() {
        if (this._destroyed || !this._grid) return;
        const focusInfo = this._captureFocus();
        const activeKeys = this._keysAt(this._active.row, this._active.col);
        const selectionKeys = this._selectionKeys();
        this._restructuring = true;
        try {
            const problems = [...this._rebuildColumns(), ...this._rebuildSlots()];
            this._slotHeight = TimeGrid._positive(this.options.slotHeight, 40, 8);
            this._resolveAll(problems);
            this._report(problems);
            this._applySizing();
            const fragment = this._buildGrid();
            this._layoutColumns(this._allColumnIndexes());
            this._grid.textContent = '';
            this._grid.appendChild(fragment);
            this._restoreActive(activeKeys);
            this._restoreSelection(selectionKeys);
            this._refreshMove();
        } finally {
            this._restructuring = false;
        }
        this._restoreFocus(focusInfo);
        if (this._mounted) this._startNowTimer();
    }

    _applySizing() {
        const scroller = this._scroller;
        const height = this.options.height;
        if (this._hasFixedHeight()) {
            scroller.style.height = typeof height === 'number' ? `${height}px` : String(height);
            scroller.style.overflow = 'auto';
        } else {
            scroller.style.height = '';
            scroller.style.overflow = '';
            scroller.style.overflowX = 'auto';
        }
    }

    _buildGrid() {
        const grid = this._grid;
        const columns = this._columns;
        const slots = this._slots;
        const nCols = columns.length;
        const labelWidth = TimeGrid._positive(this.options.slotLabelWidth, 72, 24);
        const minWidth = TimeGrid._positive(this.options.columnMinWidth, 120, 20);
        const template = nCols ? `${labelWidth}px repeat(${nCols}, minmax(${minWidth}px, 1fr))` : `${labelWidth}px`;
        const selectable = this._canSelect();

        grid.setAttribute('aria-label', this.options.ariaLabel ? String(this.options.ariaLabel) : Locale.t('timeGrid.gridLabel'));
        grid.setAttribute('aria-rowcount', String(slots.length + 1));
        grid.setAttribute('aria-colcount', String(nCols + 1));
        if (selectable) grid.setAttribute('aria-multiselectable', 'true'); else grid.removeAttribute('aria-multiselectable');
        if (this.options.readonly) grid.setAttribute('aria-readonly', 'true'); else grid.removeAttribute('aria-readonly');
        if (this._pendingMove) grid.setAttribute('aria-busy', 'true'); else grid.removeAttribute('aria-busy');
        grid.style.minWidth = `${labelWidth + nCols * minWidth}px`;

        const fragment = document.createDocumentFragment();

        const header = document.createElement('div');
        header.className = 'b4a-timegrid__header';
        header.setAttribute('role', 'row');
        header.setAttribute('aria-rowindex', '1');
        header.style.cssText = `display:grid; grid-template-columns:${template}; position:sticky; top:0; z-index:${Z.header}; background:var(--cl-bg-secondary);`;
        const corner = document.createElement('div');
        corner.className = 'b4a-timegrid__corner';
        corner.setAttribute('role', 'columnheader');
        corner.setAttribute('aria-colindex', '1');
        corner.style.cssText = CORNER_CSS;
        corner.textContent = Locale.t('timeGrid.slotHeader');
        header.appendChild(corner);
        this._headerCells = columns.map((column, c) => {
            const cell = document.createElement('div');
            cell.className = 'b4a-timegrid__colheader';
            cell.setAttribute('role', 'columnheader');
            cell.setAttribute('aria-colindex', String(c + 2));
            cell.dataset.column = column.lookup;
            cell.style.cssText = COLUMN_HEADER_CSS;
            const label = document.createElement('span');
            label.className = 'b4a-timegrid__colheader-label';
            label.style.cssText = ELLIPSIS_CSS;
            label.textContent = column.label;
            cell.appendChild(label);
            if (column.sublabel) {
                const sub = document.createElement('span');
                sub.className = 'b4a-timegrid__colheader-sublabel';
                sub.style.cssText = `${ELLIPSIS_CSS} font-size:var(--cl-font-size-xs); font-weight:400; color:var(--cl-text-secondary);`;
                sub.textContent = column.sublabel;
                cell.appendChild(sub);
            }
            header.appendChild(cell);
            return cell;
        });
        this._headerRow = header;
        fragment.appendChild(header);

        const rowCss = `display:grid; grid-template-columns:${template}; height:${this._slotHeight}px;`;
        this._cells = new Array(slots.length * nCols);
        this._cover = new Array(slots.length * nCols).fill(null);
        this._rowEls = slots.map((slot, r) => {
            const row = document.createElement('div');
            row.className = 'b4a-timegrid__row';
            row.setAttribute('role', 'row');
            row.setAttribute('aria-rowindex', String(r + 2));
            row.dataset.slot = slot.lookup;
            row.style.cssText = rowCss;
            const rowHeader = document.createElement('div');
            rowHeader.className = 'b4a-timegrid__slot';
            rowHeader.setAttribute('role', 'rowheader');
            rowHeader.setAttribute('aria-colindex', '1');
            rowHeader.style.cssText = ROW_HEADER_CSS;
            rowHeader.textContent = this._slotLabel(slot);
            row.appendChild(rowHeader);
            for (let c = 0; c < nCols; c++) {
                const cell = document.createElement('div');
                cell.className = 'b4a-timegrid__cell';
                cell.setAttribute('role', 'gridcell');
                cell.setAttribute('aria-colindex', String(c + 2));
                cell.tabIndex = -1;
                cell.dataset.row = String(r);
                cell.dataset.col = String(c);
                cell.dataset.column = columns[c].lookup;
                cell.dataset.slot = slot.lookup;
                if (selectable) cell.setAttribute('aria-selected', 'false');
                cell.style.cssText = CELL_CSS;
                row.appendChild(cell);
                this._cells[r * nCols + c] = cell;
            }
            fragment.appendChild(row);
            return row;
        });

        // 舊的項目元素隨舊儲存格一起丟棄，重新建立
        this._itemEls = new Map();
        this._placements = new Map();
        this._colPlaced = columns.map(() => []);
        this._nowEls = [];
        return fragment;
    }

    _layoutColumns(cols) {
        const nCols = this._columns.length;
        const nRows = this._slots.length;
        const batch = [...new Set(cols)].filter((c) => Number.isInteger(c) && c >= 0 && c < nCols);
        if (!batch.length) return;
        const inBatch = new Set(batch);
        const placedKeys = new Set();
        const previous = new Set();
        for (const c of batch) {
            for (const key of this._colPlaced[c] || []) previous.add(key);
            for (let r = 0; r < nRows; r++) this._cover[r * nCols + c] = null;
            const solid = [];
            const ghost = [];
            for (const key of this._byColumn[c] || []) {
                const res = this._resolved.get(key);
                const rec = this._items.get(key);
                if (!res || !rec) continue;
                (rec.ghost ? ghost : solid).push({ key, col: c, s: res.s, e: res.e, lane: 0, lanes: 1, order: this._orderOf.get(key) || 0 });
            }
            TimeGrid._assignLanes(solid);
            TimeGrid._assignLanes(ghost);
            const placed = [];
            for (const entry of solid.concat(ghost)) {
                this._placements.set(entry.key, entry);
                placed.push(entry.key);
                placedKeys.add(entry.key);
                for (let r = entry.s; r < entry.e; r++) {
                    const idx = r * nCols + c;
                    if (this._cover[idx]) this._cover[idx].push(entry.key); else this._cover[idx] = [entry.key];
                }
            }
            this._colPlaced[c] = placed;
        }
        const move = this._state.snapshot().move;
        for (const key of placedKeys) this._syncItemElement(key, move);
        for (const key of previous) {
            if (placedKeys.has(key)) continue;
            const placement = this._placements.get(key);
            if (placement && !inBatch.has(placement.col)) continue;
            this._placements.delete(key);
            const el = this._itemEls.get(key);
            if (el) {
                el.remove();
                this._itemEls.delete(key);
            }
        }
        this._refreshMove();
    }

    _syncItemElement(key, move) {
        const rec = this._items.get(key);
        const placement = this._placements.get(key);
        if (!rec || !placement) return;
        let el = this._itemEls.get(key);
        if (!el) {
            el = document.createElement('button');
            el.type = 'button';
            el.tabIndex = -1;
            const title = document.createElement('span');
            title.className = 'b4a-timegrid__item-title';
            title.style.cssText = `${ELLIPSIS_CSS} font-size:var(--cl-font-size-sm); font-weight:600;`;
            const subtitle = document.createElement('span');
            subtitle.className = 'b4a-timegrid__item-subtitle';
            subtitle.style.cssText = `${ELLIPSIS_CSS} color:var(--cl-text-secondary);`;
            el.append(title, subtitle);
            this._itemEls.set(key, el);
        }
        el.className = rec.ghost ? 'b4a-timegrid__item b4a-timegrid__item--ghost' : 'b4a-timegrid__item';
        el.dataset.itemId = key;
        el.dataset.variant = VARIANT_TOKENS[rec.variant] ? rec.variant : 'primary';
        if (rec.ghost) el.dataset.ghost = 'true'; else delete el.dataset.ghost;
        el.firstChild.textContent = this._titleOf(rec);
        const subtitle = rec.subtitle === undefined || rec.subtitle === null ? '' : String(rec.subtitle);
        el.lastChild.textContent = subtitle;
        // inline display 會蓋過 hidden 屬性，直接切換 display
        el.lastChild.style.display = subtitle ? 'block' : 'none';
        const label = this._itemLabel(rec, placement);
        el.setAttribute('aria-label', label);
        el.title = label;
        this._restyleItem(key, move);
        const cell = this._cells[placement.s * this._columns.length + placement.col];
        if (cell && el.parentNode !== cell) cell.appendChild(el);
    }

    _itemCss(rec, placement) {
        const tokens = VARIANT_TOKENS[rec.variant] || VARIANT_TOKENS.primary;
        const lanes = Math.max(1, placement.lanes);
        const width = 100 / lanes;
        const left = placement.lane * width;
        const height = Math.max(4, (placement.e - placement.s) * this._slotHeight - 3);
        const ghost = !!rec.ghost;
        const movable = this._isMovable(rec);
        return [
            'position:absolute',
            'top:1px',
            `left:calc(${left.toFixed(4)}% + 2px)`,
            `width:calc(${width.toFixed(4)}% - 4px)`,
            `height:${height}px`,
            `z-index:${ghost ? Z.ghost : Z.item}`,
            'box-sizing:border-box',
            'margin:0',
            'appearance:none',
            '-webkit-appearance:none',
            'display:flex',
            'flex-direction:column',
            'align-items:stretch',
            'justify-content:flex-start',
            'gap:1px',
            'padding:2px 6px',
            'overflow:hidden',
            'text-align:left',
            'font-family:inherit',
            'font-size:var(--cl-font-size-xs)',
            'line-height:1.3',
            'color:var(--cl-text)',
            `background:${tokens.fill}`,
            // 左側 3px 色條；ghost 為四邊虛線（用 border-width/style/color 長寫，避免 var() 在單邊簡寫的相容問題）
            `border-width:${ghost ? '1px 1px 1px 3px' : '0 0 0 3px'}`,
            `border-style:${ghost ? 'dashed' : 'solid'}`,
            `border-color:${tokens.accent}`,
            'border-radius:var(--cl-radius-sm)',
            `opacity:${ghost ? '0.7' : '1'}`,
            `cursor:${movable ? 'grab' : 'pointer'}`,
            movable ? 'touch-action:none' : ''
        ].filter(Boolean).join('; ') + ';';
    }

    _restyleItem(key, move = this._state.snapshot().move) {
        const el = this._itemEls.get(key);
        const rec = this._items.get(key);
        const placement = this._placements.get(key);
        if (!el || !rec || !placement) return;
        const moving = !!move && move.key === key;
        let css = this._itemCss(rec, placement);
        if (moving && move.via === 'pointer') css += ' opacity:0.4;';
        const focused = typeof document !== 'undefined' && document.activeElement === el;
        if (focused) css += ` outline:2px ${moving ? 'dashed' : 'solid'} var(--cl-border-focus); outline-offset:1px; z-index:${Z.focus};`;
        else if (moving) css += ' outline:2px dashed var(--cl-primary); outline-offset:1px;';
        el.style.cssText = css;
        if (this._isMovable(rec)) el.setAttribute('aria-grabbed', moving ? 'true' : 'false');
        else el.removeAttribute('aria-grabbed');
    }

    _slotLabel(slot) {
        return slot.label || Locale.t('timeGrid.timeRange', { start: TimeGrid._formatTime(slot.start), end: TimeGrid._formatTime(slot.end) });
    }

    _titleOf(rec) {
        const title = rec ? rec.title : '';
        return title === undefined || title === null || title === '' ? Locale.t('timeGrid.untitled') : String(title);
    }

    _itemLabel(rec, placement) {
        const params = {
            title: this._titleOf(rec),
            column: this._columns[placement.col].label,
            time: this._itemTimeText(rec, placement)
        };
        const subtitle = rec.subtitle === undefined || rec.subtitle === null ? '' : String(rec.subtitle);
        let label = subtitle
            ? Locale.t('timeGrid.itemLabelWithSubtitle', { ...params, subtitle })
            : Locale.t('timeGrid.itemLabel', params);
        if (rec.ghost) label = Locale.t('timeGrid.ghostLabel', { label });
        return label;
    }

    _itemTimeText(rec, placement) {
        const slots = this._slots;
        const startTime = this._slotIndex.has(String(rec.start)) ? null : TimeGrid._parseTime(rec.start);
        const hasEnd = !(rec.end === undefined || rec.end === null || rec.end === '');
        const endTime = hasEnd && !this._slotIndex.has(String(rec.end)) ? TimeGrid._parseTime(rec.end) : null;
        const start = startTime !== null ? startTime : slots[placement.s].start;
        const end = endTime !== null && endTime > start ? endTime : slots[placement.e - 1].end;
        return Locale.t('timeGrid.timeRange', { start: TimeGrid._formatTime(start), end: TimeGrid._formatTime(end) });
    }

    _rangeParams(col, s, e) {
        return {
            column: this._columns[col] ? this._columns[col].label : '',
            time: Locale.t('timeGrid.timeRange', {
                start: TimeGrid._formatTime(this._slots[s].start),
                end: TimeGrid._formatTime(this._slots[e - 1].end)
            })
        };
    }

    _describe(col, s, e) {
        const slots = this._slots;
        return {
            column: this._columns[col].id,
            start: slots[s].id,
            end: e < slots.length ? slots[e].id : TimeGrid._formatTime(slots[e - 1].end),
            startTime: TimeGrid._formatTime(slots[s].start),
            endTime: TimeGrid._formatTime(slots[e - 1].end),
            slots: slots.slice(s, e).map((slot) => slot.id)
        };
    }

    // ── 選取 ──────────────────────────────────────────────────

    _canSelect() {
        return !!this.options.selectable && !this.options.readonly;
    }

    _isMovable(rec) {
        return !!rec && !!this.options.editable && !this.options.readonly && rec.draggable !== false;
    }

    _isOccupied(row, col) {
        const list = this._cover[row * this._columns.length + col];
        if (!list) return false;
        for (const key of list) {
            const rec = this._items.get(key);
            if (rec && !rec.ghost) return true;
        }
        return false;
    }

    _itemsAt(row, col) {
        const list = this._cover[row * this._columns.length + col];
        if (!list) return [];
        const rank = (key) => (this._items.get(key)?.ghost ? 1 : 0);
        return list.slice().sort((a, b) => (rank(a) - rank(b)) || ((this._placements.get(a)?.lane || 0) - (this._placements.get(b)?.lane || 0)));
    }

    _setSelection(selection) {
        const previous = this._state.snapshot().selection;
        this._state.send('SELECT', { selection });
        this._paintSelection(previous, selection);
    }

    _paintSelection(previous, next) {
        const nCols = this._columns.length;
        const paint = (sel, on) => {
            if (!sel) return;
            const [lo, hi] = TimeGrid._span(sel);
            for (let r = lo; r <= hi; r++) {
                const cell = this._cells[r * nCols + sel.col];
                if (!cell) continue;
                if (on) {
                    const shadows = ['inset 2px 0 0 var(--cl-primary)', 'inset -2px 0 0 var(--cl-primary)'];
                    if (r === lo) shadows.push('inset 0 2px 0 var(--cl-primary)');
                    if (r === hi) shadows.push('inset 0 -2px 0 var(--cl-primary)');
                    cell.style.background = 'var(--cl-bg-active)';
                    cell.style.boxShadow = shadows.join(', ');
                } else {
                    cell.style.background = 'var(--cl-bg)';
                    cell.style.boxShadow = '';
                }
                if (cell.hasAttribute('aria-selected')) cell.setAttribute('aria-selected', on ? 'true' : 'false');
            }
        };
        paint(previous, false);
        paint(next, true);
    }

    _clampToFree(col, anchor, target) {
        const step = target >= anchor ? 1 : -1;
        let row = anchor;
        while (row !== target) {
            const next = row + step;
            if (this._isOccupied(next, col)) break;
            row = next;
        }
        return row;
    }

    _startKeyboardSelection(row, col) {
        if (this._isOccupied(row, col)) return;
        this._setSelection({ col, anchor: row, focus: row, confirmed: false, via: 'keyboard' });
        this._announce(Locale.t('timeGrid.selectionStarted', this._rangeParams(col, row, row + 1)));
    }

    _extendKeyboardSelection(row, col, key) {
        let sel = this._state.snapshot().selection;
        if (!sel || sel.confirmed || sel.via !== 'keyboard' || sel.col !== col) {
            if (this._isOccupied(row, col)) return false;
            sel = { col, anchor: row, focus: row, confirmed: false, via: 'keyboard' };
        }
        const delta = { ArrowUp: -1, ArrowDown: 1, PageUp: -PAGE_STEP, PageDown: PAGE_STEP }[key] || 0;
        const wanted = TimeGrid._clamp(sel.focus + delta, 0, this._slots.length - 1);
        const focus = this._clampToFree(col, sel.anchor, wanted);
        this._setSelection({ ...sel, focus });
        this._focusCellAt(focus, col);
        const [lo, hi] = TimeGrid._span({ anchor: sel.anchor, focus });
        this._announce(Locale.t('timeGrid.selectionUpdated', this._rangeParams(col, lo, hi + 1)));
        return true;
    }

    _confirmSelection() {
        const sel = this._state.snapshot().selection;
        if (!sel) return;
        this._setSelection({ ...sel, confirmed: true });
        const [lo, hi] = TimeGrid._span(sel);
        const payload = this._describe(sel.col, lo, hi + 1);
        this._announce(Locale.t('timeGrid.selectionConfirmed', this._rangeParams(sel.col, lo, hi + 1)));
        if (typeof this.options.onSelect === 'function') this.options.onSelect(payload);
    }

    _cancelSelection(announce) {
        if (!this._state.snapshot().selection) return;
        this._setSelection(null);
        if (announce) this._announce(Locale.t('timeGrid.selectionCancelled'));
    }

    _selectionKeys() {
        const sel = this._state.snapshot().selection;
        if (!sel || !this._columns[sel.col]) return null;
        return {
            column: this._columns[sel.col].lookup,
            anchor: this._slots[sel.anchor]?.lookup,
            focus: this._slots[sel.focus]?.lookup,
            confirmed: sel.confirmed,
            via: sel.via
        };
    }

    _restoreSelection(keys) {
        this._state.send('SELECT', { selection: null });
        if (!keys || !this._canSelect()) return;
        const col = this._colIndex.get(keys.column);
        const anchor = this._slotIndex.get(keys.anchor);
        const focus = this._slotIndex.get(keys.focus);
        if (col === undefined || anchor === undefined || focus === undefined) return;
        this._setSelection({ col, anchor, focus, confirmed: keys.confirmed, via: keys.via });
    }

    // ── 移動 ──────────────────────────────────────────────────

    _beginMove(key, via) {
        const rec = this._items.get(key);
        const placement = this._placements.get(key);
        if (!rec || !placement || !this._isMovable(rec) || this._state.snapshot().mode !== 'idle' || this._pendingMove) return false;
        const move = {
            key,
            via,
            source: { col: placement.col, s: placement.s, e: placement.e },
            target: { col: placement.col, s: placement.s },
            pending: false
        };
        this._state.send('MOVE_BEGIN', { via, move });
        this._restyleItem(key, move);
        this._showDrop(move);
        return true;
    }

    _setMoveTarget(col, s) {
        const move = this._state.snapshot().move;
        if (!move || move.pending) return false;
        const span = move.source.e - move.source.s;
        const target = {
            col: TimeGrid._clamp(col, 0, this._columns.length - 1),
            s: TimeGrid._clamp(s, 0, Math.max(0, this._slots.length - span))
        };
        if (target.col === move.target.col && target.s === move.target.s) return false;
        this._state.send('MOVE_SYNC', { target });
        this._showDrop({ ...move, target });
        return true;
    }

    _commitMove() {
        const move = this._state.snapshot().move;
        if (!move || move.pending) return;
        const rec = this._items.get(move.key);
        if (!rec) {
            this._finishMove(move.key);
            return;
        }
        const { source, target } = move;
        const span = source.e - source.s;
        if (target.col === source.col && target.s === source.s) {
            this._finishMove(move.key);
            this._announce(Locale.t('timeGrid.moveCancelled', { title: this._titleOf(rec) }));
            return;
        }
        const from = this._describe(source.col, source.s, source.e);
        const to = this._describe(target.col, target.s, target.s + span);
        const token = ++this._moveToken;
        this._pendingMove = { token, key: move.key, via: move.via, source, target, span, touched: false };
        this._state.send('MOVE_PENDING');
        this._grid.setAttribute('aria-busy', 'true');
        this._showDrop({ ...move, pending: true });
        this._announce(Locale.t('timeGrid.movePending', { title: this._titleOf(rec) }));

        const handler = this.options.onItemMove;
        let result = true;
        if (typeof handler === 'function') {
            try {
                result = handler({ item: { ...rec }, from, to });
            } catch (error) {
                console.warn('[TimeGrid] onItemMove 拋出錯誤，項目已放回原位', error);
                this._settleMove(token, false);
                return;
            }
        }
        if (result && typeof result.then === 'function') {
            result.then(
                (value) => this._settleMove(token, value !== false),
                (error) => {
                    console.warn('[TimeGrid] onItemMove 的 Promise 被拒絕，項目已放回原位', error);
                    this._settleMove(token, false);
                }
            );
        } else {
            this._settleMove(token, result !== false);
        }
    }

    _settleMove(token, accepted) {
        if (this._destroyed) return;
        const pending = this._pendingMove;
        if (!pending || pending.token !== token) return;
        this._pendingMove = null;
        if (this._grid) this._grid.removeAttribute('aria-busy');
        const rec = this._items.get(pending.key);
        const el = this._itemEls.get(pending.key);
        // 只在焦點仍在該項目上（或鍵盤移動後焦點遺失）時把焦點帶回項目，不搶走使用者已移開的焦點
        const active = document.activeElement;
        const hadFocus = (!!el && active === el) || (pending.via === 'keyboard' && (!active || active === document.body));
        const title = this._titleOf(rec);
        this._state.send('MOVE_END');
        this._hideDrop();
        let targetParams = this._columns[pending.target.col] && pending.target.s + pending.span <= this._slots.length
            ? this._rangeParams(pending.target.col, pending.target.s, pending.target.s + pending.span)
            : { column: '', time: '' };
        if (accepted && rec && !pending.touched) {
            // 呼叫端接受且期間未自行改動該項目：由元件更新自己的狀態
            Object.assign(rec, this._movedPosition(rec, pending));
            this._itemAnchor = null;
            this._refreshItems([pending.key], [pending.source.col]);
            const placement = this._placements.get(pending.key);
            if (placement) targetParams = { column: this._columns[placement.col].label, time: this._itemTimeText(rec, placement) };
        } else {
            this._restyleItem(pending.key, null);
        }
        this._announce(accepted
            ? Locale.t('timeGrid.moveAccepted', { title, ...targetParams })
            : Locale.t('timeGrid.moveRejected', { title }));
        if (hadFocus) {
            const target = this._itemEls.get(pending.key);
            if (target && target.isConnected) {
                target.focus({ preventScroll: true });
                this._restyleItem(pending.key, null);
            }
        }
    }

    _movedPosition(rec, pending) {
        const slots = this._slots;
        const { source, target, span } = pending;
        const ts = target.s;
        const te = target.s + span;
        const startIsKey = this._slotIndex.has(String(rec.start));
        const hasEnd = !(rec.end === undefined || rec.end === null || rec.end === '');
        const endIsKey = hasEnd && this._slotIndex.has(String(rec.end));
        const endKey = te < slots.length ? slots[te].id : TimeGrid._formatTime(slots[te - 1].end);
        const snapped = {
            column: this._columns[target.col].id,
            start: startIsKey ? slots[ts].id : TimeGrid._formatTime(slots[ts].start)
        };
        if (hasEnd) snapped.end = endIsKey ? endKey : TimeGrid._formatTime(slots[te - 1].end);
        // 時間字串項目：先嘗試整段平移（保留原本長度與非整點的起訖），落點不符目標時段才對齊時段邊界
        if (startIsKey && (!hasEnd || endIsKey)) return snapped;
        const delta = slots[ts].start - slots[source.s].start;
        const shift = (value) => {
            const minutes = TimeGrid._parseTime(value);
            if (minutes === null) return null;
            const moved = minutes + delta;
            return moved >= 0 && moved <= 1440 ? TimeGrid._formatTime(moved) : null;
        };
        const candidate = { ...snapped };
        if (!startIsKey) candidate.start = shift(rec.start);
        if (hasEnd && !endIsKey) candidate.end = shift(rec.end);
        if (candidate.start === null || candidate.end === null) return snapped;
        const check = this._resolve({ ...rec, ...candidate });
        return !check.error && check.col === target.col && check.s === ts && check.e === te ? candidate : snapped;
    }

    _finishMove(key) {
        this._state.send('MOVE_END');
        this._hideDrop();
        if (key !== undefined) this._restyleItem(key, null);
    }

    _cancelMove(announce) {
        const move = this._state.snapshot().move;
        if (!move || move.pending) return;
        this._finishMove(move.key);
        if (announce) this._announce(Locale.t('timeGrid.moveCancelled', { title: this._titleOf(this._items.get(move.key)) }));
    }

    _refreshMove() {
        const move = this._state.snapshot().move;
        if (!move) {
            this._hideDrop();
            return;
        }
        const placement = this._placements.get(move.key);
        if (!placement) {
            if (move.pending) {
                this._hideDrop();
                return;
            }
            if (this._press && this._press.key === move.key) this._endPress();
            this._finishMove();
            this._announce(Locale.t('timeGrid.moveCancelled', { title: this._titleOf(this._items.get(move.key)) }));
            return;
        }
        if (move.pending) {
            const pending = this._pendingMove;
            if (pending) this._showDrop({ key: move.key, source: pending.source, target: pending.target, pending: true });
            this._restyleItem(move.key, move);
            return;
        }
        const source = { col: placement.col, s: placement.s, e: placement.e };
        const span = source.e - source.s;
        const target = {
            col: TimeGrid._clamp(move.target.col, 0, this._columns.length - 1),
            s: TimeGrid._clamp(move.target.s, 0, Math.max(0, this._slots.length - span))
        };
        this._state.send('MOVE_SYNC', { source, target });
        const synced = { ...move, source, target };
        this._restyleItem(move.key, synced);
        this._showDrop(synced);
    }

    _touchPending(key) {
        if (this._pendingMove && (key === null || this._pendingMove.key === key)) this._pendingMove.touched = true;
    }

    _showDrop(move) {
        const nCols = this._columns.length;
        const cell = this._cells[move.target.s * nCols + move.target.col];
        if (!cell) return;
        let el = this._dropEl;
        if (!el) {
            el = document.createElement('div');
            el.className = 'b4a-timegrid__drop';
            el.setAttribute('aria-hidden', 'true');
            this._dropEl = el;
        }
        const span = move.source.e - move.source.s;
        el.style.cssText = `position:absolute; top:1px; left:2px; right:2px; height:${Math.max(4, span * this._slotHeight - 3)}px; box-sizing:border-box; padding:2px 6px; border:2px dashed var(--cl-primary); border-radius:var(--cl-radius-sm); background:var(--cl-primary-soft-subtle); color:var(--cl-primary-dark); font-size:var(--cl-font-size-xs); overflow:hidden; white-space:nowrap; text-overflow:ellipsis; pointer-events:none; z-index:${Z.drop}; opacity:${move.pending ? '0.6' : '1'};`;
        el.textContent = this._titleOf(this._items.get(move.key));
        if (el.parentNode !== cell) cell.appendChild(el);
    }

    _hideDrop() {
        if (this._dropEl && this._dropEl.parentNode) this._dropEl.remove();
    }

    // ── 鍵盤 ──────────────────────────────────────────────────

    _handleKeyDown(event) {
        if (this._destroyed || event.defaultPrevented || event.isComposing || event.altKey) return;
        if (this._press) return; // 指標操作進行中：Escape 由 document 監聽處理
        const mode = this._state.snapshot().mode;
        if (mode === 'grabbed') {
            this._handleGrabKey(event);
            return;
        }
        // 等待 onItemMove 期間（pending）仍可瀏覽；_beginMove 會拒絕再拿起其他項目
        const target = event.target instanceof Element ? event.target : null;
        if (!target || !this._grid) return;
        const itemEl = target.closest('[data-item-id]');
        if (itemEl && this._grid.contains(itemEl)) {
            this._handleItemKey(event, itemEl);
            return;
        }
        const cell = target.closest('[role="gridcell"]');
        if (cell && this._grid.contains(cell)) this._handleCellKey(event, cell);
    }

    _handleKeyUp(event) {
        // <button> 會在 Space 放開時觸發 click；可移動的項目以 Space 拿起，這裡擋掉原生啟動
        if (event.key !== ' ' && event.key !== 'Spacebar') return;
        const itemEl = event.target instanceof Element ? event.target.closest('[data-item-id]') : null;
        if (itemEl && this._isMovable(this._items.get(itemEl.dataset.itemId))) event.preventDefault();
    }

    _handleCellKey(event, cell) {
        const row = Number(cell.dataset.row);
        const col = Number(cell.dataset.col);
        const key = event.key;
        const sel = this._state.snapshot().selection;
        const pendingKeyboardSelection = !!sel && !sel.confirmed && sel.via === 'keyboard';
        if (event.shiftKey && this._canSelect() && (key === 'ArrowUp' || key === 'ArrowDown' || key === 'PageUp' || key === 'PageDown')) {
            event.preventDefault();
            if (this._extendKeyboardSelection(row, col, key)) return;
        }
        if (NAV_KEYS.has(key)) {
            if (pendingKeyboardSelection) this._cancelSelection(false);
            this._navigate(event, { row, col });
            return;
        }
        if (key === 'Enter') {
            event.preventDefault();
            if (pendingKeyboardSelection && sel.col === col) {
                this._confirmSelection();
                return;
            }
            const covering = this._itemsAt(row, col);
            if (covering.length) {
                this._enterItem(covering[0], { row, col });
                return;
            }
            this._emitCellClick(row, col, event);
            if (!this._destroyed && this._canSelect()) this._startKeyboardSelection(row, col);
            return;
        }
        if (key === ' ' || key === 'Spacebar') {
            event.preventDefault();
            return;
        }
        if (key === 'Escape' && sel) {
            event.preventDefault();
            this._cancelSelection(true);
        }
    }

    _handleItemKey(event, itemEl) {
        const key = itemEl.dataset.itemId;
        const rec = this._items.get(key);
        if (!rec) return;
        const anchor = this._anchorFor(key);
        switch (event.key) {
            case 'Enter':
                event.preventDefault();
                this._emitItemClick(rec, event);
                return;
            case ' ':
            case 'Spacebar':
                // 不可移動的項目保留按鈕原生行為（Space 放開時 click → onItemClick）
                if (!this._isMovable(rec)) return;
                event.preventDefault();
                if (this._beginMove(key, 'keyboard')) this._announce(Locale.t('timeGrid.grabbed', { title: this._titleOf(rec) }));
                return;
            case 'Escape':
                event.preventDefault();
                this._focusCellAt(anchor.row, anchor.col);
                return;
            case 'ArrowLeft':
            case 'ArrowRight': {
                const list = this._itemsAt(anchor.row, anchor.col);
                const at = list.indexOf(key);
                const next = at < 0 ? -1 : at + (event.key === 'ArrowRight' ? 1 : -1);
                if (at >= 0 && next >= 0 && next < list.length) {
                    event.preventDefault();
                    this._enterItem(list[next], anchor);
                    return;
                }
                this._navigate(event, anchor);
                return;
            }
            default:
                if (NAV_KEYS.has(event.key)) this._navigate(event, anchor);
        }
    }

    _handleGrabKey(event) {
        const move = this._state.snapshot().move;
        if (!move) return;
        const { col, s } = move.target;
        switch (event.key) {
            case 'ArrowUp': event.preventDefault(); this._keyboardTarget(col, s - 1); return;
            case 'ArrowDown': event.preventDefault(); this._keyboardTarget(col, s + 1); return;
            case 'ArrowLeft': event.preventDefault(); this._keyboardTarget(col - 1, s); return;
            case 'ArrowRight': event.preventDefault(); this._keyboardTarget(col + 1, s); return;
            case 'PageUp': event.preventDefault(); this._keyboardTarget(col, s - PAGE_STEP); return;
            case 'PageDown': event.preventDefault(); this._keyboardTarget(col, s + PAGE_STEP); return;
            case 'Home': event.preventDefault(); this._keyboardTarget(0, s); return;
            case 'End': event.preventDefault(); this._keyboardTarget(this._columns.length - 1, s); return;
            case 'Enter': event.preventDefault(); this._commitMove(); return;
            case 'Escape': event.preventDefault(); this._cancelMove(true); return;
            case ' ':
            case 'Spacebar': event.preventDefault(); return;
            case 'Tab': this._cancelMove(true); return;
            default:
        }
    }

    _keyboardTarget(col, s) {
        if (!this._setMoveTarget(col, s)) return;
        const move = this._state.snapshot().move;
        const span = move.source.e - move.source.s;
        // 讓落點跟著捲入可視範圍（落點可能在捲動區外的欄或時段）
        const cell = this._cells[move.target.s * this._columns.length + move.target.col];
        if (cell) this._revealCell(move.target.s, cell);
        this._announce(Locale.t('timeGrid.moveTarget', this._rangeParams(move.target.col, move.target.s, move.target.s + span)));
    }

    _navigate(event, origin) {
        const nRows = this._slots.length;
        const nCols = this._columns.length;
        if (!nRows || !nCols) return;
        let { row, col } = origin;
        switch (event.key) {
            case 'ArrowUp': row -= 1; break;
            case 'ArrowDown': row += 1; break;
            case 'ArrowLeft': col -= 1; break;
            case 'ArrowRight': col += 1; break;
            case 'Home': col = 0; if (event.ctrlKey || event.metaKey) row = 0; break;
            case 'End': col = nCols - 1; if (event.ctrlKey || event.metaKey) row = nRows - 1; break;
            case 'PageUp': row -= PAGE_STEP; break;
            case 'PageDown': row += PAGE_STEP; break;
            default: return;
        }
        event.preventDefault();
        this._focusCellAt(TimeGrid._clamp(row, 0, nRows - 1), TimeGrid._clamp(col, 0, nCols - 1));
    }

    _anchorFor(key) {
        if (this._itemAnchor && this._itemAnchor.key === key) return this._itemAnchor;
        const placement = this._placements.get(key);
        return placement ? { key, row: placement.s, col: placement.col } : { key, row: this._active.row, col: this._active.col };
    }

    _enterItem(key, anchor) {
        const el = this._itemEls.get(key);
        if (!el) return;
        this._itemAnchor = { key, row: anchor.row, col: anchor.col };
        this._setActive(anchor.row, anchor.col, { focus: false });
        el.focus({ preventScroll: true });
        const cell = this._cells[anchor.row * this._columns.length + anchor.col];
        if (cell) this._revealCell(anchor.row, cell);
    }

    // ── 焦點與捲動 ────────────────────────────────────────────

    _setActive(row, col, { focus = true } = {}) {
        const nCols = this._columns.length;
        const nRows = this._slots.length;
        if (!nCols || !nRows) return;
        row = TimeGrid._clamp(row, 0, nRows - 1);
        col = TimeGrid._clamp(col, 0, nCols - 1);
        const previous = this._cells[this._active.row * nCols + this._active.col];
        if (previous && (this._active.row !== row || this._active.col !== col)) previous.tabIndex = -1;
        this._active = { row, col };
        const cell = this._cells[row * nCols + col];
        if (!cell) return;
        cell.tabIndex = 0;
        if (focus) {
            cell.focus({ preventScroll: true });
            this._revealCell(row, cell);
        }
    }

    _focusCellAt(row, col) {
        this._itemAnchor = null;
        this._setActive(row, col, { focus: true });
    }

    _restoreActive(keys) {
        const nCols = this._columns.length;
        const nRows = this._slots.length;
        if (!nCols || !nRows) {
            this._active = { row: 0, col: 0 };
            return;
        }
        const col = keys ? this._colIndex.get(keys.column) : undefined;
        const row = keys ? this._slotIndex.get(keys.slot) : undefined;
        const next = {
            row: row !== undefined ? row : TimeGrid._clamp(this._active.row, 0, nRows - 1),
            col: col !== undefined ? col : TimeGrid._clamp(this._active.col, 0, nCols - 1)
        };
        this._active = next;
        const cell = this._cells[next.row * nCols + next.col];
        if (cell) cell.tabIndex = 0;
    }

    _keysAt(row, col) {
        const column = this._columns[col];
        const slot = this._slots[row];
        return column && slot ? { column: column.lookup, slot: slot.lookup } : null;
    }

    _revealCell(row, cell) {
        const scroller = this._scroller;
        if (!scroller) return;
        if (!this._hasFixedHeight() && typeof cell.scrollIntoView === 'function') {
            cell.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        }
        const headerHeight = this._headerRow ? this._headerRow.offsetHeight : 0;
        if (this._hasFixedHeight()) {
            const top = row * this._slotHeight;
            const viewHeight = scroller.clientHeight - headerHeight;
            if (top < scroller.scrollTop) scroller.scrollTop = top;
            else if (viewHeight > 0 && top + this._slotHeight > scroller.scrollTop + viewHeight) scroller.scrollTop = top + this._slotHeight - viewHeight;
        }
        const labelWidth = TimeGrid._positive(this.options.slotLabelWidth, 72, 24);
        const left = cell.offsetLeft;
        const right = left + cell.offsetWidth;
        if (left - labelWidth < scroller.scrollLeft) scroller.scrollLeft = Math.max(0, left - labelWidth);
        else if (scroller.clientWidth > 0 && right > scroller.scrollLeft + scroller.clientWidth) scroller.scrollLeft = right - scroller.clientWidth;
    }

    _hasFixedHeight() {
        const height = this.options.height;
        return !(height === null || height === undefined || height === '');
    }

    _captureFocus() {
        if (typeof document === 'undefined' || !this._grid) return null;
        const active = document.activeElement;
        if (!active || !this._grid.contains(active)) return null;
        const itemEl = active.closest('[data-item-id]');
        return { item: itemEl ? itemEl.dataset.itemId : null };
    }

    _restoreFocus(info) {
        if (!info || this._destroyed || !this._grid || !this._grid.isConnected) return;
        if (info.item) {
            const el = this._itemEls.get(info.item);
            if (el && el.isConnected) {
                if (document.activeElement !== el) el.focus({ preventScroll: true });
                this._restyleItem(info.item);
                return;
            }
        }
        const cell = this._cells[this._active.row * this._columns.length + this._active.col];
        if (cell && cell.isConnected && document.activeElement !== cell) {
            this._itemAnchor = null;
            cell.focus({ preventScroll: true });
        }
    }

    _preserveFocus(work) {
        const info = this._captureFocus();
        this._restructuring = true;
        try {
            work();
        } finally {
            this._restructuring = false;
        }
        this._restoreFocus(info);
    }

    _handleFocusIn(event) {
        const target = event.target instanceof Element ? event.target : null;
        if (!target || this._destroyed) return;
        const itemEl = target.closest('[data-item-id]');
        if (itemEl) {
            const key = itemEl.dataset.itemId;
            const anchor = this._anchorFor(key);
            this._itemAnchor = anchor;
            this._setActive(anchor.row, anchor.col, { focus: false });
            this._hideRing();
            this._restyleItem(key);
            return;
        }
        if (target.getAttribute('role') === 'gridcell') {
            this._itemAnchor = null;
            this._setActive(Number(target.dataset.row), Number(target.dataset.col), { focus: false });
            this._showRing(target);
        }
    }

    _handleFocusOut(event) {
        if (this._destroyed || !this._grid) return;
        const target = event.target instanceof Element ? event.target : null;
        const itemEl = target ? target.closest('[data-item-id]') : null;
        const next = event.relatedTarget;
        const inside = next instanceof Node && this._grid.contains(next);
        if (itemEl) {
            // 焦點離開後才重算樣式（此時 activeElement 已不是它）
            const key = itemEl.dataset.itemId;
            const restyle = () => { if (!this._destroyed) this._restyleItem(key); };
            if (typeof queueMicrotask === 'function') queueMicrotask(restyle); else Promise.resolve().then(restyle);
        }
        if (!inside) {
            this._hideRing();
            if (!this._restructuring && this._state.snapshot().mode === 'grabbed') this._cancelMove(true);
        }
    }

    _showRing(cell) {
        let ring = this._ring;
        if (!ring) {
            ring = document.createElement('div');
            ring.className = 'b4a-timegrid__focus';
            ring.setAttribute('aria-hidden', 'true');
            ring.style.cssText = `position:absolute; top:0; right:0; bottom:0; left:0; box-sizing:border-box; border:2px solid var(--cl-border-focus); pointer-events:none; z-index:${Z.focus};`;
            this._ring = ring;
        }
        if (ring.parentNode !== cell) cell.appendChild(ring);
    }

    _hideRing() {
        if (this._ring && this._ring.parentNode) this._ring.remove();
    }

    // ── 指標 ──────────────────────────────────────────────────

    _handlePointerDown(event) {
        if (this._destroyed || event.button !== 0 || this._press) return;
        const mode = this._state.snapshot().mode;
        if (mode === 'grabbed') this._cancelMove(true);
        const target = event.target instanceof Element ? event.target : null;
        if (!target || !this._grid) return;
        const itemEl = target.closest('[data-item-id]');
        if (itemEl && this._grid.contains(itemEl)) {
            const key = itemEl.dataset.itemId;
            const rec = this._items.get(key);
            if (this._isMovable(rec) && this._placements.has(key) && !this._pendingMove) {
                this._press = { kind: 'item', key, pointerId: event.pointerId, x: event.clientX, y: event.clientY, started: false, offset: 0, el: itemEl };
                this._listenDocument(true);
            }
            return;
        }
        const cell = target.closest('[role="gridcell"]');
        if (!cell || !this._grid.contains(cell) || !this._canSelect()) return;
        const row = Number(cell.dataset.row);
        const col = Number(cell.dataset.col);
        if (this._isOccupied(row, col)) return;
        this._press = { kind: 'select', pointerId: event.pointerId, col, anchor: row };
        this._setSelection({ col, anchor: row, focus: row, confirmed: false, via: 'pointer' });
        this._listenDocument(true);
    }

    _handleDocPointerMove(event) {
        const press = this._press;
        if (!press || event.pointerId !== press.pointerId) return;
        this._lastPointer = { x: event.clientX, y: event.clientY };
        if (press.kind === 'item') {
            if (!press.started) {
                if (Math.hypot(event.clientX - press.x, event.clientY - press.y) < DRAG_THRESHOLD) return;
                if (!this._beginMove(press.key, 'pointer')) {
                    this._endPress();
                    return;
                }
                press.started = true;
                const placement = this._placements.get(press.key);
                const hit = this._hitTest(press.x, press.y);
                press.offset = hit && placement ? TimeGrid._clamp(hit.row - placement.s, 0, placement.e - placement.s - 1) : 0;
                if (this._grid) this._grid.style.cursor = 'grabbing';
                if (press.el && typeof press.el.setPointerCapture === 'function') {
                    try { press.el.setPointerCapture(event.pointerId); } catch (_) { /* 不支援時改靠 document 監聽 */ }
                }
            }
            this._dragTo(event.clientX, event.clientY, press.offset);
        } else {
            this._selectTo(event.clientX, event.clientY);
        }
        this._updateEdgeScroll(event.clientX, event.clientY);
    }

    _handleDocPointerUp(event) {
        const press = this._press;
        if (!press || event.pointerId !== press.pointerId) return;
        if (press.kind === 'item') {
            if (press.started) this._dragTo(event.clientX, event.clientY, press.offset);
            this._endPress();
            if (press.started) {
                this._armClickGuard();
                this._commitMove();
            }
            return;
        }
        this._selectTo(event.clientX, event.clientY);
        this._endPress();
        this._confirmSelection();
    }

    _handleDocPointerCancel(event) {
        const press = this._press;
        if (!press || event.pointerId !== press.pointerId) return;
        this._endPress();
        if (press.kind === 'item') {
            if (press.started) this._cancelMove(false);
        } else {
            this._cancelSelection(false);
        }
    }

    _handleDocKeyDown(event) {
        const press = this._press;
        if (!press || event.key !== 'Escape') return;
        event.preventDefault();
        this._endPress();
        if (press.kind === 'item') {
            if (press.started) {
                this._armClickGuard();
                this._cancelMove(true);
            }
        } else {
            this._cancelSelection(true);
        }
    }

    _dragTo(x, y, offset = 0) {
        const hit = this._hitTest(x, y);
        if (!hit) return;
        this._setMoveTarget(hit.col, hit.row - offset);
    }

    _selectTo(x, y) {
        const sel = this._state.snapshot().selection;
        const hit = this._hitTest(x, y);
        if (!sel || !hit) return;
        const focus = this._clampToFree(sel.col, sel.anchor, hit.row);
        if (focus !== sel.focus) this._setSelection({ ...sel, focus });
    }

    _hitTest(x, y) {
        const first = this._cells[0];
        if (!first || typeof x !== 'number' || typeof y !== 'number') return null;
        const rect = first.getBoundingClientRect();
        if (!rect || !(rect.width > 0)) return null;
        return {
            col: TimeGrid._clamp(Math.floor((x - rect.left) / rect.width), 0, this._columns.length - 1),
            row: TimeGrid._clamp(Math.floor((y - rect.top) / this._slotHeight), 0, this._slots.length - 1)
        };
    }

    _listenDocument(on) {
        if (on === this._docListening || typeof document === 'undefined') return;
        this._docListening = on;
        const method = on ? 'addEventListener' : 'removeEventListener';
        document[method]('pointermove', this._onDocPointerMove);
        document[method]('pointerup', this._onDocPointerUp);
        document[method]('pointercancel', this._onDocPointerCancel);
        document[method]('keydown', this._onDocKeyDown);
    }

    _endPress() {
        const press = this._press;
        this._press = null;
        this._listenDocument(false);
        this._stopEdgeScroll();
        if (this._grid) this._grid.style.cursor = '';
        if (press && press.started && press.el && typeof press.el.releasePointerCapture === 'function') {
            try { press.el.releasePointerCapture(press.pointerId); } catch (_) { /* 已釋放 */ }
        }
    }

    _armClickGuard() {
        // 拖放結束後瀏覽器仍會送出 click；略過這一次，避免誤觸 onItemClick/onCellClick
        this._suppressClick = true;
        clearTimeout(this._clickGuardTimer);
        this._clickGuardTimer = setTimeout(() => {
            this._suppressClick = false;
            this._clickGuardTimer = null;
        }, 0);
    }

    _updateEdgeScroll(x, y) {
        const scroller = this._scroller;
        if (!scroller || !this._press) return;
        const rect = scroller.getBoundingClientRect();
        if (!rect || !(rect.height > 0) || !(rect.width > 0)) return;
        const headerHeight = this._headerRow ? this._headerRow.offsetHeight : 0;
        const labelWidth = TimeGrid._positive(this.options.slotLabelWidth, 72, 24);
        let dy = 0;
        let dx = 0;
        if (this._hasFixedHeight()) {
            if (y < rect.top + headerHeight + EDGE_ZONE) dy = -EDGE_SPEED;
            else if (y > rect.bottom - EDGE_ZONE) dy = EDGE_SPEED;
        }
        if (x < rect.left + labelWidth + EDGE_ZONE) dx = -EDGE_SPEED;
        else if (x > rect.right - EDGE_ZONE) dx = EDGE_SPEED;
        this._edge = { dx, dy };
        if ((dx || dy) && !this._raf && typeof requestAnimationFrame === 'function') {
            this._raf = requestAnimationFrame(this._onEdgeFrame);
        }
    }

    _edgeFrame() {
        this._raf = 0;
        const edge = this._edge;
        const scroller = this._scroller;
        if (!this._press || !edge || !scroller || (!edge.dx && !edge.dy)) return;
        const top = scroller.scrollTop;
        const left = scroller.scrollLeft;
        scroller.scrollTop = top + edge.dy;
        scroller.scrollLeft = left + edge.dx;
        if ((scroller.scrollTop !== top || scroller.scrollLeft !== left) && this._lastPointer) {
            if (this._press.kind === 'item') {
                if (this._press.started) this._dragTo(this._lastPointer.x, this._lastPointer.y, this._press.offset);
            } else {
                this._selectTo(this._lastPointer.x, this._lastPointer.y);
            }
        }
        this._raf = requestAnimationFrame(this._onEdgeFrame);
    }

    _stopEdgeScroll() {
        this._edge = null;
        if (this._raf && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(this._raf);
        this._raf = 0;
    }

    _handleClick(event) {
        if (this._destroyed) return;
        if (this._suppressClick) {
            this._suppressClick = false;
            return;
        }
        const target = event.target instanceof Element ? event.target : null;
        if (!target || !this._grid) return;
        const itemEl = target.closest('[data-item-id]');
        if (itemEl && this._grid.contains(itemEl)) {
            const rec = this._items.get(itemEl.dataset.itemId);
            if (rec) this._emitItemClick(rec, event);
            return;
        }
        const cell = target.closest('[role="gridcell"]');
        if (cell && this._grid.contains(cell)) this._emitCellClick(Number(cell.dataset.row), Number(cell.dataset.col), event);
    }

    _emitItemClick(rec, event) {
        if (typeof this.options.onItemClick === 'function') this.options.onItemClick({ ...rec }, event);
    }

    _emitCellClick(row, col, event) {
        if (typeof this.options.onCellClick !== 'function') return;
        const slot = this._slots[row];
        const column = this._columns[col];
        if (!slot || !column) return;
        this.options.onCellClick({
            column: column.id,
            slot: slot.id,
            startTime: TimeGrid._formatTime(slot.start),
            endTime: TimeGrid._formatTime(slot.end)
        }, event);
    }

    // ── 播報與目前時間 ────────────────────────────────────────

    _announce(message) {
        if (!this._live || !message || this._destroyed) return;
        // 節流：冷卻期間只保留最後一則，冷卻結束時才寫入（不讀時鐘，只靠計時器）
        if (this._announceTimer) {
            this._announcePending = message;
            return;
        }
        this._writeLive(message);
        this._armAnnounceCooldown();
    }

    _armAnnounceCooldown() {
        this._announceTimer = setTimeout(() => {
            this._announceTimer = null;
            const pending = this._announcePending;
            this._announcePending = '';
            if (!pending || this._destroyed) return;
            this._writeLive(pending);
            this._armAnnounceCooldown();
        }, ANNOUNCE_INTERVAL);
    }

    _writeLive(message) {
        if (!this._live) return;
        // 相同字串連續播報時補一個不換行空白，讓螢幕報讀器視為新內容
        this._live.textContent = this._live.textContent === message ? `${message} ` : message;
    }

    _startNowTimer() {
        this._stopNowTimer();
        if (!this.options.nowIndicator || this._destroyed || !this._mounted) return;
        if (!this._columns.some((column) => column.date)) return;
        this._updateNow();
        this._nowTimer = setInterval(this._onNowTick, NOW_INTERVAL);
    }

    _stopNowTimer() {
        if (this._nowTimer) clearInterval(this._nowTimer);
        this._nowTimer = null;
    }

    _updateNow() {
        if (this._destroyed) return;
        for (const el of this._nowEls) el.remove();
        this._nowEls = [];
        let now;
        try {
            now = typeof this.options.now === 'function' ? this.options.now() : new Date();
        } catch (error) {
            console.warn('[TimeGrid] now() 發生錯誤，略過本次目前時間更新', error);
            return;
        }
        if (!(now instanceof Date)) now = new Date(now);
        if (Number.isNaN(now.getTime())) return;
        const today = TimeGrid._dateKey(now);
        const todayColumns = [];
        this._columns.forEach((column, c) => {
            const isToday = !!column.date && column.date === today;
            const header = this._headerCells[c];
            if (header) {
                if (isToday) {
                    header.setAttribute('aria-current', 'date');
                    header.style.color = 'var(--cl-primary)';
                    header.style.boxShadow = 'inset 0 -2px 0 var(--cl-primary)';
                } else if (header.hasAttribute('aria-current')) {
                    header.removeAttribute('aria-current');
                    header.style.color = '';
                    header.style.boxShadow = '';
                }
            }
            if (isToday) todayColumns.push(c);
        });
        if (!todayColumns.length) return;
        const minutes = now.getHours() * 60 + now.getMinutes();
        const slots = this._slots;
        let row = -1;
        let fraction = 0;
        for (let i = 0; i < slots.length; i++) {
            if (minutes < slots[i].start) {
                if (i > 0) row = i; // 落在空檔：畫在下一個時段的頂端
                break;
            }
            if (minutes < slots[i].end) {
                row = i;
                fraction = (minutes - slots[i].start) / (slots[i].end - slots[i].start);
                break;
            }
        }
        if (row < 0) return;
        const nCols = this._columns.length;
        for (const c of todayColumns) {
            const cell = this._cells[row * nCols + c];
            if (!cell) continue;
            const line = document.createElement('div');
            line.className = 'b4a-timegrid__now';
            line.setAttribute('aria-hidden', 'true');
            line.dataset.time = TimeGrid._formatTime(minutes);
            line.style.cssText = `position:absolute; left:0; right:0; top:${(fraction * 100).toFixed(4)}%; height:2px; margin-top:-1px; background:var(--cl-danger); pointer-events:none; z-index:${Z.now};`;
            const dot = document.createElement('div');
            dot.style.cssText = 'position:absolute; left:-4px; top:-3px; width:8px; height:8px; border-radius:var(--cl-radius-round); background:var(--cl-danger);';
            line.appendChild(dot);
            cell.appendChild(line);
            this._nowEls.push(line);
        }
    }

    _allColumnIndexes() {
        return this._columns.map((_, i) => i);
    }

    // ── 靜態工具（底線開頭：不列入公開方法）──────────────────

    static _parseTime(value) {
        if (typeof value !== 'string') return null;
        const match = TIME_PATTERN.exec(value.trim());
        if (!match) return null;
        const hours = Number(match[1]);
        const minutes = Number(match[2]);
        if (minutes > 59 || hours > 24 || (hours === 24 && minutes !== 0)) return null;
        return hours * 60 + minutes;
    }

    static _formatTime(total) {
        const minutes = Math.max(0, Math.round(total));
        return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
    }

    static _dateKey(date) {
        return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    }

    static _boundaryText(kind, value) {
        return kind === 'outside' ? `時間 "${value}" 超出時段範圍` : `時段 "${value}" 不存在`;
    }

    static _clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    static _positive(value, fallback, min) {
        const number = Number(value);
        return Number.isFinite(number) && number >= min ? number : fallback;
    }

    static _span(sel) {
        return sel.anchor <= sel.focus ? [sel.anchor, sel.focus] : [sel.focus, sel.anchor];
    }

    /**
     * 區間分道：依開始排序後，以兩個最小堆（進行中項目依結束時間、空出的道依編號）
     * 指派最小可用道；同一重疊群組內的項目共用群組的總道數。O(n log n)。
     */
    static _assignLanes(entries) {
        if (!entries.length) return;
        entries.sort((a, b) => (a.s - b.s) || (b.e - a.e) || (a.order - b.order));
        const active = [];
        const free = [];
        const byEnd = (a, b) => (a.e - b.e) || (a.lane - b.lane);
        const byNumber = (a, b) => a - b;
        let cluster = [];
        let clusterLanes = 0;
        for (const entry of entries) {
            while (active.length && active[0].e <= entry.s) {
                TimeGrid._heapPush(free, TimeGrid._heapPop(active, byEnd).lane, byNumber);
            }
            if (!active.length && cluster.length) {
                for (const member of cluster) member.lanes = clusterLanes;
                cluster = [];
                clusterLanes = 0;
                free.length = 0;
            }
            const lane = free.length ? TimeGrid._heapPop(free, byNumber) : clusterLanes++;
            entry.lane = lane;
            TimeGrid._heapPush(active, { e: entry.e, lane }, byEnd);
            cluster.push(entry);
        }
        for (const member of cluster) member.lanes = clusterLanes;
    }

    static _heapPush(heap, value, compare) {
        heap.push(value);
        let i = heap.length - 1;
        while (i > 0) {
            const parent = (i - 1) >> 1;
            if (compare(heap[i], heap[parent]) >= 0) break;
            [heap[i], heap[parent]] = [heap[parent], heap[i]];
            i = parent;
        }
    }

    static _heapPop(heap, compare) {
        const top = heap[0];
        const last = heap.pop();
        if (heap.length) {
            heap[0] = last;
            let i = 0;
            for (;;) {
                const left = 2 * i + 1;
                const right = left + 1;
                let smallest = i;
                if (left < heap.length && compare(heap[left], heap[smallest]) < 0) smallest = left;
                if (right < heap.length && compare(heap[right], heap[smallest]) < 0) smallest = right;
                if (smallest === i) break;
                [heap[i], heap[smallest]] = [heap[smallest], heap[i]];
                i = smallest;
            }
        }
        return top;
    }
}

TimeGrid.VARIANTS = Object.freeze(Object.keys(VARIANT_TOKENS));

export default TimeGrid;
