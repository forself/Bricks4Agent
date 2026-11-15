import { PrototypeStore, testStorage } from './prototype-store.mjs';
import { SqlitePrototypeBackend } from './sqlite-backend.mjs';
import { scopeAllowed } from './prototype-rules.mjs';

export async function validatePrototype(config, seed) {
    const cases = [];
    const check = async (name, run) => {
        try { await run(); cases.push({ name, status: 'passed' }); }
        catch (error) { cases.push({ name, status: 'failed', reason: error.message }); }
    };
    const storage = testStorage();
    const store = new PrototypeStore(config, seed, storage);
    const resource = config.resources.find(item => (seed[item.api] || []).length > 0);
    const fullRole = config.roles.find(role => role.actions.includes('read') && role.vertical === 'all' && role.horizontal === 'all');
    const scopedRole = config.roles.find(role => role.actions.includes('read') && role.code !== fullRole?.code &&
        (role.vertical !== 'all' || role.horizontal !== 'all'));
    if (!resource || !fullRole || !scopedRole) {
        return { schema_version: 1, result: 'incomplete', cases: [{
            name: '角色與展示資料', status: 'skipped', reason: '需要至少一筆資料、全範圍角色及受限角色'
        }] };
    }
    const allRows = seed[resource.api];
    await check('全範圍角色可查詢展示資料', async () => {
        store.setRole(fullRole.code);
        const result = await store.get(`${resource.api}?pageSize=100`);
        if (result.total !== allRows.length) throw new Error('Visible count differs from seed');
    });
    await check('受限角色每次查詢套用範圍', async () => {
        store.setRole(scopedRole.code);
        const result = await store.get(`${resource.api}?pageSize=100`);
        const expected = store.rows[resource.api].filter(row => {
            const s = row.__scope;
            const v = scopedRole.vertical === 'all' ||
                (['self', 'self_descendants'].includes(scopedRole.vertical) && s.owner === config.subject.owner) ||
                (['descendants', 'self_descendants'].includes(scopedRole.vertical) && config.subject.descendants.includes(s.owner));
            const u = s.unit === config.subject.unit, f = s.function === config.subject.function;
            const h = scopedRole.horizontal === 'all' ||
                (scopedRole.horizontal === 'same_unit' && u) ||
                (scopedRole.horizontal === 'same_function' && f) ||
                (scopedRole.horizontal === 'unit_or_function' && (u || f)) ||
                (scopedRole.horizontal === 'unit_and_function' && u && f);
            return v && h;
        });
        if (result.total !== expected.length || expected.length === allRows.length)
            throw new Error('Scope did not narrow the result');
        const hidden = allRows.find(row => !expected.some(item => item.id === row.id));
        const field = resource.fields.find(item => typeof hidden?.[item.code] === 'string');
        if (hidden && field) {
            const query = await store.get(`${resource.api}?${encodeURIComponent(field.code)}=${encodeURIComponent(hidden[field.code])}`);
            if (query.items.some(row => row.id === hidden.id)) throw new Error('User filter bypassed scope');
        }
    });
    const editor = config.roles.find(role => role.actions.includes('read') && role.actions.includes('update'));
    if (editor) await check('修改資料後重新建立畫面仍可讀取', async () => {
        store.setRole(editor.code);
        const visible = (await store.get(resource.api)).items[0];
        if (!visible) throw new Error('No editable record');
        const field = resource.fields.find(item => item.type === 'text');
        if (!field) throw new Error('No text field');
        await store.put(`${resource.api}/${visible.id}`, { [field.code]: '驗證後資料' });
        const reopened = new PrototypeStore(config, seed, storage);
        reopened.setRole(editor.code);
        const value = await reopened.get(`${resource.api}/${visible.id}`);
        if (value[field.code] !== '驗證後資料') throw new Error('Update was not persisted');
    });
    const readOnly = config.roles.find(role => role.actions.includes('read') && !role.actions.includes('update'));
    if (readOnly) await check('唯讀角色不可修改', async () => {
        store.setRole(readOnly.code);
        const visible = (await store.get(resource.api)).items[0];
        if (!visible) throw new Error('No readable record');
        try { await store.put(`${resource.api}/${visible.id}`, {}); }
        catch (error) { if (error.status === 404 || error.status === 403) return; throw error; }
        throw new Error('Update unexpectedly succeeded');
    });
    return { schema_version: 1, result: cases.every(item => item.status === 'passed') ? 'passed' : 'failed', cases };
}

export async function validateSqlitePrototype(config, mapping, seed) {
    const cases = [];
    const check = (name, run) => {
        try { run(); cases.push({ name, status: 'passed' }); }
        catch (error) { cases.push({ name, status: 'failed', reason: error.message }); }
    };
    const resource = config.resources.find(item => (seed[item.api] || []).length > 0);
    const fullRole = config.roles.find(role => role.actions.includes('read') && role.vertical === 'all' && role.horizontal === 'all');
    const scopedRole = config.roles.find(role => role.actions.includes('read') && role.code !== fullRole?.code &&
        (role.vertical !== 'all' || role.horizontal !== 'all'));
    if (!resource || !fullRole || !scopedRole) return { schema_version: 1, result: 'incomplete', cases: [{
        name: 'SQLite 角色與展示資料', status: 'skipped', reason: '需要展示資料、全範圍及受限角色'
    }] };
    const backend = new SqlitePrototypeBackend({ dbPath: ':memory:', config, mapping, seed });
    try {
        const allRows = seed[resource.api];
        check('SQLite 全範圍角色可查詢', () => {
            if (backend.get(resource.api, fullRole).total !== allRows.length) throw new Error('Seed count differs');
        });
        check('SQLite 受限查詢與使用者篩選相交', () => {
            const expected = allRows.filter((_, index) =>
                scopeAllowed(scopedRole, config.subject, 'read', resource.sample_scopes[index]));
            const result = backend.get(resource.api, scopedRole);
            if (result.total !== expected.length || expected.length === allRows.length)
                throw new Error('Scope count differs');
            const hidden = allRows.find(row => !expected.some(item => item.id === row.id));
            const field = resource.fields.find(item => typeof hidden?.[item.code] === 'string');
            if (hidden && field && backend.get(`${resource.api}?${field.code}=${encodeURIComponent(hidden[field.code])}`, scopedRole).total !== 0)
                throw new Error('Filter bypassed scope');
        });
        check('SQLite 模組查詢、選項、摘要與匯出共用範圍', () => {
            const total = backend.get(resource.api, scopedRole).total;
            const field = resource.fields[0];
            if (backend.query(resource.api, {}, scopedRole).total !== total ||
                backend.lookup(resource.api, { field: field.code }, scopedRole)
                    .items.reduce((count, item) => count + item.count, 0) !== total ||
                backend.summary(resource.api, { metric: { op: 'count' } }, scopedRole).items[0].value !== total ||
                backend.export(resource.api, { fields: [field.code] }, scopedRole).row_count !== total)
                throw new Error('Read module scope differs');
        });
        const creator = config.roles.find(role => role.actions.includes('create'));
        if (creator) check('SQLite 批次新增預檢不寫入', () => {
            const before = backend.get(resource.api, fullRole).total;
            const row = Object.fromEntries(resource.fields.map(field => [field.code, allRows[0][field.code]]));
            const result = backend.batchCreate(resource.api, {
                request_id: '00000000000000000000000000000001', items: [row], dry_run: true
            }, creator);
            if (result.accepted !== 1 || backend.get(resource.api, fullRole).total !== before)
                throw new Error('Batch dry run changed records');
        });
        const editor = config.roles.find(role => role.actions.includes('read') && role.actions.includes('update'));
        if (editor) check('SQLite 型別核對及修改保存', () => {
            const visible = backend.get(resource.api, editor).items[0];
            const field = resource.fields.find(item => item.type === 'text');
            if (!visible || !field) throw new Error('No editable text record');
            let rejected = false;
            try { backend.put(`${resource.api}/${visible.id}`, { [field.code]: 42 }, editor); }
            catch (error) { if (error.status !== 400) throw error; rejected = true; }
            if (!rejected) throw new Error('Wrong type was accepted');
            backend.put(`${resource.api}/${visible.id}`, { [field.code]: 'SQLite 驗證' }, editor);
            if (backend.get(`${resource.api}/${visible.id}`, editor)[field.code] !== 'SQLite 驗證')
                throw new Error('Update was not persisted');
        });
        const readOnly = config.roles.find(role => role.actions.includes('read') && !role.actions.includes('update'));
        if (readOnly) check('SQLite 唯讀角色不可修改', () => {
            const visible = backend.get(resource.api, readOnly).items[0];
            if (!visible) throw new Error('No readable record');
            try { backend.put(`${resource.api}/${visible.id}`, {}, readOnly); }
            catch (error) { if (error.status === 404 || error.status === 403) return; throw error; }
            throw new Error('Update unexpectedly succeeded');
        });
    } finally { backend.close(); }
    return { schema_version: 1, result: cases.every(item => item.status === 'passed') ? 'passed' : 'failed', cases };
}
