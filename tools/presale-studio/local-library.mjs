import { createHash, randomBytes } from 'node:crypto';
import { existsSync, lstatSync, mkdirSync, readFileSync, readdirSync,
    unlinkSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { validateDraft } from './model.mjs';
import { generateBindings, generateCandidates, validatePagePlan } from './page-planner.mjs';

const KINDS = new Set(['component', 'page']);
const ID = /^[a-f0-9]{32}$/;
const TITLE = /^[^<>\u0000-\u001f\u007f]{1,60}$/;
const MAX_ENTRIES = 200;
const MAX_FILE_BYTES = 256 * 1024;
const own = (value, keys) => value && typeof value === 'object' && !Array.isArray(value) &&
    Object.keys(value).sort().join('|') === [...keys].sort().join('|');
const digest = entry => createHash('sha256').update(JSON.stringify(entry)).digest('hex');

function sourceFor(draft, plan) {
    const bindings = new Map(generateBindings(draft).map(item => [item.ref, item]));
    const codes = new Set(), candidateRefs = new Map();
    for (const item of plan.items) {
        if (item.kind === 'candidate') {
            const binding = bindings.get(item.ref);
            codes.add(binding.entity_code);
            candidateRefs.set(item.ref, { ref: item.ref, entity_code: binding.entity_code, kind: binding.kind });
        }
        if (item.binding?.entity_code) codes.add(item.binding.entity_code);
    }
    for (const flow of plan.flows || []) codes.add(flow.entity_code);
    return { entities: draft.entities.filter(entity => codes.has(entity.code)).map(entity => ({
        code: entity.code, label: entity.label,
        fields: entity.fields.map(field => ({ code: field.code, label: field.label,
            type: field.type, required: field.required }))
    })), candidate_refs: [...candidateRefs.values()] };
}

export function createLibraryEntry(input, id = randomBytes(16).toString('hex')) {
    const { kind, title, draft, plan, instance_id } = input;
    if (!KINDS.has(kind) || !ID.test(id) || typeof title !== 'string' ||
        !TITLE.test(title.trim()) || title !== title.trim())
        throw new TypeError('本地庫項目的類型或名稱無效。');
    validateDraft(draft);
    validatePagePlan(plan, generateCandidates(draft), draft);
    let savedPlan;
    if (kind === 'component') {
        if (typeof instance_id !== 'string') throw new TypeError('請選擇要儲存的元件。');
        const item = plan.items.find(value => value.instance_id === instance_id);
        if (!item) throw new TypeError('找不到要儲存的元件。');
        savedPlan = { schema_version: 1, items: [structuredClone(item)] };
        try { validatePagePlan(savedPlan, generateCandidates(draft), draft); }
        catch { throw new TypeError('此元件依賴頁面上的其他元件，請改存整個頁面。'); }
    } else {
        if (Object.hasOwn(input, 'instance_id') || !plan.items.length)
            throw new TypeError('頁面模板須包含元件，且不接受單一元件代碼。');
        savedPlan = structuredClone(plan);
    }
    return validateRecord({ schema_version: 1, id, kind, title, created_at: new Date().toISOString(),
        source: sourceFor(draft, savedPlan), plan: savedPlan });
}

function validateRecord(entry) {
    if (!own(entry, ['schema_version', 'id', 'kind', 'title', 'created_at', 'source', 'plan']) ||
        entry.schema_version !== 1 || !ID.test(entry.id) || !KINDS.has(entry.kind) ||
        typeof entry.title !== 'string' || !TITLE.test(entry.title) ||
        typeof entry.created_at !== 'string' || Number.isNaN(Date.parse(entry.created_at)) ||
        !own(entry.source, ['entities', 'candidate_refs']) ||
        !Array.isArray(entry.source.entities) || !Array.isArray(entry.source.candidate_refs) ||
        !own(entry.plan, Object.hasOwn(entry.plan || {}, 'flows')
            ? ['schema_version', 'items', 'flows'] : ['schema_version', 'items']) ||
        entry.plan.schema_version !== 1 || !Array.isArray(entry.plan.items) ||
        entry.plan.items.length > 24 || (entry.kind === 'component' && entry.plan.items.length !== 1))
        throw new TypeError('本地庫檔案格式無效。');
    const entityCodes = new Set(), fieldCodes = new Set(), refs = new Set();
    for (const entity of entry.source.entities) {
        if (!own(entity, ['code', 'label', 'fields']) || !/^E_[A-F0-9]{12}$/.test(entity.code) ||
            entityCodes.has(entity.code) || typeof entity.label !== 'string' ||
            !TITLE.test(entity.label) || !Array.isArray(entity.fields) || entity.fields.length > 24)
            throw new TypeError('本地庫資料區塊無效。');
        entityCodes.add(entity.code);
        for (const field of entity.fields) {
            if (!own(field, ['code', 'label', 'type', 'required']) ||
                !/^F_[A-F0-9]{12}$/.test(field.code) || fieldCodes.has(field.code) ||
                typeof field.label !== 'string' || !TITLE.test(field.label) ||
                !['text', 'email', 'tel', 'number', 'date', 'textarea', 'toggle', 'select'].includes(field.type) ||
                typeof field.required !== 'boolean') throw new TypeError('本地庫欄位無效。');
            fieldCodes.add(field.code);
        }
    }
    for (const ref of entry.source.candidate_refs) {
        if (!own(ref, ['ref', 'entity_code', 'kind']) ||
            typeof ref.ref !== 'string' || !/^Presale[A-F0-9]{12}(Search|Summary|Editor)$/.test(ref.ref) ||
            refs.has(ref.ref) || !entityCodes.has(ref.entity_code) ||
            !['search', 'summary', 'editor'].includes(ref.kind))
            throw new TypeError('本地庫元件參照無效。');
        refs.add(ref.ref);
    }
    return entry;
}

export function applyLibraryEntry(entry, draft, mapping) {
    validateRecord(entry);
    validateDraft(draft);
    if (!own(mapping, ['entities', 'fields']) || !mapping.entities || !mapping.fields ||
        typeof mapping.entities !== 'object' || Array.isArray(mapping.entities) ||
        typeof mapping.fields !== 'object' || Array.isArray(mapping.fields))
        throw new TypeError('請先完成資料區塊與欄位對應。');
    const entityMap = new Map(), fieldMap = new Map(), usedEntities = new Set();
    if (Object.keys(mapping.entities).length !== entry.source.entities.length ||
        Object.keys(mapping.fields).length !== entry.source.entities.reduce((count, entity) =>
            count + entity.fields.length, 0)) throw new TypeError('資料區塊與欄位對應不完整。');
    for (const source of entry.source.entities) {
        const target = draft.entities.find(entity => entity.code === mapping.entities[source.code]);
        if (!target || usedEntities.has(target.code)) throw new TypeError('資料區塊對應重複或不存在。');
        usedEntities.add(target.code); entityMap.set(source.code, target.code);
        const sourceCandidates = entry.source.candidate_refs.filter(ref => ref.entity_code === source.code);
        if (sourceCandidates.length && (source.fields.length !== target.fields.length ||
            source.fields.some((field, index) => field.type !== target.fields[index].type)))
            throw new TypeError('複合元件需要欄位數量與型別順序相同的資料區塊。');
        const usedFields = new Set();
        for (const field of source.fields) {
            const selected = target.fields.find(item => item.code === mapping.fields[field.code]);
            if (!selected || selected.type !== field.type || usedFields.has(selected.code))
                throw new TypeError('欄位對應重複、型別不符或不存在。');
            usedFields.add(selected.code); fieldMap.set(field.code, selected.code);
        }
    }
    const bindings = generateBindings(draft);
    const refs = new Map(entry.source.candidate_refs.map(source => [source.ref,
        bindings.find(binding => binding.entity_code === entityMap.get(source.entity_code) &&
            binding.kind === source.kind)?.ref]));
    const rewrite = value => {
        if (typeof value === 'string') return entityMap.get(value) || fieldMap.get(value) || refs.get(value) || value;
        if (Array.isArray(value)) return value.map(rewrite);
        if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value)
            .map(([key, child]) => [key, rewrite(child)]));
        return value;
    };
    const plan = rewrite(entry.plan);
    validatePagePlan(plan, generateCandidates(draft), draft);
    return plan;
}

export class LocalTemplateLibrary {
    constructor(root) {
        this.root = path.resolve(root);
        if (existsSync(this.root)) {
            const state = lstatSync(this.root);
            if (!state.isDirectory() || state.isSymbolicLink()) throw new TypeError('本地庫目錄無效。');
        } else mkdirSync(this.root, { recursive: true });
    }
    path(kind, id) {
        if (!KINDS.has(kind) || !ID.test(id)) throw new TypeError('本地庫代碼無效。');
        return path.join(this.root, `${kind}-${id}.json`);
    }
    list() {
        const entries = [];
        for (const name of readdirSync(this.root)) {
            if (!/^(component|page)-[a-f0-9]{32}\.json$/.test(name)) continue;
            const [kind, id] = /^(.+)-([a-f0-9]{32})\.json$/.exec(name).slice(1);
            entries.push(this.get(kind, id));
        }
        return entries.sort((a, b) => b.created_at.localeCompare(a.created_at))
            .map(({ id, kind, title, created_at, source }) => ({ id, kind, title, created_at,
                entity_labels: source.entities.map(entity => entity.label) }));
    }
    agentCatalog() {
        return this.list().map(summary => {
            const entry = this.get(summary.kind, summary.id);
            return { id: entry.id, kind: entry.kind,
                entities: entry.source.entities.map(entity => ({ code: entity.code,
                    fields: entity.fields.map(field => ({ code: field.code, type: field.type })) })),
                candidate_refs: entry.source.candidate_refs.map(ref => ({ ...ref })) };
        });
    }
    get(kind, id) {
        const file = this.path(kind, id);
        if (!existsSync(file)) return null;
        const state = lstatSync(file);
        if (!state.isFile() || state.isSymbolicLink() || state.size > MAX_FILE_BYTES)
            throw new TypeError('本地庫檔案無效。');
        const data = JSON.parse(readFileSync(file, 'utf8'));
        if (!own(data, ['digest', 'entry']) || data.digest !== digest(data.entry) ||
            data.entry.id !== id || data.entry.kind !== kind)
            throw new TypeError('本地庫檔案內容不一致。');
        return validateRecord(data.entry);
    }
    save(input) {
        if (this.list().length >= MAX_ENTRIES) throw new TypeError('本地庫已達項目上限。');
        const entry = createLibraryEntry(input);
        const file = this.path(entry.kind, entry.id);
        writeFileSync(file, `${JSON.stringify({ digest: digest(entry), entry }, null, 2)}\n`,
            { encoding: 'utf8', flag: 'wx' });
        return { id: entry.id, kind: entry.kind, title: entry.title, created_at: entry.created_at };
    }
    remove(kind, id) {
        const entry = this.get(kind, id);
        if (!entry) return false;
        unlinkSync(this.path(kind, id));
        return true;
    }
}
