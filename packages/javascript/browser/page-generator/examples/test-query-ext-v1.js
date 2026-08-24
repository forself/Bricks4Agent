import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';

import { validateDefinition } from '../PageDefinition.js';
import { PageDefinitionAdapter } from '../PageDefinitionAdapter.js';
import { DynamicPageRenderer } from '../DynamicPageRenderer.js';
import { DynamicListRenderer } from '../DynamicListRenderer.js';
import {
    buildActionRequest,
    buildDownloadRequest,
    buildQueryPayload,
    formatRocDateTime,
    getQueryColumns,
    getQuerySearchFields,
    getUiActions,
    normalizeQueryDefinition,
    resolveFieldOptions,
    resolveLookupLabel,
    resolveRouteTemplate,
    validateQueryDefinition,
} from '../QueryDefinitionAdapter.js';

const queryDefinition = {
    schemaVersion: 1,
    page: {
        id: 'log.login-search',
        entity: 'LoginRecord',
        title: '登入紀錄查詢',
        view: 'query',
        permissionKey: 'LoginSearch',
    },
    api: {
        searchlist: { legacyPath: 'Log/LoginSearch' },
        download: {
            legacyPath: 'Log/ExportLoginData',
            fileName: '登入紀錄_{timestamp}.xlsx',
            payload: { AuditList: '$selection.verify_id', type: null },
            confirmText: '本資料僅供警察機關內部運用或參考，嚴禁對外公開。',
        },
    },
    searchFields: [
        {
            name: 'ID',
            label: '帳號',
            type: 'text',
            placeholder: '請輸入帳號，多筆資料請以逗號分隔',
            omitWhenEmpty: true,
        },
        {
            name: 'DateS',
            label: '操作時間起',
            type: 'rocDate',
            required: true,
            payload: { roc: 'DateSCH', western: 'DateS' },
            min: '1912-01-01',
            maxYearOffset: 4,
        },
        {
            name: 'DateE',
            label: '操作時間迄',
            type: 'rocDate',
            required: true,
            payload: { roc: 'DateECH', western: 'DateE' },
            pairWith: 'DateS',
            separator: '～',
        },
    ],
    columns: [
        { key: 'verify_id', title: '流水號', hidden: true, isSelectionKey: true },
        { key: 'user_id', title: '姓名', width: '15%' },
        { key: 'CreUserID', title: '帳號', width: '15%' },
        { key: 'CreDateTime', title: '操作時間', width: '20%', format: 'raw' },
    ],
    table: {
        selectableRows: 'multiple',
        rowsPerPageOptions: [10, 20, 100, 500, 1000],
        tableBodyHeight: '500px',
        titleTemplate: '查詢結果 共{count}筆查詢結果',
        textLabels: {
            rowsPerPage: '每頁筆數:',
            displayRows: '共',
            noMatch: '無查詢結果',
            selectedUnit: '筆',
        },
    },
    behaviors: {
        collapsibleSearch: {
            initialOpen: false,
            panelTitle: '查詢條件',
            toggleLabel: '查詢條件收合',
        },
        requiredMessage: '*必填欄位*',
    },
    fixtures: {
        sampleRow: {
            verify_id: 12345,
            user_id: '王小明',
            CreUserID: 'A123456789',
            CreDateTime: '115/07/02 09:30:15',
        },
    },
};

const gangAdvanceDefinition = {
    schemaVersion: 1,
    page: {
        id: 'search.gang-advance',
        entity: 'Gang',
        title: '幫派組合查詢',
        view: 'query',
        permissionKey: 'GangAdvance',
    },
    api: {
        searchlist: { legacyPath: 'Gang/Search', behavior: 'search.query' },
        download: [
            {
                id: 'gang-roster',
                behavior: 'template.export',
                legacyPath: 'Gang/Roster',
                label: '組合名冊',
                fileName: '組合名冊_{timestamp}.xlsx',
                selectionKey: 'GangNo',
                payload: { AuditList: '$selection.GangNo' },
                confirmText: '本資料僅供警察機關內部運用或參考，嚴禁對外公開。',
            },
            {
                id: 'member-act-roster',
                behavior: 'template.export',
                legacyPath: 'Gang/MemberActRoster',
                label: '相關情資名冊',
                fileName: '相關情資名冊_{timestamp}.xlsx',
                selectionKey: 'GangID',
                payload: { AuditList: '$selection.GangID' },
                confirmText: '本資料僅供警察機關內部運用或參考，嚴禁對外公開。',
            },
        ],
    },
    searchFields: [
        {
            name: 'TubePolices',
            label: '註記警察局',
            type: 'multiselect',
            options: [
                { value: 'P1', label: '第一警局' },
                { value: 'P2', label: '第二警局' },
            ],
            omitWhenEmpty: true,
        },
        {
            name: 'TubeBranches',
            label: '註記分局',
            type: 'multiselect',
            optionsSource: {
                type: 'static',
                items: [
                    { ADCode: 'B1', TIMCode: 'P1', Name: '第一分局' },
                    { ADCode: 'B2', TIMCode: 'P2', Name: '第二分局' },
                ],
                valueField: 'ADCode',
                labelField: 'Name',
            },
            valueField: 'ADCode',
            labelField: 'Name',
            dependsOn: 'TubePolices',
            filter: { sourceField: 'TIMCode', equalsField: 'TubePolices' },
            omitWhenEmpty: true,
        },
    ],
    columns: [
        { key: 'GangNo', title: '流水號', hidden: true, isSelectionKey: true },
        { key: 'GangID', title: '幫派名稱流水號', hidden: true },
        {
            key: 'GangNameID',
            title: '幫派名稱',
            width: '15%',
            lookup: {
                optionsSource: { type: 'api', endpoint: 'loadData.gangFullCharts', valueField: 'ID', labelField: 'NText' },
                valueField: 'ID',
                labelField: 'NText',
                fallback: '(尚未指定幫派名稱)',
            },
            link: { route: '/search/TIMGang/{GangID}', target: '_blank' },
        },
        { key: 'TubeAppvDate', title: '核定日期', width: '12.5%', format: 'rocDate', rocDateSource: 'westernDate' },
        { key: 'CreDateTime', title: '後端民國時間', width: '12.5%', format: 'rocDate', rocDateSource: 'backendString' },
        {
            key: 'TubePolice',
            title: '註記警察局',
            optionsSource: { type: 'api', endpoint: 'loadData.allunits', valueField: 'ADCode', labelField: 'Name' },
            valueField: 'ADCode',
            labelField: 'Name',
        },
    ],
    table: {
        selectableRows: 'multiple',
        titleTemplate: '查詢結果  共{count}筆查詢結果',
    },
    behaviors: {
        searchForm: {
            searchText: '查詢',
            resetText: '清除欄位值',
            requiredMark: '(必填)',
            datePlaceholder: '請點選日期',
        },
    },
    fixtures: {
        sampleRow: {
            GangNo: 9001,
            GangID: 'G001',
            GangNameID: 'G001',
            TubeAppvDate: '2026-07-02T09:30:15',
            CreDateTime: '115/07/02 09:30:15',
            TubePolice: 'P1',
        },
        lookups: {
            'loadData.gangFullCharts': [{ ID: 'G001', NText: '仁義會' }],
            'loadData.allunits': [{ ADCode: 'P1', Name: '第一警局' }],
        },
    },
};

const peopleDefinition = {
    schemaVersion: 1,
    page: {
        id: 'maintain.people',
        entity: 'People',
        title: '規模資料維護',
        view: 'adminList',
        permissionKey: 'People',
    },
    api: {
        list: { legacyPath: 'People', behavior: 'codeTable.list', method: 'GET' },
        create: {
            legacyPath: 'People',
            behavior: 'codeTable.insert',
            method: 'POST',
            payload: { People: '$search.People', IsDelete: '$search.IsDelete', Seq: '$search.Seq' },
            toasts: { success: '新增成功', error: '新增失敗' },
        },
        update: {
            legacyPath: 'People',
            behavior: 'codeTable.update',
            method: 'PUT',
            payload: { PeopleID: '$row.PeopleID', People: '$search.People', IsDelete: '$search.IsDelete', Seq: '$search.Seq' },
            toasts: { success: '修改成功', error: '修改失敗' },
        },
    },
    columns: [
        { key: 'PeopleID', title: '代碼', width: '10%' },
        { key: 'People', title: '規模', width: '50%' },
        {
            key: 'IsDelete',
            title: '刪除註記',
            width: '15%',
            options: [{ value: '1', label: '是' }, { value: '0', label: '' }],
        },
    ],
    actions: [
        { id: 'addPeople', type: 'modal', label: '新增', placement: 'toolbar', modal: 'addPeople' },
        { id: 'editPeople', type: 'modal', label: '編輯', placement: 'row', modal: 'editPeople' },
    ],
    modals: [
        {
            id: 'addPeople',
            title: '新增',
            submitAction: 'create',
            submitText: '送出',
            cancelText: '取消',
            fields: [
                { name: 'People', label: '規模', type: 'text', required: true, placeholder: '請填寫規模範圍' },
                { name: 'IsDelete', label: '是否刪除', type: 'radio', defaultValue: '0', options: [{ value: '1', label: '是' }, { value: '0', label: '否' }] },
                { name: 'Seq', label: '排序', type: 'text', pattern: '^[0-9\\b]+$', validationMessage: '請輸入數字' },
            ],
        },
        {
            id: 'editPeople',
            title: '修改',
            submitAction: 'update',
            submitText: '送出',
            cancelText: '取消',
            fields: [
                { name: 'PeopleID', label: '代碼', type: 'text', readOnly: true },
                { name: 'People', label: '規模', type: 'text', required: true, placeholder: '請填寫規模範圍' },
                { name: 'IsDelete', label: '是否刪除', type: 'radio', options: [{ value: '1', label: '是' }, { value: '0', label: '否' }] },
                { name: 'Seq', label: '排序', type: 'text', pattern: '^[0-9\\b]+$', validationMessage: '請輸入數字' },
            ],
        },
    ],
    table: {
        selectableRows: 'none',
        titleTemplate: '共{count}筆',
    },
    fixtures: {
        sampleRow: { PeopleID: 1, People: '10人以下', IsDelete: '0', Seq: '1' },
    },
};

export async function runQueryExtV1Tests() {
    const results = [];
    const t = async (name, fn) => {
        try {
            await fn();
            results.push({ name, pass: true });
        } catch (error) {
            results.push({ name, pass: false, error });
        }
    };

    await t('query definition validates directly and through PageDefinitionAdapter', () => {
        assert.equal(validateQueryDefinition(queryDefinition).valid, true);
        assert.equal(validateDefinition(queryDefinition).valid, true);

        const oldDefinition = PageDefinitionAdapter.toOldFormat(queryDefinition);
        assert.equal(oldDefinition.type, 'list');
        assert.equal(oldDefinition.name, 'LoginRecordPage');
        assert.equal(oldDefinition.description, '登入紀錄查詢');
        assert.equal(oldDefinition.fields.length, 7);
        assert.equal(oldDefinition.api.list, 'Log/LoginSearch');
        assert.deepEqual(oldDefinition.api.download.payload, { AuditList: '$selection.verify_id', type: null });
        assert.equal(validateDefinition(oldDefinition).valid, true);
    });

    await t('query materializer preserves searchFields and columns ext-v1 metadata', () => {
        const normalized = normalizeQueryDefinition(queryDefinition);
        assert.equal(normalized.type, 'list');
        assert.equal(normalized.fields.length, 7);

        const searchFields = getQuerySearchFields(normalized);
        assert.deepEqual(
            searchFields.map((field) => [field.fieldName, field.fieldType, field.isRequired]),
            [['ID', 'text', false], ['DateS', 'rocDate', true], ['DateE', 'rocDate', true]],
        );

        const columns = getQueryColumns(normalized);
        assert.equal(columns[0].fieldName, 'verify_id');
        assert.equal(columns[0].hidden, true);
        assert.equal(columns[0].isSelectionKey, true);
        assert.equal(columns[1].width, '15%');
        assert.equal(columns[3].format, 'raw');
    });

    await t('query payload emits dual ROC and western dates and omits empty optional fields', () => {
        const result = buildQueryPayload(getQuerySearchFields(queryDefinition), {
            ID: '',
            DateS: new Date(2026, 0, 1),
            DateE: new Date(2026, 6, 2),
        }, { requiredMessage: queryDefinition.behaviors.requiredMessage });

        assert.deepEqual(result.errors, []);
        assert.deepEqual(result.payload, {
            DateSCH: '115/01/01',
            DateS: '2026/01/01',
            DateECH: '115/07/02',
            DateE: '2026/07/02',
        });
    });

    await t('query payload keeps non-empty text values verbatim', () => {
        const result = buildQueryPayload(getQuerySearchFields(queryDefinition), {
            ID: ' user01,user02 ',
            DateS: '2026-01-01',
            DateE: '115/07/02',
        });

        assert.equal(result.payload.ID, ' user01,user02 ');
        assert.equal(result.payload.DateSCH, '115/01/01');
        assert.equal(result.payload.DateE, '2026/07/02');
    });

    await t('required query fields fail without writing a partial date payload', () => {
        const result = buildQueryPayload(getQuerySearchFields(queryDefinition), {
            ID: '',
            DateE: new Date(2026, 6, 2),
        }, { requiredMessage: queryDefinition.behaviors.requiredMessage });

        assert.equal(result.errors.length, 1);
        assert.deepEqual(result.errors[0], { field: 'DateS', message: '*必填欄位*' });
        assert.deepEqual(result.payload, {
            DateECH: '115/07/02',
            DateE: '2026/07/02',
        });
    });

    await t('download request resolves selection payload and timestamp filename', () => {
        const rows = [
            queryDefinition.fixtures.sampleRow,
            { ...queryDefinition.fixtures.sampleRow, verify_id: 12346 },
        ];
        const request = buildDownloadRequest(queryDefinition, rows, [0, 1], {
            now: new Date(2026, 6, 2, 9, 30, 15),
        });

        assert.equal(request.legacyPath, 'Log/ExportLoginData');
        assert.equal(request.fileName, '登入紀錄_20260702093015.xlsx');
        assert.deepEqual(request.payload, { AuditList: [12345, 12346], type: null });
        assert.equal(request.selectionKey, 'verify_id');
        assert.deepEqual(request.selectionValues, [12345, 12346]);
        assert.equal(request.confirmText, queryDefinition.api.download.confirmText);
    });

    await t('invalid rocDate payload schema fails closed', () => {
        const badDefinition = {
            ...queryDefinition,
            searchFields: [
                { name: 'DateS', label: '操作時間起', type: 'rocDate', required: true },
            ],
        };
        const validation = validateDefinition(badDefinition);
        assert.equal(validation.valid, false);
        assert.ok(validation.errors.some((error) => error.includes('payload')));
    });

    await t('DynamicPageRenderer defaults query to list and permission hook fails closed', async () => {
        const calls = [];
        const renderer = new DynamicPageRenderer({
            definition: queryDefinition,
            onPermissionCheck: (permissionKey, page) => {
                calls.push({ permissionKey, pageId: page.id });
                return false;
            },
        });

        assert.equal(renderer.options.mode, 'list');
        await renderer.init();
        assert.deepEqual(calls, [{ permissionKey: 'LoginSearch', pageId: 'log.login-search' }]);
        assert.equal(renderer.getRenderer(), null);
        assert.deepEqual(renderer.getPermissionState(), { checked: true, allowed: false, error: null });
    });

    await t('ext-v2 supports multiple export actions with per-action selection keys', () => {
        assert.equal(validateDefinition(gangAdvanceDefinition).valid, true);
        const actions = getUiActions(gangAdvanceDefinition, { placement: 'toolbarSelect' });
        assert.deepEqual(actions.map((action) => action.id), ['gang-roster', 'member-act-roster']);

        const rows = [
            gangAdvanceDefinition.fixtures.sampleRow,
            { ...gangAdvanceDefinition.fixtures.sampleRow, GangNo: 9002, GangID: 'G002' },
        ];
        const gangRoster = buildDownloadRequest(gangAdvanceDefinition, rows, [0, 1], {
            actionId: 'gang-roster',
            now: new Date(2026, 6, 2, 9, 30, 15),
        });
        assert.equal(gangRoster.legacyPath, 'Gang/Roster');
        assert.equal(gangRoster.fileName, '組合名冊_20260702093015.xlsx');
        assert.deepEqual(gangRoster.payload, { AuditList: [9001, 9002] });
        assert.equal(gangRoster.selectionKey, 'GangNo');

        const memberActRoster = buildDownloadRequest(gangAdvanceDefinition, rows, [0, 1], {
            actionId: 'member-act-roster',
            now: new Date(2026, 6, 2, 9, 30, 15),
        });
        assert.equal(memberActRoster.legacyPath, 'Gang/MemberActRoster');
        assert.equal(memberActRoster.fileName, '相關情資名冊_20260702093015.xlsx');
        assert.deepEqual(memberActRoster.payload, { AuditList: ['G001', 'G002'] });
        assert.equal(memberActRoster.selectionKey, 'GangID');
    });

    await t('ext-v2 resolves code-table lookup labels and drill-down route templates', () => {
        const columns = getQueryColumns(gangAdvanceDefinition);
        const nameColumn = columns.find((column) => column.key === 'GangNameID');
        const policeColumn = columns.find((column) => column.key === 'TubePolice');
        assert.equal(resolveLookupLabel(nameColumn, 'G001', gangAdvanceDefinition), '仁義會');
        assert.equal(resolveLookupLabel(policeColumn, 'P1', gangAdvanceDefinition), '第一警局');
        assert.equal(resolveRouteTemplate(nameColumn.link.route, {
            row: gangAdvanceDefinition.fixtures.sampleRow,
            columns,
        }), '/search/TIMGang/G001');
    });

    await t('list links reject protocol-relative URLs while keeping internal routes', () => {
        const renderer = new DynamicListRenderer({ definition: gangAdvanceDefinition });
        const baseColumn = { fieldName: 'GangID', fieldType: 'text' };
        const row = gangAdvanceDefinition.fixtures.sampleRow;

        const unsafe = renderer._formatCellValue({
            ...baseColumn,
            link: { route: '//example.invalid/{GangID}' },
        }, row.GangID, row);
        assert.equal(unsafe, row.GangID);

        const safe = renderer._formatCellValue({
            ...baseColumn,
            link: { route: '/search/TIMGang/{GangID}' },
        }, row.GangID, row);
        assert.match(safe.__html, /^<span data-field-link-host="" data-link-label="G001"><\/span>$/);
        assert.doesNotMatch(safe.__html, /<a\b/i);
    });

    await t('ext-v2 supports backend ROC strings and client-side western-to-ROC columns', () => {
        const columns = getQueryColumns(gangAdvanceDefinition);
        const westernColumn = columns.find((column) => column.key === 'TubeAppvDate');
        const backendColumn = columns.find((column) => column.key === 'CreDateTime');
        assert.equal(westernColumn.rocDateSource, 'westernDate');
        assert.equal(formatRocDateTime(gangAdvanceDefinition.fixtures.sampleRow.TubeAppvDate), '115/07/02 09:30:15');
        assert.equal(backendColumn.rocDateSource, 'backendString');
        assert.equal(gangAdvanceDefinition.fixtures.sampleRow.CreDateTime, '115/07/02 09:30:15');
    });

    await t('ext-v2 field.options and dependsOn filter multiselect child options', () => {
        const fields = getQuerySearchFields(gangAdvanceDefinition);
        const parent = fields.find((field) => field.fieldName === 'TubePolices');
        const child = fields.find((field) => field.fieldName === 'TubeBranches');
        assert.deepEqual(parent.options.map((item) => item.value), ['P1', 'P2']);
        const filtered = resolveFieldOptions(child, gangAdvanceDefinition, { TubePolices: ['P1'] });
        assert.deepEqual(filtered.map((item) => [item.value, item.label]), [['B1', '第一分局']]);
    });

    await t('ext-v2 adminList supports toolbar action, row action, modal forms, methods, and per-action toasts', () => {
        assert.equal(validateDefinition(peopleDefinition).valid, true);
        assert.equal(validateQueryDefinition(peopleDefinition).valid, true);
        const normalized = normalizeQueryDefinition(peopleDefinition);
        assert.equal(normalized.type, 'list');
        assert.equal(normalized.fields.length, 3);
        assert.deepEqual(getUiActions(peopleDefinition, { placement: 'toolbar' }).map((action) => action.id), ['addPeople']);
        assert.deepEqual(getUiActions(peopleDefinition, { placement: 'row' }).map((action) => action.id), ['editPeople']);
        assert.equal(peopleDefinition.modals[0].submitAction, 'create');
        assert.equal(peopleDefinition.api.create.method, 'POST');
        assert.deepEqual(peopleDefinition.api.create.toasts, { success: '新增成功', error: '新增失敗' });
        assert.deepEqual(
            resolveFieldOptions(peopleDefinition.modals[1].fields.find((field) => field.name === 'IsDelete'), peopleDefinition),
            [{ value: '1', label: '是' }, { value: '0', label: '否' }],
        );

        const createRequest = buildActionRequest(peopleDefinition, {
            id: 'addPeople',
            type: 'modal',
            apiAction: 'create',
            modal: 'addPeople',
        }, {
            searchValues: { People: '21至30人', IsDelete: '1', Seq: '3' },
        });
        assert.equal(createRequest.method, 'POST');
        assert.deepEqual(createRequest.payload, {
            People: '21至30人',
            IsDelete: '1',
            Seq: '3',
        });

        const updateRequest = buildActionRequest(peopleDefinition, {
            id: 'editPeople',
            type: 'modal',
            apiAction: 'update',
            modal: 'editPeople',
        }, {
            row: peopleDefinition.fixtures.sampleRow,
            searchValues: { People: '11至20人', IsDelete: '0', Seq: '2' },
        });
        assert.equal(updateRequest.method, 'PUT');
        assert.deepEqual(updateRequest.payload, {
            PeopleID: 1,
            People: '11至20人',
            IsDelete: '0',
            Seq: '2',
        });
    });

    const failed = results.filter((result) => !result.pass);
    if (failed.length > 0) {
        const error = new Error(`Query ext-v1 tests failed: ${failed.map((result) => result.name).join(', ')}`);
        error.results = results;
        throw error;
    }

    return results;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    const results = await runQueryExtV1Tests();
    for (const result of results) {
        console.log(`ok ${result.name}`);
    }
}
