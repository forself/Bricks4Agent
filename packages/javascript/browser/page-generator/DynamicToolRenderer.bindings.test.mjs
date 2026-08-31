/**
 * DynamicToolRenderer 綁定套用的行為測試。
 *
 * 背景：消費端（如 Custom Component Studio 的 syncUi）習慣在每次同步把整份
 * state 攤平後對每個葉路徑呼叫 setState。在加上相等性檢查之前，每一次都會
 * 無條件呼叫 setter；而沒有對應 setter 的綁定會走 _replaceComponent——
 * 也就是整個元件銷毀重建。
 *
 * 這裡用替身元件隔離綁定層，正向案例都配負向對照，確保斷言不是空過。
 */
import test from 'node:test';
import assert from 'node:assert/strict';

import { installTestDom } from '../ui_components/common/TreeList/test-dom.mjs';

installTestDom();

const { DynamicToolRenderer } = await import('./DynamicToolRenderer.js');

/** 有 setLabel 的元件：綁定會走 setter 路徑 */
class Labelled {
    static created = 0;
    constructor(options = {}) {
        Labelled.created += 1;
        this.options = options;
        this.element = null;
        this.setLabelCalls = [];
    }
    mount(host) {
        this.element = document.createElement('div');
        this.element.className = 'labelled';
        this.element.textContent = String(this.options.label ?? '');
        host.appendChild(this.element);
        return this;
    }
    setLabel(value) {
        this.setLabelCalls.push(value);
        if (this.element) this.element.textContent = String(value ?? '');
    }
    destroy() { this.element?.remove?.(); this.element = null; }
}

/** 沒有任何 setter 的元件：綁定只能靠 _replaceComponent 更新 */
class Frozen {
    static created = 0;
    constructor(options = {}) {
        Frozen.created += 1;
        this.options = options;
        this.element = null;
    }
    mount(host) {
        this.element = document.createElement('div');
        this.element.className = 'frozen';
        this.element.textContent = String(this.options.caption ?? '');
        host.appendChild(this.element);
        return this;
    }
    destroy() { this.element?.remove?.(); this.element = null; }
}

const factory = {
    create(name, options) {
        if (name === 'Labelled') return new Labelled(options);
        if (name === 'Frozen') return new Frozen(options);
        return null;
    },
    has(name) { return name === 'Labelled' || name === 'Frozen'; },
};

function definition(component, optionName) {
    return {
        schema_version: 1,
        name: 'BindingProbePage',
        type: 'tool',
        root: {
            type: 'component',
            id: 'probe',
            component,
            options: {},
            bindings: { [optionName]: 'model.text' },
        },
    };
}

async function mountRenderer(component, optionName, initial = 'A') {
    Labelled.created = 0;
    Frozen.created = 0;
    const renderer = new DynamicToolRenderer({
        definition: definition(component, optionName),
        state: { model: { text: initial } },
        factory,
    });
    await renderer.init();
    const host = document.createElement('div');
    renderer.mount(host);
    return { renderer, host };
}

test('值沒變時不呼叫 setter', async () => {
    const { renderer } = await mountRenderer('Labelled', 'label');
    try {
        const instance = renderer.getComponent('probe');
        assert.equal(instance.setLabelCalls.length, 0, '掛載後不應套用綁定');

        renderer.setState('model.text', 'A');   // 同值
        assert.deepEqual(instance.setLabelCalls, [], '同值仍呼叫了 setter');

        renderer.setState('model.text', 'A');   // 再一次同值
        assert.deepEqual(instance.setLabelCalls, [], '重複同值仍呼叫了 setter');
    } finally {
        renderer.destroy();
    }
});

test('負向對照：值真的改變時必須呼叫 setter', async () => {
    const { renderer } = await mountRenderer('Labelled', 'label');
    try {
        const instance = renderer.getComponent('probe');
        renderer.setState('model.text', 'B');
        assert.deepEqual(instance.setLabelCalls, ['B'], '值改變卻沒套用——相等性檢查過度跳過');
        assert.equal(instance.element.textContent, 'B');

        renderer.setState('model.text', 'B');   // 回到同值，不應再推
        assert.deepEqual(instance.setLabelCalls, ['B']);

        renderer.setState('model.text', 'C');
        assert.deepEqual(instance.setLabelCalls, ['B', 'C']);
    } finally {
        renderer.destroy();
    }
});

test('物件值以內容比較，不是以參考比較', async () => {
    Labelled.created = 0;
    const renderer = new DynamicToolRenderer({
        definition: {
            schema_version: 1,
            name: 'BindingProbePage',
            type: 'tool',
            root: {
                type: 'component',
                id: 'probe',
                component: 'Labelled',
                options: {},
                bindings: { label: 'model.rows' },
            },
        },
        state: { model: { rows: [{ id: 1, name: '甲' }] } },
        factory,
    });
    await renderer.init();
    renderer.mount(document.createElement('div'));
    try {
        const instance = renderer.getComponent('probe');
        // 內容相同但是全新的物件：不應觸發套用
        renderer.setState('model.rows', [{ id: 1, name: '甲' }]);
        assert.equal(instance.setLabelCalls.length, 0, '內容相同的新物件被當成有變動');

        // 深處差一個字：必須觸發
        renderer.setState('model.rows', [{ id: 1, name: '乙' }]);
        assert.equal(instance.setLabelCalls.length, 1, '深層差異沒被偵測到');

        // 鍵順序不同：保守地視為有變動（元件可能依 Object.entries 順序渲染）
        renderer.setState('model.rows', [{ name: '乙', id: 1 }]);
        assert.equal(instance.setLabelCalls.length, 2, '鍵順序改變應保守地重新套用');
    } finally {
        renderer.destroy();
    }
});

test('沒有 setter 的綁定：同值不再銷毀重建元件', async () => {
    const { renderer } = await mountRenderer('Frozen', 'caption');
    try {
        assert.equal(Frozen.created, 1, '掛載應只建構一次');
        const first = renderer.getComponent('probe');

        renderer.setState('model.text', 'A');   // 同值
        assert.equal(Frozen.created, 1, '同值仍重建了元件');
        assert.equal(renderer.getComponent('probe'), first, '同值仍替換了實例');
    } finally {
        renderer.destroy();
    }
});

test('負向對照：沒有 setter 的綁定，值改變時仍必須重建', async () => {
    const { renderer } = await mountRenderer('Frozen', 'caption');
    try {
        const first = renderer.getComponent('probe');
        renderer.setState('model.text', 'B');

        assert.equal(Frozen.created, 2, '值改變卻沒重建——綁定更新遺失');
        const second = renderer.getComponent('probe');
        assert.notEqual(second, first, '應換成新實例');
        assert.equal(second.options.caption, 'B', '新實例應帶著新值');
        assert.equal(second.element.textContent, 'B');

        // 重建後的紀錄已種下新值，再送同值不應又重建一次
        renderer.setState('model.text', 'B');
        assert.equal(Frozen.created, 2, '重建後同值又重建了一次');
    } finally {
        renderer.destroy();
    }
});

test('套用時仍會同步 instance.options', async () => {
    const { renderer } = await mountRenderer('Labelled', 'label');
    try {
        const instance = renderer.getComponent('probe');
        assert.equal(instance.options.label, 'A');
        renderer.setState('model.text', 'B');
        assert.equal(instance.options.label, 'B', 'options 未同步');
    } finally {
        renderer.destroy();
    }
});

test('綁定路徑不存在時仍然拋錯（相等性檢查不得吞掉）', async () => {
    const { renderer } = await mountRenderer('Labelled', 'label');
    try {
        // 覆蓋掉父層，使綁定路徑消失；此路徑與 model.text 重疊，會觸發套用
        assert.throws(() => renderer.setState('model', {}), /Missing bound state path/);
    } finally {
        renderer.destroy();
    }
});
