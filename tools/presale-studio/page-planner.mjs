import { validateDraft } from './model.mjs';
import { componentEvents } from './component-events.mjs';

export const BUILTIN_CHOICES = Object.freeze([
    { ref: 'Heading', label: '標題', options: { text: '頁面標題', level: 2 } },
    { ref: 'Text', label: '說明文字', options: { text: '頁面說明', tag: 'p' } },
    { ref: 'TextInput', label: '文字輸入', options: { label: '欄位', placeholder: '請輸入' } },
    { ref: 'NumberInput', label: '數值輸入', options: { label: '數值' } },
    { ref: 'DatePicker', label: '日期輸入', options: { label: '日期' } },
    { ref: 'TextArea', label: '多行輸入', options: { label: '內容' } },
    { ref: 'ToggleSwitch', label: '切換開關', options: { label: '狀態' } },
    { ref: 'Dropdown', label: '選單', options: { items: [], placeholder: '選擇項目' } },
    { ref: 'BasicButton', label: '操作按鈕', options: { customLabel: '操作', type: 'plain' } },
    { ref: 'DataTable', label: '資料表', options: { columns: [], data: [], pagination: false } },
    { ref: 'Divider', label: '分隔線', options: {} }
]);

const FIELD_COMPONENTS = Object.freeze({
    text: 'TextInput', email: 'TextInput', tel: 'TextInput', number: 'NumberInput',
    date: 'DatePicker', textarea: 'TextArea', toggle: 'ToggleSwitch', select: 'Dropdown'
});
const PARTS = Object.freeze({ search: '查詢列', summary: '資料摘要', editor: '編輯欄位組' });

function leaf(id, component, options) { return { type: 'component', id, component, options }; }
function group(id, children, columns = 1) {
    return { type: 'group', id, layout: { mode: columns === 1 ? 'stack' : 'grid', gap: 'md', ...(columns === 1 ? {} : { columns }) }, children };
}
function definition(entity, kind) {
    const token = entity.code.slice(2).toLowerCase();
    const id = `custom.presale_${token}_${kind}`;
    const name = `Presale${entity.code.slice(2)}${kind[0].toUpperCase()}${kind.slice(1)}`;
    let children;
    if (kind === 'search') {
        children = [
            leaf('heading', 'Heading', { text: `${entity.label}${PARTS[kind]}`, level: 3 }),
            ...entity.fields.slice(0, 2).map((field, index) => leaf(`filter-${index}`, FIELD_COMPONENTS[field.type],
                { label: field.label, ...(FIELD_COMPONENTS[field.type] === 'TextInput'
                    ? { placeholder: `搜尋${field.label}` } : {}) })),
            leaf('submit', 'BasicButton', { customLabel: '查詢', type: 'search' }),
            leaf('results', 'DataTable', { columns: entity.fields.slice(0, 4).map(field => ({
                name: field.code, label: field.label
            })), data: [], pagination: false })
        ];
    } else if (kind === 'summary') {
        children = [
            leaf('heading', 'Heading', { text: `${entity.label}${PARTS[kind]}`, level: 3 }),
            ...entity.fields.slice(0, 4).map((field, index) => leaf(`value-${index}`, 'Text', {
                text: `${field.label}：${String(entity.examples[0]?.[index] ?? '—')}`, tag: 'p'
            })),
            leaf('metric', 'Text', { text: '摘要：—', tag: 'p' }),
            leaf('refresh', 'BasicButton', { customLabel: '更新摘要', type: 'plain' })
        ];
    } else {
        children = [
            leaf('heading', 'Heading', { text: `${entity.label}${PARTS[kind]}`, level: 3 }),
            ...entity.fields.map((field, index) => {
                const component = FIELD_COMPONENTS[field.type];
                const options = component === 'TextInput' || component === 'NumberInput' || component === 'TextArea'
                    ? { label: field.label, placeholder: field.label }
                    : { label: field.label };
                return leaf(`field-${index}`, component, options);
            }),
            leaf('save', 'BasicButton', { customLabel: '儲存', type: 'save' })
        ];
    }
    return {
        schema_version: 1, component_id: id, registry_name: name,
        display_name: `${entity.label}・${PARTS[kind]}`, version: '1.0.0', kind: 'composite',
        description: `由已確認的${entity.label}訪談欄位產生，供頁面雛型規劃。`,
        root: group(`${kind}-root`, children, kind === 'editor' ? 2 : 1)
    };
}

export function generateCandidates(draft) {
    validateDraft(draft);
    return draft.entities.flatMap(entity => ['search', 'summary', 'editor'].map(kind => definition(entity, kind)));
}

export function generateBindings(draft) {
    validateDraft(draft);
    return draft.entities.flatMap(entity => ['search', 'summary', 'editor'].map(kind => ({
        ref: definition(entity, kind).registry_name,
        entity_code: entity.code,
        kind,
        field_codes: (kind === 'search' ? entity.fields.slice(0, 2) :
            kind === 'summary' ? entity.fields.slice(0, 4) : entity.fields).map(field => field.code)
    })));
}

export function defaultPagePlan() { return { schema_version: 1, items: [] }; }

const INSTANCE_ID = /^I_[A-F0-9]{12}$/;
const FIELD_NODE = { search: 'filter-', summary: 'value-', editor: 'field-' };

export function candidateNodes(candidate) {
    const nodes = [];
    const visit = node => {
        if (node.type === 'group') for (const child of node.children) visit(child);
        else nodes.push({ id: node.id, component: node.component });
    };
    visit(candidate.root);
    return nodes;
}

export function defaultCandidateWiring(candidate, source, instanceId) {
    const nodes = new Map(candidateNodes(candidate).map(node => [node.id, node]));
    const fields = source.field_codes.map((field_code, index) => ({
        node_id: `${FIELD_NODE[source.kind]}${index}`, field_code,
        ...(source.kind === 'search' ? { operator: ['TextInput', 'TextArea'].includes(
            nodes.get(`filter-${index}`)?.component) ? 'contains' : 'eq' } : {})
    }));
    const operations = { search: ['submit', 'query', 'results'],
        summary: ['refresh', 'summary', 'metric'], editor: ['save', 'save', null] };
    const [node_id, operation, target_node] = operations[source.kind];
    const action = { node_id, event: 'click', operation, input_instance_id: instanceId };
    if (target_node) Object.assign(action, { target_instance_id: instanceId, target_node });
    if (operation === 'summary') action.metric = { op: 'count' };
    const actions = [action];
    if (['query', 'summary'].includes(operation)) actions.push({ ...action, node_id: '@page', event: 'load' });
    return { fields, actions,
        ...(source.kind === 'search' ? { columns: source.field_codes.slice(0, 4) } : {}) };
}

function exact(value, allowed, required = allowed) {
    return value && typeof value === 'object' && !Array.isArray(value) &&
        Object.keys(value).every(key => allowed.includes(key)) &&
        required.every(key => Object.hasOwn(value, key));
}

function validateWiring(item, candidate, source, entities, itemsById, candidatesByName) {
    const wiring = item.wiring;
    if (!INSTANCE_ID.test(item.instance_id) || !exact(wiring, ['fields', 'actions', 'columns'], ['fields', 'actions']) ||
        !Array.isArray(wiring.fields) || !Array.isArray(wiring.actions) ||
        wiring.fields.length !== source.field_codes.length || wiring.actions.length > 8)
        throw new TypeError('複合元件互動配置格式無效。');
    const entity = entities.get(source.entity_code);
    if (source.kind === 'search') {
        if (!Array.isArray(wiring.columns) || !wiring.columns.length || wiring.columns.length > 6 ||
            new Set(wiring.columns).size !== wiring.columns.length ||
            wiring.columns.some(code => !entity?.fields.some(field => field.code === code)))
            throw new TypeError('資料表欄位連結無效。');
    } else if (Object.hasOwn(wiring, 'columns')) throw new TypeError('此元件不支援資料表欄位。');
    const nodes = new Map(candidateNodes(candidate).map(node => [node.id, node]));
    const fields = new Set(), fieldCodes = new Set();
    for (const field of wiring.fields) {
        if (!exact(field, ['node_id', 'field_code', 'operator'], ['node_id', 'field_code']) ||
            !/^F_[A-F0-9]{12}$/.test(field.field_code) ||
            !nodes.has(field.node_id) || !field.node_id.startsWith(FIELD_NODE[source.kind]) ||
            fields.has(field.node_id) || fieldCodes.has(field.field_code))
            throw new TypeError('複合元件欄位連結無效。');
        const index = Number(field.node_id.slice(FIELD_NODE[source.kind].length));
        const original = entity?.fields[index];
        const selected = entity?.fields.find(value => value.code === field.field_code);
        if (!Number.isInteger(index) || index < 0 || index >= source.field_codes.length ||
            !original || !selected || original.type !== selected.type)
            throw new TypeError('欄位型別與元件不相符。');
        if (source.kind === 'search') {
            const allowed = ['number', 'date'].includes(selected.type) ? ['eq', 'gte', 'lte']
                : ['toggle', 'select'].includes(selected.type) ? ['eq'] : ['eq', 'contains'];
            if (!allowed.includes(field.operator)) throw new TypeError('欄位篩選操作無效。');
        } else if (Object.hasOwn(field, 'operator')) throw new TypeError('此欄位不支援篩選操作。');
        fields.add(field.node_id); fieldCodes.add(field.field_code);
    }
    for (let index = 0; index < source.field_codes.length; index++)
        if (!fields.has(`${FIELD_NODE[source.kind]}${index}`)) throw new TypeError('複合元件缺少欄位連結。');
    const actions = new Set();
    for (const action of wiring.actions) {
        if (!exact(action, ['node_id', 'event', 'operation', 'input_instance_id',
            'target_instance_id', 'target_node', 'metric', 'field_code'],
        ['node_id', 'event', 'operation', 'input_instance_id']))
            throw new TypeError('複合元件事件格式無效。');
        const trigger = nodes.get(action.node_id);
        if (!(action.node_id === '@page' && action.event === 'load') &&
            !componentEvents(trigger?.component).includes(action.event))
            throw new TypeError('觸發節點不支援此事件。');
        if (actions.has(`${action.node_id}:${action.event}`)) throw new TypeError('同一事件不得重複綁定。');
        actions.add(`${action.node_id}:${action.event}`);
        if (['save', 'new', 'delete', 'export'].includes(action.operation) &&
            !(trigger?.component === 'BasicButton' && action.event === 'click'))
            throw new TypeError('異動或匯出只能由按鈕點擊觸發。');
        const input = itemsById.get(action.input_instance_id);
        const inputSource = input && candidatesByName.get(input.ref)?.source;
        if (!input || !inputSource || inputSource.entity_code !== source.entity_code)
            throw new TypeError('操作來源必須是同一資料區塊的複合元件。');
        const target = itemsById.get(action.target_instance_id);
        const targetCandidate = target && candidatesByName.get(target.ref);
        const result = targetCandidate && new Map(candidateNodes(targetCandidate.candidate).map(node => [node.id, node])).get(action.target_node);
        const targetValid = targetCandidate?.source.entity_code === source.entity_code;
        if (action.operation === 'query') {
            if (inputSource.kind !== 'search' || !targetValid || result?.component !== 'DataTable' ||
                action.target_node !== 'results' || Object.hasOwn(action, 'metric') || Object.hasOwn(action, 'field_code'))
                throw new TypeError('查詢結果必須綁定同區塊的資料表節點。');
        } else if (action.operation === 'summary') {
            if (!targetValid || result?.component !== 'Text' || action.target_node !== 'metric' ||
                !exact(action.metric, ['op', 'field_code'], ['op']) ||
                !['count', 'sum'].includes(action.metric.op) ||
                (action.metric.op === 'count' && Object.hasOwn(action.metric, 'field_code')) ||
                (action.metric.op === 'sum' && entity.fields.find(field => field.code === action.metric.field_code)?.type !== 'number') ||
                Object.hasOwn(action, 'field_code'))
                throw new TypeError('摘要欄位或結果節點無效。');
        } else if (action.operation === 'lookup') {
            if (!targetValid || result?.component !== 'Dropdown' ||
                !entity.fields.some(field => field.code === action.field_code) || Object.hasOwn(action, 'metric'))
                throw new TypeError('選項查詢結果必須綁定同區塊的選單節點。');
        } else if (['save', 'new', 'delete', 'export'].includes(action.operation)) {
            if (Object.hasOwn(action, 'target_instance_id') || Object.hasOwn(action, 'target_node') ||
                Object.hasOwn(action, 'metric') || Object.hasOwn(action, 'field_code') ||
                (['save', 'new', 'delete'].includes(action.operation) && inputSource.kind !== 'editor') ||
                (action.operation === 'export' && inputSource.kind !== 'search'))
                throw new TypeError('操作來源或輸出設定無效。');
        } else throw new TypeError('操作不在白名單內。');
    }
}

export const FLOW_INPUT_TYPES = Object.freeze({
    TextInput: ['text', 'email', 'tel'], NumberInput: ['number'], DatePicker: ['date'],
    TextArea: ['textarea', 'text'], ToggleSwitch: ['toggle'], Dropdown: ['select', 'text']
});

function validateFlows(flows, itemsById, entities) {
    if (!Array.isArray(flows) || flows.length > 8) throw new TypeError('頁面資料流程最多八條。');
    const targetBindings = new Map(), inputBindings = new Map(), triggerOperations = new Map();
    for (const flow of flows) {
        if (!exact(flow, ['entity_code', 'trigger_instance_id', 'event', 'operation', 'filters',
            'target_instance_id', 'columns', 'metric', 'field_code', 'values', 'source_table_instance_id'],
        ['entity_code', 'trigger_instance_id', 'event', 'operation']))
            throw new TypeError('頁面資料流程格式無效。');
        const entity = entities.get(flow.entity_code);
        if (!entity) throw new TypeError('頁面資料流程的資料區塊無效。');
        const trigger = itemsById.get(flow.trigger_instance_id);
        if (!(flow.trigger_instance_id === '@page' && flow.event === 'load') &&
            !(trigger?.kind === 'builtin' && componentEvents(trigger.ref).includes(flow.event)))
            throw new TypeError('頁面資料流程的觸發元件或事件無效。');
        if (trigger?.ref === 'BasicButton' && Object.hasOwn(trigger, 'binding'))
            throw new TypeError('資料流程按鈕不能同時使用舊式按鈕動作。');
        const isWrite = ['create', 'update', 'delete'].includes(flow.operation);
        const triggerKey = `${flow.trigger_instance_id}:${flow.event}`;
        if ((isWrite && triggerOperations.has(triggerKey)) ||
            triggerOperations.get(triggerKey) === 'write')
            throw new TypeError('資料異動須使用獨立的按鈕事件。');
        triggerOperations.set(triggerKey, isWrite ? 'write' : 'read');
        if (isWrite) {
            if (trigger?.ref !== 'BasicButton' || flow.event !== 'click' ||
                Object.hasOwn(flow, 'filters') || Object.hasOwn(flow, 'target_instance_id') ||
                Object.hasOwn(flow, 'columns') || Object.hasOwn(flow, 'metric') || Object.hasOwn(flow, 'field_code'))
                throw new TypeError('資料異動只能由獨立按鈕點擊觸發。');
            if (flow.operation === 'create') {
                if (Object.hasOwn(flow, 'source_table_instance_id'))
                    throw new TypeError('新增資料不接受資料表選取來源。');
            } else {
                const table = itemsById.get(flow.source_table_instance_id);
                if (table?.kind !== 'builtin' || table.ref !== 'DataTable' ||
                    !flows.some(other => other.operation === 'query' &&
                        other.entity_code === flow.entity_code &&
                        other.target_instance_id === flow.source_table_instance_id))
                    throw new TypeError('修改或刪除須選取同區塊查詢資料表的資料列。');
            }
            if (flow.operation === 'delete') {
                if (Object.hasOwn(flow, 'values')) throw new TypeError('刪除資料不接受欄位值。');
                continue;
            }
            if (!Array.isArray(flow.values) || !flow.values.length || flow.values.length > 12)
                throw new TypeError('資料異動欄位配置無效。');
            const usedInputs = new Set(), usedFields = new Set();
            for (const value of flow.values) {
                if (!exact(value, ['input_instance_id', 'field_code']) ||
                    usedInputs.has(value.input_instance_id) || usedFields.has(value.field_code))
                    throw new TypeError('資料異動欄位不可重複。');
                usedInputs.add(value.input_instance_id); usedFields.add(value.field_code);
                const input = itemsById.get(value.input_instance_id);
                const field = entity.fields.find(item => item.code === value.field_code);
                if (input?.kind !== 'builtin' || !FLOW_INPUT_TYPES[input.ref]?.includes(field?.type))
                    throw new TypeError('資料異動輸入元件與欄位型別不相符。');
                const signature = `${flow.entity_code}:${value.field_code}`;
                if (inputBindings.has(value.input_instance_id) &&
                    inputBindings.get(value.input_instance_id) !== signature)
                    throw new TypeError('同一輸入元件不可連結不同欄位。');
                inputBindings.set(value.input_instance_id, signature);
            }
            if (flow.operation === 'create' && entity.fields.some(field =>
                field.required && !usedFields.has(field.code)))
                throw new TypeError('新增資料須連結所有必填欄位。');
            continue;
        }
        if (!['query', 'summary', 'lookup'].includes(flow.operation) ||
            !Array.isArray(flow.filters) || flow.filters.length > 6 ||
            !Object.hasOwn(flow, 'target_instance_id') ||
            Object.hasOwn(flow, 'values') || Object.hasOwn(flow, 'source_table_instance_id'))
            throw new TypeError('唯讀資料流程格式無效。');
        const target = itemsById.get(flow.target_instance_id);
        const expected = { query: 'DataTable', summary: 'Text', lookup: 'Dropdown' }[flow.operation];
        if (!expected || target?.kind !== 'builtin' || target.ref !== expected)
            throw new TypeError('頁面資料流程的結果元件無效。');
        const targetSignature = `${flow.entity_code}:${flow.operation}:${
            flow.operation === 'query' && Array.isArray(flow.columns) ? flow.columns.join(',') : ''}`;
        if (targetBindings.has(flow.target_instance_id) &&
            targetBindings.get(flow.target_instance_id) !== targetSignature)
            throw new TypeError('同一結果元件的資料區塊或欄位配置不一致。');
        targetBindings.set(flow.target_instance_id, targetSignature);
        if (flow.operation === 'query') {
            if (!Array.isArray(flow.columns) || flow.columns.length < 1 || flow.columns.length > 6 ||
                new Set(flow.columns).size !== flow.columns.length ||
                flow.columns.some(code => !entity.fields.some(field => field.code === code)) ||
                Object.hasOwn(flow, 'metric') || Object.hasOwn(flow, 'field_code'))
                throw new TypeError('頁面資料表欄位無效。');
        } else if (flow.operation === 'summary') {
            if (!exact(flow.metric, ['op', 'field_code'], ['op']) ||
                !['count', 'sum'].includes(flow.metric.op) ||
                (flow.metric.op === 'count' && Object.hasOwn(flow.metric, 'field_code')) ||
                (flow.metric.op === 'sum' && entity.fields.find(field => field.code === flow.metric.field_code)?.type !== 'number') ||
                Object.hasOwn(flow, 'columns') || Object.hasOwn(flow, 'field_code'))
                throw new TypeError('頁面摘要計算無效。');
        } else if (!entity.fields.some(field => field.code === flow.field_code) ||
            Object.hasOwn(flow, 'columns') || Object.hasOwn(flow, 'metric'))
            throw new TypeError('頁面選項欄位無效。');
        const used = new Set();
        for (const filter of flow.filters) {
            if (!exact(filter, ['input_instance_id', 'field_code', 'operator']) ||
                used.has(filter.input_instance_id)) throw new TypeError('頁面查詢條件格式無效或重複。');
            used.add(filter.input_instance_id);
            const input = itemsById.get(filter.input_instance_id);
            const field = entity.fields.find(value => value.code === filter.field_code);
            if (input?.kind !== 'builtin' || !FLOW_INPUT_TYPES[input.ref]?.includes(field?.type))
                throw new TypeError('頁面輸入元件與欄位型別不相符。');
            const inputSignature = `${flow.entity_code}:${filter.field_code}`;
            if (inputBindings.has(filter.input_instance_id) &&
                inputBindings.get(filter.input_instance_id) !== inputSignature)
                throw new TypeError('同一輸入元件不可連結不同欄位。');
            inputBindings.set(filter.input_instance_id, inputSignature);
            const allowed = ['number', 'date'].includes(field.type) ? ['eq', 'gte', 'lte']
                : ['toggle', 'select'].includes(field.type) ? ['eq'] : ['eq', 'contains'];
            if (!allowed.includes(filter.operator)) throw new TypeError('頁面查詢條件操作無效。');
        }
    }
}

export function validatePagePlan(plan, candidates, draft = null) {
    if (!plan || typeof plan !== 'object' || Array.isArray(plan) ||
        !['items|schema_version', 'flows|items|schema_version'].includes(Object.keys(plan).sort().join('|')) ||
        plan.schema_version !== 1 ||
        !Array.isArray(plan.items) || plan.items.length > 24)
        throw new TypeError('頁面規劃格式無效，最多 24 個元件。');
    const candidateNames = new Set(candidates.map(item => item.registry_name));
    const builtinNames = new Set(BUILTIN_CHOICES.map(item => item.ref));
    const candidateBindings = draft ? new Map(generateBindings(draft).map(item => [item.ref, item])) : new Map();
    const candidatesByName = new Map(candidates.map(candidate => [candidate.registry_name, {
        candidate, source: candidateBindings.get(candidate.registry_name)
    }]));
    const entities = new Map((draft?.entities || []).map(entity => [entity.code, entity]));
    const itemsById = new Map();
    for (const item of plan.items) {
        if (!item || typeof item !== 'object' || Array.isArray(item) || item.instance_id === undefined) continue;
        if (!INSTANCE_ID.test(item.instance_id) || itemsById.has(item.instance_id))
            throw new TypeError('元件實例代碼無效或重複。');
        itemsById.set(item.instance_id, item);
    }
    for (const item of plan.items) {
        if (!item || typeof item !== 'object' || Array.isArray(item) ||
            !Object.keys(item).every(key => ['kind', 'ref', 'span', 'binding', 'instance_id', 'wiring'].includes(key)) ||
            !['kind', 'ref', 'span'].every(key => Object.hasOwn(item, key)) ||
            !['candidate', 'builtin'].includes(item.kind) || ![6, 12].includes(item.span) ||
            typeof item.ref !== 'string' ||
            !(item.kind === 'candidate' ? candidateNames : builtinNames).has(item.ref))
            throw new TypeError('頁面規劃含有未知元件或無效配置。');
        if (Object.hasOwn(item, 'instance_id') && !INSTANCE_ID.test(item.instance_id))
            throw new TypeError('元件實例代碼無效。');
        if (Object.hasOwn(item, 'wiring')) {
            if (item.kind !== 'candidate' || Object.hasOwn(item, 'binding') || !draft)
                throw new TypeError('互動配置只接受已確認的複合元件。');
            const source = candidateBindings.get(item.ref);
            if (!source) throw new TypeError('複合元件缺少資料區塊。');
            validateWiring(item, candidatesByName.get(item.ref).candidate, source,
                entities, itemsById, candidatesByName);
            continue;
        }
        if (!Object.hasOwn(item, 'binding')) continue;
        const binding = item.binding;
        if (!binding || typeof binding !== 'object' || Array.isArray(binding))
            throw new TypeError('元件連結格式無效。');
        if (item.kind === 'candidate') {
            const source = candidateBindings.get(item.ref);
            const entity = entities.get(source?.entity_code);
            const codes = binding.field_codes;
            if (!source || !['search', 'summary'].includes(source.kind) ||
                Object.keys(binding).join('|') !== 'field_codes' ||
                !Array.isArray(codes) || codes.length !== source.field_codes.length ||
                new Set(codes).size !== codes.length ||
                codes.some(code => !entity?.fields.some(field => field.code === code)))
                throw new TypeError('複合元件只能連結同區塊的既有欄位。');
        } else if (item.ref === 'BasicButton') {
            if (Object.keys(binding).sort().join('|') !== 'entity_code|operation' ||
                !entities.has(binding.entity_code) ||
                !['search', 'new', 'save'].includes(binding.operation))
                throw new TypeError('按鈕只能執行白名單內的資料操作。');
        } else throw new TypeError('這個元件不支援資料連結。');
    }
    if (Object.hasOwn(plan, 'flows')) validateFlows(plan.flows, itemsById, entities);
    return plan;
}
