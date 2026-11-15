import { CustomComponentRegistry } from './runtime/custom_components/CustomComponentRegistry.js';
import { ComponentFactory } from './runtime/ui_components/binding/ComponentFactory.js';
import { PrototypeStore } from './prototype-store.mjs';
import { RemotePrototypeStore, prototypeApiBase } from './remote-store.mjs';
import { bindComponentEvent } from './component-events.mjs';

const canvas = document.getElementById('plan-canvas');
const instances = [];
const element = (tag, value, className = '') => {
    const result = document.createElement(tag);
    if (value !== undefined) result.textContent = value;
    if (className) result.className = className;
    return result;
};

async function start() {
    const [plan, candidates, bindings, config, seed, site, report] = await Promise.all([
        'page-plan.json', 'candidate-components.json', 'candidate-bindings.json',
        'prototype-config.json', 'demo-seed.json', 'site.json', 'functional-validation.json'
    ].map(name => fetch(name).then(response => {
        if (!response.ok) throw new Error(`無法讀取 ${name}`);
        return response.json();
    })));
    const store = ['sqlite', 'sqlite-formal'].includes(site.prototype_backend)
        ? await RemotePrototypeStore.connect(config, prototypeApiBase(),
            { formal: site.prototype_backend === 'sqlite-formal' })
        : new PrototypeStore(config, seed);
    const byRef = new Map(bindings.map(binding => [binding.ref, binding]));
    const resources = new Map(config.resources.map(resource => [resource.entity_code, resource]));
    const selected = new Map(), editors = [], searches = [], summaries = [], buttons = [];
    const entriesById = new Map();
    const builtinEntries = new Map(), flowRows = new Map(), touchedInputs = new Set();
    const selectedByTable = new Map(), writeBusy = new Set();
    const flowRevisions = new Map();
    const fieldBindings = (item, source) => item.wiring?.fields ??
        (item.binding?.field_codes || source.field_codes).map((field_code, index) => ({
            node_id: `${{ search: 'filter-', summary: 'value-', editor: 'field-' }[source.kind]}${index}`,
            field_code
        }));
    document.querySelector('h1').textContent = site.title || site.meta?.title || '頁面規劃';
    document.getElementById('validation-result').textContent = site.prototype_backend === 'sqlite-formal'
        ? '正式模式使用 Broker 身分與核定權限；展示資料驗證報告不代表正式驗收。'
        : report.result === 'passed'
        ? `自動驗證通過 ${report.cases.length} 項；詳細結果包含在下載檔。`
        : '部分情境缺少角色或展示資料，請查看下載檔中的驗證報告。';
    const roleSelect = document.getElementById('test-role');
    if (site.prototype_backend === 'sqlite-formal') {
        roleSelect.hidden = true;
        document.getElementById('reset-prototype').hidden = true;
    } else {
        for (const role of config.roles) {
            const option = element('option', role.label);
            option.value = role.code;
            roleSelect.append(option);
        }
        roleSelect.value = store.role.code;
        roleSelect.addEventListener('change', async () => {
            try { await store.setRole(roleSelect.value); location.reload(); }
            catch (error) { roleSelect.value = store.role.code; console.error(error); }
        });
    }
    document.getElementById('reset-prototype').addEventListener('click', async () => {
        if (window.confirm('重設此雛型的展示資料？目前 SQLite 中的修改會清除。')) {
            await store.resetData(); location.reload();
        }
    });
    const status = message => { document.getElementById('plan-status').textContent = message; };
    const format = value => value === null || value === undefined ? '—' : String(value);
    const syncWriteButtons = () => {
        for (const flow of plan.flows || []) {
            if (!['create', 'update', 'delete'].includes(flow.operation)) continue;
            const button = builtinEntries.get(flow.trigger_instance_id)?.instance.element?.querySelector('button') ||
                builtinEntries.get(flow.trigger_instance_id)?.instance.element;
            if (button) button.disabled = site.prototype_backend !== 'sqlite' ||
                !store.role.actions.includes(flow.operation) || writeBusy.has(flow) ||
                (flow.operation !== 'create' && !selectedByTable.has(flow.source_table_instance_id));
        }
    };
    const selectRecord = (resource, row) => {
        selected.set(resource.entity_code, row?.id ?? null);
        for (const entry of editors.filter(item => item.resource === resource)) {
            const values = {};
            entry.fields.forEach(({ node_id, field_code }) => {
                const field = resource.fields.find(item => item.code === field_code);
                values[node_id] = row?.[field_code] ?? (field.type === 'toggle' ? false : '');
            });
            entry.instance.setValue(values);
            entry.selector.value = row ? String(row.id) : '';
            const save = entry.instance.getComponent('save')?.element?.querySelector('button') ||
                entry.instance.getComponent('save')?.element;
            if (save) save.disabled = !store.role.actions.includes(row ? 'update' : 'create');
        }
        for (const entry of summaries.filter(item => item.resource === resource)) {
            entry.fields.forEach(({ node_id, field_code }) => {
                const field = resource.fields.find(item => item.code === field_code);
                const component = entry.instance.getComponent(node_id);
                if (component?.element) component.element.textContent = `${field.label}：${format(row?.[field_code])}`;
            });
        }
        for (const entry of buttons.filter(item => item.resource === resource)) {
            const action = entry.operation === 'search' ? 'read'
                : entry.operation === 'new' ? 'create' : row ? 'update' : 'create';
            const target = entry.instance.element?.querySelector('button') || entry.instance.element;
            if (target) target.disabled = !store.role.actions.includes(action);
        }
    };
    const refreshChoices = async resource => {
        const rows = (await store.get(`${resource.api}?pageSize=100`)).items;
        for (const entry of editors.filter(item => item.resource === resource)) {
            const blank = element('option', '新增一筆');
            blank.value = '';
            entry.selector.replaceChildren(blank);
            for (const row of rows) {
                const option = element('option', `${row.id} · ${format(row[resource.fields[0].code])}`);
                option.value = String(row.id);
                entry.selector.append(option);
            }
            const id = selected.get(resource.entity_code);
            entry.selector.value = rows.some(row => row.id === id) ? String(id) : '';
        }
    };
    const refreshSearch = async entry => {
        if (entry.item.wiring) {
            const result = await store.query(entry.resource.api, {
                filters: inputFilters(entry), page: 1, page_size: 100
            });
            const table = entry.instance.getComponent('results');
            if (typeof table?.setData !== 'function') throw new Error('資料表結果節點已失效。');
            entry.currentRows = result.items;
            table.setData(result.items, result.total);
            entry.results.textContent = `符合 ${result.total} 筆；目前角色：${store.role.label}`;
            return;
        }
        const params = new URLSearchParams({ pageSize: '100' });
        entry.fields.forEach(({ node_id, field_code }) => {
            const value = entry.instance.getComponent(node_id)?.getValue?.();
            if (String(value ?? '').trim()) params.set(field_code, String(value).trim());
        });
        const result = await store.get(`${entry.resource.api}?${params}`);
        entry.currentRows = result.items;
        const table = element('table', undefined, 'plan-table');
        const head = element('tr');
        for (const field of entry.resource.fields.slice(0, 4)) head.append(element('th', field.label));
        head.append(element('th', '操作'));
        const thead = element('thead'); thead.append(head); table.append(thead);
        const body = element('tbody');
        for (const row of result.items) {
            const tr = element('tr');
            for (const field of entry.resource.fields.slice(0, 4)) tr.append(element('td', format(row[field.code])));
            const cell = element('td');
            const button = element('button', '選取', 'plan-action');
            button.type = 'button';
            button.addEventListener('click', () => { selectRecord(entry.resource, row); status(`已選取 ${entry.resource.label} #${row.id}`); });
            cell.append(button); tr.append(cell); body.append(tr);
        }
        table.append(body);
        entry.results.replaceChildren(element('p', `符合 ${result.total} 筆；目前角色：${store.role.label}`), table);
    };
    const inputFilters = entry => {
        if (entry.binding.kind !== 'search') return [];
        return entry.fields.flatMap(({ node_id, field_code, operator }) => {
            const field = entry.resource.fields.find(item => item.code === field_code);
            const component = entry.instance.getComponent(node_id);
            const raw = typeof component?.getValue === 'function' ? component.getValue()
                : component?.isChecked?.();
            if (raw === null || raw === undefined || raw === '') return [];
            const value = field.type === 'number' ? Number(raw)
                : field.type === 'toggle' ? Boolean(raw) : String(raw).trim();
            if (value === '' || (field.type === 'number' && !Number.isFinite(value)))
                throw new Error(`${field.label}的查詢值無效。`);
            return [{ field: field_code, op: operator ||
                (['number', 'date', 'toggle', 'select'].includes(field.type) ? 'eq' : 'contains'), value }];
        });
    };
    const saveEditor = async entry => {
        const raw = entry.instance.getValue();
        const body = Object.fromEntries(entry.fields.map(({ node_id, field_code }) => {
            const field = entry.resource.fields.find(item => item.code === field_code);
            const value = raw[node_id];
            return [field_code, field.type === 'number' ? (value === '' || value === null ? null : Number(value))
                : field.type === 'toggle' ? Boolean(value) : String(value ?? '')];
        }));
        const id = selected.get(entry.resource.entity_code);
        const row = id ? await store.put(`${entry.resource.api}/${id}`, body)
            : await store.post(entry.resource.api, body);
        selectRecord(entry.resource, row);
        await refreshChoices(entry.resource);
        for (const search of searches.filter(item => item.resource === entry.resource)) await refreshSearch(search);
        await refreshPageFlows(entry.resource);
        status(`${entry.resource.label} #${row.id} 已儲存；重新整理頁面可再次讀取。`);
    };
    const runAction = async (entry, action) => {
        const input = entriesById.get(action.input_instance_id);
        if (!input || input.resource !== entry.resource) throw new Error('資料來源已失效。');
        const filters = inputFilters(input);
        if (action.operation === 'query') {
            const target = entriesById.get(action.target_instance_id);
            const table = target?.instance.getComponent(action.target_node);
            if (!target || target.resource !== entry.resource || typeof table?.setData !== 'function')
                throw new Error('查詢結果節點已失效。');
            const result = await store.query(entry.resource.api, { filters, page: 1, page_size: 100 });
            target.currentRows = result.items;
            table.setData(result.items, result.total);
            if (target.results) target.results.textContent = `符合 ${result.total} 筆；目前角色：${store.role.label}`;
            status(`已查詢 ${result.total} 筆。`);
        } else if (action.operation === 'summary') {
            const target = entriesById.get(action.target_instance_id);
            const display = target?.instance.getComponent(action.target_node);
            if (!target || target.resource !== entry.resource || !display?.element)
                throw new Error('摘要結果節點已失效。');
            const metric = action.metric.op === 'count' ? { op: 'count' }
                : { op: 'sum', field: action.metric.field_code };
            const result = await store.summary(entry.resource.api, { filters, metric });
            const label = action.metric.op === 'count' ? '筆數'
                : entry.resource.fields.find(field => field.code === action.metric.field_code).label + '合計';
            display.element.textContent = `${label}：${format(result.items[0]?.value ?? 0)}`;
            status('摘要已更新。');
        } else if (action.operation === 'lookup') {
            const target = entriesById.get(action.target_instance_id);
            const dropdown = target?.instance.getComponent(action.target_node);
            if (!target || target.resource !== entry.resource || typeof dropdown?.setItems !== 'function')
                throw new Error('選項結果節點已失效。');
            const result = await store.lookup(entry.resource.api, { field: action.field_code, filters, limit: 100 });
            dropdown.setItems(result.items.map(item => ({ value: item.value, label: format(item.value) })));
            status(`已載入 ${result.items.length} 個選項。`);
        } else if (action.operation === 'save') await saveEditor(input);
        else if (action.operation === 'new') { selectRecord(input.resource, null); status('請填寫新資料後儲存。'); }
        else if (action.operation === 'delete') {
            const id = selected.get(input.resource.entity_code);
            if (!id) return status('請先選取資料。');
            if (!window.confirm(`確定刪除 ${input.resource.label} #${id}？`)) return;
            await store.delete(`${input.resource.api}/${id}`);
            selectRecord(input.resource, null);
            await refreshChoices(input.resource);
            for (const search of searches.filter(item => item.resource === input.resource)) await refreshSearch(search);
            await refreshPageFlows(input.resource);
            status(`資料 #${id} 已刪除。`);
        } else if (action.operation === 'export') {
            const blob = await store.exportCsv(entry.resource.api, {
                fields: input.item.wiring?.columns || input.resource.fields.slice(0, 4).map(field => field.code), filters
            });
            const url = URL.createObjectURL(blob);
            const link = element('a'); link.href = url; link.download = 'export.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            status('CSV 已下載。');
        }
    };
    const pageActions = [];
    const attachWiring = entry => {
        for (const action of entry.item.wiring?.actions || []) {
            if (action.node_id === '@page') { pageActions.push({ entry, action }); continue; }
            const component = entry.instance.getComponent(action.node_id);
            const name = entry.definitionNodes.get(action.node_id);
            bindComponentEvent(component, name, action.event, () => {
                void runAction(entry, action).catch(error => status(`操作失敗：${error.message}`));
            });
        }
    };
    const registry = new CustomComponentRegistry({ registerWithFactory: false });
    registry.registerMany(candidates);
    const builtinOptions = { Heading: { text: '頁面標題', level: 2 }, Text: { text: '頁面說明', tag: 'p' },
        TextInput: { label: '欄位', placeholder: '請輸入' }, NumberInput: { label: '數值' },
        DatePicker: { label: '日期' }, TextArea: { label: '內容' }, ToggleSwitch: { label: '狀態' },
        Dropdown: { items: [], placeholder: '選擇項目' },
        BasicButton: { customLabel: '操作', type: 'plain' },
        DataTable: { columns: [], data: [], pagination: false }, Divider: {} };
    if (!plan.items.length) canvas.textContent = '尚未在工作台配置元件。';
    for (const item of plan.items) {
        const host = element('section', undefined, `plan-item plan-item--${item.span}`);
        canvas.append(host);
        const sourceBinding = byRef.get(item.ref);
        const sourceResource = resources.get(sourceBinding?.entity_code);
        const nodeOptions = {};
        if (item.kind === 'candidate' && sourceResource) {
            fieldBindings(item, sourceBinding).forEach(({ node_id, field_code }) => {
                const field = sourceResource.fields.find(value => value.code === field_code);
                nodeOptions[node_id] = sourceBinding.kind === 'search'
                    ? { label: field.label, placeholder: `搜尋${field.label}` }
                    : sourceBinding.kind === 'summary' ? { text: `${field.label}：—` }
                        : { label: field.label };
            });
            if (sourceBinding.kind === 'search') nodeOptions.results = {
                columns: (item.wiring?.columns || sourceResource.fields.slice(0, 4).map(field => field.code))
                    .map(code => { const field = sourceResource.fields.find(value => value.code === code);
                        return { name: field.code, label: field.label }; }),
                options: { selectableRows: true, onRowSelectionChange: (_current, rows) => {
                    const row = candidateEntry?.currentRows?.[rows.at(-1)?.dataIndex];
                    if (row) selectRecord(sourceResource, row);
                } }
            };
        }
        const buttonResource = resources.get(item.binding?.entity_code);
        const buttonLabel = item.kind === 'builtin' && item.ref === 'BasicButton' && buttonResource
            ? { search: '查詢', new: '新增', save: '儲存' }[item.binding.operation] + buttonResource.label : null;
        const writeButtonFlow = item.kind === 'builtin' && item.ref === 'BasicButton'
            ? (plan.flows || []).find(flow => flow.trigger_instance_id === item.instance_id &&
                ['create', 'update', 'delete'].includes(flow.operation)) : null;
        const writeButtonResource = resources.get(writeButtonFlow?.entity_code);
        const inputFlow = (plan.flows || []).flatMap(flow =>
            [...(flow.filters || []), ...(flow.values || [])].map(filter => ({ flow, filter })))
            .find(value => value.filter.input_instance_id === item.instance_id);
        const inputField = inputFlow && resources.get(inputFlow.flow.entity_code)?.fields.find(field =>
            field.code === inputFlow.filter.field_code);
        const tableFlow = (plan.flows || []).find(flow => flow.operation === 'query' &&
            flow.target_instance_id === item.instance_id);
        const tableResource = tableFlow && resources.get(tableFlow.entity_code);
        const options = item.kind === 'builtin' ? { ...builtinOptions[item.ref],
            ...(inputField ? { label: inputField.label,
                ...(item.ref === 'TextInput' ? { placeholder: `搜尋${inputField.label}` } : {}) } : {}),
            ...(tableResource ? { columns: tableFlow.columns.map(code => {
                const field = tableResource.fields.find(value => value.code === code);
                return { name: code, label: field.label };
            }), options: { selectableRows: true, onRowSelectionChange: (_current, rows) => {
                const row = flowRows.get(item.instance_id)?.[rows.at(-1)?.dataIndex];
                if (row) selectedByTable.set(item.instance_id, row.id);
                else selectedByTable.delete(item.instance_id);
                for (const flow of plan.flows || []) if (flow.operation === 'update' &&
                    flow.source_table_instance_id === item.instance_id) {
                    for (const value of flow.values) {
                        const input = builtinEntries.get(value.input_instance_id)?.instance;
                        const field = tableResource.fields.find(entry => entry.code === value.field_code);
                        input?.setValue?.(row?.[value.field_code] ?? (field.type === 'toggle' ? false : ''));
                    }
                }
                selectRecord(tableResource, row || null);
                syncWriteButtons();
            } } } : {}),
            ...(buttonLabel || writeButtonFlow ? { customLabel: buttonLabel ||
                `${{ create: '新增', update: '修改', delete: '刪除' }[writeButtonFlow.operation]}${writeButtonResource.label}` } : {})
        } : null;
        let candidateEntry = null;
        const instance = item.kind === 'candidate' ? registry.create(item.ref, { nodeOptions })
            : ComponentFactory.create(item.ref, options);
        instance.mount(host); instances.push(instance);
        if (item.kind === 'builtin' && item.instance_id) builtinEntries.set(item.instance_id, { instance, item });
        if (buttonLabel) {
            const entry = { instance, resource: buttonResource, operation: item.binding.operation };
            buttons.push(entry);
            (instance.element?.querySelector('button') || instance.element)?.addEventListener('click', async () => {
                try {
                    if (entry.operation === 'new') {
                        selectRecord(entry.resource, null);
                        status(`可新增${entry.resource.label}；請在編輯欄位組填寫後儲存。`);
                    } else if (entry.operation === 'search') {
                        const matches = searches.filter(value => value.resource === entry.resource);
                        if (!matches.length) return status(`請先加入${entry.resource.label}查詢列。`);
                        for (const match of matches) await refreshSearch(match);
                    } else {
                        const match = editors.find(value => value.resource === entry.resource);
                        if (!match) return status(`請先加入${entry.resource.label}編輯欄位組。`);
                        const target = match.instance.getComponent('save')?.element;
                        target?.click();
                    }
                } catch (error) { status(error.message); }
            });
        }
        if (item.kind !== 'candidate') continue;
        const binding = sourceBinding;
        const resource = sourceResource;
        if (!binding || !resource) continue;
        const definition = candidates.find(candidate => candidate.registry_name === item.ref);
        const definitionNodes = new Map();
        const visitNode = node => { if (node.type === 'group') node.children.forEach(visitNode);
            else definitionNodes.set(node.id, node.component); };
        visitNode(definition.root);
        const entry = { instance, binding, resource, item, definitionNodes,
            fields: fieldBindings(item, binding), currentRows: [] };
        candidateEntry = entry;
        if (item.instance_id) entriesById.set(item.instance_id, entry);
        if (binding.kind === 'search') {
            entry.results = element('div', undefined, 'plan-results'); host.append(entry.results); searches.push(entry);
            if (item.wiring) {
                const table = instance.getComponent('results');
                if (!table?.setData) throw new Error('資料表結果節點不可用。');
            } else {
                const legacyTable = host.querySelector('[data-custom-component-node="results"]');
                if (legacyTable) legacyTable.hidden = true;
                instance.getComponent('submit')?.element?.addEventListener('click', () => {
                void refreshSearch(entry).catch(error => status(error.message));
                });
            }
        } else if (binding.kind === 'editor') {
            const toolbar = element('div', undefined, 'plan-editor-toolbar');
            toolbar.append(element('label', '選擇現有資料或新增：'));
            entry.selector = element('select');
            entry.selector.setAttribute('aria-label', `${resource.label}資料選擇`);
            entry.selector.addEventListener('change', async () => {
                try { selectRecord(resource, entry.selector.value
                    ? await store.get(`${resource.api}/${entry.selector.value}`) : null); }
                catch (error) { status(error.message); }
            });
            toolbar.append(entry.selector); host.append(toolbar); editors.push(entry);
            if (!item.wiring) instance.getComponent('save')?.element?.addEventListener('click', () => {
                void saveEditor(entry).catch(error => status(`儲存失敗：${error.message}`));
            });
        } else if (binding.kind === 'summary') summaries.push(entry);
    }
    for (const entry of [...searches, ...summaries, ...editors]) if (entry.item.wiring) attachWiring(entry);
    for (const id of new Set((plan.flows || []).flatMap(flow => (flow.filters || []).map(filter =>
        filter.input_instance_id)))) {
        const entry = builtinEntries.get(id);
        if (entry?.item.ref === 'ToggleSwitch') bindComponentEvent(entry.instance, 'ToggleSwitch',
            'change', () => touchedInputs.add(id));
    }
    const runFlow = async flow => {
        const resource = resources.get(flow.entity_code);
        if (['create', 'update', 'delete'].includes(flow.operation)) {
            if (site.prototype_backend !== 'sqlite') throw new Error('獨立元件寫入僅供展示資料雛型使用。');
            if (!store.role.actions.includes(flow.operation)) throw new Error('目前角色沒有這項資料操作權限。');
            const id = flow.operation === 'create' ? null : selectedByTable.get(flow.source_table_instance_id);
            if (flow.operation !== 'create' && !id) throw new Error('請先在指定資料表選取一列。');
            if (writeBusy.has(flow)) return;
            if (flow.operation === 'delete' && !window.confirm(`確定刪除 ${resource.label} #${id}？`)) return;
            writeBusy.add(flow); syncWriteButtons();
            try {
                const body = Object.fromEntries((flow.values || []).map(({ input_instance_id, field_code }) => {
                    const field = resource.fields.find(value => value.code === field_code);
                    const input = builtinEntries.get(input_instance_id)?.instance;
                    const raw = typeof input?.getValue === 'function' ? input.getValue() : input?.isChecked?.();
                    const value = field.type === 'number' ? (raw === '' || raw === null ? null : Number(raw))
                        : field.type === 'toggle' ? Boolean(raw) : String(raw ?? '');
                    if (field.type === 'number' && value !== null && !Number.isFinite(value))
                        throw new Error(`${field.label}的數值無效。`);
                    return [field_code, value];
                }));
                const row = flow.operation === 'create' ? await store.post(resource.api, body)
                    : flow.operation === 'update' ? await store.put(`${resource.api}/${id}`, body)
                        : await store.delete(`${resource.api}/${id}`);
                if (flow.operation !== 'create') selectedByTable.delete(flow.source_table_instance_id);
                selectRecord(resource, flow.operation === 'delete' ? null : row);
                const resultMessage = flow.operation === 'delete' ? `資料 #${id} 已刪除。`
                    : `${resource.label} #${row.id} 已${flow.operation === 'create' ? '新增' : '修改'}。`;
                try {
                    await refreshChoices(resource);
                    for (const search of searches.filter(item => item.resource === resource)) await refreshSearch(search);
                    await refreshPageFlows(resource);
                    status(resultMessage);
                } catch (error) { status(`${resultMessage}清單更新失敗：${error.message}`); }
            } finally { writeBusy.delete(flow); syncWriteButtons(); }
            return;
        }
        const revision = (flowRevisions.get(flow.target_instance_id) || 0) + 1;
        flowRevisions.set(flow.target_instance_id, revision);
        const filters = flow.filters.flatMap(filter => {
            const input = builtinEntries.get(filter.input_instance_id)?.instance;
            const field = resource.fields.find(value => value.code === filter.field_code);
            if (field.type === 'toggle' && !touchedInputs.has(filter.input_instance_id)) return [];
            const raw = typeof input?.getValue === 'function' ? input.getValue() : input?.isChecked?.();
            if (raw === null || raw === undefined || raw === '') return [];
            const value = field.type === 'number' ? Number(raw)
                : field.type === 'toggle' ? Boolean(raw) : String(raw).trim();
            if (value === '' || (field.type === 'number' && !Number.isFinite(value)))
                throw new Error(`${field.label}的查詢值無效。`);
            return [{ field: filter.field_code, op: filter.operator, value }];
        });
        const target = builtinEntries.get(flow.target_instance_id)?.instance;
        if (flow.operation === 'query') {
            const result = await store.query(resource.api, { filters, page: 1, page_size: 100 });
            if (flowRevisions.get(flow.target_instance_id) !== revision) return;
            selectedByTable.delete(flow.target_instance_id);
            syncWriteButtons();
            flowRows.set(flow.target_instance_id, result.items);
            target.setData(result.items, result.total);
            status(`已查詢 ${result.total} 筆。`);
        } else if (flow.operation === 'summary') {
            const metric = flow.metric.op === 'count' ? { op: 'count' }
                : { op: 'sum', field: flow.metric.field_code };
            const result = await store.summary(resource.api, { filters, metric });
            if (flowRevisions.get(flow.target_instance_id) !== revision) return;
            const label = flow.metric.op === 'count' ? '筆數'
                : resource.fields.find(field => field.code === flow.metric.field_code).label + '合計';
            target.element.textContent = `${label}：${format(result.items[0]?.value ?? 0)}`;
            status('摘要已更新。');
        } else {
            const result = await store.lookup(resource.api, { field: flow.field_code, filters, limit: 100 });
            if (flowRevisions.get(flow.target_instance_id) !== revision) return;
            target.setItems(result.items.map(item => ({ value: item.value, label: format(item.value) })));
            status(`已載入 ${result.items.length} 個選項。`);
        }
    };
    const refreshPageFlows = async resource => {
        for (const flow of plan.flows || []) if (flow.entity_code === resource.entity_code &&
            ['query', 'summary', 'lookup'].includes(flow.operation)) await runFlow(flow);
    };
    const loadFlows = [];
    for (const flow of plan.flows || []) {
        if (flow.trigger_instance_id === '@page') { loadFlows.push(flow); continue; }
        const trigger = builtinEntries.get(flow.trigger_instance_id);
        bindComponentEvent(trigger.instance, trigger.item.ref, flow.event, () => {
            void runFlow(flow).catch(error => status(`操作失敗：${error.message}`));
        });
    }
    syncWriteButtons();
    for (const resource of config.resources) {
        try {
            await refreshChoices(resource);
            for (const search of searches.filter(item => item.resource === resource && !item.item.wiring)) await refreshSearch(search);
            selectRecord(resource, (await store.get(resource.api)).items[0] || null);
        } catch (error) { status(error.message); }
    }
    for (const { entry, action } of pageActions) {
        try { await runAction(entry, action); }
        catch (error) { status(`載入失敗：${error.message}`); }
    }
    for (const flow of loadFlows) {
        try { await runFlow(flow); }
        catch (error) { status(`載入失敗：${error.message}`); }
    }
    window.addEventListener('pagehide', () => instances.forEach(instance => instance.destroy()), { once: true });
}
start().catch(error => { canvas.textContent = `無法顯示頁面規劃：${error.message}`; });
