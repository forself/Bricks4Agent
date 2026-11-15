import { BUILTIN_CHOICES, FLOW_INPUT_TYPES, candidateNodes, defaultCandidateWiring, defaultPagePlan,
    generateBindings, generateCandidates, validatePagePlan } from './page-planner.mjs';
import { componentEvents } from './component-events.mjs';
import { CustomComponentRegistry } from './runtime/custom_components/CustomComponentRegistry.js';
import { ComponentFactory } from './runtime/ui_components/binding/ComponentFactory.js';

const $ = id => document.getElementById(id);
const el = (tag, className, content) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (content !== undefined) node.textContent = content;
    return node;
};

export function createPlanner(getDraft, onStatus, onPlanChange = () => {}, onLibraryComponent = () => {}) {
    let plan = defaultPagePlan();
    let candidates = [];
    let bindings = new Map();
    let registry = null;
    let mounted = [];
    let pointerChoice = null;
    let libraryComponents = [];
    const palette = $('component-palette');
    const canvas = $('page-canvas');
    const flowPanel = $('page-flows');

    const destroyMounted = () => {
        for (const instance of mounted) instance.destroy();
        mounted = [];
    };
    const describe = item => item.kind === 'candidate'
        ? candidates.find(candidate => candidate.registry_name === item.ref)?.display_name || '未知複合元件'
        : BUILTIN_CHOICES.find(choice => choice.ref === item.ref)?.label || '未知元件';

    const instanceId = () => `I_${Array.from(crypto.getRandomValues(new Uint8Array(6)),
        value => value.toString(16).padStart(2, '0')).join('').toUpperCase()}`;

    function candidateFor(item) { return candidates.find(candidate => candidate.registry_name === item.ref); }
    function entityFor(item) {
        return getDraft().entities.find(entity => entity.code === bindings.get(item.ref)?.entity_code);
    }
    function fieldBindings(item) {
        const source = bindings.get(item.ref);
        return item.wiring?.fields ?? (item.binding?.field_codes || source?.field_codes || [])
            .map((field_code, index) => ({ node_id: `${{ search: 'filter-', summary: 'value-', editor: 'field-' }[source.kind]}${index}`,
                field_code }));
    }
    function modify(item, change) {
        const prior = structuredClone(item);
        change();
        try { validatePagePlan(plan, candidates, getDraft()); }
        catch (error) { Object.keys(item).forEach(key => delete item[key]); Object.assign(item, prior); return onStatus(error.message); }
        renderCanvas();
    }
    function targetChoices(item, operation) {
        const entity = entityFor(item);
        const kind = operation === 'query' ? 'DataTable' : operation === 'summary' ? 'Text' : 'Dropdown';
        const nodeId = operation === 'query' ? 'results' : operation === 'summary' ? 'metric' : null;
        return plan.items.flatMap(target => {
            if (target.kind !== 'candidate' || !target.instance_id || entityFor(target)?.code !== entity?.code) return [];
            return candidateNodes(candidateFor(target)).filter(node => node.component === kind &&
                (nodeId === null || node.id === nodeId)).map(node => ({
                id: target.instance_id, node: node.id,
                label: `${describe(target)} · ${node.id}`
            }));
        });
    }
    function inputChoices(item, operation) {
        const needed = ['query', 'export'].includes(operation) ? 'search'
            : ['save', 'new', 'delete'].includes(operation) ? 'editor' : null;
        return plan.items.filter(target => target.kind === 'candidate' && target.instance_id &&
            entityFor(target)?.code === entityFor(item)?.code &&
            (!needed || bindings.get(target.ref)?.kind === needed));
    }
    function actionFor(item, operation, nodeId) {
        const trigger = candidateNodes(candidateFor(item)).find(node => node.id === nodeId);
        if (['save', 'new', 'delete', 'export'].includes(operation) && trigger?.component !== 'BasicButton') return null;
        const input = inputChoices(item, operation)[0];
        if (!input) return null;
        const action = { node_id: nodeId, event: nodeId === '@page' ? 'load'
            : trigger?.component === 'BasicButton' ? 'click' : 'change', operation, input_instance_id: input.instance_id };
        if (['query', 'summary', 'lookup'].includes(operation)) {
            const target = targetChoices(item, operation)[0];
            if (!target) return null;
            action.target_instance_id = target.id;
            action.target_node = target.node;
        }
        if (operation === 'summary') action.metric = { op: 'count' };
        if (operation === 'lookup') action.field_code = entityFor(item).fields[0].code;
        return action;
    }

    function add(item, index = plan.items.length) {
        if (item.kind === 'library') return onLibraryComponent(item.ref);
        if (plan.items.length >= 24) return onStatus('頁面規劃最多 24 個元件。');
        const added = { kind: item.kind, ref: item.ref, span: 12, instance_id: instanceId() };
        if (item.kind === 'candidate') {
            const source = bindings.get(item.ref);
            added.wiring = defaultCandidateWiring(candidateFor(item), source, added.instance_id);
        }
        plan.items.splice(index, 0, added);
        renderCanvas();
    }
    const flowTargets = operation => plan.items.filter(item => item.kind === 'builtin' && item.instance_id &&
        item.ref === ({ query: 'DataTable', summary: 'Text', lookup: 'Dropdown' }[operation]));
    const flowInputs = () => plan.items.filter(item => item.kind === 'builtin' && item.instance_id &&
        FLOW_INPUT_TYPES[item.ref]);
    const isWriteFlow = flow => ['create', 'update', 'delete'].includes(flow.operation);
    const writeTables = entityCode => [...new Set((plan.flows || []).filter(flow =>
        flow.operation === 'query' && flow.entity_code === entityCode)
        .map(flow => flow.target_instance_id))].map(id => plan.items.find(item => item.instance_id === id));
    function inputBinding(id, excluding = null) {
        for (const flow of plan.flows || []) {
            if (flow === excluding) continue;
            for (const link of [...(flow.filters || []), ...(flow.values || [])])
                if (link.input_instance_id === id) return `${flow.entity_code}:${link.field_code}`;
        }
        return null;
    }
    function suggestValues(entity, operation, excluding = null) {
        const fields = operation === 'create' ? entity.fields.filter(field => field.required) : entity.fields;
        const used = new Set(), values = [];
        for (const field of fields) {
            const input = flowInputs().find(item => !used.has(item.instance_id) &&
                FLOW_INPUT_TYPES[item.ref].includes(field.type) &&
                (!inputBinding(item.instance_id, excluding) ||
                    inputBinding(item.instance_id, excluding) === `${entity.code}:${field.code}`));
            if (!input) {
                if (operation === 'create') return null;
                continue;
            }
            used.add(input.instance_id);
            values.push({ input_instance_id: input.instance_id, field_code: field.code });
            if (operation === 'update') break;
        }
        return values.length ? values : null;
    }
    function makeWriteFlow(operation, entity, excluding = null) {
        const button = plan.items.find(item => item.kind === 'builtin' && item.ref === 'BasicButton' &&
            item.instance_id && !item.binding && !(plan.flows || []).some(flow =>
                flow !== excluding && flow.trigger_instance_id === item.instance_id));
        if (!button) { onStatus('請先加入未設定其他動作的獨立按鈕。'); return null; }
        const flow = { entity_code: entity.code, trigger_instance_id: button.instance_id,
            event: 'click', operation };
        if (operation !== 'create') {
            const table = writeTables(entity.code)[0];
            if (!table) { onStatus('請先用查詢流程連結同區塊的獨立資料表。'); return null; }
            flow.source_table_instance_id = table.instance_id;
        }
        if (operation !== 'delete') {
            const values = suggestValues(entity, operation, excluding);
            if (!values) { onStatus('請先加入足夠的獨立輸入元件，並對應此區塊的必填欄位。'); return null; }
            flow.values = values;
        }
        return flow;
    }
    function changeFlow(change) {
        const prior = structuredClone(plan.flows || []);
        change();
        try { validatePagePlan(plan, candidates, getDraft()); }
        catch (error) {
            plan.flows = prior;
            renderCanvas();
            return onStatus(error.message);
        }
        renderCanvas();
    }
    function renderFlows() {
        const expanded = [...flowPanel.querySelectorAll('details')].map(node => node.open);
        flowPanel.replaceChildren();
        (plan.flows || []).forEach((flow, index) => {
            const entity = getDraft().entities.find(value => value.code === flow.entity_code);
            const write = isWriteFlow(flow);
            const details = el('details', 'wiring-panel');
            details.open = Boolean(expanded[index]);
            details.append(el('summary', '', `流程 ${index + 1} · ${entity.label} · ${{
                query: '查詢', summary: '摘要', lookup: '選項',
                create: '新增', update: '修改', delete: '刪除'
            }[flow.operation]}`));
            const controls = el('div', 'wiring-action');
            controls.append(selection('資料區塊', getDraft().entities.map(value => [value.code, value.label]),
                flow.entity_code, next => {
                    const changed = getDraft().entities.find(value => value.code === next);
                    if (write) {
                        const replacement = makeWriteFlow(flow.operation, changed, flow);
                        if (replacement) changeFlow(() => { plan.flows[index] = replacement; });
                    } else changeFlow(() => {
                        flow.entity_code = next; flow.filters = [];
                        if (flow.operation === 'query') flow.columns = changed.fields.slice(0, 4).map(field => field.code);
                        if (flow.operation === 'summary') flow.metric = { op: 'count' };
                        if (flow.operation === 'lookup') flow.field_code = changed.fields[0].code;
                    });
                }));
            const triggers = write ? plan.items.filter(item => item.kind === 'builtin' &&
                item.ref === 'BasicButton' && item.instance_id && !item.binding &&
                !(plan.flows || []).some(other => other !== flow &&
                    other.trigger_instance_id === item.instance_id))
                .map(item => [`${item.instance_id}|click`, `${describe(item)} ${item.instance_id.slice(-4)} · 點擊`])
                : [['@page|load', '頁面載入'], ...plan.items.flatMap(item =>
                    item.kind !== 'builtin' || !item.instance_id ||
                    (item.ref === 'BasicButton' && item.binding) ? [] : componentEvents(item.ref).map(event =>
                        [`${item.instance_id}|${event}`, `${describe(item)} ${item.instance_id.slice(-4)} · ${event}`]))];
            controls.append(selection('觸發元件與事件', triggers,
                `${flow.trigger_instance_id}|${flow.event}`, next => changeFlow(() => {
                    [flow.trigger_instance_id, flow.event] = next.split('|');
                })));
            if (write) {
                if (flow.operation !== 'create') controls.append(selection('選取資料的資料表',
                    writeTables(entity.code).map(item => [item.instance_id,
                        `${describe(item)} ${item.instance_id.slice(-4)}`]),
                flow.source_table_instance_id, next => changeFlow(() => {
                    flow.source_table_instance_id = next;
                })));
                for (const [valueIndex, value] of (flow.values || []).entries()) {
                    const input = plan.items.find(item => item.instance_id === value.input_instance_id);
                    const field = entity.fields.find(item => item.code === value.field_code);
                    controls.append(selection(`寫入欄 ${valueIndex + 1} 元件`, flowInputs().filter(item =>
                        FLOW_INPUT_TYPES[item.ref].includes(field.type)).map(item =>
                        [item.instance_id, `${describe(item)} ${item.instance_id.slice(-4)}`]),
                    value.input_instance_id, next => changeFlow(() => { value.input_instance_id = next; })));
                    controls.append(selection(`寫入欄 ${valueIndex + 1} 欄位`, entity.fields.filter(item =>
                        FLOW_INPUT_TYPES[input.ref].includes(item.type)).map(item => [item.code, item.label]),
                    value.field_code, next => changeFlow(() => { value.field_code = next; })));
                    if (flow.operation === 'update' || !field.required) controls.append(tool(
                        `移除寫入欄 ${valueIndex + 1}`, () => changeFlow(() => {
                            flow.values.splice(valueIndex, 1);
                        })));
                }
                if (flow.operation !== 'delete') controls.append(tool('增加寫入欄', () => {
                    const pair = entity.fields.flatMap(field => flowInputs().filter(item =>
                        !flow.values.some(value => value.input_instance_id === item.instance_id ||
                            value.field_code === field.code) && FLOW_INPUT_TYPES[item.ref].includes(field.type) &&
                        (!inputBinding(item.instance_id, flow) ||
                            inputBinding(item.instance_id, flow) === `${entity.code}:${field.code}`))
                        .map(item => ({ input_instance_id: item.instance_id, field_code: field.code })))[0];
                    if (!pair) return onStatus('沒有可對應的空白欄位與輸入元件。');
                    changeFlow(() => { flow.values.push(pair); });
                }));
            } else {
                controls.append(selection('後端行為', [['query', '查詢'], ['summary', '摘要'], ['lookup', '選項']],
                flow.operation, next => {
                    const target = flowTargets(next)[0];
                    if (!target) return onStatus('請先在畫布加入對應的資料表、文字或選單元件。');
                    changeFlow(() => {
                        delete flow.columns; delete flow.metric; delete flow.field_code;
                        flow.operation = next; flow.target_instance_id = target.instance_id;
                        if (next === 'query') flow.columns = entity.fields.slice(0, 4).map(field => field.code);
                        if (next === 'summary') flow.metric = { op: 'count' };
                        if (next === 'lookup') flow.field_code = entity.fields[0].code;
                    });
                }));
            controls.append(selection('結果元件', flowTargets(flow.operation).map(item =>
                [item.instance_id, `${describe(item)} ${item.instance_id.slice(-4)}`]),
            flow.target_instance_id, next => changeFlow(() => { flow.target_instance_id = next; })));
            if (flow.operation === 'query') {
                flow.columns.forEach((code, columnIndex) => controls.append(selection(`結果欄 ${columnIndex + 1}`,
                    entity.fields.map(field => [field.code, field.label]), code,
                    next => changeFlow(() => { flow.columns[columnIndex] = next; }))));
                controls.append(tool('增加結果欄', () => {
                    const next = entity.fields.find(field => !flow.columns.includes(field.code));
                    if (!next || flow.columns.length >= 6) return onStatus('結果表最多六欄。');
                    changeFlow(() => { flow.columns.push(next.code); });
                }));
                if (flow.columns.length > 1) controls.append(tool('移除最後一欄', () =>
                    changeFlow(() => { flow.columns.pop(); })));
            } else if (flow.operation === 'summary') {
                controls.append(selection('摘要計算', [['count', '筆數'],
                    ...entity.fields.filter(field => field.type === 'number')
                        .map(field => [`sum:${field.code}`, `${field.label}合計`])],
                flow.metric.op === 'count' ? 'count' : `sum:${flow.metric.field_code}`,
                next => changeFlow(() => { flow.metric = next === 'count' ? { op: 'count' }
                    : { op: 'sum', field_code: next.slice(4) }; })));
            } else controls.append(selection('選項欄位', entity.fields.map(field => [field.code, field.label]),
                flow.field_code, next => changeFlow(() => { flow.field_code = next; })));
            flow.filters.forEach((filter, filterIndex) => {
                const input = plan.items.find(item => item.instance_id === filter.input_instance_id);
                const compatible = entity.fields.filter(field => FLOW_INPUT_TYPES[input.ref].includes(field.type));
                controls.append(selection(`條件 ${filterIndex + 1} 元件`, flowInputs().filter(item =>
                    entity.fields.some(field => FLOW_INPUT_TYPES[item.ref].includes(field.type)))
                    .map(item => [item.instance_id, `${describe(item)} ${item.instance_id.slice(-4)}`]),
                filter.input_instance_id, next => changeFlow(() => {
                    const chosen = plan.items.find(item => item.instance_id === next);
                    const field = entity.fields.find(value => FLOW_INPUT_TYPES[chosen.ref].includes(value.type));
                    filter.input_instance_id = next; filter.field_code = field.code;
                    filter.operator = ['number', 'date', 'toggle', 'select'].includes(field.type) ? 'eq' : 'contains';
                })));
                controls.append(selection(`條件 ${filterIndex + 1} 欄位`, compatible.map(field => [field.code, field.label]),
                    filter.field_code, next => changeFlow(() => {
                        filter.field_code = next;
                        const field = entity.fields.find(value => value.code === next);
                        filter.operator = ['number', 'date', 'toggle', 'select'].includes(field.type) ? 'eq' : 'contains';
                    })));
                const field = entity.fields.find(value => value.code === filter.field_code);
                const operators = ['number', 'date'].includes(field.type) ? [['eq', '等於'], ['gte', '大於等於'], ['lte', '小於等於']]
                    : ['toggle', 'select'].includes(field.type) ? [['eq', '等於']] : [['contains', '包含'], ['eq', '等於']];
                controls.append(selection(`條件 ${filterIndex + 1} 比較`, operators, filter.operator,
                    next => changeFlow(() => { filter.operator = next; })));
                controls.append(tool(`移除條件 ${filterIndex + 1}`, () => changeFlow(() => {
                    flow.filters.splice(filterIndex, 1);
                })));
            });
            controls.append(tool('增加查詢條件', () => {
                const input = flowInputs().find(item => !flow.filters.some(filter =>
                    filter.input_instance_id === item.instance_id) &&
                    entity.fields.some(field => FLOW_INPUT_TYPES[item.ref].includes(field.type)));
                if (!input) return onStatus('請先加入可對應此資料區塊欄位的獨立輸入元件。');
                const field = entity.fields.find(value => FLOW_INPUT_TYPES[input.ref].includes(value.type));
                changeFlow(() => { flow.filters.push({ input_instance_id: input.instance_id,
                    field_code: field.code, operator: ['number', 'date', 'toggle', 'select'].includes(field.type)
                        ? 'eq' : 'contains' }); });
            }));
            }
            controls.append(tool('移除此流程', () => changeFlow(() => {
                plan.flows.splice(index, 1);
                if (flow.operation === 'query') plan.flows = plan.flows.filter(other =>
                    !['update', 'delete'].includes(other.operation) ||
                    other.source_table_instance_id !== flow.target_instance_id ||
                    plan.flows.some(query => query.operation === 'query' &&
                        query.entity_code === other.entity_code &&
                        query.target_instance_id === other.source_table_instance_id));
            })));
            details.append(controls); flowPanel.append(details);
        });
    }
    $('add-flow').addEventListener('click', () => {
        const target = flowTargets('query')[0];
        if (!target) return onStatus('請先把獨立資料表拖到頁面畫布。');
        const entity = getDraft().entities.find(value => value.code === $('flow-entity').value);
        changeFlow(() => {
            plan.flows ||= [];
            plan.flows.push({ entity_code: entity.code, trigger_instance_id: '@page', event: 'load',
                operation: 'query', filters: [], target_instance_id: target.instance_id,
                columns: entity.fields.slice(0, 4).map(field => field.code) });
        });
    });
    for (const operation of ['create', 'update', 'delete']) {
        $(`add-${operation}-flow`).addEventListener('click', () => {
            const entity = getDraft().entities.find(value => value.code === $('flow-entity').value);
            const flow = makeWriteFlow(operation, entity);
            if (flow) changeFlow(() => { plan.flows ||= []; plan.flows.push(flow); });
        });
    }
    function tool(label, action) {
        const button = el('button', 'planner-tool', label);
        button.type = 'button';
        button.addEventListener('click', action);
        return button;
    }
    function selection(label, choices, value, action) {
        const wrapper = el('label', 'planner-setting', label);
        const select = el('select');
        for (const [key, title] of choices) {
            const option = el('option', '', title);
            option.value = key;
            select.append(option);
        }
        select.value = value;
        select.addEventListener('change', () => action(select.value));
        wrapper.append(select);
        return wrapper;
    }
    function settings(item) {
        const draft = getDraft();
        const row = el('div', 'canvas-settings');
        const binding = bindings.get(item.ref);
        if (item.kind === 'candidate') {
            const entity = draft.entities.find(value => value.code === binding.entity_code);
            const fields = fieldBindings(item);
            if (item.wiring || binding.kind !== 'editor') fields.forEach((field, index) => {
                const original = entity.fields[index];
                row.append(selection(`${field.node_id} 欄位`,
                    entity.fields.filter(value => !item.wiring || value.type === original.type)
                        .map(value => [value.code, value.label]), field.field_code, next => {
                        if (item.wiring) modify(item, () => { item.wiring.fields[index].field_code = next; });
                        else {
                            const updated = fields.map(value => value.field_code);
                            updated[index] = next;
                            if (new Set(updated).size !== updated.length) return onStatus('同一元件不可重複使用相同欄位。');
                            modify(item, () => { item.binding = { field_codes: updated }; });
                        }
                    }));
                if (item.wiring && binding.kind === 'search') {
                    const selected = entity.fields.find(value => value.code === field.field_code);
                    const operators = ['number', 'date'].includes(selected.type)
                        ? [['eq', '等於'], ['gte', '大於等於'], ['lte', '小於等於']]
                        : ['toggle', 'select'].includes(selected.type)
                            ? [['eq', '等於']] : [['contains', '包含'], ['eq', '等於']];
                    row.append(selection(`${field.node_id} 條件`, operators, field.operator,
                        next => modify(item, () => { item.wiring.fields[index].operator = next; })));
                }
            });
            if (item.wiring && binding.kind === 'search') {
                item.wiring.columns.forEach((code, index) => row.append(selection(`結果欄 ${index + 1}`,
                    entity.fields.map(field => [field.code, field.label]), code,
                    next => modify(item, () => { item.wiring.columns[index] = next; }))));
                row.append(tool('增加結果欄', () => {
                    const next = entity.fields.find(field => !item.wiring.columns.includes(field.code));
                    if (!next || item.wiring.columns.length >= 6) return onStatus('結果表最多六欄。');
                    modify(item, () => { item.wiring.columns.push(next.code); });
                }));
                if (item.wiring.columns.length > 1) row.append(tool('移除最後一欄', () =>
                    modify(item, () => { item.wiring.columns.pop(); })));
            }
            if (!item.wiring) row.append(tool('啟用節點互動', () => modify(item, () => {
                item.instance_id ||= instanceId();
                item.wiring = defaultCandidateWiring(candidateFor(item), binding, item.instance_id);
                item.wiring.fields = fields;
                delete item.binding;
            })));
            if (item.wiring) {
                const details = el('details', 'wiring-panel');
                details.append(el('summary', '', '內部節點與事件'));
                const triggers = [{ id: '@page', component: 'Page' }, ...candidateNodes(candidateFor(item))
                    .filter(node => componentEvents(node.component).length)];
                item.wiring.actions.forEach((action, actionIndex) => {
                    const controls = el('div', 'wiring-action');
                    const eventChoices = nodeId => {
                        const component = triggers.find(node => node.id === nodeId)?.component;
                        return component === 'Page' ? [['load', '頁面載入']]
                            : componentEvents(component).map(event => [event,
                                { click: '點擊', change: '值變更', blur: '離開欄位' }[event]]);
                    };
                    controls.append(selection('觸發節點', triggers.map(node => [node.id, `${node.id} (${node.component})`]),
                        action.node_id, next => modify(item, () => {
                            action.node_id = next; action.event = eventChoices(next)[0][0];
                        })));
                    controls.append(selection('事件', eventChoices(action.node_id), action.event,
                        next => modify(item, () => { action.event = next; })));
                    const ops = [['query', '查詢'], ['summary', '摘要'], ['lookup', '選項'],
                        ['save', '儲存'], ['new', '新增'], ['delete', '刪除'], ['export', '匯出 CSV']];
                    controls.append(selection('行為', ops, action.operation, next => {
                        const replacement = actionFor(item, next, action.node_id);
                        if (!replacement) return onStatus('請先在畫布加入相同資料區塊的輸入與結果元件。');
                        modify(item, () => { item.wiring.actions[actionIndex] = replacement; });
                    }));
                    controls.append(selection('資料來源', inputChoices(item, action.operation)
                        .map(source => [source.instance_id, describe(source)]), action.input_instance_id,
                    next => modify(item, () => { action.input_instance_id = next; })));
                    if (['query', 'summary', 'lookup'].includes(action.operation)) {
                        const targets = targetChoices(item, action.operation);
                        controls.append(selection('結果節點', targets.map(target =>
                            [`${target.id}:${target.node}`, target.label]),
                        `${action.target_instance_id}:${action.target_node}`, next => modify(item, () => {
                            const target = targets.find(value => `${value.id}:${value.node}` === next);
                            action.target_instance_id = target.id; action.target_node = target.node;
                        })));
                    }
                    if (action.operation === 'summary') controls.append(selection('摘要計算', [
                        ['count', '筆數'], ...entity.fields.filter(field => field.type === 'number')
                            .map(field => [`sum:${field.code}`, `${field.label}合計`])
                    ], action.metric.op === 'count' ? 'count' : `sum:${action.metric.field_code}`, next =>
                        modify(item, () => { action.metric = next === 'count' ? { op: 'count' }
                            : { op: 'sum', field_code: next.slice(4) }; })));
                    if (action.operation === 'lookup') controls.append(selection('選項欄位',
                        entity.fields.map(field => [field.code, field.label]), action.field_code,
                        next => modify(item, () => { action.field_code = next; })));
                    controls.append(tool('移除此事件', () => modify(item, () => { item.wiring.actions.splice(actionIndex, 1); })));
                    details.append(controls);
                });
                details.append(tool('新增事件', () => {
                    const free = triggers.flatMap(node => (node.component === 'Page' ? ['load']
                        : componentEvents(node.component))
                        .filter(event => !item.wiring.actions.some(action => action.node_id === node.id && action.event === event))
                        .map(event => ({ node: node.id, event })))[0];
                    if (!free) return onStatus('沒有可用的內部節點事件。');
                    const operation = binding.kind === 'search' ? 'query'
                        : binding.kind === 'summary' ? 'summary' : 'save';
                    const next = actionFor(item, operation, free.node);
                    if (!next) return onStatus('請先加入對應的資料與結果元件。');
                    next.event = free.event;
                    modify(item, () => { item.wiring.actions.push(next); });
                }));
                row.append(details);
            }
        } else if (item.kind === 'builtin' && item.ref === 'BasicButton') {
            const operations = [['', '僅顯示'], ['search', '查詢'], ['new', '新增'], ['save', '儲存']];
            row.append(selection('按鈕動作', operations, item.binding?.operation || '', next => {
                if (next) item.binding = { entity_code: item.binding?.entity_code || draft.entities[0].code, operation: next };
                else delete item.binding;
                renderCanvas();
            }));
            if (item.binding) row.append(selection('資料區塊',
                draft.entities.map(entity => [entity.code, entity.label]), item.binding.entity_code, next => {
                    item.binding.entity_code = next;
                    renderCanvas();
                }));
        }
        return row;
    }
    function componentOptions(item) {
        if (item.kind === 'candidate') {
            const binding = bindings.get(item.ref);
            if (!binding) return {};
            const entity = getDraft().entities.find(value => value.code === binding.entity_code);
            const nodeOptions = {};
            fieldBindings(item).forEach(({ node_id, field_code }) => {
                const field = entity.fields.find(value => value.code === field_code);
                if (binding.kind === 'search') nodeOptions[node_id] = { label: field.label, placeholder: `搜尋${field.label}` };
                else if (binding.kind === 'summary') nodeOptions[node_id] = {
                    text: `${field.label}：${String(entity.examples[0]?.[entity.fields.indexOf(field)] ?? '—')}`
                };
                else nodeOptions[node_id] = { label: field.label };
            });
            if (binding.kind === 'search') nodeOptions.results = { columns: (item.wiring?.columns ||
                entity.fields.slice(0, 4).map(field => field.code)).map(code => {
                const field = entity.fields.find(value => value.code === code);
                return { name: field.code, label: field.label };
            }) };
            return { nodeOptions };
        }
        const choice = BUILTIN_CHOICES.find(value => value.ref === item.ref);
        const writeFlow = item.ref === 'BasicButton' ? (plan.flows || []).find(flow =>
            flow.trigger_instance_id === item.instance_id && isWriteFlow(flow)) : null;
        if (writeFlow) {
            const entity = getDraft().entities.find(value => value.code === writeFlow.entity_code);
            return { ...choice.options, customLabel: `${{ create: '新增', update: '修改',
                delete: '刪除' }[writeFlow.operation]}${entity.label}` };
        }
        const filter = (plan.flows || []).flatMap(flow => [...(flow.filters || []), ...(flow.values || [])]
            .map(value => ({ flow, value })))
            .find(value => value.value.input_instance_id === item.instance_id);
        if (filter) {
            const field = getDraft().entities.find(entity => entity.code === filter.flow.entity_code)
                ?.fields.find(value => value.code === filter.value.field_code);
            if (field) return { ...choice.options, label: field.label,
                ...(item.ref === 'TextInput' ? { placeholder: `搜尋${field.label}` } : {}) };
        }
        const tableFlow = (plan.flows || []).find(flow => flow.operation === 'query' &&
            flow.target_instance_id === item.instance_id);
        if (tableFlow) {
            const entity = getDraft().entities.find(value => value.code === tableFlow.entity_code);
            return { ...choice.options, columns: tableFlow.columns.map(code => {
                const field = entity.fields.find(value => value.code === code);
                return { name: code, label: field.label };
            }) };
        }
        if (item.ref !== 'BasicButton' || !item.binding) return choice.options;
        const entity = getDraft().entities.find(value => value.code === item.binding.entity_code);
        const verb = { search: '查詢', new: '新增', save: '儲存' }[item.binding.operation];
        return { ...choice.options, customLabel: `${verb}${entity.label}` };
    }
    function renderPalette() {
        palette.replaceChildren();
        const entitySelect = $('flow-entity');
        const currentEntity = entitySelect.value;
        entitySelect.replaceChildren();
        for (const entity of getDraft().entities) {
            const option = el('option', '', entity.label);
            option.value = entity.code;
            entitySelect.append(option);
        }
        if (getDraft().entities.some(entity => entity.code === currentEntity)) entitySelect.value = currentEntity;
        const entries = [
            ...candidates.map(candidate => ({ kind: 'candidate', ref: candidate.registry_name,
                label: candidate.display_name, note: '訪談生成・複合元件' })),
            ...libraryComponents.map(saved => ({ kind: 'library', ref: saved.id,
                label: saved.title, note: '本地庫・選擇欄位後加入' })),
            ...BUILTIN_CHOICES.map(choice => ({ kind: 'builtin', ref: choice.ref,
                label: choice.label, note: `B4A 元件・${choice.ref}` }))
        ];
        for (const entry of entries) {
            const card = el('div', 'palette-item');
            card.draggable = true;
            card.addEventListener('dragstart', event => {
                if (pointerChoice) pointerChoice.native = true;
                event.dataTransfer.effectAllowed = 'copy';
                event.dataTransfer.setData('text/plain', `${entry.kind}:${entry.ref}`);
            });
            card.addEventListener('dragend', () => { pointerChoice = null; canvas.classList.remove('page-canvas--drag-over'); });
            card.addEventListener('pointerdown', event => {
                if (event.button !== 0 || event.target.closest('button')) return;
                pointerChoice = { entry, x: event.clientX, y: event.clientY, moved: false, native: false };
            });
            const caption = el('div');
            caption.append(el('strong', '', entry.label), el('small', '', entry.note));
            card.append(caption, tool('加入', () => add(entry)));
            palette.append(card);
        }
    }
    function renderCanvas() {
        const expanded = [...canvas.querySelectorAll('.canvas-item')]
            .map(card => Boolean(card.querySelector('.wiring-panel')?.open));
        destroyMounted();
        canvas.replaceChildren();
        canvas.classList.toggle('page-canvas--empty', plan.items.length === 0);
        if (!plan.items.length) canvas.append(el('p', 'canvas-empty', '把左側元件拖到這裡，或按「加入」。'));
        plan.items.forEach((item, index) => {
            const card = el('article', `canvas-item canvas-item--${item.span}`);
            card.dataset.index = String(index);
            const head = el('div', 'canvas-item-head');
            head.append(el('strong', '', `${index + 1}. ${describe(item)}`));
            const actions = el('div', 'canvas-actions');
            actions.append(
                tool('↑', () => { if (index > 0) { [plan.items[index - 1], plan.items[index]] = [plan.items[index], plan.items[index - 1]]; renderCanvas(); } }),
                tool('↓', () => { if (index < plan.items.length - 1) { [plan.items[index], plan.items[index + 1]] = [plan.items[index + 1], plan.items[index]]; renderCanvas(); } }),
                tool(item.span === 12 ? '半寬' : '全寬', () => { item.span = item.span === 12 ? 6 : 12; renderCanvas(); }),
                tool('移除', () => {
                    const removed = plan.items.splice(index, 1)[0];
                    for (const remaining of plan.items) if (remaining.wiring)
                        remaining.wiring.actions = remaining.wiring.actions.filter(action =>
                            action.input_instance_id !== removed.instance_id &&
                            action.target_instance_id !== removed.instance_id);
                    if (plan.flows) {
                        const remainingFlows = plan.flows.filter(flow =>
                            flow.trigger_instance_id !== removed.instance_id &&
                            flow.target_instance_id !== removed.instance_id &&
                            flow.source_table_instance_id !== removed.instance_id &&
                            !(flow.values || []).some(value => value.input_instance_id === removed.instance_id))
                            .map(flow => ({ ...flow,
                                ...(flow.filters ? { filters: flow.filters.filter(filter =>
                                    filter.input_instance_id !== removed.instance_id) } : {}) }));
                        plan.flows = remainingFlows.filter(flow =>
                            !['update', 'delete'].includes(flow.operation) ||
                            remainingFlows.some(query => query.operation === 'query' &&
                                query.entity_code === flow.entity_code &&
                                query.target_instance_id === flow.source_table_instance_id));
                    }
                    renderCanvas();
                })
            );
            head.append(actions);
            const host = el('div', 'canvas-render');
            const configuration = settings(item);
            const details = configuration.querySelector('.wiring-panel');
            if (details) details.open = Boolean(expanded[index]);
            card.append(head, configuration, host);
            card.addEventListener('dragover', event => { event.preventDefault(); card.classList.add('drop-target'); });
            card.addEventListener('dragleave', () => card.classList.remove('drop-target'));
            card.addEventListener('drop', event => {
                event.preventDefault(); event.stopPropagation(); card.classList.remove('drop-target');
                const choice = fromTransfer(event.dataTransfer.getData('text/plain'));
                pointerChoice = null;
                if (choice) add(choice, index);
            });
            canvas.append(card);
            try {
                const instance = item.kind === 'candidate'
                    ? registry.create(item.ref, componentOptions(item))
                    : ComponentFactory.create(item.ref, componentOptions(item));
                instance.mount(host);
                mounted.push(instance);
            } catch (error) { host.textContent = `元件預覽失敗：${error.message}`; }
        });
        $('plan-count').textContent = `已配置 ${plan.items.length} 個元件；可拖入、排序並切換寬度。`;
        renderFlows();
        onPlanChange();
    }
    function fromTransfer(value) {
        const [kind, ref, extra] = value.split(':');
        if (extra !== undefined) return null;
        if (kind === 'candidate' && candidates.some(item => item.registry_name === ref)) return { kind, ref };
        if (kind === 'builtin' && BUILTIN_CHOICES.some(item => item.ref === ref)) return { kind, ref };
        return null;
    }
    canvas.addEventListener('dragover', event => event.preventDefault());
    canvas.addEventListener('drop', event => {
        event.preventDefault();
        const choice = fromTransfer(event.dataTransfer.getData('text/plain'));
        pointerChoice = null;
        if (choice) add(choice);
    });
    document.addEventListener('pointermove', event => {
        if (!pointerChoice || pointerChoice.native) return;
        if (Math.hypot(event.clientX - pointerChoice.x, event.clientY - pointerChoice.y) > 8)
            pointerChoice.moved = true;
        const target = document.elementFromPoint(event.clientX, event.clientY);
        canvas.classList.toggle('page-canvas--drag-over', pointerChoice.moved && canvas.contains(target));
    });
    document.addEventListener('pointerup', event => {
        const active = pointerChoice;
        pointerChoice = null;
        canvas.classList.remove('page-canvas--drag-over');
        if (!active || !active.moved || active.native) return;
        const target = document.elementFromPoint(event.clientX, event.clientY);
        if (!canvas.contains(target)) return;
        const card = target.closest('.canvas-item');
        add(active.entry, card ? Number(card.dataset.index) : plan.items.length);
    });
    document.addEventListener('pointercancel', () => {
        pointerChoice = null;
        canvas.classList.remove('page-canvas--drag-over');
    });
    $('clear-plan').addEventListener('click', () => { plan = defaultPagePlan(); renderCanvas(); });

    function clearPreview() {
            destroyMounted();
            pointerChoice = null;
            registry?.dispose();
            registry = null;
            candidates = [];
            bindings = new Map();
            palette.replaceChildren();
            canvas.replaceChildren(el('p', 'canvas-empty', '確認訪談後，這裡會顯示可拖拉的元件。'));
            flowPanel.replaceChildren();
            $('planner-panel').hidden = true;
    }
    return {
        getPlan: () => structuredClone(plan),
        listSavableComponents: () => plan.items.filter(item => item.instance_id &&
            (item.kind === 'candidate' || item.kind === 'builtin' && !item.binding))
            .map(item => ({ instance_id: item.instance_id, label: describe(item) })),
        setLibraryComponents(entries) {
            libraryComponents = entries.filter(item => item.kind === 'component');
            if (getDraft().confirmed) renderPalette();
        },
        addLibraryItem(source) {
            const item = structuredClone(source);
            const previous = item.instance_id;
            item.instance_id = instanceId();
            for (const action of item.wiring?.actions || []) {
                if (action.input_instance_id === previous) action.input_instance_id = item.instance_id;
                if (action.target_instance_id === previous) action.target_instance_id = item.instance_id;
            }
            const proposed = { ...plan, items: [...plan.items, item] };
            validatePagePlan(proposed, candidates, getDraft());
            plan = proposed;
            renderCanvas();
        },
        loadPlan(value) {
            plan = structuredClone(value);
            this.refresh();
        },
        reset() {
            clearPreview();
            plan = defaultPagePlan();
        },
        invalidate() { clearPreview(); },
        refresh() {
            clearPreview();
            if (!getDraft().confirmed) return;
            try {
                candidates = generateCandidates(getDraft());
                bindings = new Map(generateBindings(getDraft()).map(item => [item.ref, item]));
                const validCandidates = new Set(candidates.map(item => item.registry_name));
                plan.items = plan.items.filter(item => item.kind === 'builtin' || validCandidates.has(item.ref));
                for (const item of plan.items) item.instance_id ||= instanceId();
                for (const item of plan.items) {
                    if (item.wiring) continue;
                    if (!item.binding) continue;
                    try { validatePagePlan({ schema_version: 1, items: [item] }, candidates, getDraft()); }
                    catch { delete item.binding; }
                }
                try { validatePagePlan(plan, candidates, getDraft()); }
                catch {
                    for (const item of plan.items) if (item.wiring) {
                        item.wiring = defaultCandidateWiring(candidateFor(item), bindings.get(item.ref), item.instance_id);
                    }
                }
                if (plan.flows) {
                    const retained = [];
                    for (const flow of [...plan.flows].sort((a, b) =>
                        Number(b.operation === 'query') - Number(a.operation === 'query'))) {
                        try { validatePagePlan({ schema_version: 1, items: plan.items,
                            flows: [...retained, flow] }, candidates, getDraft()); retained.push(flow); }
                        catch { /* A changed interview invalidated this flow. */ }
                    }
                    plan.flows = plan.flows.filter(flow => retained.includes(flow));
                }
                validatePagePlan(plan, candidates, getDraft());
                registry = new CustomComponentRegistry({ registerWithFactory: false });
                registry.registerMany(candidates);
                $('planner-panel').hidden = false;
                renderPalette();
                renderCanvas();
            } catch (error) { onStatus(error.message); }
        }
    };
}
