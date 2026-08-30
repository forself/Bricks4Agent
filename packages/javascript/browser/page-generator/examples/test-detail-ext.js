import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';

import { installFakeDom } from '../../../../../tools/scripts/lib/fake-dom.mjs';
import { DynamicPageRenderer } from '../DynamicPageRenderer.js';

const detailDefinition = {
    schemaVersion: 1,
    page: {
        id: 'detail.tim-gang',
        title: '幫派組合詳情',
        view: 'detail',
        permissionKey: 'TIMGang',
    },
    fields: [],
    detail: {
        routeParam: 'ColValue',
        sourceRoot: 'GangData',
        titleSource: 'GangData.HostName',
        layout: 'tabs-with-steps',
        steps: [
            { id: 'report', title: '陳報', source: ['GangData.SubmitPolice', 'GangData.SubmitDate'], order: 1 },
            { id: 'review', title: '審轉', source: ['GangData.AuditPolice', 'GangData.AuditDate'], order: 2 },
        ],
        mainFields: [
            { id: 'searchUnit', label: '蒐報單位', source: 'GangData.SearchUnit' },
            { id: 'gangId', label: '幫派組合', source: 'GangData.GangID' },
            { id: 'areas', label: '活動地區', source: 'sActAreas' },
        ],
        tabs: [
            { id: 'basic', key: '1', title: '基本資料', kind: 'main', order: 1 },
            { id: 'members', key: '2', title: '成員', kind: 'subtable', order: 2 },
            { id: 'attachments', key: '3', title: '附件', kind: 'attachment', order: 3 },
            { id: 'history', key: '4', title: '紀錄', kind: 'history', order: 4 },
            { id: 'orgChart', key: '5', title: '組織架構圖', kind: 'chart', order: 5 },
        ],
        subtables: [
            {
                id: 'secondaryActivityAreas',
                title: '次要活動處所',
                source: 'sActAreas',
                fields: [{ label: '次要活動處所', source: 'SActAreaID' }],
            },
            {
                id: 'relationMember',
                title: '成員',
                source: 'Member/GetByGangID/{ColValue}',
                tabId: 'members',
                fields: [
                    { label: '姓名', source: 'Name' },
                    { label: '身分證字號', source: 'IDNo' },
                ],
            },
        ],
        attachments: [
            {
                id: 'gangAttachments',
                component: 'Attachment',
                tabId: 'attachments',
                tableName: 'TIM_GANG',
                tablePk: 'GangData.GangNo',
                action: 'SEARCH',
            },
        ],
        history: [
            {
                id: 'gangRecord',
                component: 'Record',
                tabId: 'history',
                tableName: 'TIM_GANG',
                tablePk: 'GangData.GangNo',
            },
        ],
        media: [
            {
                id: 'gangOrgChart',
                component: 'OrgChartComponent',
                source: 'GangChart/GetGraph/{GangID}',
                tabId: 'orgChart',
            },
        ],
        actions: [
            {
                id: 'getWord',
                label: '列印',
                component: 'BrowseButtons',
                tablePk: 'GangData.GangNo',
                route: 'Gang',
            },
        ],
    },
    fixtures: { sampleRow: {} },
};

const detailData = {
    GangData: {
        HostName: '仁義會',
        GangNo: 'GAN-001',
        GangID: 'G001',
        SearchUnit: '第一分局',
        SubmitPolice: '送件分局',
        SubmitDate: '115/07/28',
        AuditPolice: '審轉分局',
        AuditDate: '115/07/29',
    },
    sActAreas: [{ SActAreaID: '北區據點' }],
    relationMember: [
        { Name: '王小明', IDNo: 'A123456789' },
    ],
};

function byClass(root, className) {
    return root.querySelectorAll(`.${className}`);
}

function textOf(node) {
    if (!node) return '';
    const own = node.textContent || node.innerHTML || '';
    return `${own}${(node.children || []).map(child => textOf(child)).join('')}`;
}

function findByDataset(nodes, key, value) {
    return nodes.find(node => node.dataset?.[key] === value);
}

function activateTab(root, tabId) {
    const button = findByDataset(byClass(root, 'dynamic-detail__tab'), 'tabId', tabId);
    assert.ok(button, `tab button not found: ${tabId}`);
    button.dispatchEvent({ type: 'click' });
    return button;
}

export async function runDetailExtTests() {
    const results = [];
    const t = async (name, fn) => {
        const dom = installFakeDom();
        try {
            await fn(dom);
            results.push({ name, pass: true });
        } catch (error) {
            results.push({ name, pass: false, error });
        } finally {
            dom.cleanup();
        }
    };

    await t('detail renderer renders data and emits host descriptors for attachments, history, media, and actions', async () => {
        const renderer = new DynamicPageRenderer({
            definition: detailDefinition,
            mode: 'detail',
            data: detailData,
            routeParams: { ColValue: 'fixture-1' },
        });
        await renderer.init();
        const detail = renderer.getRenderer();
        const root = detail.element;

        assert.equal(root.dataset.routeParamName, 'ColValue');
        assert.equal(root.dataset.routeParamValue, 'fixture-1');
        assert.match(textOf(root), /仁義會/);
        assert.match(textOf(root), /送件分局/);

        const tabs = byClass(root, 'dynamic-detail__tab');
        assert.deepEqual(tabs.map(tab => tab.textContent), ['基本資料', '成員', '附件', '紀錄', '組織架構圖']);
        assert.equal(detail.getActiveTabId(), 'basic');

        // lazyTabs 預設開啟：未啟用的分頁面板要先是空的，首次啟用才補內容
        const attachmentPanel = findByDataset(byClass(root, 'dynamic-detail__tab-panel'), 'tabPanelId', 'attachments');
        assert.equal(attachmentPanel.children.length, 0);
        activateTab(root, 'attachments');
        assert.ok(attachmentPanel.children.length > 0);

        // 其餘分頁的內容同樣延後產生，先逐一啟用後面的斷言才看得到
        activateTab(root, 'members');
        activateTab(root, 'history');
        activateTab(root, 'orgChart');

        const subtables = byClass(root, 'dynamic-detail__subtable');
        const mainSubtable = findByDataset(subtables, 'subtableId', 'secondaryActivityAreas');
        const memberSubtable = findByDataset(subtables, 'subtableId', 'relationMember');
        assert.equal(mainSubtable.dataset.subtableSource, 'sActAreas');
        assert.equal(memberSubtable.dataset.subtableSource, 'Member/GetByGangID/fixture-1');

        // Subtables are rendered by the B4A DataTable.  The fake DOM deliberately
        // does not parse the table component's HTML string, so assert the B4A
        // component model rather than expecting the retired hand-built table DOM.
        const headers = detail._controlComponents
            .filter(control => control?.constructor?.name === 'DataTable')
            .flatMap(control => control.columns.map(column => column.label || column.title || column.name || ''));
        assert.ok(headers.includes('次要活動處所'), JSON.stringify(headers));
        assert.ok(headers.includes('姓名'));
        assert.ok(headers.includes('身分證字號'));
        assert.match(textOf(root), /王小明/);

        const attachments = byClass(root, 'dynamic-detail__attachment');
        assert.equal(attachments.length, 1);
        assert.equal(attachments[0].dataset.tableName, 'TIM_GANG');
        assert.equal(attachments[0].dataset.tablePk, 'GAN-001');
        assert.equal(attachments[0].dataset.action, 'SEARCH');

        const history = byClass(root, 'dynamic-detail__history');
        assert.equal(history.length, 1);
        assert.equal(history[0].dataset.tablePk, 'GAN-001');

        const media = byClass(root, 'dynamic-detail__media');
        assert.equal(media.length, 1);
        assert.equal(media[0].dataset.source, 'GangChart/GetGraph/G001');

        const actions = byClass(root, 'dynamic-detail__action');
        assert.equal(actions.length, 1);
        assert.equal(actions[0].dataset.component, 'BrowseButtons');
        assert.equal(actions[0].dataset.tablePk, 'GAN-001');
    });

    await t('detail tabs switch visible panels without rebuilding data', async () => {
        const renderer = new DynamicPageRenderer({
            definition: detailDefinition,
            mode: 'detail',
            data: detailData,
            routeParams: { ColValue: 'fixture-1' },
        });
        await renderer.init();
        const detail = renderer.getRenderer();
        const root = detail.element;
        const tabs = byClass(root, 'dynamic-detail__tab');
        const panels = byClass(root, 'dynamic-detail__tab-panel');

        findByDataset(tabs, 'tabId', 'attachments').dispatchEvent({ type: 'click' });
        assert.equal(detail.getActiveTabId(), 'attachments');
        assert.equal(findByDataset(panels, 'tabPanelId', 'basic').hidden, true);
        assert.equal(findByDataset(panels, 'tabPanelId', 'attachments').hidden, false);
        assert.equal(byClass(root, 'dynamic-detail__attachment')[0].dataset.tablePk, 'GAN-001');
    });

    await t('detail action forwards resolved metadata through DynamicPageRenderer', async () => {
        const calls = [];
        const renderer = new DynamicPageRenderer({
            definition: detailDefinition,
            mode: 'detail',
            data: detailData,
            routeParams: { ColValue: 'fixture-1' },
            onAction: (...args) => calls.push(args),
        });
        await renderer.init();

        const action = byClass(renderer.getRenderer().element, 'dynamic-detail__action')[0];
        action.dispatchEvent({ type: 'click' });

        assert.equal(calls.length, 1);
        assert.equal(calls[0][0], 'getWord');
        assert.equal(calls[0][1], detailData);
        assert.equal(calls[0][2].tablePk, 'GAN-001');
        assert.equal(calls[0][2].route, 'Gang');
    });

    await t('flat detail treats ordinary values as text and sanitizes explicit rich text', async () => {
        const payload = '<img src=x onerror=alert(1)><script>alert(2)</script>';
        const renderer = new DynamicPageRenderer({
            definition: {
                page: { id: 'detail.security', view: 'detail' },
                fields: [
                    { fieldName: 'timeValue', label: 'Time', fieldType: 'time', formRow: 1, formCol: 3 },
                    { fieldName: 'weatherValue', label: 'Weather', fieldType: 'weather', formRow: 1, formCol: 3 },
                    { fieldName: 'richValue', label: 'Rich', fieldType: 'richtext', formRow: 1, formCol: 3 },
                    { fieldName: 'imageValue', label: 'Image', fieldType: 'image', formRow: 1, formCol: 3 },
                    { fieldName: 'colorValue', label: 'Color', fieldType: 'color', formRow: 2, formCol: 3 },
                ],
            },
            mode: 'detail',
            data: {
                timeValue: payload,
                weatherValue: { icon: payload, temperature: '20', unit: 'C', description: payload },
                richValue: payload,
                imageValue: ['java', 'script:alert(3)'].join(''),
                colorValue: 'url(//example.invalid/tracker)',
            },
        });
        await renderer.init();

        const values = byClass(renderer.getRenderer().element, 'dynamic-detail__value');
        assert.equal(values[0].textContent, payload);
        assert.equal(values[0].innerHTML, '');
        assert.match(values[1].textContent, /<script>alert\(2\)<\/script>/);
        assert.equal(values[1].innerHTML, '');
        assert.equal(values[2].children.length, 1);
        assert.doesNotMatch(values[2].children[0].innerHTML, /<script|<img/i);
        assert.match(values[2].children[0].innerHTML, /&lt;script&gt;/);
        assert.equal(values[3].children[0].tagName, 'SPAN');
        assert.equal(values[4].children[0].children[0].style.background, 'transparent');
    });

    const failed = results.filter((result) => !result.pass);
    if (failed.length > 0) {
        const error = new Error(`Detail ext tests failed: ${failed.map((result) => result.name).join(', ')}`);
        error.results = results;
        throw error;
    }

    return results;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    const results = await runDetailExtTests();
    for (const result of results) {
        console.log(`ok ${result.name}`);
    }
}
