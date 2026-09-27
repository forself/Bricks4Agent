import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { ImportWizard } from '../../ui_components/layout/ImportWizard/ImportWizard.js';
import ImportWizardDefault from '../../ui_components/layout/ImportWizard/index.js';
import { parseCsv, detectDelimiter, stripBom } from '../../ui_components/layout/ImportWizard/csv-parser.js';
import Locale from '../../ui_components/i18n/index.js';

const STAFF_FIELDS = [
    { key: 'name', label: '姓名', required: true, aliases: ['full name'] },
    { key: 'email', label: 'Email', required: true, aliases: ['e-mail', '電子郵件'], validate: (value) => (value.includes('@') ? null : '格式錯誤') },
    { key: 'quota', label: '配額', type: 'number' },
    { key: 'joined', label: '到職日', type: 'date', aliases: ['joined on'] },
    { key: 'active', label: '啟用', type: 'boolean', transform: (value) => (value ? 'Y' : 'N') },
];
const STAFF_CSV = [
    'Full Name,E-mail,配額,Joined On,啟用',
    '王小明,ming@example.com,"1,234.5",2026/3/5,是',
    ',lee@example.com,12a,2026-02-30,maybe',
    '陳大文,not-an-email,,,',
    '林小華,hua@example.com',
    '"張""三",zhang@example.com,7,2026-12-31,false',
    '',
].join('\r\n');
const VALID_CSV = 'Full Name,E-mail\r\n王小明,ming@example.com\r\n李四,lee@example.com\r\n';

let host;
let created = [];
beforeEach(() => {
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    document.body.appendChild(host);
});
afterEach(() => {
    created.forEach((instance) => instance.destroy());
    created = [];
    host.remove();
    Locale.setLang('zh-TW');
    vi.restoreAllMocks();
});

const create = (options = {}) => {
    const instance = new ImportWizard({ fields: STAFF_FIELDS, ...options }).mount(host);
    created.push(instance);
    return instance;
};
const textFile = (text, name = 'staff.csv', type = 'text/csv') => new File([text], name, { type });
const byteFile = (bytes, name = 'data.csv') => new File([new Uint8Array(bytes)], name, { type: 'text/csv' });
const q = (wizard, selector) => wizard.element.querySelector(selector);
const button = (wizard, key) => q(wizard, `.cl-import-wizard__button--${key}`);

async function chooseFile(wizard, file) {
    const input = q(wizard, '.cl-import-wizard__file');
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(wizard.snapshot().status).not.toBe('reading'));
}

function trackGlobalListeners() {
    const added = [];
    const removed = [];
    for (const target of [window, document]) {
        const originalAdd = target.addEventListener;
        const originalRemove = target.removeEventListener;
        vi.spyOn(target, 'addEventListener').mockImplementation(function (type, listener, options) {
            added.push({ target, type, listener, capture: typeof options === 'boolean' ? options : Boolean(options?.capture) });
            return originalAdd.call(this, type, listener, options);
        });
        vi.spyOn(target, 'removeEventListener').mockImplementation(function (type, listener, options) {
            removed.push({ target, type, listener, capture: typeof options === 'boolean' ? options : Boolean(options?.capture) });
            return originalRemove.call(this, type, listener, options);
        });
    }
    return {
        added,
        active: () => added.filter((entry) => !removed.some((gone) => gone.target === entry.target
            && gone.type === entry.type && gone.listener === entry.listener && gone.capture === entry.capture)),
    };
}

describe('csv-parser', () => {
    it('parses CRLF/LF records, ignores the trailing newline and strips the BOM', () => {
        expect(parseCsv('﻿a,b\r\n1,2\r\n')).toEqual({ rows: [['a', 'b'], ['1', '2']], lines: [1, 2], errors: [], truncated: false });
        expect(parseCsv('a,b\n1,2').rows).toEqual([['a', 'b'], ['1', '2']]);
        expect(parseCsv('a\rb\r').rows).toEqual([['a'], ['b']]);
        expect(stripBom('﻿x')).toBe('x');
        expect(parseCsv('')).toEqual({ rows: [], lines: [], errors: [], truncated: false });
    });

    it('handles quotes, escaped quotes, delimiters and newlines inside quotes', () => {
        const result = parseCsv('"x, y","say ""hi""","multi\nline"\r\nz,"a\r\nb",""\n');
        expect(result.rows).toEqual([['x, y', 'say "hi"', 'multi\nline'], ['z', 'a\r\nb', '']]);
        expect(result.lines).toEqual([1, 3]);
        expect(result.errors).toEqual([]);
    });

    it('keeps empty fields, trailing delimiters and ragged rows as-is', () => {
        expect(parseCsv('a,,c,\n').rows).toEqual([['a', '', 'c', '']]);
        expect(parseCsv('a,b\n1\n1,2,3').rows.map((row) => row.length)).toEqual([2, 1, 3]);
    });

    it('skips blank lines (greedy also skips whitespace-only and delimiter-only lines)', () => {
        expect(parseCsv('a\n\nb\n')).toMatchObject({ rows: [['a'], ['b']], lines: [1, 3] });
        expect(parseCsv('a,b\n , \n,,\nc,d', { skipEmptyLines: 'greedy' })).toMatchObject({ rows: [['a', 'b'], ['c', 'd']], lines: [1, 4] });
        expect(parseCsv('a\n\nb', { skipEmptyLines: false }).rows).toEqual([['a'], [''], ['b']]);
        // 明確的空字串欄位不是空白行
        expect(parseCsv('""\n').rows).toEqual([['']]);
    });

    it('reports unterminated and malformed quotes but still returns the content', () => {
        expect(parseCsv('a,"bc\nd,e')).toEqual({
            rows: [['a', 'bc\nd,e']],
            lines: [1],
            errors: [{ code: 'unterminatedQuote', line: 1, record: 0 }],
            truncated: false,
        });
        expect(parseCsv('x\n"ab"c,d\n')).toMatchObject({
            rows: [['x'], ['abc', 'd']],
            errors: [{ code: 'malformedQuote', line: 2, record: 1 }],
        });
        // 未加引號欄位中間的引號視為一般字元
        expect(parseCsv('5"x,1').rows).toEqual([['5"x', '1']]);
    });

    it('stops at maxRecords and validates the delimiter', () => {
        expect(parseCsv('a\nb\nc', { maxRecords: 2 })).toMatchObject({ rows: [['a'], ['b']], truncated: true });
        expect(parseCsv('a;b\n1;2', { delimiter: ';' }).rows).toEqual([['a', 'b'], ['1', '2']]);
        expect(() => parseCsv('a', { delimiter: '"' })).toThrow(TypeError);
        expect(() => parseCsv('a', { delimiter: ',,' })).toThrow(TypeError);
    });

    it('detects comma, tab and semicolon delimiters, respecting quotes', () => {
        expect(detectDelimiter('a,b,c\n1,2,3')).toBe(',');
        expect(detectDelimiter('name\tnote\nA\tx,y\nB\tz')).toBe('\t');
        expect(detectDelimiter('a;b\n"1,5";2\n"3,5";4')).toBe(';');
        expect(detectDelimiter('single\ncolumn')).toBe(',');
        expect(detectDelimiter('a\tb\n1\t2', [';', '\t'])).toBe('\t');
    });
});

describe('ImportWizard — upload step', () => {
    it('renders the first step with defaults', () => {
        const wizard = create();
        expect(ImportWizardDefault).toBe(ImportWizard);
        expect(wizard.element.getAttribute('aria-label')).toBe('資料匯入');
        expect(q(wizard, '[role="heading"]').textContent).toBe('選擇要匯入的檔案');
        const steps = [...wizard.element.querySelectorAll('.cl-stepper__step')];
        expect(steps.map((step) => step.textContent)).toEqual(['1上傳檔案', '2欄位對應', '3預覽與驗證', '4確認匯入']);
        expect(steps[0].getAttribute('aria-current')).toBe('step');
        const input = q(wizard, '.cl-import-wizard__file');
        expect(input.accept).toBe('.csv,.tsv,.txt');
        expect(q(wizard, `label[for="${input.id}"]`).textContent).toBe('選擇檔案');
        expect(q(wizard, '.cl-import-wizard__paste-input')).not.toBeNull();
        expect(q(wizard, '.cl-import-wizard__accept-hint').textContent).toBe('支援格式：.csv,.tsv,.txt；大小上限 5 MB');
        expect(button(wizard, 'next').disabled).toBe(true);
        expect(button(wizard, 'cancel')).toBeNull();
        expect(wizard.getMapping()).toEqual({ name: null, email: null, quota: null, joined: null, active: null });
        expect(wizard.getRows()).toEqual({ valid: [], invalid: [] });
    });

    it('reads a UTF-8 file with BOM and auto-maps headers through keys, labels and aliases', async () => {
        const wizard = create();
        const bom = [0xef, 0xbb, 0xbf];
        const body = new TextEncoder().encode('Ｆｕｌｌ　Ｎａｍｅ,E-mail,配額,JOINED_ON\n王小明,ming@example.com,3,2026-01-02\n');
        await chooseFile(wizard, byteFile([...bom, ...body], 'staff.csv'));
        expect(wizard.snapshot().status).toBe('ready');
        expect(q(wizard, '.cl-import-wizard__loaded').textContent).toBe('已讀取「staff.csv」：1 列、4 欄');
        expect(wizard.element.querySelector('.cl-import-wizard__live').textContent).toBe('已讀取「staff.csv」：1 列、4 欄');
        expect(wizard.getMapping()).toEqual({ name: 0, email: 1, quota: 2, joined: 3, active: null });
        expect(button(wizard, 'next').disabled).toBe(false);
    });

    it('decodes other encodings through TextDecoder and warns about undecodable bytes', async () => {
        // Big5：「中」= A4 A4、「文」= A4 E5
        const big5 = [0xa4, 0xa4, 0xa4, 0xe5, 0x2c, 0x63, 0x6f, 0x64, 0x65, 0x0a, 0xa4, 0xa4, 0xa4, 0xe5, 0x2c, 0x31];
        const fields = [{ key: 'title', label: '中文' }, { key: 'code', label: 'Code' }];
        const wizard = create({ fields, encoding: 'big5' });
        await chooseFile(wizard, byteFile(big5, 'legacy.csv'));
        expect(wizard.getMapping()).toEqual({ title: 0, code: 1 });
        expect(wizard.getRows().valid).toEqual([{ title: '中文', code: '1' }]);
        expect(q(wizard, '.cl-import-wizard__warning')).toBeNull();

        const utf8 = create({ fields });
        await chooseFile(utf8, byteFile(big5, 'legacy.csv'));
        expect(q(utf8, '.cl-import-wizard__warning').textContent).toBe('有部分字元無法以 utf-8 解碼，請確認檔案的編碼。');
        expect(utf8.getMapping().title).toBeNull();

        const unknown = create({ fields, encoding: 'no-such-encoding' });
        await chooseFile(unknown, byteFile(big5, 'legacy.csv'));
        expect(q(unknown, '.cl-import-wizard__error').textContent).toBe('不支援的文字編碼：no-such-encoding');
        expect(q(unknown, '.cl-import-wizard__error').getAttribute('role')).toBe('alert');
    });

    it('detects the delimiter automatically, or uses a fixed one', async () => {
        const fields = [{ key: 'a', label: 'A' }, { key: 'b', label: 'B' }];
        const tsv = create({ fields });
        await chooseFile(tsv, textFile('A\tB\n1,5\t2\n', 'data.tsv'));
        expect(tsv.getRows().valid).toEqual([{ a: '1,5', b: '2' }]);

        const semicolon = create({ fields });
        await chooseFile(semicolon, textFile('A;B\n"x;y";2\n3;4\n', 'data.txt'));
        expect(semicolon.getRows().valid).toEqual([{ a: 'x;y', b: '2' }, { a: '3', b: '4' }]);

        const fixed = create({ fields, delimiter: ';' });
        await chooseFile(fixed, textFile('A,B;C\n1,2;3\n'));
        expect(fixed.getMapping()).toEqual({ a: null, b: null });
        expect(fixed.snapshot().source.columns).toBe(2);
    });

    it('rejects wrong types, oversized files, empty data and too many rows', async () => {
        const wizard = create({ maxFileSize: 10, maxRows: 2 });
        await chooseFile(wizard, textFile('x', 'photo.png', 'image/png'));
        expect(q(wizard, '.cl-import-wizard__error').textContent).toBe('不支援的檔案類型：photo.png');
        await chooseFile(wizard, textFile('name,email\nx,y\n'));
        expect(q(wizard, '.cl-import-wizard__error').textContent).toBe('檔案過大（15 B），上限為 10 B');

        const roomy = create({ maxRows: 2 });
        await chooseFile(roomy, textFile(''));
        expect(q(roomy, '.cl-import-wizard__error').textContent).toBe('沒有任何資料。');
        await chooseFile(roomy, textFile('name,email\n'));
        expect(q(roomy, '.cl-import-wizard__error').textContent).toBe('只有標題列，沒有資料列。');
        await chooseFile(roomy, textFile('name\na\nb\nc\n'));
        expect(q(roomy, '.cl-import-wizard__error').textContent).toBe('資料列數超過上限 2 列。');
        await chooseFile(roomy, textFile('"name,email\na,b\n'));
        expect(q(roomy, '.cl-import-wizard__error').textContent).toBe('標題列的引號格式錯誤（第 1 行）。');
        expect(button(roomy, 'next').disabled).toBe(true);
        expect(roomy.goTo(1)).toBe(false);
    });

    it('honours allowPaste:false and a custom accept list', async () => {
        const wizard = create({ allowPaste: false, accept: '.txt,text/plain' });
        expect(q(wizard, '.cl-import-wizard__paste-input')).toBeNull();
        expect(q(wizard, '.cl-import-wizard__file').accept).toBe('.txt,text/plain');
        await chooseFile(wizard, textFile(VALID_CSV, 'staff.csv', 'text/csv'));
        expect(q(wizard, '.cl-import-wizard__error').textContent).toBe('不支援的檔案類型：staff.csv');
        await chooseFile(wizard, textFile(VALID_CSV, 'export', 'text/plain'));
        expect(wizard.snapshot().status).toBe('ready');
        expect(wizard.getRows().valid).toHaveLength(2);
    });

    it('accepts pasted text and dropped files', async () => {
        const fields = [{ key: 'name', label: 'Name' }, { key: 'email', label: 'Email' }];
        const wizard = create({ fields });
        button(wizard, 'use-paste').click();
        expect(q(wizard, '.cl-import-wizard__error').textContent).toBe('請先貼上資料。');
        const textarea = q(wizard, '.cl-import-wizard__paste-input');
        textarea.value = '﻿name\temail\nA\ta@x.io';
        textarea.dispatchEvent(new Event('input', { bubbles: true }));
        button(wizard, 'use-paste').click();
        expect(q(wizard, '.cl-import-wizard__loaded').textContent).toBe('已讀取「貼上的資料」：1 列、2 欄');
        // 重新渲染後貼上的內容仍保留
        expect(q(wizard, '.cl-import-wizard__paste-input').value).toBe('﻿name\temail\nA\ta@x.io');
        expect(wizard.getRows().valid).toEqual([{ name: 'A', email: 'a@x.io' }]);

        const dropzone = q(wizard, '.cl-import-wizard__dropzone');
        const over = new Event('dragover', { bubbles: true, cancelable: true });
        dropzone.dispatchEvent(over);
        expect(over.defaultPrevented).toBe(true);
        expect(dropzone.style.borderColor).toBe('var(--cl-primary)');
        const drop = new Event('drop', { bubbles: true, cancelable: true });
        Object.defineProperty(drop, 'dataTransfer', { value: { files: [textFile('Name,Email\nB,b@x.io\nC,c@x.io\n', 'dropped.csv')] } });
        dropzone.dispatchEvent(drop);
        expect(drop.defaultPrevented).toBe(true);
        await vi.waitFor(() => expect(wizard.snapshot().source?.name).toBe('dropped.csv'));
        expect(wizard.getRows().valid).toHaveLength(2);
    });
});

describe('ImportWizard — mapping step', () => {
    const fields = [
        { key: 'name', label: '姓名', required: true },
        { key: 'email', label: 'Email', required: true },
        { key: 'note', label: '備註' },
    ];

    it('requires every required field to be mapped before moving on', async () => {
        const wizard = create({ fields });
        await chooseFile(wizard, textFile('Person,Mail,Note\nAmy,amy@example.com,hi\n'));
        expect(wizard.getMapping()).toEqual({ name: null, email: null, note: 2 });
        button(wizard, 'next').click();
        expect(wizard.snapshot().step).toBe(1);
        expect(document.activeElement).toBe(q(wizard, '[role="heading"]'));
        expect(q(wizard, '[role="heading"]').textContent).toBe('設定欄位對應');
        expect(wizard.element.querySelector('.cl-import-wizard__live').textContent).toBe('第 2 步，共 4 步：欄位對應');

        const rowFor = (key) => q(wizard, `.cl-import-wizard__mapping-row[data-field="${key}"]`);
        expect(rowFor('name').querySelector('.dropdown__input').getAttribute('aria-label')).toBe('姓名 的來源欄位');
        expect(rowFor('name').querySelector('.dropdown__input').getAttribute('aria-required')).toBe('true');
        expect(rowFor('note').querySelector('.cl-import-wizard__sample').textContent).toBe('範例：hi');

        expect(wizard.goTo(2)).toBe(false);
        button(wizard, 'next').click();
        expect(wizard.snapshot().step).toBe(1);
        expect(q(wizard, '.cl-import-wizard__mapping-error').textContent).toBe('以下必填欄位尚未對應：姓名、Email');
        expect(rowFor('name').querySelector('.b4a-field-error').textContent).toBe('必填');
        expect(document.activeElement).toBe(rowFor('name').querySelector('.dropdown__input'));

        const pick = (key, column) => {
            const input = rowFor(key).querySelector('.dropdown__input');
            // 聚焦時開啟清單；被「下一步」那次點擊關掉的清單，以 ↓ 重新開啟
            input.focus();
            input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
            rowFor(key).querySelector(`.dropdown__option[data-value="${column}"]`).click();
        };
        pick('name', 0);
        expect(wizard.getMapping().name).toBe(0);
        expect(rowFor('name').querySelector('.cl-import-wizard__sample').textContent).toBe('範例：Amy');
        expect(rowFor('name').querySelector('.b4a-field-error')).toBeNull();
        expect(q(wizard, '.cl-import-wizard__mapping-error').textContent).toBe('以下必填欄位尚未對應：Email');
        pick('email', 1);
        expect(q(wizard, '.cl-import-wizard__mapping-error')).toBeNull();

        button(wizard, 'next').click();
        expect(wizard.snapshot().step).toBe(2);
        expect(wizard.getRows().valid).toEqual([{ name: 'Amy', email: 'amy@example.com', note: 'hi' }]);
    });

    it('maps by position and labels columns by number without a header row', async () => {
        const wizard = create({ fields, hasHeader: false });
        await chooseFile(wizard, textFile('Amy,amy@example.com\nBen,ben@example.com\n'));
        expect(wizard.getMapping()).toEqual({ name: 0, email: 1, note: null });
        expect(wizard.goTo('mapping')).toBe(true);
        const options = [...q(wizard, '.cl-import-wizard__mapping-row[data-field="note"]').querySelectorAll('.dropdown__option')];
        q(wizard, '.cl-import-wizard__mapping-row[data-field="note"] .dropdown__input').focus();
        const labels = [...q(wizard, '.cl-import-wizard__mapping-row[data-field="note"]').querySelectorAll('.dropdown__option')].map((option) => option.textContent);
        expect(options).toHaveLength(0);
        expect(labels).toEqual(['（不匯入）', '第 1 欄', '第 2 欄']);
        expect(wizard.getRows().valid).toHaveLength(2);
        expect(wizard.getRows().valid[0]).toEqual({ name: 'Amy', email: 'amy@example.com', note: null });
    });
});

describe('ImportWizard — validation and preview', () => {
    it('coerces types, runs validate/transform and reports row-level problems', async () => {
        const wizard = create();
        await chooseFile(wizard, textFile(STAFF_CSV));
        expect(wizard.getMapping()).toEqual({ name: 0, email: 1, quota: 2, joined: 3, active: 4 });
        expect(wizard.getRows()).toEqual({
            valid: [
                { name: '王小明', email: 'ming@example.com', quota: 1234.5, joined: '2026-03-05', active: 'Y' },
                { name: '張"三', email: 'zhang@example.com', quota: 7, joined: '2026-12-31', active: 'N' },
            ],
            invalid: [
                {
                    row: 3,
                    values: { name: '', email: 'lee@example.com', quota: '12a', joined: '2026-02-30', active: 'maybe' },
                    errors: [
                        { field: 'name', message: '必填' },
                        { field: 'quota', message: '不是有效的數字' },
                        { field: 'joined', message: '日期格式應為 YYYY-MM-DD' },
                        { field: 'active', message: '不是有效的是／否值' },
                    ],
                },
                {
                    row: 4,
                    values: { name: '陳大文', email: 'not-an-email', quota: '', joined: '', active: '' },
                    errors: [{ field: 'email', message: '格式錯誤' }],
                },
                {
                    row: 5,
                    values: { name: '林小華', email: 'hua@example.com', quota: '', joined: '', active: '' },
                    errors: [{ field: null, message: '欄位數不符（應為 5 欄，實際 2 欄）' }],
                },
            ],
        });
    });

    it('reports failing validators, transforms and quote errors per row', async () => {
        const explode = (value) => {
            if (value === 'bad') throw new Error('boom');
            return value;
        };
        const fields = [
            { key: 'a', label: 'A', validate: (value) => (explode(value) ? null : null) },
            { key: 'b', label: 'B', transform: explode },
        ];
        const wizard = create({ fields });
        await chooseFile(wizard, textFile('A,B\nbad,2\n1,bad\n"x"y,2\n3,"4\n5,6\n'));
        expect(wizard.getRows().invalid.map((entry) => [entry.row, entry.errors.map((error) => error.message)])).toEqual([
            [2, ['驗證失敗']],
            [3, ['轉換失敗']],
            [4, ['引號格式錯誤']],
            [5, ['引號未關閉']],
        ]);
    });

    it('shows counts, the error list and a text-only preview, and blocks confirm unless allowPartial', async () => {
        const wizard = create();
        await chooseFile(wizard, textFile(STAFF_CSV));
        expect(wizard.goTo('preview')).toBe(true);
        expect(q(wizard, '.cl-import-wizard__summary').textContent).toBe('共 5 列：有效 2 列，無效 3 列');
        const items = [...wizard.element.querySelectorAll('.cl-import-wizard__error-list li')].map((item) => item.textContent);
        expect(items).toHaveLength(6);
        expect(items[0]).toBe('第 3 列，姓名：必填');
        expect(items[5]).toBe('第 5 列，整列：欄位數不符（應為 5 欄，實際 2 欄）');

        const table = q(wizard, '.cl-import-wizard__table');
        expect(table.querySelector('caption').textContent).toBe('預覽前 5 列');
        expect([...table.querySelectorAll('thead th')].map((th) => th.textContent)).toEqual(['列', '姓名', 'Email', '配額', '到職日', '啟用']);
        const bodyRows = [...table.querySelectorAll('tbody tr')];
        expect(bodyRows.map((row) => row.querySelector('th').textContent)).toEqual(['2', '3', '4', '5', '6']);
        expect(bodyRows[1].dataset.invalid).toBe('true');
        expect(bodyRows[1].querySelectorAll('td[data-invalid="true"]')).toHaveLength(4);
        expect(bodyRows[4].querySelector('td').textContent).toBe('張"三');
        expect(q(wizard, '.cl-import-wizard__table-wrap').tabIndex).toBe(0);

        const blocked = q(wizard, '.cl-import-wizard__blocked');
        expect(blocked.textContent).toBe('仍有 3 列資料無效，請修正檔案後重新上傳。');
        expect(button(wizard, 'next').disabled).toBe(true);
        expect(button(wizard, 'next').getAttribute('aria-describedby')).toBe(blocked.id);
        expect(wizard.goTo(3)).toBe(false);

        const partial = create({ allowPartial: true });
        await chooseFile(partial, textFile(STAFF_CSV));
        partial.goTo(2);
        expect(q(partial, '.cl-import-wizard__blocked')).toBeNull();
        button(partial, 'next').click();
        expect(partial.snapshot().step).toBe(3);
        expect(q(partial, '.cl-import-wizard__summary').textContent).toBe('即將匯入 2 列資料。');
        expect(q(partial, '.cl-import-wizard__skipped').textContent).toBe('將略過 3 列無效資料。');
    });

    it('caps the preview at 100 rows and the error list at 200 entries', async () => {
        const fields = [{ key: 'n', label: 'N', type: 'number', required: true }];
        const lines = ['N', ...Array.from({ length: 450 }, (_, index) => (index % 2 ? 'x' : String(index)))];
        const wizard = create({ fields });
        await chooseFile(wizard, textFile(lines.join('\n')));
        wizard.goTo(2);
        expect(wizard.element.querySelectorAll('.cl-import-wizard__table tbody tr')).toHaveLength(100);
        expect(wizard.element.querySelectorAll('.cl-import-wizard__error-list li')).toHaveLength(200);
        expect(q(wizard, '.cl-import-wizard__more-errors').textContent).toBe('另有 25 項錯誤未列出');
    });
});

describe('ImportWizard — import', () => {
    async function readyToConfirm(options = {}) {
        const wizard = create(options);
        await chooseFile(wizard, textFile(VALID_CSV));
        expect(wizard.goTo('confirm')).toBe(true);
        return wizard;
    }

    it('runs onImport with a busy state and shows the summary', async () => {
        let finish;
        const onImport = vi.fn(() => new Promise((resolve) => { finish = resolve; }));
        const wizard = await readyToConfirm({ onImport });
        const start = button(wizard, 'import');
        expect(start.textContent).toBe('開始匯入');
        start.focus();
        start.click();
        expect(onImport).toHaveBeenCalledTimes(1);
        const [rows, context] = onImport.mock.calls[0];
        expect(rows).toEqual([
            { name: '王小明', email: 'ming@example.com', quota: null, joined: null, active: null },
            { name: '李四', email: 'lee@example.com', quota: null, joined: null, active: null },
        ]);
        expect(context).toEqual({
            invalidRows: [],
            mapping: { name: 0, email: 1, quota: null, joined: null, active: null },
            headers: ['Full Name', 'E-mail'],
            rowNumbers: [2, 3],
        });

        const busy = button(wizard, 'import');
        expect(wizard.snapshot().status).toBe('importing');
        expect(q(wizard, '.cl-import-wizard__panel').getAttribute('aria-busy')).toBe('true');
        expect(busy.getAttribute('aria-disabled')).toBe('true');
        expect(busy.textContent).toBe('匯入中，請稍候…');
        expect(document.activeElement).toBe(busy);
        expect(button(wizard, 'back').disabled).toBe(true);
        busy.click();
        expect(onImport).toHaveBeenCalledTimes(1);
        expect(wizard.goTo(2)).toBe(false);

        finish({ imported: 2, failed: 0 });
        await vi.waitFor(() => expect(wizard.snapshot().status).toBe('done'));
        expect(q(wizard, '.cl-import-wizard__result').textContent).toBe('匯入完成：成功 2 列，失敗 0 列。');
        expect(document.activeElement).toBe(button(wizard, 'restart'));
        expect(wizard.element.querySelectorAll('.cl-stepper__step.is-done')).toHaveLength(4);
        expect(wizard.goTo(0)).toBe(false);

        button(wizard, 'restart').click();
        expect(wizard.snapshot()).toMatchObject({ step: 0, status: 'idle', source: null });
        expect(wizard.getRows()).toEqual({ valid: [], invalid: [] });
    });

    it('shows a failure with retry, then succeeds with default counts', async () => {
        const onImport = vi.fn()
            .mockRejectedValueOnce(new Error('duplicate key'))
            .mockResolvedValueOnce('ok');
        const wizard = await readyToConfirm({ onImport, allowPartial: true });
        button(wizard, 'import').click();
        await vi.waitFor(() => expect(wizard.snapshot().status).toBe('failed'));
        const failure = q(wizard, '.cl-import-wizard__failure');
        expect(failure.textContent).toBe('匯入失敗：duplicate key');
        expect(failure.getAttribute('role')).toBe('alert');
        button(wizard, 'retry').click();
        await vi.waitFor(() => expect(wizard.snapshot().status).toBe('done'));
        expect(q(wizard, '.cl-import-wizard__result').textContent).toBe('匯入完成：成功 2 列，失敗 0 列。');
    });

    it('ignores a pending import result after reset', async () => {
        let finish;
        const onImport = vi.fn(() => new Promise((resolve) => { finish = resolve; }));
        const wizard = await readyToConfirm({ onImport });
        button(wizard, 'import').click();
        wizard.reset();
        finish({ imported: 9 });
        await Promise.resolve();
        await new Promise((resolve) => setTimeout(resolve, 0));
        expect(wizard.snapshot()).toMatchObject({ step: 0, status: 'idle', result: null });
    });

    it('cancel resets the wizard and calls onCancel', async () => {
        const onCancel = vi.fn();
        const wizard = create({ onCancel });
        await chooseFile(wizard, textFile(VALID_CSV));
        wizard.goTo(1);
        button(wizard, 'cancel').click();
        expect(onCancel).toHaveBeenCalledTimes(1);
        expect(wizard.snapshot()).toMatchObject({ step: 0, status: 'idle' });
    });
});

describe('ImportWizard — navigation, lifecycle and locale', () => {
    it('goTo only reaches steps that are available', async () => {
        const wizard = create();
        expect(wizard.goTo(0)).toBe(true);
        expect(wizard.goTo(1)).toBe(false);
        expect(wizard.goTo('nope')).toBe(false);
        expect(wizard.goTo(7)).toBe(false);
        await chooseFile(wizard, textFile(VALID_CSV));
        expect(wizard.goTo(3)).toBe(true);
        expect(wizard.goTo('upload')).toBe(true);
        expect(wizard.snapshot().step).toBe(0);
        expect(ImportWizard.STEPS).toEqual(['upload', 'mapping', 'preview', 'confirm']);
    });

    it('destroy removes the DOM, child components and listeners, and ignores late reads', async () => {
        const tracker = trackGlobalListeners();
        const wizard = create();
        await chooseFile(wizard, textFile(VALID_CSV));
        wizard.goTo(1);
        const input = q(wizard, '.dropdown__input');
        input.focus();
        expect(tracker.active().some((entry) => entry.target === document && entry.type === 'click')).toBe(true);
        wizard.destroy();
        expect(host.children).toHaveLength(0);
        expect(tracker.active()).toEqual([]);
        expect(() => {
            wizard.destroy();
            wizard.goTo(0);
            wizard.reset();
            wizard.getRows();
            wizard.getMapping();
            wizard.show();
            wizard.hide();
        }).not.toThrow();

        const late = create();
        const lateInput = q(late, '.cl-import-wizard__file');
        Object.defineProperty(lateInput, 'files', { value: [textFile(VALID_CSV)] });
        lateInput.dispatchEvent(new Event('change', { bubbles: true }));
        late.destroy();
        await new Promise((resolve) => setTimeout(resolve, 20));
        expect(late.snapshot().lifecycle).toBe('destroyed');
        expect(late.snapshot().status).toBe('reading');
    });

    it('re-renders its strings when the locale changes', async () => {
        const wizard = create();
        await chooseFile(wizard, textFile(VALID_CSV, 'staff.csv'));
        Locale.setLang('en');
        expect(q(wizard, '[role="heading"]').textContent).toBe('Choose a file to import');
        expect(q(wizard, '.cl-import-wizard__loaded').textContent).toBe('Read "staff.csv": 2 rows, 2 columns');
        expect([...wizard.element.querySelectorAll('.cl-stepper__step')].map((step) => step.textContent))
            .toEqual(['1Upload', '2Map columns', '3Preview & validate', '4Confirm']);
        expect(button(wizard, 'next').textContent).toBe('Next');
        wizard.goTo(2);
        expect(q(wizard, '.cl-import-wizard__summary').textContent).toBe('2 rows: 2 valid, 0 invalid');
    });

    it('hide/show toggles visibility', () => {
        const wizard = create();
        wizard.hide();
        expect(wizard.element.style.display).toBe('none');
        wizard.show();
        expect(wizard.element.style.display).toBe('flex');
    });
});
