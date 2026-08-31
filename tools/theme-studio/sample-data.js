/**
 * sample-data.js — Theme Studio 元件展示廊的範例 options。
 * 只列「需要資料才顯示」或「建構參數特殊」的元件;其餘用預設 {} 即可。
 * key = ComponentFactory registry 名。
 */
// 內嵌灰階佔位圖(零外部依賴;PhotoWall/PhotoCard 用)
const PH = 'data:image/gif;base64,R0lGODlhAQABAIAAAMLCwgAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==';

export const SAMPLE_OPTIONS = {
    // form
    TextInput: { label: '姓名', placeholder: '請輸入', value: '王小明' },
    TextArea: { label: '備註', rows: 3, value: '多行文字\n第二行' },
    NumberInput: { label: '數量', value: 42 },
    Slider: { label: '圓角', min: 0, max: 24, step: 1, value: 8, unit: 'px' },
    Dropdown: { label: '分類', items: [{ label: '甲', value: 'a' }, { label: '乙', value: 'b' }], value: 'a' },
    MultiSelectDropdown: { label: '標籤', items: [{ label: 'A', value: 'a' }, { label: 'B', value: 'b' }] },
    Checkbox: { label: '同意' },
    Radio: { name: 'demo', items: [{ label: '男', value: 'm' }, { label: '女', value: 'f' }] },
    ToggleSwitch: { label: '啟用' },
    DatePicker: { label: '日期', format: 'taiwan' },
    TimePicker: { label: '時間' },
    SearchForm: { fields: [{ name: 'kw', label: '關鍵字', type: 'text' }] },
    FormField: { label: '欄位' },

    // common
    BasicButton: { type: 'search', customLabel: '查詢' },
    ActionButton: { label: '動作' },
    DownloadButton: { label: '下載' },
    UploadButton: { label: '上傳' },
    Badge: { text: '99+' },
    Tag: { text: 'JavaScript', variant: 'primary' },
    Icon: { name: 'search', size: 24 },
    Progress: { value: 65 },
    LoadingSpinner: { variant: 'dots' },
    Pagination: { total: 100, pageSize: 10, current: 3 },
    Breadcrumb: { items: [{ text: '首頁', label: '首頁' }, { text: '查詢', label: '查詢' }, { text: '明細', label: '明細' }] },
    Divider: {},
    ColorPicker: { value: 'var(--cl-primary)' },
    FeatureCard: { title: '功能', description: '說明文字' },
    // 元件讀 options.data 而非 items；節點需要 id，否則 String(node.id) 會是字面 'undefined'
    // 而使所有節點共用同一份展開狀態。
    TreeList: { data: [{ id: 'n1', label: '節點1', children: [{ id: 'n1-1', label: '子節點' }] }] },

    // layout
    DataTable: {
        columns: [{ name: '姓名' }, { name: '帳號' }, { name: '單位' }],
        data: [['王小明', 'A123', '偵查科'], ['李美華', 'B456', '資訊科']]
    },
    TabContainer: { tabs: [{ id: 'tab1', title: '頁籤一', content: '內容一' }, { id: 'tab2', title: '頁籤二', content: '內容二' }] },
    Stepper: { steps: [{ title: '基本' }, { title: '相關人' }, { title: '確認' }], current: 1 },
    FormRow: {},
    InfoPanel: {
        columns: 1,
        panels: [
            { id: 'stat1', type: 'stat', title: '本月案件', data: { value: 128, label: '受理件數', change: 12 } },
            { id: 'notif1', type: 'notification', title: '最新通知', data: { items: [{ time: '09:30', message: '王小明案已移送' }, { time: '11:00', message: '搜索票已核發' }] } }
        ]
    },

    // social
    Avatar: { name: '王', size: 48 },
    StatCard: { title: '案件數', value: 128 },

    // viz — 圖表資料形狀:{ labels:[...], series:[{ name, data:[...] }] }
    BarChart: { width: 200, height: 140, data: { labels: ['一月', '二月', '三月'], series: [{ name: '數量', data: [30, 55, 40] }] } },
    LineChart: { width: 200, height: 140, data: { labels: ['A', 'B', 'C'], series: [{ name: '值', data: [10, 25, 18] }] } },
    PieChart: { width: 200, height: 140, data: [{ name: '甲', value: 60 }, { name: '乙', value: 40 }] },
    RoseChart: { width: 200, height: 140, data: { labels: ['東', '南', '西'], series: [{ name: '風', data: [20, 35, 15] }] } },
    Sparkline: { data: [5, 8, 6, 12, 9, 14, 11], type: 'line' },
    // Canvas 新家族(CanvasChart 基底)
    HeatmapChart: { height: '160px', title: '', unit: '件', hue: 'red',
        xLabels: ['中山', '大安', '信義'], yLabels: ['竊盜', '詐欺', '毒品'],
        matrix: [[12, 5, 8], [3, 17, 6], [7, null, 11]] },
    ScatterChart: { height: '170px', xLabel: '年齡', yLabel: '涉案金額', yUnit: '萬', colorLabel: '案類', sizeLabel: '前科數',
        points: [
            { x: 24, y: 35, c: '詐欺', s: 1, label: '甲' }, { x: 31, y: 120, c: '詐欺', s: 4, label: '乙' },
            { x: 45, y: 60, c: '竊盜', s: 2, label: '丙' }, { x: 38, y: 15, c: '竊盜', s: 1, label: '丁' },
            { x: 52, y: 210, c: '毒品', s: 6, label: '戊' }, { x: 29, y: 80, c: '毒品', s: 3, label: '己' },
            { x: 61, y: 45, c: '詐欺', s: 5, label: '庚' }, { x: 35, y: 160, c: '竊盜', s: 2, label: '辛' }
        ] },

    // common(補齊)
    AuthButton: { type: 'logout', variant: 'filled', size: 'medium', showLabel: true, customLabel: '登出(王小明)' },
    EditorButton: { type: 'search', label: '搜尋案件', variant: 'primary', size: 'medium', showLabel: true, theme: 'light' },
    PhotoCard: { type: 'portrait', width: '160px', alt: '嫌犯 李美華 檔案照', clickable: true },
    SortButton: { field: '受理日期', state: 'desc', size: 'medium' },

    // input(複合輸入;ListInput 子類無法由 options 預填列值,卡片顯示空白輸入列屬正常)
    AddressInput: { layout: 'horizontal' },
    AddressListInput: { title: '涉案地址清單', addButtonText: '新增地址' },
    ChainedInput: {
        layout: 'vertical', gap: '8px',
        fields: [
            { name: 'unit', type: 'select', label: '偵辦單位', required: true, options: [{ value: 'criminal', label: '刑事局' }, { value: 'precinct', label: '中正一分局' }] },
            { name: 'caseNo', type: 'text', label: '案件編號', placeholder: '例:偵字第112號' }
        ]
    },
    DateTimeInput: { label: '案發時間', useROC: true, showTime: true, minuteStep: 15, dateValue: '2026-06-30', timeValue: '14:30' },
    ListInput: {
        title: '涉案人清單', minItems: 1, maxItems: 5, addButtonText: '新增涉案人',
        fields: [
            { name: 'name', type: 'text', label: '姓名', placeholder: '王小明', required: true },
            { name: 'role', type: 'select', label: '身分', options: [{ value: 'suspect', label: '嫌疑人' }, { value: 'witness', label: '證人' }] }
        ]
    },
    OrganizationInput: { gap: '8px' },
    PersonInfoList: { title: '涉案人員資料', addButtonText: '新增人員', minItems: 1, maxItems: 3 },
    PhoneListInput: { title: '聯絡電話', addButtonText: '新增電話', minItems: 1, maxItems: 3 },
    SocialMediaList: { title: '社群帳號', addButtonText: '新增社群', minItems: 1, maxItems: 3 },
    StudentInput: { layout: 'horizontal' },

    // layout(補齊)
    DocumentWall: {
        readOnly: true,
        documents: [
            { id: 'd1', title: '偵訊筆錄.pdf', type: 'pdf', description: '嫌疑人第一次偵訊紀錄' },
            { id: 'd2', title: '搜索票.pdf', type: 'pdf', description: '法院核發搜索票' },
            { id: 'd3', title: '扣押清單.doc', type: 'doc', description: '現場扣押物品明細' }
        ]
    },
    FunctionMenu: {
        layout: 'grid', columns: 2, size: 'medium',
        items: [
            { id: 'case-search', label: '案件查詢', tooltip: '查詢刑案資料' },
            { id: 'person-mgmt', label: '人員管理', badge: 3 },
            { id: 'evidence', label: '證物管理' },
            { id: 'report', label: '報表統計' }
        ]
    },
    PhotoWall: {
        readOnly: true,
        photos: [
            { id: 'p1', src: PH, alt: '案發現場外觀' },
            { id: 'p2', src: PH, alt: '扣押證物' },
            { id: 'p3', src: PH, alt: '涉案車輛' }
        ]
    },
    SideMenu: {
        width: '220px', activeId: 'case-list',
        items: [
            { id: 'case', text: '案件管理', icon: '📁', children: [{ id: 'case-list', text: '案件清單' }, { id: 'case-new', text: '新增受理', badge: 2 }] },
            { id: 'person', text: '人員管理', icon: '👤' },
            { id: 'evidence', text: '證物室', icon: '🔒', badge: 5 }
        ]
    },
    WorkflowPanel: {
        itemsPerRow: 3,
        data: [
            { StageName: 'Create', DateTime: '2026-06-01 09:00', UnitName: '刑警大隊', UserName: '王警員' },
            { StageName: 'Edit', DateTime: '2026-06-05 14:30', UnitName: '偵查隊', UserName: '李小隊長' },
            { StageName: 'Submit', DateTime: '2026-06-10 11:00', UnitName: '偵查隊' }
        ],
        nextStage: { StageName: 'Approved', NextUnit: '分局長室' }
    },

    // social(補齊)
    ConnectionCard: { avatar: '', name: '王小明', subtitle: '涉毒前科 · 幫派成員', tags: ['販毒', '勒索', '通緝中'] },
    FeedCard: {
        avatar: '', author: '李美華', authorSub: '刑事偵查隊', timestamp: '2026-07-01T09:30:00',
        type: '緊急事件', title: '毒品交易查緝',
        content: '偵查隊於中山區據點查獲王小明涉嫌販毒,現場起獲毒品及帳冊,已上傳筆錄與證物照片。',
        tags: ['販毒', '中山區'], relatedCount: 2
    },
    Timeline: {
        grouped: true,
        items: [
            { timestamp: '2026-06-28', type: '公開資訊', title: '受理報案', description: '受理民眾檢舉王小明販毒情資,登記立案。', icon: '受' },
            { timestamp: '2026-06-30', type: '一般活動', title: '偵辦蒐證', description: '偵查隊跟監蒐證並聲請搜索票。', icon: '偵' },
            { timestamp: '2026-07-02', type: '緊急事件', title: '移送法辦', description: '拘提到案,製作筆錄後移送地檢署偵辦。', icon: '送' }
        ]
    },

    // form(補齊)
    BatchUploader: {
        apiEndpoint: '/api/files/upload', maxFiles: 5, allowedExtensions: ['.pdf', '.jpg', '.png'],
        labels: { dropzone: '將筆錄/證物檔案拖曳到此,或點擊選擇', browse: '選擇檔案', upload: '全部上傳', clear: '全部清除', pending: '待上傳', success: '已上傳', error: '失敗' }
    },

    // ── 0626 合併進來的元件擴充(atoms/composites/sections;已轉 CSSOM 樣式)──
    Text: { text: '本案犯嫌已於中山分局轄區查獲到案,全案依刑法第339條詐欺罪嫌移送臺北地檢署偵辦。', variant: 'body', align: 'left', tag: 'p', truncate: false },
    Heading: { text: '刑事警察大隊113年度肅槍專案執行成果報告', level: 2, align: 'left' },
    Link: { text: '165全民防騙網——查詢假投資案件通報', href: 'https://165.npa.gov.tw', scope: 'external', disabled: false },
    Skeleton: { variant: 'text', lines: 4 },
    CodeBlock: { code: '{\n  "案號": "北市警刑大字第1130045678號",\n  "案由": "詐欺",\n  "狀態": "移送偵辦"\n}', language: 'json' },
    Alert: { variant: 'warning', message: '通緝犯資料已逾 30 日未同步,請確認戶役政連線狀態。', closable: true },
    EmptyState: { icon: 'search', title: '查無攔查紀錄', description: '車牌 BNF-2317 近三個月無攔查與違規紀錄。', actionLabel: '重新查詢' },
    List: { items: [{ primary: '113年度刑字第0421號', secondary: '竊盜案|偵查中' }, { primary: '113年度交查字第1187號', secondary: '肇事逃逸|已移送地檢' }] },
    DescriptionList: { items: [{ label: '案件編號', value: '113-0058821' }, { label: '受理單位', value: '中正第一分局' }, { label: '受理時間', value: '113-07-05 22:41' }] },
    StatGrid: { stats: [{ label: '今日受理報案', value: 47 }, { label: '在途案件', value: 12, detail: '較昨日 +3' }, { label: '今日結案', value: 35 }, { label: '出勤警力', value: 128 }], columns: 2 },
    CardGrid: { columns: 1, cards: [{ title: '受理報案 e 化平台', description: '線上受理竊盜、詐欺等案件報案', tags: ['報案', '線上服務'], badge: 'NEW' }, { title: '失蹤人口查詢', description: '查詢失蹤人口協尋公告與通報進度', tags: ['協尋'] }] },
    StepIndicator: { steps: [{ label: '受理報案' }, { label: '案件分派' }, { label: '偵查中' }, { label: '移送地檢' }], current: 3 },
    DropdownMenu: { label: '案件操作', items: [{ label: '檢視案件詳情', href: '/case/113001234' }, { label: '列印三聯單', href: '/case/print' }, { label: '移轉管轄', href: '/case/transfer' }] },
    FilterBar: { filters: [{ key: 'unit', label: '轄區分局', options: ['中山分局', '大安分局', '信義分局'], defaultValue: '中山分局' }, { key: 'urgent', label: '僅顯示重大案件', type: 'checkbox', defaultValue: false }] },
    ResultList: { items: [{ title: '113年度住宅竊盜案件統計', url: '/stats/burglary-113', snippet: '本年度轄內住宅竊盜較去年同期下降 12%', meta: '刑事警察大隊・2026-06-30', tags: ['統計', '竊盜'] }], emptyText: '查無相關案件紀錄' },
    MediaPlayer: { kind: 'video', src: '', poster: '', controls: true },
    Rating: { value: 4, max: 5, readonly: false },
    TagInput: { tags: ['竊盜', '毒品', '通緝'], placeholder: '輸入案類後按 Enter' },
    Form: { fields: [{ name: 'caseNo', label: '案件編號', type: 'text', required: true, placeholder: '例:114-0001234' }, { name: 'category', label: '案類', type: 'select', options: ['竊盜', '詐欺', '毒品'], required: true }, { name: 'summary', label: '案情摘要', type: 'textarea', placeholder: '概述案發經過' }], submitLabel: '送出', resetLabel: '重設' },
    CommandComposer: { placeholder: '輸入勤務指令,Ctrl+Enter 送出', rows: 3, submitLabel: '送出', ariaLabel: '送出勤務指令', clearOnSubmit: true },
    EditableTable: { columns: [{ key: 'caseNo', label: '案件編號', sortable: true }, { key: 'assignee', label: '承辦員警', editable: true }, { key: 'status', label: '辦理情形', editable: true }], rows: [{ caseNo: 'A11307020015', assignee: '王大明', status: '偵辦中' }, { caseNo: 'A11307020016', assignee: '林小華', status: '已移送' }] },
    BannerSection: { title: '113年暑期青春專案', body: '結合警政、社政與教育資源,加強熱點巡查,守護青少年暑期安全。', actionLabel: '查看專案詳情', actionUrl: '/projects/summer-youth', actionScope: 'internal' },
    ContentSection: { title: '受理報案流程說明', body: '民眾可親臨各分駐(派出)所報案,或撥打110由勤指中心派遣線上警力到場處理;受理後開立報案三聯單。', level: 2 },
    PageFooter: { links: [{ label: '資料開放宣告', href: '/opendata', scope: 'internal' }, { label: '隱私權政策', href: '/privacy', scope: 'internal' }], copyright: '© 2026 臺北市政府警察局中山分局 版權所有' },
    PageHeader: { brand: '臺北市政府警察局中山分局', navLinks: [{ label: '最新消息', href: '/news' }, { label: '為民服務', href: '/services' }, { label: '治安資訊', href: '/safety' }] }
};

// Function-valued loaders stay outside SAMPLE_OPTIONS because structuredClone
// deliberately rejects functions. They are deterministic local studio data,
// not production fallbacks for components that require real data sources.
const DEMO_CITIES = [{ value: 'TPE', label: '臺北市' }];
const DEMO_DISTRICTS = [{ value: 'Zhongzheng', label: '中正區' }];
const DEMO_UNITS = [{ value: 'hq', label: '示範總局' }];
export const SAMPLE_RUNTIME_OPTIONS = {
    AddressInput: {
        loadCities: async () => DEMO_CITIES,
        loadDistricts: async () => DEMO_DISTRICTS,
    },
    AddressListInput: {
        loadCities: async () => DEMO_CITIES,
        loadDistricts: async () => DEMO_DISTRICTS,
    },
    OrganizationInput: {
        loadUnits: async () => DEMO_UNITS,
    },
};

// 展示廊要略過的元件(需大量外部資源 / 全螢幕互動 / 專屬圖資 / 非視覺,不適合縮圖預覽)
export const GALLERY_SKIP = new Set([
    'LeafletMap', 'OSMMapEditor', 'TGOSMapEditor', 'MapEditor', 'MapEditorV2', 'CanvasMap',   // 需圖磚/大畫布
    'WebPainter', 'DrawingBoard',                                            // 全畫布互動
    'WebTextEditor',                                                         // 大型編輯器(另有專頁)
    'GeolocationService', 'WeatherService', 'RegionMap',                     // 非視覺 / 全尺寸區域圖(Canvas)
    'ComponentFactory', 'ComponentBinder', 'PanelManager',                   // 基礎設施 / 需觸發
    'ButtonGroup',                                                           // buttons 需子元件實例,無法用純 options 展示
    'ImageViewer',                                                           // 全螢幕燈箱 + 建構子用位置參數 src,非可內嵌小卡
    // 需專屬圖/樹/流程資料 + 全尺寸畫布,縮圖卡不適用(預覽仍涵蓋 Bar/Line/Pie/Rose/Sparkline)
    'RelationChart', 'OrgChart', 'HierarchyChart', 'SankeyChart', 'SunburstChart', 'TimelineChart', 'FlameChart',
    'ClusterGraph',                                                          // 分群關係圖:大畫布互動,舞台展示
    'DataExplorer'                                                           // 統計探索面板:大型複合件,舞台展示
]);

// 「舞台」全尺寸範例:GALLERY_SKIP 中可在大彈窗渲染者(點卡片開舞台 + 頂部切換)。
// 不在此表者(ButtonGroup 需子實例 / ImageViewer 全螢幕燈箱 / PanelManager 單例)維持純標記。
export const STAGE_SAMPLES = {
    // 圖表(CanvasChart 子類:建構即自動 resize→render,填滿容器)
    FlameChart: { title: '案件耗時火焰圖', width: '100%', height: '100%', data: { name: '專案總工時', value: 100, children: [
        { name: '情資蒐集', value: 40, children: [
            { name: '通聯調閱', value: 22, children: [{ name: '王小明門號', value: 12 }, { name: '李美華門號', value: 10 }] },
            { name: '金流追查', value: 18, children: [{ name: '某幫派帳戶', value: 18 }] }] },
        { name: '跟監佈線', value: 35, children: [{ name: '據點監控', value: 20 }, { name: '車輛追蹤', value: 15 }] },
        { name: '結案報告', value: 25, children: [{ name: '證物彙整', value: 15 }, { name: '移送地檢', value: 10 }] }
    ] } },
    HierarchyChart: { title: '刑事局組織關聯階層', width: '100%', height: '100%', root: { id: 'HQ', title: '刑事警察局', label: '總局', children: [
        { id: 'D1', title: '偵查第一大隊', label: '幫派專責', children: [{ id: 'T1', title: '掃黑小組', label: '某幫派專案' }, { id: 'T2', title: '槍械小組', label: '軍火流向' }] },
        { id: 'D2', title: '偵查第二大隊', label: '經濟犯罪', children: [{ id: 'T3', title: '金流小組', label: '洗錢追查' }, { id: 'T4', title: '詐欺小組', label: '車手集團' }] }
    ] } },
    OrgChart: { title: '專案偵辦組織圖', width: '100%', height: '100%', root: { id: 'CHIEF', title: '專案指揮官', label: '陳隊長', children: [
        { id: 'A', title: '情資組長', label: '王小明', children: [{ id: 'A1', title: '通聯分析', label: '林志豪' }, { id: 'A2', title: '金流分析', label: '李美華' }] },
        { id: 'B', title: '行動組長', label: '張大偉', children: [{ id: 'B1', title: '跟監小組', label: '黃建國' }, { id: 'B2', title: '逮捕小組', label: '吳俊傑' }] }
    ] } },
    RelationChart: { title: '涉案人物關聯網', width: '100%', height: '100%', showLegend: true,
        nodes: [
            { id: 'n1', label: '王小明', group: '主嫌' }, { id: 'n2', label: '李美華', group: '共犯' },
            { id: 'n3', label: '張大偉', group: '車手' }, { id: 'n4', label: '某幫派', group: '組織' },
            { id: 'n5', label: '陳老闆', group: '金主' }, { id: 'n6', label: '人頭帳戶A', group: '金流' }
        ],
        links: [
            { source: 'n1', target: 'n2' }, { source: 'n1', target: 'n4' }, { source: 'n2', target: 'n3' },
            { source: 'n4', target: 'n5' }, { source: 'n5', target: 'n6' }, { source: 'n1', target: 'n6' }, { source: 'n3', target: 'n6' }
        ] },
    SankeyChart: { data: {
        nodes: [{ name: '博弈網站金流' }, { name: '第三方支付' }, { name: '人頭帳戶A' }, { name: '人頭帳戶B' }, { name: '車手集團' }, { name: '虛擬貨幣交易所' }, { name: '境外空殼公司' }],
        links: [{ source: 0, target: 1, value: 50 }, { source: 0, target: 2, value: 30 }, { source: 1, target: 3, value: 25 }, { source: 1, target: 4, value: 20 }, { source: 2, target: 4, value: 15 }, { source: 2, target: 5, value: 15 }, { source: 4, target: 5, value: 20 }, { source: 3, target: 6, value: 18 }, { source: 5, target: 6, value: 30 }]
    } },
    SunburstChart: { data: { name: '刑事警察局', children: [
        { name: '偵查第一大隊', children: [{ name: '重案組', children: [{ name: '組長', value: 1 }, { name: '偵查佐', value: 6 }] }, { name: '詐欺組', children: [{ name: '組長', value: 1 }, { name: '偵查佐', value: 8 }] }] },
        { name: '偵查第二大隊', children: [{ name: '毒品組', children: [{ name: '組長', value: 1 }, { name: '偵查佐', value: 7 }] }, { name: '科技犯罪組', children: [{ name: '組長', value: 1 }, { name: '偵查佐', value: 5 }] }] },
        { name: '鑑識中心', children: [{ name: '鑑識技正', value: 3 }, { name: '鑑識技士', value: 9 }] }
    ] } },
    TimelineChart: { data: [
        { id: 'E1', group: '報案受理', start: 1719800000000, end: 1719801800000, label: '110 報案', details: '民眾報案遭詐騙匯款 80 萬' },
        { id: 'E2', group: '初步偵查', start: 1719802000000, end: 1719807400000, label: '調閱監視器與金流', details: '鎖定人頭帳戶與提款車手' },
        { id: 'E3', group: '初步偵查', start: 1719808000000, end: 1719812000000, label: '聲請通聯調取票', details: '分析嫌犯通聯與位置' },
        { id: 'E4', group: '拘提逮捕', start: 1719813000000, end: 1719816600000, label: '埋伏逮捕車手', details: '於超商 ATM 前逮捕提款車手 2 名' },
        { id: 'E5', group: '移送偵辦', start: 1719817000000, end: 1719822400000, label: '製作筆錄移送地檢', details: '依詐欺、洗錢罪嫌移送台北地檢署' }
    ] },
    // 地圖 / 畫布(純 Canvas 者免服務;LeafletMap 建構即需 Leaflet+圖磚,缺網不 throw、空底圖)
    CanvasMap: { width: 900, height: 650, images: [] },
    LeafletMap: { center: { lat: 25.0330, lng: 121.5654 }, zoom: 16, tileLayer: 'osm' },
    MapEditor: { width: 1000, height: 640 },
    MapEditorV2: { width: 1000, height: 640 },
    OSMMapEditor: { width: 900, height: 600, center: { lat: 25.0330, lng: 121.5654 }, zoom: 16, tileLayer: 'osm', showCompass: true, showScale: true, showCoords: true },
    TGOSMapEditor: { width: 900, height: 600, center: { lat: 25.0330, lng: 121.5654 }, zoom: 16, showCompass: true, showScale: true, showCoords: true },
    DataExplorer: { width: '100%', height: '100%', title: '案件統計探索', pageSize: 10,
        fields: [
            { name: 'unit', label: '轄區', type: 'category' },
            { name: 'type', label: '案類', type: 'category' },
            { name: 'amount', label: '金額', type: 'number', unit: '萬' },
            { name: 'age', label: '嫌犯年齡', type: 'number', unit: '歲' },
            { name: 'prior', label: '前科數', type: 'number' },
            { name: 'at', label: '發生日', type: 'time' }
        ],
        spec: { chartType: 'heatmap', x: { field: 'unit' }, series: { field: 'type' }, y: { field: 'amount', agg: 'sum', unit: '萬' } },
        data: [
            { unit: '中山', type: '詐欺', amount: 120, age: 31, prior: 4, at: '2026-01-15' },
            { unit: '中山', type: '詐欺', amount: 65, age: 27, prior: 2, at: '2026-02-03' },
            { unit: '中山', type: '竊盜', amount: 8, age: 45, prior: 6, at: '2026-02-19' },
            { unit: '中山', type: '毒品', amount: 30, age: 24, prior: 1, at: '2026-03-08' },
            { unit: '大安', type: '詐欺', amount: 210, age: 38, prior: 3, at: '2026-01-22' },
            { unit: '大安', type: '竊盜', amount: 15, age: 52, prior: 8, at: '2026-03-14' },
            { unit: '大安', type: '竊盜', amount: 6, age: 19, prior: 0, at: '2026-04-02' },
            { unit: '大安', type: '毒品', amount: 45, age: 29, prior: 2, at: '2026-04-27' },
            { unit: '信義', type: '詐欺', amount: 88, age: 33, prior: 1, at: '2026-02-11' },
            { unit: '信義', type: '詐欺', amount: 160, age: 41, prior: 5, at: '2026-05-06' },
            { unit: '信義', type: '毒品', amount: 22, age: 26, prior: 3, at: '2026-05-19' },
            { unit: '萬華', type: '竊盜', amount: 12, age: 48, prior: 9, at: '2026-01-30' },
            { unit: '萬華', type: '毒品', amount: 55, age: 35, prior: 4, at: '2026-03-25' },
            { unit: '萬華', type: '詐欺', amount: 70, age: 30, prior: 2, at: '2026-06-01' },
            { unit: '萬華', type: '竊盜', amount: 9, age: 61, prior: 7, at: '2026-06-15' },
            { unit: '中山', type: '詐欺', amount: 95, age: 36, prior: 3, at: '2026-04-18' },
            { unit: '大安', type: '詐欺', amount: 130, age: 44, prior: 6, at: '2026-05-28' },
            { unit: '信義', type: '竊盜', amount: 7, age: 22, prior: 1, at: '2026-06-09' }
        ] },
    ClusterGraph: { width: '100%', height: '100%', mode: 'drill', title: '幫派組織關聯(點聚合節點展開)',
        groups: [
            { id: 'gX', parent: null, label: '幫派X' }, { id: 'gY', parent: null, label: '幫派Y' },
            { id: 'gXa', parent: 'gX', label: '堂口A' }, { id: 'gXb', parent: 'gX', label: '堂口B' },
            { id: 'gYa', parent: 'gY', label: '角頭甲' }, { id: 'gYb', parent: 'gY', label: '角頭乙' }
        ],
        nodes: [
            { id: 'p1', label: '王小明', group: 'gXa' }, { id: 'p2', label: '李大華', group: 'gXa' },
            { id: 'p3', label: '張三', group: 'gXa' }, { id: 'p4', label: '陳四', group: 'gXb' },
            { id: 'p5', label: '林五', group: 'gXb' }, { id: 'p6', label: '黃六', group: 'gXb' },
            { id: 'p7', label: '吳七', group: 'gYa' }, { id: 'p8', label: '鄭八', group: 'gYa' },
            { id: 'p9', label: '朱九', group: 'gYb' }, { id: 'p10', label: '洪十', group: 'gYb' },
            { id: 'p11', label: '郭十一', group: 'gYb' }, { id: 'p12', label: '曾十二', group: 'gXa' }
        ],
        edges: [
            { source: 'p1', target: 'p2', type: '共犯' }, { source: 'p1', target: 'p7', type: '通聯' },
            { source: 'p4', target: 'p9', type: '金流' }, { source: 'p3', target: 'p12', type: '共犯' },
            { source: 'p5', target: 'p8', type: '通聯' }, { source: 'p2', target: 'p10', type: '金流' }
        ] },
    DrawingBoard: { width: 900, height: 520, lineWidth: 3, strokeColor: 'var(--cl-text)', opacity: 1.0 },
    WebPainter: { width: 900, height: 520 },
    WebTextEditor: { height: '520px', placeholder: '請輸入筆錄內容…', readOnly: false,
        content: '<h2>詢問筆錄</h2><p>問:請說明案發當日之經過。</p><p>答:當日下午三時許,我在自宅接獲電話後即前往現場……</p><blockquote>本筆錄經受詢問人閱覽後簽名確認。</blockquote>' }
};
