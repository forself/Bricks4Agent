import { createComponentState } from '../../utils/component-state.js';
import { TextInput } from '../../form/TextInput/index.js';

/**
 * EditableTable — 可編輯 + 可排序表格(複合)。表頭點擊排序(確定性比較);可編輯格用 TextInput 原子。
 */
export class EditableTable {
    constructor(options = {}) {
        this.options = { columns: [], rows: [], onCellEdit: null, ...options };
        this.element = null;
        this._inputs = [];
        this._state = createComponentState(
            {
                lifecycle: 'created', visibility: 'visible',
                sortKey: null, sortDir: 'asc',
                rows: (Array.isArray(this.options.rows) ? this.options.rows : []).map((r) => ({ ...r }))
            },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SORT: (s, p) => {
                    const dir = s.sortKey === p.key && s.sortDir === 'asc' ? 'desc' : 'asc';
                    const rows = [...s.rows].sort((a, b) => EditableTable._cmp(a[p.key], b[p.key]) * (dir === 'asc' ? 1 : -1));
                    return { ...s, sortKey: p.key, sortDir: dir, rows };
                },
                EDIT: (s, p) => {
                    const rows = s.rows.map((r, i) => i === p.index ? { ...r, [p.key]: p.value } : r);
                    return { ...s, rows };
                },
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._create();
        this._render();
    }

    /** 儲存格共用樣式(CSSOM,CSP 合規) */
    static _CELL_CSS = 'border: 1px solid var(--cl-border); padding: 6px 10px; text-align: left; font-size: var(--cl-font-size-sm);';

    static _cmp(a, b) {
        const na = Number(a), nb = Number(b);
        if (!Number.isNaN(na) && !Number.isNaN(nb)) return na - nb;
        return String(a ?? '').localeCompare(String(b ?? ''));
    }

    _create() {
        this.element = document.createElement('table');
        this.element.className = 'cl-edtable';
        this.element.style.cssText = 'width: 100%; border-collapse: collapse; font-family: var(--cl-font-family);';
        this._thead = document.createElement('thead');
        this._tbody = document.createElement('tbody');
        this.element.appendChild(this._thead);
        this.element.appendChild(this._tbody);
    }

    _render() {
        this._inputs.forEach((i) => i.destroy?.());
        this._inputs = [];
        const cols = Array.isArray(this.options.columns) ? this.options.columns : [];
        const s = this.snapshot();

        const htr = document.createElement('tr');
        cols.forEach((col) => {
            const th = document.createElement('th');
            th.style.cssText = EditableTable._CELL_CSS + ' background: var(--cl-bg-secondary);';
            th.textContent = String(col.label ?? col.key);
            if (col.sortable) {
                th.className = 'cl-edtable--sortable';
                th.style.cursor = 'pointer';
                th.style.userSelect = 'none';
                if (s.sortKey === col.key) th.textContent += s.sortDir === 'asc' ? ' ▲' : ' ▼';
                th.addEventListener('click', () => this.send('SORT', { key: col.key }));
            }
            htr.appendChild(th);
        });
        this._thead.replaceChildren(htr);

        this._tbody.replaceChildren();
        s.rows.forEach((row, index) => {
            const tr = document.createElement('tr');
            tr.addEventListener('mouseenter', () => { tr.style.background = 'var(--cl-bg-hover)'; });
            tr.addEventListener('mouseleave', () => { tr.style.background = ''; });
            cols.forEach((col) => {
                const td = document.createElement('td');
                td.style.cssText = EditableTable._CELL_CSS;
                if (col.editable) {
                    const input = new TextInput({ value: String(row[col.key] ?? '') });
                    input.mount(td);
                    if (input.input) { input.input.style.width = '100%'; input.input.style.boxSizing = 'border-box'; }
                    input.input?.addEventListener('change', () => {
                        const value = input.getValue();
                        this.send('EDIT', { index, key: col.key, value });
                        if (typeof this.options.onCellEdit === 'function') this.options.onCellEdit(index, col.key, value);
                    });
                    this._inputs.push(input);
                } else {
                    td.textContent = String(row[col.key] ?? '');
                }
                tr.appendChild(td);
            });
            this._tbody.appendChild(tr);
        });
        if (this.element) this.element.style.display = s.visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._render(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[EditableTable] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getRows() { return this.snapshot().rows.map((r) => ({ ...r })); }
    destroy() { this._inputs.forEach((i) => i.destroy?.()); this._inputs = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default EditableTable;
