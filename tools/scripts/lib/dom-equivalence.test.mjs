/**
 * dom-equivalence 自身的測試。
 *
 * 重點不是「工具說相等」，而是「工具在該報錯時真的會報錯」——先前的版本在
 * fake DOM 下把 style 逐屬性賦值完全看不見，於是 EditableTable 的等價比對
 * 會沉默通過。這裡每個正向案例都配一個負向對照。
 */
import test from 'node:test';
import assert from 'node:assert/strict';

import { installTestDom } from '../../../packages/javascript/browser/ui_components/common/TreeList/test-dom.mjs';
import { serializeDom, compareDom } from './dom-equivalence.mjs';

installTestDom();

const { EditableTable } = await import(
    '../../../packages/javascript/browser/ui_components/layout/EditableTable/EditableTable.js'
);

const COLUMNS = [
    { key: 'name', label: '姓名', editable: true },
    { key: 'unit', label: '單位', sortable: true },
];
const ROWS = [
    { name: '王小明', unit: '偵查科' },
    { name: '李美華', unit: '資訊科' },
];

function makeTable(rows = ROWS) {
    return new EditableTable({ columns: COLUMNS, rows: rows.map((r) => ({ ...r })) });
}

test('逐屬性 style 賦值必須被偵測到（先前的沉默通過案例）', () => {
    const table = makeTable();
    try {
        const before = serializeDom(table.element);

        // EditableTable 用的就是這種寫法（th.style.cursor、tr.style.background…）。
        // 舊版只讀 style.cssText，而 fake DOM 的 cssText 不會被逐屬性賦值更新，
        // 因此 before/after 會完全相同——斷言恆綠。
        table._tbody.children[0].style.background = 'var(--cl-bg-hover)';
        const after = serializeDom(table.element);

        assert.notEqual(after, before, '逐屬性 style 變更未被序列化捕捉');
        assert.match(after, /background: var\(--cl-bg-hover\)/);
    } finally {
        table.destroy();
    }
});

test('還原後回到等價，不產生偽陽性', () => {
    const table = makeTable();
    try {
        const before = serializeDom(table.element);
        const tr = table._tbody.children[0];
        tr.style.background = 'var(--cl-bg-hover)';
        assert.notEqual(serializeDom(table.element), before);

        tr.style.background = '';
        assert.equal(serializeDom(table.element), before, '還原後應回到原序列化結果');
    } finally {
        table.destroy();
    }
});

test('cssText 與逐屬性兩種來源都納入，且順序無關', () => {
    const table = makeTable();
    try {
        const th = table._thead.children[0].children[1]; // sortable 欄，cssText + cursor/userSelect
        const sig = serializeDom(th);
        assert.match(sig, /cursor: pointer/, 'cssText 之外的逐屬性值遺失');
        assert.match(sig, /border: 1px solid var\(--cl-border\)/, 'cssText 來源的宣告遺失');

        // 宣告順序不應影響比對結果
        const a = { style: { cssText: 'color: red; background: blue;' } };
        const b = { style: { cssText: 'background: blue; color: red;' } };
        a.tagName = b.tagName = 'DIV';
        assert.equal(serializeDom(a), serializeDom(b), '樣式宣告順序不應造成差異');
    } finally {
        table.destroy();
    }
});

test('定向更新（_patchCell）與完整重建的 DOM 等價', () => {
    const targeted = makeTable();
    const rebuilt = makeTable([{ name: '王大明', unit: '偵查科' }, { name: '李美華', unit: '資訊科' }]);
    try {
        // 走定點更新路徑：patchable 成立時 EDIT 不會整表重繪
        targeted.send('EDIT', { index: 0, key: 'name', value: '王大明' });

        const result = compareDom(targeted.element, rebuilt.element);
        assert.equal(result.equal, true, `定向更新與重建在第 ${result.line} 行不等價：`
            + `\n  實際：${result.actual}\n  預期：${result.expected}`);
    } finally {
        targeted.destroy();
        rebuilt.destroy();
    }
});

test('負向對照：內容真的不同時 compareDom 必須回報不等價', () => {
    const a = makeTable();
    const b = makeTable([{ name: '完全不同', unit: '偵查科' }, { name: '李美華', unit: '資訊科' }]);
    try {
        const result = compareDom(a.element, b.element);
        assert.equal(result.equal, false, 'compareDom 對明顯不同的樹回報等價——斷言是空的');
        assert.ok(result.line > 0, '不等價時應指出差異行號');
        assert.match(result.actual, /王小明/, '差異報告應指出實際值');
        assert.match(result.expected, /完全不同/, '差異報告應指出預期值');
    } finally {
        a.destroy();
        b.destroy();
    }
});
