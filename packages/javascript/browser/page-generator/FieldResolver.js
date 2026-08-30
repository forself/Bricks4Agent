/**
 * FieldResolver
 * 欄位推論引擎 - 依 fieldType 自動推論元件並實例化
 *
 * 支援 30 種 fieldType：
 * - 基本：text, email, password, number, textarea
 * - 日期時間：date, time, datetime
 * - 選擇：select, multiselect, checkbox, toggle, radio
 * - 進階：color, image, file, richtext, canvas
 * - 服務：geolocation, weather
 * - 複合輸入：address, addresslist, chained, list, personinfo,
 *             phonelist, socialmedia, organization, student
 * - 隱藏：hidden
 *
 * 負責：
 * 1. fieldType → 元件映射（封閉集合，30 種）
 * 2. 指定 component 優先，推論 fallback
 * 3. optionsSource 解析（static 直接傳、api 記錄 endpoint）
 * 4. validation 規則設定
 * 5. defaultValue 初始化
 * 6. 包裝為 FormField
 */

import { FormField } from '../ui_components/form/FormField/FormField.js';

// fieldType／內建 component 名稱 → preload() 模組鍵。
// 需與 _registerBuiltins / _registerBuiltinComponents 同步維護；
// 未列出的型別（含自訂 component）一律回退為全量預載，確保行為等價。
const PRELOAD_KEYS_BY_TYPE = {
    // fieldType 推論
    text: ['TextInput'],
    email: ['TextInput'],
    password: ['TextInput'],
    number: ['NumberInput'],
    textarea: ['TextArea'],
    memo: ['TextArea'],
    plaintext: ['TextArea'],
    slider: ['Slider'],
    date: ['DatePicker'],
    rocDate: ['DatePicker'],
    time: ['TimePicker'],
    select: ['Dropdown'],
    multiselect: ['MultiSelectDropdown'],
    checkbox: ['Checkbox'],
    toggle: ['ToggleSwitch'],
    radio: ['Radio'],
    color: ['ColorPicker'],
    image: ['ImageViewer'],
    file: ['BatchUploader'],
    hidden: [],
    datetime: ['DateTimeInput'],
    richtext: ['WebTextEditor'],
    canvas: ['DrawingBoard'],
    geolocation: ['GeolocationService'],
    weather: ['WeatherService'],
    address: ['AddressInput'],
    addresslist: ['AddressListInput'],
    chained: ['ChainedInput'],
    list: ['ListInput'],
    personinfo: ['PersonInfoList'],
    phonelist: ['PhoneListInput'],
    socialmedia: ['SocialMediaList'],
    organization: ['OrganizationInput'],
    student: ['StudentInput'],
    // 明示 field.component 的內建名稱
    TextInput: ['TextInput'],
    NumberInput: ['NumberInput'],
    Slider: ['Slider'],
    TextArea: ['TextArea'],
    Textarea: ['TextArea'],
    DatePicker: ['DatePicker'],
    TimePicker: ['TimePicker'],
    Dropdown: ['Dropdown'],
    MultiSelectDropdown: ['MultiSelectDropdown'],
    Checkbox: ['Checkbox'],
    ToggleSwitch: ['ToggleSwitch'],
    Radio: ['Radio'],
    ColorPicker: ['ColorPicker'],
    ImageViewer: ['ImageViewer'],
    BatchUploader: ['BatchUploader'],
    HiddenInput: [],
    DateTimeInput: ['DateTimeInput'],
    WebTextEditor: ['WebTextEditor'],
    DrawingBoard: ['DrawingBoard'],
    GeolocationService: ['GeolocationService'],
    WeatherService: ['WeatherService'],
    AddressInput: ['AddressInput'],
    AddressListInput: ['AddressListInput'],
    ChainedInput: ['ChainedInput'],
    ListInput: ['ListInput'],
    PersonInfoList: ['PersonInfoList'],
    PhoneListInput: ['PhoneListInput'],
    SocialMediaList: ['SocialMediaList'],
    OrganizationInput: ['OrganizationInput'],
    StudentInput: ['StudentInput'],
};

export class FieldResolver {
    constructor() {
        /** @type {Map<string, Function>} fieldType → 元件工廠函式 */
        this._typeMap = new Map();

        /** @type {Map<string, Function>} component 名稱 → 元件工廠函式 */
        this._componentMap = new Map();

        /** @type {Map<string, Function>} 可被 field.component 明示引用的內建元件 */
        this._builtinComponentMap = new Map();

        // 註冊內建映射
        this._registerBuiltins();
        this._registerBuiltinComponents();
    }

    /**
     * 註冊 fieldType → 元件的映射
     * 使用延遲 import，避免一次載入全部元件
     */
    _registerBuiltins() {
        // text / email / password
        this._typeMap.set('text', (def) => this._createTextInput(def, 'text'));
        this._typeMap.set('email', (def) => this._createTextInput(def, 'email'));
        this._typeMap.set('password', (def) => this._createTextInput(def, 'password'));

        // number
        this._typeMap.set('number', (def) => this._createNumberInput(def));

        // textarea / memo / plaintext 全部交由 B4A TextArea 元件。
        this._typeMap.set('textarea', (def) => this._createTextArea(def));
        this._typeMap.set('memo', (def) => this._createTextArea(def));
        this._typeMap.set('plaintext', (def) => this._createTextArea(def));
        this._typeMap.set('slider', (def) => this._createSlider(def));

        // date
        this._typeMap.set('date', (def) => this._createDatePicker(def));
        this._typeMap.set('rocDate', (def) => this._createDatePicker({
            ...def,
            format: 'taiwan',
        }));

        // time
        this._typeMap.set('time', (def) => this._createTimePicker(def));

        // select
        this._typeMap.set('select', (def) => this._createDropdown(def));

        // multiselect
        this._typeMap.set('multiselect', (def) => this._createMultiSelectDropdown(def));

        // checkbox
        this._typeMap.set('checkbox', (def) => this._createCheckbox(def));

        // toggle
        this._typeMap.set('toggle', (def) => this._createToggleSwitch(def));

        // radio
        this._typeMap.set('radio', (def) => this._createRadio(def));

        // color
        this._typeMap.set('color', (def) => this._createColorPicker(def));

        // image
        this._typeMap.set('image', (def) => this._createImageViewer(def));

        // file
        this._typeMap.set('file', (def) => this._createBatchUploader(def));

        // hidden
        this._typeMap.set('hidden', (def) => this._createHiddenInput(def));

        // ─── 進階類型 ───

        // datetime（日期+時間複合）
        this._typeMap.set('datetime', (def) => this._createDateTimeInput(def));

        // richtext（富文本編輯器）
        this._typeMap.set('richtext', (def) => this._createWebTextEditor(def));

        // canvas（繪圖白板）
        this._typeMap.set('canvas', (def) => this._createDrawingBoard(def));

        // geolocation（地理位置服務）
        this._typeMap.set('geolocation', (def) => this._createGeolocationService(def));

        // weather（天氣服務）
        this._typeMap.set('weather', (def) => this._createWeatherService(def));

        // ─── 複合輸入元件 ───

        // address（地址輸入，含縣市連動）
        this._typeMap.set('address', (def) => this._createAddressInput(def));

        // addresslist（地址列表）
        this._typeMap.set('addresslist', (def) => this._createAddressListInput(def));

        // chained（聯動輸入）
        this._typeMap.set('chained', (def) => this._createChainedInput(def));

        // list（動態列表輸入）
        this._typeMap.set('list', (def) => this._createListInput(def));

        // personinfo（人員資訊列表）
        this._typeMap.set('personinfo', (def) => this._createPersonInfoList(def));

        // phonelist（電話列表）
        this._typeMap.set('phonelist', (def) => this._createPhoneListInput(def));

        // socialmedia（社群媒體列表）
        this._typeMap.set('socialmedia', (def) => this._createSocialMediaList(def));

        // organization（組織資訊輸入）
        this._typeMap.set('organization', (def) => this._createOrganizationInput(def));

        // student（學生資訊輸入）
        this._typeMap.set('student', (def) => this._createStudentInput(def));
    }

    /**
     * 註冊可安全明示的既有 field 元件名稱。
     * 這與 fieldType 推論分開，讓未知 field.component 能 fail-closed，
     * 同時保留既有內建名稱的使用方式。
     */
    _registerBuiltinComponents() {
        const textInput = (def) => {
            const type = ['email', 'password'].includes(def.fieldType) ? def.fieldType : 'text';
            return this._createTextInput(def, type);
        };
        const entries = {
            TextInput: textInput,
            NumberInput: (def) => this._createNumberInput(def),
            Slider: (def) => this._createSlider(def),
            TextArea: (def) => this._createTextArea(def),
            Textarea: (def) => this._createTextArea(def),
            DatePicker: (def) => this._createDatePicker(def),
            TimePicker: (def) => this._createTimePicker(def),
            Dropdown: (def) => this._createDropdown(def),
            MultiSelectDropdown: (def) => this._createMultiSelectDropdown(def),
            Checkbox: (def) => this._createCheckbox(def),
            ToggleSwitch: (def) => this._createToggleSwitch(def),
            Radio: (def) => this._createRadio(def),
            ColorPicker: (def) => this._createColorPicker(def),
            ImageViewer: (def) => this._createImageViewer(def),
            BatchUploader: (def) => this._createBatchUploader(def),
            HiddenInput: (def) => this._createHiddenInput(def),
            DateTimeInput: (def) => this._createDateTimeInput(def),
            WebTextEditor: (def) => this._createWebTextEditor(def),
            DrawingBoard: (def) => this._createDrawingBoard(def),
            GeolocationService: (def) => this._createGeolocationService(def),
            WeatherService: (def) => this._createWeatherService(def),
            AddressInput: (def) => this._createAddressInput(def),
            AddressListInput: (def) => this._createAddressListInput(def),
            ChainedInput: (def) => this._createChainedInput(def),
            ListInput: (def) => this._createListInput(def),
            PersonInfoList: (def) => this._createPersonInfoList(def),
            PhoneListInput: (def) => this._createPhoneListInput(def),
            SocialMediaList: (def) => this._createSocialMediaList(def),
            OrganizationInput: (def) => this._createOrganizationInput(def),
            StudentInput: (def) => this._createStudentInput(def),
        };

        for (const [name, factory] of Object.entries(entries)) {
            this._builtinComponentMap.set(name, factory);
        }
    }

    // ─── 元件工廠方法 ───

    _createTextInput(def, type) {
        const { TextInput } = this._getModule('TextInput');
        const opts = {
            type,
            placeholder: def.placeholder || def.label,
            disabled: def.isReadonly,
            required: def.isRequired,
        };
        if (def.validation) {
            if (def.validation.maxLength) opts.maxLength = def.validation.maxLength;
        }
        if (def.defaultValue != null) opts.value = def.defaultValue;
        return new TextInput(opts);
    }

    _createNumberInput(def) {
        const { NumberInput } = this._getModule('NumberInput');
        const opts = {
            placeholder: def.label,
            disabled: def.isReadonly,
        };
        if (def.validation) {
            if (def.validation.min != null) opts.min = def.validation.min;
            if (def.validation.max != null) opts.max = def.validation.max;
        }
        if (def.defaultValue != null) opts.value = Number(def.defaultValue);
        return new NumberInput(opts);
    }

    _createSlider(def) {
        const { Slider } = this._getModule('Slider');
        const opts = { label: def.label, disabled: def.isReadonly };
        if (def.validation) {
            if (def.validation.min != null) opts.min = def.validation.min;
            if (def.validation.max != null) opts.max = def.validation.max;
            if (def.validation.step != null) opts.step = def.validation.step;
        }
        if (def.defaultValue != null) opts.value = Number(def.defaultValue);
        return new Slider(opts);
    }

    _createTextArea(def) {
        const { TextArea } = this._getModule('TextArea');
        // FormField already renders the field label. Keeping TextArea's own label
        // empty prevents generated forms from displaying it twice.
        const opts = {
            label: '',
            placeholder: def.placeholder || '',
            rows: Number(def.rows || def.componentOptions?.rows || 4),
            required: def.isRequired,
            disabled: def.isReadonly,
            readonly: def.isReadonly,
        };
        if (def.validation && def.validation.maxLength != null) opts.maxLength = def.validation.maxLength;
        if (def.defaultValue != null) opts.value = String(def.defaultValue);
        return new TextArea(opts);
    }

    _createDatePicker(def) {
        const { DatePicker } = this._getModule('DatePicker');
        const opts = {
            placeholder: def.placeholder || def.label,
            disabled: def.isReadonly,
            required: def.isRequired,
        };
        if (def.format) opts.format = def.format;
        if (def.fieldType === 'rocDate' && !opts.format) opts.format = 'taiwan';
        if (def.min) opts.min = def.min;
        if (def.max) opts.max = def.max;
        if (def.maxYearOffset !== null && def.maxYearOffset !== undefined && !opts.max) {
            const year = new Date().getFullYear() + Number(def.maxYearOffset || 0);
            opts.max = new Date(year, 11, 31);
        }
        if (def.defaultValue === 'today') opts.value = new Date();
        else if (def.defaultValue) opts.value = def.defaultValue;
        return new DatePicker(opts);
    }

    _createTimePicker(def) {
        const { TimePicker } = this._getModule('TimePicker');
        const opts = {
            placeholder: def.label,
            disabled: def.isReadonly,
            required: def.isRequired,
        };
        if (def.defaultValue) opts.value = def.defaultValue;
        return new TimePicker(opts);
    }

    _createDropdown(def) {
        const { Dropdown } = this._getModule('Dropdown');
        const opts = {
            variant: 'searchable',
            placeholder: def.placeholder || `請選擇${def.label}`,
            disabled: def.isReadonly,
            items: this._resolveStaticOptions(def),
        };
        if (def.defaultValue) opts.value = def.defaultValue;
        return new Dropdown(opts);
    }

    _createMultiSelectDropdown(def) {
        const { MultiSelectDropdown } = this._getModule('MultiSelectDropdown');
        const opts = {
            placeholder: `請選擇${def.label}`,
            disabled: def.isReadonly,
            modalTitle: def.label,
            items: this._resolveStaticOptions(def),
        };
        if (def.defaultValue) {
            try { opts.values = JSON.parse(def.defaultValue); } catch(e) { /* 忽略 */ }
        }
        return new MultiSelectDropdown(opts);
    }

    _createCheckbox(def) {
        const { Checkbox } = this._getModule('Checkbox');
        const opts = {
            // FormField owns the field label; the checkbox remains the control.
            label: def.componentOptions?.controlLabel || '',
            disabled: def.isReadonly,
            checked: def.defaultValue === 'true',
        };
        return new Checkbox(opts);
    }

    _createToggleSwitch(def) {
        const { ToggleSwitch } = this._getModule('ToggleSwitch');
        const opts = {
            label: '',  // FormField 已有 label
            disabled: def.isReadonly,
            checked: def.defaultValue === 'true',
        };
        return new ToggleSwitch(opts);
    }

    _createRadio(def) {
        const { Radio } = this._getModule('Radio');
        const items = this._resolveStaticOptions(def).map(opt => ({
            label: opt.label,
            value: opt.value
        }));
        const group = Radio.createGroup({
            name: def.fieldName,
            items,
            value: def.defaultValue || null,
        });
        // Radio.createGroup 已提供 getValue/setValue，僅補充 options 和 clear
        group.options = { defaultValue: def.defaultValue || null };
        if (!group.clear) {
            group.clear = () => {
                if (typeof group.setValue === 'function') group.setValue(null);
            };
        }
        return group;
    }

    _createColorPicker(def) {
        const { ColorPicker } = this._getModule('ColorPicker');
        const opts = {};
        if (def.defaultValue) opts.color = def.defaultValue;
        return new ColorPicker(opts);
    }

    _createImageViewer(def) {
        const { ImageViewer } = this._getModule('ImageViewer');
        return new ImageViewer({});
    }

    _createBatchUploader(def) {
        const { BatchUploader } = this._getModule('BatchUploader');
        const componentOptions = def.componentOptions || {};
        const validation = def.validation || {};
        const opts = {
            ...componentOptions,
            multiple: componentOptions.multiple ?? validation.multiple ?? true,
            maxFiles: componentOptions.maxFiles ?? validation.maxItems ?? 10,
            maxFileSize: componentOptions.maxFileSize ?? validation.maxFileSize ?? 10 * 1024 * 1024,
            allowedExtensions: componentOptions.allowedExtensions ?? validation.allowedExtensions ?? null,
            autoUpload: componentOptions.autoUpload ?? false,
        };
        return new BatchUploader(opts);
    }

    _createHiddenInput(def) {
        const wrapper = {
            element: null,
            _input: null,
            mount(container) {
                const target = typeof container === 'string' ? document.querySelector(container) : container;
                if (target) target.appendChild(this.element);
                return this;
            },
            destroy() { this.element?.remove(); },
            getValue() { return this._input.value; },
            setValue(v) { this._input.value = v || ''; },
            clear() { this._input.value = ''; },
            options: {}
        };

        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = def.fieldName;
        if (def.defaultValue) input.value = def.defaultValue;

        wrapper.element = input;
        wrapper._input = input;
        return wrapper;
    }

    // ─── 進階類型工廠方法 ───

    _createDateTimeInput(def) {
        const { DateTimeInput } = this._getModule('DateTimeInput');
        const opts = {
            label: def.label,
            showTime: true,
        };
        if (def.defaultValue) {
            // 嘗試拆分 "YYYY-MM-DD HH:MM"
            const parts = def.defaultValue.split(' ');
            if (parts[0]) opts.dateValue = parts[0];
            if (parts[1]) opts.timeValue = parts[1];
        }
        return new DateTimeInput(opts);
    }

    _createWebTextEditor(def) {
        const { WebTextEditor } = this._getModule('WebTextEditor');
        const opts = {};
        if (def.defaultValue) opts.content = def.defaultValue;
        return new WebTextEditor(opts);
    }

    _createDrawingBoard(def) {
        const { DrawingBoard } = this._getModule('DrawingBoard');
        return new DrawingBoard({});
    }

    _createGeolocationService(def) {
        const { GeolocationService } = this._getModule('GeolocationService');
        return new GeolocationService({});
    }

    _createWeatherService(def) {
        const { WeatherService } = this._getModule('WeatherService');
        return new WeatherService({});
    }

    // ─── 複合輸入元件工廠方法 ───

    _createAddressInput(def) {
        const { AddressInput } = this._getModule('AddressInput');
        const opts = {};
        return new AddressInput(opts);
    }

    _createAddressListInput(def) {
        const { AddressListInput } = this._getModule('AddressListInput');
        const opts = {};
        if (def.validation?.maxItems) opts.maxItems = def.validation.maxItems;
        if (def.validation?.minItems) opts.minItems = def.validation.minItems;
        return new AddressListInput(opts);
    }

    _createChainedInput(def) {
        const { ChainedInput } = this._getModule('ChainedInput');
        // 從 optionsSource 解析欄位定義
        const fields = def.config?.fields || [];
        const opts = { fields };
        return new ChainedInput(opts);
    }

    _createListInput(def) {
        const { ListInput } = this._getModule('ListInput');
        const opts = {
            title: def.label,
        };
        if (def.config?.fields) opts.fields = def.config.fields;
        if (def.validation?.maxItems) opts.maxItems = def.validation.maxItems;
        if (def.validation?.minItems) opts.minItems = def.validation.minItems;
        return new ListInput(opts);
    }

    _createPersonInfoList(def) {
        const { PersonInfoList } = this._getModule('PersonInfoList');
        const opts = {};
        if (def.validation?.maxItems) opts.maxItems = def.validation.maxItems;
        return new PersonInfoList(opts);
    }

    _createPhoneListInput(def) {
        const { PhoneListInput } = this._getModule('PhoneListInput');
        const opts = {};
        if (def.validation?.maxItems) opts.maxItems = def.validation.maxItems;
        return new PhoneListInput(opts);
    }

    _createSocialMediaList(def) {
        const { SocialMediaList } = this._getModule('SocialMediaList');
        const opts = {};
        if (def.validation?.maxItems) opts.maxItems = def.validation.maxItems;
        return new SocialMediaList(opts);
    }

    _createOrganizationInput(def) {
        const { OrganizationInput } = this._getModule('OrganizationInput');
        return new OrganizationInput({});
    }

    _createStudentInput(def) {
        const { StudentInput } = this._getModule('StudentInput');
        return new StudentInput({});
    }

    // ─── 工具方法 ───

    /**
     * 解析靜態選項
     */
    _resolveStaticOptions(def) {
        if (Array.isArray(def.options)) return def.options;
        if (!def.optionsSource) return [];
        if (def.optionsSource.type === 'static' && Array.isArray(def.optionsSource.items)) {
            return def.optionsSource.items;
        }
        return []; // API 型別在 mount 後由 TriggerEngine 或外部載入
    }

    /**
     * 動態載入元件模組
     * 模組已由 ES module 快取，不會重複載入
     */
    _getModule(name) {
        // 返回模組快取（同步方式，假設已預載入）
        // 實際使用時由外部先 import，或改用動態 import
        return this._moduleCache.get(name) || {};
    }

    // ─── 公開 API ───

    /**
     * 預載入元件模組
     * 必須在 resolve() 之前呼叫
     * @param {string[]} [neededTypes] - 頁面實際出現的 fieldType／component 名稱；
     *   省略、或包含任一未知型別時，載入全部模組（維持既有行為）
     */
    async preload(neededTypes) {
        // 合併式載入：子集預載不清空既有快取，重複／交錯 preload 只會擴充可解析集合
        if (!this._moduleCache) this._moduleCache = new Map();

        const modules = {
            TextInput: () => import('../ui_components/form/TextInput/TextInput.js'),
            NumberInput: () => import('../ui_components/form/NumberInput/NumberInput.js'),
            Slider: () => import('../ui_components/form/Slider/Slider.js'),
            TextArea: () => import('../ui_components/form/TextArea/TextArea.js'),
            DatePicker: () => import('../ui_components/form/DatePicker/DatePicker.js'),
            TimePicker: () => import('../ui_components/form/TimePicker/TimePicker.js'),
            Dropdown: () => import('../ui_components/form/Dropdown/Dropdown.js'),
            MultiSelectDropdown: () => import('../ui_components/form/MultiSelectDropdown/MultiSelectDropdown.js'),
            Checkbox: () => import('../ui_components/form/Checkbox/Checkbox.js'),
            ToggleSwitch: () => import('../ui_components/form/ToggleSwitch/ToggleSwitch.js'),
            Radio: () => import('../ui_components/form/Radio/Radio.js'),
            ColorPicker: () => import('../ui_components/common/ColorPicker/ColorPicker.js'),
            ImageViewer: () => import('../ui_components/common/ImageViewer/ImageViewer.js'),
            BatchUploader: () => import('../ui_components/form/BatchUploader/BatchUploader.js'),
            // 進階類型
            DateTimeInput: () => import('../ui_components/input/DateTimeInput/DateTimeInput.js'),
            WebTextEditor: () => import('../ui_components/editor/WebTextEditor/WebTextEditor.js'),
            DrawingBoard: () => import('../ui_components/viz/DrawingBoard/DrawingBoard.js'),
            GeolocationService: () => import('../ui_components/utils/GeolocationService.js'),
            WeatherService: () => import('../ui_components/utils/WeatherService.js'),
            // 複合輸入元件
            AddressInput: () => import('../ui_components/input/AddressInput/AddressInput.js'),
            AddressListInput: () => import('../ui_components/input/AddressListInput/AddressListInput.js'),
            ChainedInput: () => import('../ui_components/input/ChainedInput/ChainedInput.js'),
            ListInput: () => import('../ui_components/input/ListInput/ListInput.js'),
            PersonInfoList: () => import('../ui_components/input/PersonInfoList/PersonInfoList.js'),
            PhoneListInput: () => import('../ui_components/input/PhoneListInput/PhoneListInput.js'),
            SocialMediaList: () => import('../ui_components/input/SocialMediaList/SocialMediaList.js'),
            OrganizationInput: () => import('../ui_components/input/OrganizationInput/OrganizationInput.js'),
            StudentInput: () => import('../ui_components/input/StudentInput/StudentInput.js'),
        };

        let names = Object.keys(modules);
        if (neededTypes !== null && neededTypes !== undefined) {
            const minimal = this._resolvePreloadKeys(neededTypes);
            if (minimal) names = minimal;
        }

        const results = await Promise.all(names.map((name) => modules[name]()));
        names.forEach((name, i) => {
            this._moduleCache.set(name, results[i]);
        });
    }

    /**
     * 依實際出現的型別計算最小預載模組集合
     * @param {string[]} neededTypes
     * @returns {string[]|null} 任一型別未列於 PRELOAD_KEYS_BY_TYPE（如自訂 component）
     *   時回傳 null，由 preload() 回退為全量預載
     * @private
     */
    _resolvePreloadKeys(neededTypes) {
        if (!Array.isArray(neededTypes)) return null;
        const keys = new Set();
        for (const type of neededTypes) {
            if (!Object.prototype.hasOwnProperty.call(PRELOAD_KEYS_BY_TYPE, type)) return null;
            for (const key of PRELOAD_KEYS_BY_TYPE[type]) keys.add(key);
        }
        return [...keys];
    }

    /**
     * 註冊自訂元件（指定 component 名稱時使用）
     * @param {string} name - 元件名稱
     * @param {Function} factory - (fieldDef) => component instance
     */
    registerComponent(name, factory) {
        this._componentMap.set(name, factory);
    }

    /**
     * 解析單一欄位定義 → 元件 + FormField
     * @param {Object} fieldDef - 單一欄位定義
     * @returns {{ component: Object, formField: FormField }}
     */
    resolve(fieldDef) {
        let component;

        // 明示 component 不得靜默回退到 fieldType；只接受已註冊 custom
        // component 或上述相容性 allowlist 中的內建 field component。
        if (fieldDef.component !== null && fieldDef.component !== undefined && fieldDef.component !== '') {
            if (typeof fieldDef.component !== 'string') {
                throw new TypeError('field.component must be a registered component name.');
            }
            const factory = this._componentMap.get(fieldDef.component)
                || this._builtinComponentMap.get(fieldDef.component);
            if (!factory) {
                throw new Error(`[FieldResolver] 未註冊的 component: ${fieldDef.component}`);
            }
            component = factory(fieldDef);
        } else {
            // 依 fieldType 推論
            const factory = this._typeMap.get(fieldDef.fieldType);
            if (!factory) {
                console.warn(`[FieldResolver] 未知的 fieldType: ${fieldDef.fieldType}，使用 text 替代`);
                component = this._createTextInput(fieldDef, 'text');
            } else {
                component = factory(fieldDef);
            }
        }

        // 包裝為 FormField
        let formField = null;
        try {
            formField = new FormField({
                fieldName: fieldDef.fieldName,
                label: fieldDef.fieldType === 'hidden' ? '' : fieldDef.label,
                required: fieldDef.isRequired,
                col: fieldDef.formCol,
                component
            });

            // hidden 欄位直接隱藏
            if (fieldDef.fieldType === 'hidden') {
                formField.hide();
            }
        } catch (error) {
            const cleanupErrors = [];
            this._destroyResolvedEntry({ component, formField }, cleanupErrors);
            if (cleanupErrors.length > 0 && error && typeof error === 'object') {
                try {
                    error.cleanupErrors = cleanupErrors;
                } catch {
                    // Preserve the original failure even when it is non-extensible.
                }
            }
            throw error;
        }

        return { component, formField };
    }

    /**
     * 批次解析所有欄位
     * @param {Array} fieldDefs - fields 陣列
     * @returns {Map<string, { component, formField }>}
     */
    resolveAll(fieldDefs) {
        if (!Array.isArray(fieldDefs)) {
            throw new TypeError('resolveAll(fieldDefs) requires an array.');
        }

        // Preflight before creating anything so duplicate keys can never cause
        // a silently overwritten component or partially-created form.
        const fieldNames = new Set();
        for (const def of fieldDefs) {
            const fieldName = def?.fieldName;
            if (fieldNames.has(fieldName)) {
                throw new Error(`[FieldResolver] 重複的 fieldName: ${String(fieldName)}`);
            }
            fieldNames.add(fieldName);
        }

        const result = new Map();
        try {
            for (const def of fieldDefs) {
                result.set(def.fieldName, this.resolve(def));
            }
            return result;
        } catch (error) {
            const cleanupErrors = [];
            const entries = [...result.values()];
            for (let index = entries.length - 1; index >= 0; index -= 1) {
                this._destroyResolvedEntry(entries[index], cleanupErrors);
            }
            result.clear();
            if (cleanupErrors.length > 0 && error && typeof error === 'object') {
                try {
                    error.cleanupErrors = [
                        ...(Array.isArray(error.cleanupErrors) ? error.cleanupErrors : []),
                        ...cleanupErrors,
                    ];
                } catch {
                    // Preserve the original failure even when it is non-extensible.
                }
            }
            throw error;
        }
    }

    /**
     * Best-effort cleanup for a resolved entry without masking the root error.
     * FormField owns its component, so a successful FormField.destroy() is enough.
     *
     * @private
     */
    _destroyResolvedEntry(entry, cleanupErrors = []) {
        if (!entry) return;

        if (typeof entry.formField?.destroy === 'function') {
            try {
                entry.formField.destroy();
                return;
            } catch (error) {
                cleanupErrors.push(error);
            }
        }

        if (typeof entry.component?.destroy === 'function') {
            try {
                entry.component.destroy();
            } catch (error) {
                cleanupErrors.push(error);
            }
        }
    }
}

export default FieldResolver;
