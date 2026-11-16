import { addEntity, addField, addRole, starterDraft, validateDraft, TYPE_LABELS,
    VERTICAL_LABELS, HORIZONTAL_LABELS } from './model.mjs';
import { createPlanner } from './planner-ui.js';
import { WORKSPACE_KEY, projectSnapshot, recoverableSnapshot, validateProject } from './project-file.mjs';

let draft = starterDraft('course');
const $ = id => document.getElementById(id);
const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
};
const presets = [
    ['course', '課務資料維護', '課程欄位參考 CourseAffairs；教師、教室為展示擴充'],
    ['request', '申請案件', '案件查詢、受理與維護'],
    ['blank', '空白開始', '自行設定業務區塊與欄位']
];
const ACTIONS = { create: '新增', read: '查詢', update: '修改', delete: '刪除' };
let libraryItems = [];
const planner = createPlanner(() => draft, message => showStatus(message), () => {
    persist(); renderLibraryChooser();
}, id => { void openLibrary('component', id); });

async function libraryRequest(url, options) {
    const response = await fetch(url, options);
    const result = await response.json();
    if (!response.ok) throw new Error(result.error || '本地庫操作失敗。');
    return result;
}

function renderLibraryChooser() {
    const select = $('library-component-source');
    const previous = select.value;
    select.replaceChildren();
    const options = planner.listSavableComponents();
    for (const item of options) {
        const option = el('option', '', item.label);
        option.value = item.instance_id;
        select.append(option);
    }
    if (options.some(item => item.instance_id === previous)) select.value = previous;
    $('library-save-component').disabled = !options.length || !draft.confirmed;
    $('library-save-page').disabled = !planner.getPlan().items.length || !draft.confirmed;
}

function renderLibraryPages() {
    for (const kind of ['component', 'page']) {
        const host = $(kind === 'component' ? 'library-components' : 'library-pages');
        host.replaceChildren();
        const entries = libraryItems.filter(item => item.kind === kind);
        if (!entries.length) {
            host.append(el('p', 'helper', kind === 'page' ? '尚無保存的頁面。' : '尚無保存的元件。'));
            continue;
        }
        for (const item of entries) {
            const row = el('div', 'library-entry');
            row.append(el('span', '', item.title));
            const actions = el('div', 'library-entry-actions');
            const apply = el('button', 'secondary', kind === 'page' ? '套用' : '加入');
            apply.type = 'button';
            apply.addEventListener('click', () => { void openLibrary(kind, item.id); });
            const remove = el('button', 'secondary', '移除');
            remove.type = 'button';
            remove.addEventListener('click', () => { void removeLibrary(item); });
            actions.append(apply, remove); row.append(actions); host.append(row);
        }
    }
}

async function refreshLibrary() {
    try {
        libraryItems = (await libraryRequest('/api/library')).items;
        planner.setLibraryComponents(libraryItems);
        renderLibraryPages();
        renderLibraryChooser();
    } catch (error) { showStatus(`本地庫讀取失敗：${error.message}`); }
}

async function saveLibrary(kind) {
    try {
        if (!draft.confirmed) throw new Error('請先核對訪談內容。');
        const title = $('library-title').value.trim() || (kind === 'page' ? draft.title
            : planner.listSavableComponents().find(item =>
                item.instance_id === $('library-component-source').value)?.label);
        const plan = planner.getPlan();
        const body = { title, draft, plan,
            ...(kind === 'component' ? { instance_id: $('library-component-source').value } : {}) };
        await libraryRequest(`/api/library/${kind}`, { method: 'POST',
            headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        $('library-title').value = '';
        await refreshLibrary();
        showStatus(kind === 'page' ? '頁面已存入本地庫。' : '元件已存入本地庫，可從左側清單再次選用。');
    } catch (error) { showStatus(`無法存入本地庫：${error.message}`); }
}

async function removeLibrary(item) {
    if (!window.confirm(`確定從本地庫移除「${item.title}」？`)) return;
    try {
        await libraryRequest(`/api/library/${item.kind}/${item.id}`, { method: 'DELETE' });
        $('library-mapping').hidden = true;
        await refreshLibrary();
        showStatus('已從本地庫移除。');
    } catch (error) { showStatus(`無法移除：${error.message}`); }
}

async function exportLibrary() {
    try {
        const response = await fetch('/api/library/export');
        if (!response.ok) {
            const result = await response.json();
            throw new Error(result.error || '本地庫匯出失敗。');
        }
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const link = el('a');
        link.href = url;
        link.download = 'b4a-local-library.json';
        document.body.append(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        showStatus('本地元件與頁面庫已匯出為 JSON 檔。');
    } catch (error) { showStatus(`無法匯出本地庫：${error.message}`); }
}

async function importLibrary(file) {
    try {
        if (file.size > 8 * 1024 * 1024) throw new Error('匯入檔不得超過 8 MB。');
        const bundle = JSON.parse(await file.text());
        const result = await libraryRequest('/api/library/import', { method: 'POST',
            headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(bundle) });
        await refreshLibrary();
        showStatus(`已匯入 ${result.imported} 個本地庫項目。`);
    } catch (error) { showStatus(`無法匯入本地庫：${error.message}`); }
    finally { $('library-import-file').value = ''; }
}

function compatibleEntity(source, target, entry) {
    if (entry.source.candidate_refs.some(ref => ref.entity_code === source.code))
        return source.fields.length === target.fields.length &&
            source.fields.every((field, index) => field.type === target.fields[index].type);
    const counts = new Map();
    for (const field of target.fields) counts.set(field.type, (counts.get(field.type) || 0) + 1);
    for (const field of source.fields) {
        const remaining = counts.get(field.type) || 0;
        if (!remaining) return false;
        counts.set(field.type, remaining - 1);
    }
    return true;
}

function showLibraryMapping(entry) {
    const host = $('library-mapping');
    const mapping = { entities: {}, fields: {} };
    const selected = new Set();
    for (const source of entry.source.entities) {
        const choices = draft.entities.filter(entity => compatibleEntity(source, entity, entry));
        const chosen = choices.find(entity => entity.label === source.label && !selected.has(entity.code)) ||
            choices.find(entity => !selected.has(entity.code));
        if (chosen) { mapping.entities[source.code] = chosen.code; selected.add(chosen.code); }
    }
    const render = () => {
        host.replaceChildren();
        host.append(el('h4', '', `${entry.kind === 'page' ? '套用頁面' : '加入元件'}：${entry.title}`));
        if (!entry.source.entities.length) host.append(el('p', 'helper', '此項目沒有資料欄位，可直接使用。'));
        for (const source of entry.source.entities) {
            const choices = draft.entities.filter(entity => compatibleEntity(source, entity, entry));
            const title = el('h4', '', `來源資料區塊：${source.label}`);
            host.append(title);
            if (!choices.length) {
                host.append(el('p', 'helper', '目前訪談沒有欄位結構相容的資料區塊。'));
                continue;
            }
            if (!choices.some(entity => entity.code === mapping.entities[source.code]))
                mapping.entities[source.code] = choices[0].code;
            const entityLabel = el('label', '', '對應目前資料區塊');
            const entitySelect = el('select');
            for (const entity of choices) {
                const option = el('option', '', entity.label);
                option.value = entity.code; entitySelect.append(option);
            }
            entitySelect.value = mapping.entities[source.code];
            entitySelect.addEventListener('change', () => {
                mapping.entities[source.code] = entitySelect.value;
                for (const field of source.fields) delete mapping.fields[field.code];
                render();
            });
            entityLabel.append(entitySelect); host.append(entityLabel);
            const target = choices.find(entity => entity.code === mapping.entities[source.code]);
            const grid = el('div', 'library-mapping-grid');
            for (const [index, field] of source.fields.entries()) {
                const fieldChoices = target.fields.filter(item => item.type === field.type);
                const used = new Set(source.fields.filter(item => item.code !== field.code)
                    .map(item => mapping.fields[item.code]));
                if (!fieldChoices.some(item => item.code === mapping.fields[field.code])) {
                    const selectedField = fieldChoices.find(item => item.label === field.label && !used.has(item.code)) ||
                        (target.fields[index]?.type === field.type && !used.has(target.fields[index].code)
                            ? target.fields[index] : null) ||
                        fieldChoices.find(item => !used.has(item.code));
                    mapping.fields[field.code] = selectedField?.code || '';
                }
                const label = el('label', '', `${field.label}（${TYPE_LABELS[field.type]}）`);
                const select = el('select');
                for (const candidate of fieldChoices) {
                    const option = el('option', '', candidate.label);
                    option.value = candidate.code; select.append(option);
                }
                select.value = mapping.fields[field.code];
                select.addEventListener('change', () => { mapping.fields[field.code] = select.value; });
                label.append(select); grid.append(label);
            }
            host.append(grid);
        }
        const actions = el('div', 'actions');
        const apply = el('button', 'primary', entry.kind === 'page' ? '套用此頁面' : '加入頁面畫布');
        apply.type = 'button';
        apply.addEventListener('click', async () => {
            try {
                const result = await libraryRequest('/api/library/apply', { method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ kind: entry.kind, id: entry.id, draft, mapping }) });
                if (entry.kind === 'page') {
                    if (planner.getPlan().items.length &&
                        !window.confirm('套用頁面會取代目前畫布。確定繼續？')) return;
                    planner.loadPlan(result.plan);
                } else planner.addLibraryItem(result.plan.items[0]);
                host.hidden = true;
                showStatus(entry.kind === 'page' ? '已套用頁面模板。' : '元件已加入畫布。');
            } catch (error) { showStatus(`無法套用本地庫項目：${error.message}`); }
        });
        const cancel = el('button', 'secondary', '取消');
        cancel.type = 'button'; cancel.addEventListener('click', () => { host.hidden = true; });
        actions.append(apply, cancel); host.append(actions);
    };
    host.hidden = false;
    render();
    host.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

async function openLibrary(kind, id) {
    try {
        if (!draft.confirmed) throw new Error('請先核對目前的訪談內容。');
        showLibraryMapping(await libraryRequest(`/api/library/${kind}/${id}`));
    } catch (error) { showStatus(`無法開啟本地庫項目：${error.message}`); }
}

$('library-save-component').addEventListener('click', () => { void saveLibrary('component'); });
$('library-save-page').addEventListener('click', () => { void saveLibrary('page'); });
$('library-export').addEventListener('click', () => { void exportLibrary(); });
$('library-import-file').addEventListener('change', event => {
    const file = event.currentTarget.files?.[0];
    if (file) void importLibrary(file);
});

function persist() {
    let snapshot;
    try { snapshot = projectSnapshot(draft, planner.getPlan()); }
    catch {
        try { snapshot = recoverableSnapshot(draft, planner.getPlan()); }
        catch { return; }
    }
    try { localStorage.setItem(WORKSPACE_KEY, JSON.stringify(snapshot)); }
    catch { showStatus('本機儲存空間無法寫入；請下載工作檔保存進度。'); }
}

function changed() {
    draft.confirmed = false;
    $('confirmed').checked = false;
    $('result').hidden = true;
    planner.invalidate();
    updateReadiness();
    persist();
}

function makeInput(value, label, onChange, maxLength = 40) {
    const input = el('input');
    input.type = 'text';
    input.value = value;
    input.maxLength = maxLength;
    input.setAttribute('aria-label', label);
    input.addEventListener('input', () => { onChange(input.value); changed(); });
    return input;
}

function makeSelect(value, choices, label, onChange) {
    const select = el('select');
    select.setAttribute('aria-label', label);
    for (const [key, title] of Object.entries(choices)) {
        const option = el('option', '', title);
        option.value = key;
        select.append(option);
    }
    select.value = value;
    select.addEventListener('change', () => { onChange(select.value); changed(); });
    return select;
}

function makeCheck(value, label, onChange) {
    const input = el('input');
    input.type = 'checkbox';
    input.checked = value;
    input.setAttribute('aria-label', label);
    input.addEventListener('change', () => { onChange(input.checked); changed(); });
    return input;
}

function button(label, action, className = 'secondary') {
    const node = el('button', className, label);
    node.type = 'button';
    node.addEventListener('click', action);
    return node;
}

function renderPresets() {
    const container = $('presets');
    container.replaceChildren();
    for (const [key, title, description] of presets) {
        const card = button(title, () => {
            draft = starterDraft(key);
            renderAll();
            persist();
            $('status').textContent = '';
        }, 'preset');
        card.setAttribute('aria-pressed', String(draft.preset === key));
        card.append(el('small', '', description));
        container.append(card);
    }
}

function sampleValue(field, value) {
    if (field.type === 'toggle') return value === true;
    if (field.type === 'number') return typeof value === 'number' ? value : 0;
    return value === null ? '' : String(value);
}

function sampleEditor(entity) {
    const details = el('details', 'sample-editor');
    const summary = el('summary', '', `展示資料（${entity.examples.length} 筆）`);
    details.append(summary);
    const info = el('p', 'helper', '展示資料只存在於產生的雛型內；調整欄位後會清除不相符的舊資料。');
    details.append(info);
    entity.examples.forEach((row, rowIndex) => {
        const card = el('div', 'sample-card');
        const heading = el('div', 'sample-heading', `第 ${rowIndex + 1} 筆`);
        heading.append(button('移除', () => { entity.examples.splice(rowIndex, 1); changed(); renderEntities(); }, 'icon-button'));
        card.append(heading);
        const grid = el('div', 'sample-grid');
        entity.fields.forEach((field, fieldIndex) => {
            const label = el('label', '', field.label);
            if (field.type === 'toggle') {
                label.append(makeCheck(row[fieldIndex] === true, field.label, value => { row[fieldIndex] = value; }));
            } else if (field.type === 'select') {
                label.append(makeSelect(String(row[fieldIndex] ?? ''),
                    { '': '請選擇', new: '新建', active: '進行中', done: '完成' }, field.label,
                    value => { row[fieldIndex] = value; }));
            } else {
                const input = makeInput(String(row[fieldIndex] ?? ''), field.label, value => {
                    row[fieldIndex] = field.type === 'number' ? (value === '' ? null : Number(value)) : value;
                }, 100);
                if (field.type === 'number') input.type = 'number';
                if (field.type === 'date') input.type = 'date';
                label.append(input);
            }
            grid.append(label);
        });
        card.append(grid);
        details.append(card);
    });
    details.append(button('＋ 新增一筆展示資料', () => {
        if (entity.examples.length >= 5) return showStatus('每個資料區塊最多五筆展示資料。');
        entity.examples.push(entity.fields.map(field => sampleValue(field, null)));
        changed(); renderEntities();
    }));
    return details;
}

function renderEntities() {
    const container = $('entities');
    container.replaceChildren();
    draft.entities.forEach((entity, entityIndex) => {
        const block = el('article', 'block');
        const head = el('div', 'block-head');
        const label = el('label', '', `資料區塊 ${entityIndex + 1}`);
        label.append(makeInput(entity.label, '資料區塊名稱', value => { entity.label = value; }, 40));
        head.append(label);
        if (draft.entities.length > 1) head.append(button('移除區塊', () => {
            draft.entities.splice(entityIndex, 1); changed(); renderEntities();
        }));
        block.append(head);
        const body = el('div', 'block-body');
        const header = el('div', 'row-head');
        ['欄位名稱', '資料型態', '必填', ''].forEach(title => header.append(el('span', '', title)));
        body.append(header);
        entity.fields.forEach((field, fieldIndex) => {
            const row = el('div', 'field-row');
            row.append(makeInput(field.label, '欄位名稱', value => { field.label = value; }));
            row.append(makeSelect(field.type, TYPE_LABELS, '資料型態', value => {
                field.type = value; entity.examples = [];
                renderEntities();
            }));
            const required = el('div', 'required');
            required.append(makeCheck(field.required, '必填', value => { field.required = value; }));
            row.append(required);
            const remove = button('×', () => {
                if (entity.fields.length === 1) return showStatus('資料區塊至少需要一個欄位。');
                entity.fields.splice(fieldIndex, 1);
                entity.examples = [];
                changed(); renderEntities();
            }, 'icon-button');
            remove.setAttribute('aria-label', `移除欄位 ${field.label}`);
            row.append(remove);
            body.append(row);
        });
        body.append(button('＋ 新增欄位', () => {
            try { addField(entity); entity.examples = []; changed(); renderEntities(); }
            catch (error) { showStatus(error.message); }
        }));
        body.append(sampleEditor(entity));
        block.append(body);
        container.append(block);
    });
    $('add-entity').disabled = draft.entities.length >= 4;
}

function renderRoles() {
    const container = $('roles');
    container.replaceChildren();
    draft.roles.forEach((role, index) => {
        const block = el('article', 'block');
        const head = el('div', 'block-head');
        head.append(el('strong', '', `角色 ${index + 1}`));
        if (draft.roles.length > 1) head.append(button('移除角色', () => {
            draft.roles.splice(index, 1); changed(); renderRoles();
        }));
        const body = el('div', 'block-body');
        const grid = el('div', 'role-grid');
        const roleName = el('label', '', '角色名稱');
        roleName.append(makeInput(role.label, '角色名稱', value => { role.label = value; }));
        grid.append(roleName);
        for (const [key, title, values] of [['vertical', '垂直範圍', VERTICAL_LABELS],
            ['horizontal', '水平範圍', HORIZONTAL_LABELS]]) {
            const label = el('label', '', title);
            label.append(makeSelect(role[key], values, title, value => { role[key] = value; }));
            grid.append(label);
        }
        body.append(grid);
        const actions = el('div', 'actions-grid');
        for (const [action, title] of Object.entries(ACTIONS)) {
            const label = el('label', '', title);
            label.prepend(makeCheck(role.actions.includes(action), title, checked => {
                role.actions = checked ? [...role.actions, action] : role.actions.filter(item => item !== action);
            }));
            actions.append(label);
        }
        body.append(actions);
        block.append(head, body);
        container.append(block);
    });
    $('add-role').disabled = draft.roles.length >= 6;
}

function updateReadiness() {
    const count = draft.entities.reduce((sum, entity) => sum + entity.fields.length, 0);
    const pages = draft.entities.length * 3;
    let message = `${draft.entities.length} 個資料區塊・${count} 個欄位・${draft.roles.length} 個角色・預計 ${pages} 個畫面`;
    try { validateDraft(draft, { requireConfirmation: false }); }
    catch (error) { message += `\n請修正：${error.message}`; }
    $('readiness').textContent = message;
    $('generate').disabled = !draft.confirmed;
}

function showStatus(message) { $('status').textContent = message; }

function renderAll(plan = null) {
    planner.reset();
    renderPresets();
    $('project-title').value = draft.title;
    $('project-goal').value = draft.goal;
    $('confirmed').checked = draft.confirmed;
    renderEntities();
    renderRoles();
    updateReadiness();
    $('result').hidden = true;
    if (plan) planner.loadPlan(plan);
}

$('project-title').addEventListener('input', event => { draft.title = event.target.value; changed(); });
$('project-goal').addEventListener('input', event => { draft.goal = event.target.value; changed(); });
$('add-entity').addEventListener('click', () => {
    try { addEntity(draft); changed(); renderEntities(); }
    catch (error) { showStatus(error.message); }
});
$('add-role').addEventListener('click', () => {
    try { addRole(draft); changed(); renderRoles(); }
    catch (error) { showStatus(error.message); }
});
$('confirmed').addEventListener('change', event => {
    draft.confirmed = event.target.checked;
    planner.refresh();
    updateReadiness();
    persist();
});
$('save-work').addEventListener('click', () => {
    try {
        const project = projectSnapshot(draft, planner.getPlan());
        const url = URL.createObjectURL(new Blob([JSON.stringify(project, null, 2)], { type: 'application/json' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = 'b4a-presale-workspace.json';
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        showStatus('工作進度已下載。');
    } catch (error) { showStatus(`請先修正工作內容：${error.message}`); }
});
$('open-work').addEventListener('change', async event => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    try {
        if (file.size > 262144) throw new TypeError('工作檔不可超過 256 KB。');
        const project = validateProject(JSON.parse(await file.text()));
        draft = structuredClone(project.draft);
        renderAll(project.plan);
        persist();
        showStatus('工作進度已開啟，請核對內容後繼續。');
    } catch (error) { showStatus(`無法開啟工作檔：${error.message}`); }
});
$('generate').addEventListener('click', async () => {
    try { validateDraft(draft); }
    catch (error) { showStatus(error.message); return; }
    const buttonNode = $('generate');
    buttonNode.disabled = true;
    showStatus('正在產生畫面與交接文件…');
    try {
        const response = await fetch('/api/build', { method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ draft, plan: planner.getPlan() }) });
        const result = await response.json();
        if (!response.ok) throw new Error(result.error || '無法產生雛型。');
        $('result-count').textContent = `${result.module_count} 個資料區塊，${result.page_count} 個可操作畫面。`;
        $('open-preview').href = result.preview_url;
        $('open-planned').href = result.planned_page_url;
        $('download').href = result.download_url;
        $('preview').src = result.preview_url;
        $('review').textContent = result.review;
        $('result').hidden = false;
        showStatus('已完成。');
        $('result').scrollIntoView({ behavior: 'smooth' });
    } catch (error) { showStatus(error.message); }
    finally { updateReadiness(); }
});

try {
    const saved = localStorage.getItem(WORKSPACE_KEY);
    if (saved) {
        const project = validateProject(JSON.parse(saved));
        draft = structuredClone(project.draft);
        renderAll(project.plan);
    } else renderAll();
} catch {
    renderAll();
    showStatus('本機工作進度格式無效；已開啟預設範例，可使用「開啟工作檔」復原。');
}
void refreshLibrary();
