import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { starterDraft } from './model.mjs';
import { defaultCandidateWiring, generateBindings, generateCandidates, validatePagePlan } from './page-planner.mjs';
import { applyLibraryEntry, createLibraryEntry, LocalTemplateLibrary } from './local-library.mjs';

function fixture() {
    const source = starterDraft('blank'), target = starterDraft('blank');
    source.confirmed = true; target.confirmed = true;
    source.entities[0].examples = [['保密樣本', '不應寫入模板']];
    const candidate = generateCandidates(source)[0], binding = generateBindings(source)[0];
    const id = 'I_111111111111';
    const item = { kind: 'candidate', ref: candidate.registry_name, span: 12,
        instance_id: id, wiring: defaultCandidateWiring(candidate, binding, id) };
    const plan = { schema_version: 1, items: [item,
        { kind: 'builtin', ref: 'Text', span: 6, instance_id: 'I_222222222222' }] };
    const mapping = { entities: { [source.entities[0].code]: target.entities[0].code },
        fields: Object.fromEntries(source.entities[0].fields.map((field, index) =>
            [field.code, target.entities[0].fields[index].code])) };
    return { source, target, plan, mapping };
}

test('saved component and page templates rebind to a different confirmed interview', () => {
    const { source, target, plan, mapping } = fixture();
    const component = createLibraryEntry({ kind: 'component', title: '可重用查詢列',
        draft: source, plan, instance_id: plan.items[0].instance_id }, 'a'.repeat(32));
    assert.equal(component.plan.items.length, 1);
    assert.equal(JSON.stringify(component).includes(source.entities[0].examples[0][0]), false);
    const componentPlan = applyLibraryEntry(component, target, mapping);
    assert.equal(componentPlan.items[0].ref, generateCandidates(target)[0].registry_name);
    assert.equal(componentPlan.items[0].wiring.fields[0].field_code, target.entities[0].fields[0].code);
    assert.equal(validatePagePlan(componentPlan, generateCandidates(target), target), componentPlan);

    const page = createLibraryEntry({ kind: 'page', title: '查詢頁', draft: source, plan }, 'b'.repeat(32));
    const applied = applyLibraryEntry(page, target, mapping);
    assert.equal(applied.items.length, 2);
    assert.equal(validatePagePlan(applied, generateCandidates(target), target), applied);
    assert.throws(() => applyLibraryEntry(page, target, { entities: mapping.entities, fields: {} }), /不完整/);
    const wrongType = structuredClone(mapping);
    wrongType.fields[source.entities[0].fields[0].code] = target.entities[0].fields[1].code;
    assert.throws(() => applyLibraryEntry(page, target, wrongType), /重複|型別/);
});

test('page template rebinds standalone query and write flows without copying data', () => {
    const { source, target, mapping } = fixture();
    const field = source.entities[0].fields[0];
    const plan = { schema_version: 1, items: [
        { kind: 'builtin', ref: 'TextInput', span: 6, instance_id: 'I_111111111111' },
        { kind: 'builtin', ref: 'DataTable', span: 12, instance_id: 'I_222222222222' },
        { kind: 'builtin', ref: 'BasicButton', span: 6, instance_id: 'I_333333333333' }
    ], flows: [
        { entity_code: source.entities[0].code, trigger_instance_id: '@page', event: 'load',
            operation: 'query', filters: [{ input_instance_id: 'I_111111111111',
                field_code: field.code, operator: 'contains' }],
            target_instance_id: 'I_222222222222', columns: [field.code] },
        { entity_code: source.entities[0].code, trigger_instance_id: 'I_333333333333',
            event: 'click', operation: 'create',
            values: [{ input_instance_id: 'I_111111111111', field_code: field.code }] }
    ] };
    const entry = createLibraryEntry({ kind: 'page', title: '查詢與新增', draft: source, plan });
    const applied = applyLibraryEntry(entry, target, mapping);
    assert.equal(applied.flows[0].entity_code, target.entities[0].code);
    assert.equal(applied.flows[0].filters[0].field_code, target.entities[0].fields[0].code);
    assert.equal(applied.flows[1].values[0].field_code, target.entities[0].fields[0].code);
    assert.equal(validatePagePlan(applied, generateCandidates(target), target), applied);
});

test('local library saves immutable JSON entries and removes only a selected entry', () => {
    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-local-library-'));
    try {
        const { source, plan } = fixture();
        const library = new LocalTemplateLibrary(root);
        const saved = library.save({ kind: 'page', title: '查詢頁', draft: source, plan });
        assert.equal(library.list().length, 1);
        assert.equal(JSON.stringify(library.agentCatalog()).includes('查詢頁'), false);
        assert.equal(library.agentCatalog()[0].entities[0].fields[0].type, 'text');
        assert.equal(new LocalTemplateLibrary(root).get('page', saved.id).title, '查詢頁');
        const contents = readFileSync(path.join(root, `page-${saved.id}.json`), 'utf8');
        assert.equal(contents.includes(source.entities[0].examples[0][0]), false);
        assert.throws(() => library.save({ kind: 'page', title: '<script>', draft: source, plan }), /名稱/);
        assert.throws(() => library.path('page', '../outside'), /代碼/);
        assert.equal(library.remove('page', saved.id), true);
        assert.deepEqual(library.list(), []);
    } finally {
        const resolved = path.resolve(root), temp = path.resolve(os.tmpdir());
        if (!resolved.startsWith(`${temp}${path.sep}`) || !path.basename(resolved).startsWith('b4a-local-library-'))
            throw new Error('Unexpected library test path');
        rmSync(resolved, { recursive: true, force: true });
    }
});
