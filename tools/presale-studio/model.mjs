// The browser keeps human labels separately from the code-only agent projection.
// Generated pages are assembled here from confirmed human input; no agent text becomes code.
const TYPES = new Set(['text', 'email', 'tel', 'number', 'date', 'textarea', 'toggle', 'select']);
const ACTIONS = new Set(['create', 'read', 'update', 'delete']);
const VERTICAL = new Set(['all', 'self', 'descendants', 'self_descendants']);
const HORIZONTAL = new Set(['all', 'same_unit', 'same_function', 'unit_or_function', 'unit_and_function']);
const CODE = /^[EFR]_[A-F0-9]{12}$/;
const MARKUP = /[<>\u0000-\u001f\u007f]/;
const PRESETS = Object.freeze({
    course: {
        title: '課務基礎資料雛型',
        goal: '展示課程、教師與教室基本資料的查詢、明細及維護。',
        entities: [
            { label: '課程', fields: [
                ['課程代碼', 'text', true], ['課程名稱', 'text', true], ['開課單位', 'text', true],
                ['授課教師', 'text', true], ['學分', 'number', true], ['名額', 'number', true],
                ['開課日期', 'date', false], ['備註', 'textarea', false]
            ], examples: [
                ['CS101', '程式設計導論', '資訊系', '陳老師', 3, 45, '2026-09-01', '大一必修'],
                ['MA201', '線性代數', '數學系', '林老師', 3, 55, '2026-09-02', '跨系選修'],
                ['EN305', '學術英文', '語文中心', '王老師', 2, 35, '2026-09-03', '分組授課']
            ] },
            { label: '教師', fields: [
                ['姓名', 'text', true], ['所屬單位', 'text', true], ['電子郵件', 'email', false],
                ['專長', 'text', false], ['在職', 'toggle', false]
            ], examples: [
                ['陳老師', '資訊系', 'chen@example.edu', '軟體工程', true],
                ['林老師', '數學系', 'lin@example.edu', '應用數學', true]
            ] },
            { label: '教室', fields: [
                ['教室名稱', 'text', true], ['校區', 'text', true], ['容量', 'number', true],
                ['設備說明', 'textarea', false], ['開放使用', 'toggle', false]
            ], examples: [
                ['A101', '主校區', 60, '投影與錄影設備', true],
                ['B203', '第二校區', 40, '電腦教室', true]
            ] }
        ],
        roles: [
            ['管理者', ['create', 'read', 'update', 'delete'], 'all', 'all'],
            ['課務人員', ['create', 'read', 'update'], 'self_descendants', 'same_unit'],
            ['查詢者', ['read'], 'self', 'unit_or_function']
        ]
    },
    request: {
        title: '申請案件雛型',
        goal: '示範案件受理、查詢、明細與資料維護。',
        entities: [{ label: '申請案件', fields: [
            ['案件編號', 'text', true], ['申請人', 'text', true], ['申請單位', 'text', true],
            ['申請日期', 'date', true], ['金額', 'number', false], ['說明', 'textarea', false]
        ], examples: [
            ['REQ-001', '陳小姐', '行政處', '2026-10-01', 12000, '設備採購'],
            ['REQ-002', '林先生', '資訊處', '2026-10-03', 5000, '軟體授權']
        ] }],
        roles: [
            ['管理者', ['create', 'read', 'update', 'delete'], 'all', 'all'],
            ['承辦人', ['create', 'read', 'update'], 'self_descendants', 'same_unit']
        ]
    },
    blank: {
        title: '新系統雛型',
        goal: '',
        entities: [{ label: '資料項目', fields: [['名稱', 'text', true], ['說明', 'textarea', false]], examples: [] }],
        roles: [['管理者', ['create', 'read', 'update', 'delete'], 'all', 'all']]
    }
});

function randomCode(prefix) {
    const bytes = new Uint8Array(6);
    globalThis.crypto.getRandomValues(bytes);
    return `${prefix}_${[...bytes].map(byte => byte.toString(16).padStart(2, '0')).join('').toUpperCase()}`;
}

function plainText(value, path, maximum = 80, allowEmpty = false) {
    if (typeof value !== 'string') throw new TypeError(`${path} 必須是文字。`);
    const text = value.trim();
    if ((!allowEmpty && !text) || text.length > maximum || MARKUP.test(text))
        throw new TypeError(`${path} 長度或字元不符合規則。`);
    return text;
}

function exactKeys(value, names, path) {
    if (!value || typeof value !== 'object' || Array.isArray(value))
        throw new TypeError(`${path} 必須是物件。`);
    const keys = Object.keys(value).sort();
    if (keys.join('|') !== [...names].sort().join('|'))
        throw new TypeError(`${path} 含有未知或缺少的屬性。`);
}

function assertCode(value, prefix, path) {
    if (typeof value !== 'string' || !CODE.test(value) || !value.startsWith(`${prefix}_`))
        throw new TypeError(`${path} 必須是系統產生的代碼。`);
}

export function starterDraft(preset = 'course') {
    if (!Object.hasOwn(PRESETS, preset)) throw new TypeError('未知的起始範本。');
    const source = PRESETS[preset];
    return {
        schema_version: 1,
        preset,
        title: source.title,
        goal: source.goal,
        confirmed: false,
        entities: source.entities.map(entity => ({
            code: randomCode('E'),
            label: entity.label,
            fields: entity.fields.map(([label, type, required]) => ({ code: randomCode('F'), label, type, required })),
            examples: entity.examples.map(row => [...row])
        })),
        roles: source.roles.map(([label, actions, vertical, horizontal]) => ({
            code: randomCode('R'), label, actions, vertical, horizontal
        }))
    };
}

export function addEntity(draft) {
    if (draft.entities.length >= 4) throw new TypeError('最多四個資料區塊。');
    draft.entities.push({ code: randomCode('E'), label: '新資料項目', fields: [
        { code: randomCode('F'), label: '名稱', type: 'text', required: true }
    ], examples: [] });
    draft.confirmed = false;
}

export function addField(entity) {
    if (entity.fields.length >= 12) throw new TypeError('每個區塊最多十二個欄位。');
    entity.fields.push({ code: randomCode('F'), label: '新欄位', type: 'text', required: false });
}

export function addRole(draft) {
    if (draft.roles.length >= 6) throw new TypeError('最多六個展示角色。');
    draft.roles.push({ code: randomCode('R'), label: '新角色', actions: ['read'], vertical: 'self', horizontal: 'same_unit' });
    draft.confirmed = false;
}

export function validateDraft(draft, { requireConfirmation = true } = {}) {
    exactKeys(draft, ['schema_version', 'preset', 'title', 'goal', 'confirmed', 'entities', 'roles'], 'draft');
    if (draft.schema_version !== 1 || !Object.hasOwn(PRESETS, draft.preset) || typeof draft.confirmed !== 'boolean')
        throw new TypeError('訪談版本、範本或確認狀態無效。');
    plainText(draft.title, '專案名稱', 80);
    plainText(draft.goal, '業務目標', 400, true);
    if (requireConfirmation && !draft.confirmed) throw new TypeError('請先檢查並確認訪談摘要。');
    if (!Array.isArray(draft.entities) || draft.entities.length < 1 || draft.entities.length > 4)
        throw new TypeError('需有一至四個資料區塊。');
    if (!Array.isArray(draft.roles) || draft.roles.length < 1 || draft.roles.length > 6)
        throw new TypeError('需有一至六個展示角色。');
    const codes = new Set();
    const register = (code, prefix, path) => {
        assertCode(code, prefix, path);
        if (codes.has(code)) throw new TypeError('代碼重複。');
        codes.add(code);
    };
    for (const [index, entity] of draft.entities.entries()) {
        exactKeys(entity, ['code', 'label', 'fields', 'examples'], `區塊 ${index + 1}`);
        register(entity.code, 'E', `區塊 ${index + 1}`);
        plainText(entity.label, `區塊 ${index + 1} 名稱`, 40);
        if (!Array.isArray(entity.fields) || entity.fields.length < 1 || entity.fields.length > 12)
            throw new TypeError(`區塊 ${index + 1} 需有一至十二個欄位。`);
        if (!Array.isArray(entity.examples) || entity.examples.length > 5)
            throw new TypeError('示範資料過多。');
        for (const [fieldIndex, field] of entity.fields.entries()) {
            exactKeys(field, ['code', 'label', 'type', 'required'], `欄位 ${fieldIndex + 1}`);
            register(field.code, 'F', `欄位 ${fieldIndex + 1}`);
            plainText(field.label, `欄位 ${fieldIndex + 1} 名稱`, 40);
            if (!TYPES.has(field.type) || typeof field.required !== 'boolean')
                throw new TypeError(`欄位 ${fieldIndex + 1} 的型別或必填狀態無效。`);
        }
        for (const row of entity.examples) {
            if (!Array.isArray(row) || row.length !== entity.fields.length)
                throw new TypeError('示範資料與欄位數不一致；請移除舊資料後再產生。');
            row.forEach((value, fieldIndex) => {
                if (value !== null && !['string', 'number', 'boolean'].includes(typeof value))
                    throw new TypeError('示範資料只允許純 JSON 值。');
                if (typeof value === 'string') plainText(value, `示範資料 ${fieldIndex + 1}`, 100, true);
                const fieldType = entity.fields[fieldIndex].type;
                if (value !== null && ((fieldType === 'number' && (typeof value !== 'number' || !Number.isFinite(value))) ||
                    (fieldType === 'toggle' && typeof value !== 'boolean') ||
                    (!['number', 'toggle'].includes(fieldType) && typeof value !== 'string') ||
                    (fieldType === 'date' && value !== '' && !/^\d{4}-\d{2}-\d{2}$/.test(value)) ||
                    (fieldType === 'select' && value !== '' && !['new', 'active', 'done'].includes(value))))
                    throw new TypeError(`示範資料 ${fieldIndex + 1} 與欄位型別不符。`);
            });
        }
    }
    for (const [index, role] of draft.roles.entries()) {
        exactKeys(role, ['code', 'label', 'actions', 'vertical', 'horizontal'], `角色 ${index + 1}`);
        register(role.code, 'R', `角色 ${index + 1}`);
        plainText(role.label, `角色 ${index + 1} 名稱`, 40);
        if (!Array.isArray(role.actions) || role.actions.length < 1 || role.actions.length > 4 ||
            new Set(role.actions).size !== role.actions.length || role.actions.some(action => !ACTIONS.has(action)) ||
            !VERTICAL.has(role.vertical) || !HORIZONTAL.has(role.horizontal))
            throw new TypeError(`角色 ${index + 1} 的操作或範圍無效。`);
    }
    return draft;
}

export function agentProjection(draft) {
    validateDraft(draft, { requireConfirmation: false });
    return {
        schema_version: 1,
        pattern: 'crud_rbac_v1',
        backend_modules: ['query', 'lookup', 'summary', 'csv_export', 'batch_create'],
        entities: draft.entities.map(entity => ({
            code: entity.code,
            fields: entity.fields.map(field => ({ code: field.code, type: field.type, required: field.required })),
            behaviors: ['create', 'read', 'update', 'delete'],
            views: ['list', 'detail', 'form']
        })),
        roles: draft.roles.map(role => ({
            code: role.code, actions: [...role.actions],
            query_scope: { vertical: role.vertical, horizontal: role.horizontal, join: 'intersection' }
        }))
    };
}

function pageName(entity, suffix) { return `E${entity.code.slice(2)}${suffix}Page`; }
function pageId(entity, suffix) { return `e${entity.code.slice(2).toLowerCase()}-${suffix.toLowerCase()}`; }
function apiBase(entity) { return `/api/presale/e${entity.code.slice(2).toLowerCase()}`; }
function pageFields(entity, kind) {
    const fields = kind === 'list' ? entity.fields.slice(0, 6) : entity.fields;
    return fields.map(field => {
        const result = { name: field.code, type: field.type, label: field.label };
        if (kind === 'form' && field.required) result.required = true;
        if (field.type === 'select') result.options = [
            { value: 'new', label: '新建' }, { value: 'active', label: '進行中' }, { value: 'done', label: '完成' }
        ];
        return result;
    });
}

export function compileDraft(draft) {
    validateDraft(draft);
    const pages = [];
    const seed = {};
    const mapping = [];
    const labels = {};
    for (const entity of draft.entities) {
        labels[entity.code] = entity.label;
        const api = apiBase(entity);
        seed[api] = entity.examples.map((row, index) => Object.fromEntries([
            ['id', index + 1], ...entity.fields.map((field, fieldIndex) => [field.code, row[fieldIndex]])
        ]));
        mapping.push({ entity_code: entity.code, table_code: `T_${entity.code.slice(2)}`,
            id_key: 'id', fields: entity.fields.map(field => ({
            field_code: field.code,
            frontend_key: field.code,
            column_code: `C_${field.code.slice(2)}`,
            type: field.type
        })) });
        for (const field of entity.fields) labels[field.code] = field.label;
        for (const [suffix, type, apiKeys] of [
            ['List', 'list', { list: api, delete: api }],
            ['Detail', 'detail', { get: api }],
            ['Form', 'form', { get: api, create: api, update: api }]
        ]) {
            pages.push({ id: pageId(entity, suffix), definition: {
                name: pageName(entity, suffix), type,
                description: `${entity.label}${type === 'list' ? '清單' : type === 'detail' ? '明細' : '表單'}`,
                fields: pageFields(entity, type), api: apiKeys
            } });
        }
    }
    for (const role of draft.roles) labels[role.code] = role.label;
    const template = {
        kind: 'definition-template', version: '0.1.0',
        meta: { title: draft.title, description: draft.goal || 'Presale 資料維護雛型' },
        definitions: { pages }
    };
    const architecture = agentProjection(draft);
    const review = [
        `# ${draft.title}：訪談確認與雛型範圍`, '',
        `業務目標：${draft.goal || '尚未填寫'}`, '',
        ...(draft.preset === 'course' ? [
            '範例來源：課程欄位參考 CourseAffairs.Web 現有清單與表單；教師與教室為本工作台的展示擴充，尚非該專案已實作畫面。', ''
        ] : []),
        '## 資料區塊', '',
        ...draft.entities.flatMap(entity => [
            `### ${entity.label}`, '',
            ...entity.fields.map(field => `- ${field.label}：${field.type}${field.required ? '，必填' : ''}`), ''
        ]),
        '## 角色與資料範圍（設計模型，雛型不作正式授權）', '',
        ...draft.roles.map(role => `- ${role.label}：${role.actions.join('、')}；垂直 ${role.vertical}；水平 ${role.horizontal}`),
        '', '## 待正式導入時確認', '',
        '- 將展示用 SQLite 表與欄位映射至正式資料來源，核對唯一識別與型別。',
        '- 正式身分、組織階層與職能資料來源；每次查詢的權限範圍。',
        '- 真實 API、例外處理與驗收情境。',
        '', '此交付物使用隔離的 Node.js／SQLite 展示後端，不連接或寫入正式資料庫。測試角色可自由切換，不代表正式身分驗證。', ''
    ].join('\n');
    return { template, architecture, labels, mapping, seed, review };
}

// Explicit, fictitious test fixtures. They do not represent CourseAffairs IAM or
// infer any real organisation hierarchy from user-provided course fields.
export function prototypeConfig(draft, storageKey) {
    validateDraft(draft);
    const samples = [
        { owner: 'PERSON_SELF', unit: 'UNIT_A', function: 'FUNCTION_A' },
        { owner: 'PERSON_OTHER', unit: 'UNIT_B', function: 'FUNCTION_B' },
        { owner: 'PERSON_CHILD', unit: 'UNIT_A', function: 'FUNCTION_B' },
        { owner: 'PERSON_OTHER', unit: 'UNIT_A', function: 'FUNCTION_A' },
        { owner: 'PERSON_CHILD', unit: 'UNIT_B', function: 'FUNCTION_A' }
    ];
    return {
        schema_version: 1,
        storage_key: storageKey,
        subject: { owner: 'PERSON_SELF', descendants: ['PERSON_CHILD'], unit: 'UNIT_A', function: 'FUNCTION_A' },
        resources: draft.entities.map(entity => ({
            entity_code: entity.code, label: entity.label,
            api: apiBase(entity), fields: entity.fields.map(field => ({ ...field })),
            sample_scopes: entity.examples.map((_, index) => samples[index])
        })),
        roles: draft.roles.map(role => ({ ...role, actions: [...role.actions] }))
    };
}

export const TYPE_LABELS = Object.freeze({ text: '一般文字', email: '電子郵件', tel: '電話', number: '數字',
    date: '日期', textarea: '多行文字', toggle: '是／否', select: '狀態選單' });
export const VERTICAL_LABELS = Object.freeze({ all: '全部', self: '本人', descendants: '下層', self_descendants: '本人與下層' });
export const HORIZONTAL_LABELS = Object.freeze({ all: '全部', same_unit: '同單位', same_function: '同職能',
    unit_or_function: '同單位或同職能', unit_and_function: '同單位且同職能' });
