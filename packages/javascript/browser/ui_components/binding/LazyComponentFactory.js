/**
 * LazyComponentFactory
 * ComponentFactory 的延遲載入版:註冊表僅存 dynamic import loader,
 * 經 preload() 載入後的類別進入快取,之後 create()/getComponentClass() 保持同步。
 * 僅載入實際用到的元件模組,避免 tool 頁面拉入整套元件庫。
 */

// 延遲註冊表:元件名稱 -> 載入該元件實際來源模組的 loader
// (路徑取自各分類 barrel 的 re-export specifier;勿改為 import 分類 barrel 本身)
const loaders = new Map(Object.entries({
    // Viz
    'BarChart': () => import('../viz/BarChart.js').then((m) => m.BarChart),
    'CanvasMap': () => import('../viz/CanvasMap.js').then((m) => m.CanvasMap),
    'FlameChart': () => import('../viz/FlameChart.js').then((m) => m.FlameChart),
    'HierarchyChart': () => import('../viz/HierarchyChart.js').then((m) => m.HierarchyChart),
    'LeafletMap': () => import('../viz/LeafletMap.js').then((m) => m.LeafletMap),
    'LineChart': () => import('../viz/LineChart.js').then((m) => m.LineChart),
    'MapEditor': () => import('../viz/MapEditor.js').then((m) => m.MapEditor),
    'MapEditorV2': () => import('../viz/MapEditorV2.js').then((m) => m.MapEditorV2),
    'OrgChart': () => import('../viz/OrgChart.js').then((m) => m.OrgChart),
    'PieChart': () => import('../viz/PieChart.js').then((m) => m.PieChart),
    'RelationChart': () => import('../viz/RelationChart.js').then((m) => m.RelationChart),
    'RoseChart': () => import('../viz/RoseChart.js').then((m) => m.RoseChart),
    'Sparkline': () => import('../viz/Sparkline.js').then((m) => m.Sparkline),
    'SankeyChart': () => import('../viz/SankeyChart.js').then((m) => m.SankeyChart),
    'SunburstChart': () => import('../viz/SunburstChart.js').then((m) => m.SunburstChart),
    'TimelineChart': () => import('../viz/TimelineChart.js').then((m) => m.TimelineChart),
    'OSMMapEditor': () => import('../viz/OSMMapEditor/index.js').then((m) => m.OSMMapEditor),
    'TGOSMapEditor': () => import('../viz/TGOSMapEditor/index.js').then((m) => m.TGOSMapEditor),
    'HeatmapChart': () => import('../viz/HeatmapChart.js').then((m) => m.HeatmapChart),
    'ScatterChart': () => import('../viz/ScatterChart.js').then((m) => m.ScatterChart),
    'ClusterGraph': () => import('../viz/ClusterGraph.js').then((m) => m.ClusterGraph),
    'DrawingBoard': () => import('../viz/DrawingBoard/index.js').then((m) => m.DrawingBoard),
    'WebPainter': () => import('../viz/WebPainter/index.js').then((m) => m.WebPainter),

    // Layout
    'DataTable': () => import('../layout/DataTable/index.js').then((m) => m.DataTable),
    'DocumentWall': () => import('../layout/DocumentWall/index.js').then((m) => m.DocumentWall),
    'FormDesigner': () => import('../layout/FormDesigner/index.js').then((m) => m.FormDesigner),
    'FormRow': () => import('../layout/FormRow/index.js').then((m) => m.FormRow),
    'FunctionMenu': () => import('../layout/FunctionMenu/index.js').then((m) => m.FunctionMenu),
    'InfoPanel': () => import('../layout/InfoPanel/index.js').then((m) => m.InfoPanel),
    'PanelManager': () => import('../layout/Panel/index.js').then((m) => m.PanelManager),
    'PhotoWall': () => import('../layout/PhotoWall/index.js').then((m) => m.PhotoWall),
    'SideMenu': () => import('../layout/SideMenu/index.js').then((m) => m.SideMenu),
    'Stepper': () => import('../layout/Stepper/index.js').then((m) => m.Stepper),
    'TabContainer': () => import('../layout/TabContainer/index.js').then((m) => m.TabContainer),
    'WorkflowPanel': () => import('../layout/WorkflowPanel/index.js').then((m) => m.WorkflowPanel),
    'EditableTable': () => import('../layout/EditableTable/index.js').then((m) => m.EditableTable),

    // Form
    'BatchUploader': () => import('../form/BatchUploader/index.js').then((m) => m.BatchUploader),
    'Checkbox': () => import('../form/Checkbox/index.js').then((m) => m.Checkbox),
    'CommandComposer': () => import('../form/CommandComposer/index.js').then((m) => m.CommandComposer),
    'DatePicker': () => import('../form/DatePicker/index.js').then((m) => m.DatePicker),
    'Dropdown': () => import('../form/Dropdown/index.js').then((m) => m.Dropdown),
    'FormField': () => import('../form/FormField/index.js').then((m) => m.FormField),
    'MultiSelectDropdown': () => import('../form/MultiSelectDropdown/index.js').then((m) => m.MultiSelectDropdown),
    'NumberInput': () => import('../form/NumberInput/index.js').then((m) => m.NumberInput),
    'Radio': () => import('../form/Radio/index.js').then((m) => m.Radio),
    'SearchForm': () => import('../form/SearchForm/index.js').then((m) => m.SearchForm),
    'Slider': () => import('../form/Slider/index.js').then((m) => m.Slider),
    'TextArea': () => import('../form/TextArea/index.js').then((m) => m.TextArea),
    'TextInput': () => import('../form/TextInput/index.js').then((m) => m.TextInput),
    'TimePicker': () => import('../form/TimePicker/index.js').then((m) => m.TimePicker),
    'ToggleSwitch': () => import('../form/ToggleSwitch/index.js').then((m) => m.ToggleSwitch),
    'Rating': () => import('../form/Rating/index.js').then((m) => m.Rating),
    'Form': () => import('../form/Form/index.js').then((m) => m.Form),
    'TagInput': () => import('../form/TagInput/index.js').then((m) => m.TagInput),

    // Input
    'AddressInput': () => import('../input/AddressInput/index.js').then((m) => m.AddressInput),
    'AddressListInput': () => import('../input/AddressListInput/index.js').then((m) => m.AddressListInput),
    'ChainedInput': () => import('../input/ChainedInput/index.js').then((m) => m.ChainedInput),
    'DateTimeInput': () => import('../input/DateTimeInput/index.js').then((m) => m.DateTimeInput),
    'ListInput': () => import('../input/ListInput/index.js').then((m) => m.ListInput),
    'OrganizationInput': () => import('../input/OrganizationInput/index.js').then((m) => m.OrganizationInput),
    'PersonInfoList': () => import('../input/PersonInfoList/index.js').then((m) => m.PersonInfoList),
    'PhoneListInput': () => import('../input/PhoneListInput/index.js').then((m) => m.PhoneListInput),
    'SocialMediaList': () => import('../input/SocialMediaList/index.js').then((m) => m.SocialMediaList),
    'StudentInput': () => import('../input/StudentInput/index.js').then((m) => m.StudentInput),

    // Common
    'ActionButton': () => import('../common/ActionButton/index.js').then((m) => m.ActionButton),
    'AuthButton': () => import('../common/AuthButton/index.js').then((m) => m.AuthButton),
    'Badge': () => import('../common/Badge/index.js').then((m) => m.Badge),
    'BasicButton': () => import('../common/BasicButton/index.js').then((m) => m.BasicButton),
    'Breadcrumb': () => import('../common/Breadcrumb/index.js').then((m) => m.Breadcrumb),
    'ButtonGroup': () => import('../common/ButtonGroup/index.js').then((m) => m.ButtonGroup),
    'ColorPicker': () => import('../common/ColorPicker/index.js').then((m) => m.ColorPicker),
    'SimpleDialog': () => import('../common/Dialog/index.js').then((m) => m.SimpleDialog),
    'Divider': () => import('../common/Divider/index.js').then((m) => m.Divider),
    'DownloadButton': () => import('../common/DownloadButton/index.js').then((m) => m.DownloadButton),
    'EditorButton': () => import('../common/EditorButton/index.js').then((m) => m.EditorButton),
    'FeatureCard': () => import('../common/FeatureCard/index.js').then((m) => m.FeatureCard),
    'Icon': () => import('../common/Icon/index.js').then((m) => m.Icon),
    'ImageViewer': () => import('../common/ImageViewer/index.js').then((m) => m.ImageViewer),
    'LoadingSpinner': () => import('../common/LoadingSpinner/index.js').then((m) => m.LoadingSpinner),
    'Notification': () => import('../common/Notification/index.js').then((m) => m.Notification),
    'Pagination': () => import('../common/Pagination/index.js').then((m) => m.Pagination),
    'PhotoCard': () => import('../common/PhotoCard/index.js').then((m) => m.PhotoCard),
    'Progress': () => import('../common/Progress/index.js').then((m) => m.Progress),
    'SortButton': () => import('../common/SortButton/index.js').then((m) => m.SortButton),
    'Tag': () => import('../common/Tag/index.js').then((m) => m.Tag),
    'Tooltip': () => import('../common/Tooltip/index.js').then((m) => m.Tooltip),
    'TreeList': () => import('../common/TreeList/index.js').then((m) => m.TreeList),
    'UploadButton': () => import('../common/UploadButton/index.js').then((m) => m.UploadButton),

    // Social
    'Avatar': () => import('../social/Avatar/index.js').then((m) => m.Avatar),
    'ConnectionCard': () => import('../social/ConnectionCard/index.js').then((m) => m.ConnectionCard),
    'FeedCard': () => import('../social/FeedCard/index.js').then((m) => m.FeedCard),
    'StatCard': () => import('../social/StatCard/index.js').then((m) => m.StatCard),
    'Timeline': () => import('../social/Timeline/index.js').then((m) => m.Timeline),

    // Editor
    'WebTextEditor': () => import('../editor/WebTextEditor/index.js').then((m) => m.WebTextEditor),

    // Data
    'RegionMap': () => import('../data/RegionMap/index.js').then((m) => m.RegionMap),

    // Analytics
    'DataExplorer': () => import('../analytics/DataExplorer.js').then((m) => m.DataExplorer),

    // Foundation atoms (common)
    'Text': () => import('../common/Text/index.js').then((m) => m.Text),
    'Heading': () => import('../common/Heading/index.js').then((m) => m.Heading),
    'Link': () => import('../common/Link/index.js').then((m) => m.Link),
    'Skeleton': () => import('../common/Skeleton/index.js').then((m) => m.Skeleton),
    'MediaPlayer': () => import('../common/MediaPlayer/index.js').then((m) => m.MediaPlayer),
    'CodeBlock': () => import('../common/CodeBlock/index.js').then((m) => m.CodeBlock),
    'Alert': () => import('../common/Alert/index.js').then((m) => m.Alert),
    'EmptyState': () => import('../common/EmptyState/index.js').then((m) => m.EmptyState),

    // Retrieval / list composites (common)
    'ResultList': () => import('../common/ResultList/index.js').then((m) => m.ResultList),
    'List': () => import('../common/List/index.js').then((m) => m.List),
    'DescriptionList': () => import('../common/DescriptionList/index.js').then((m) => m.DescriptionList),
    'FilterBar': () => import('../common/FilterBar/index.js').then((m) => m.FilterBar),
    'StatGrid': () => import('../common/StatGrid/index.js').then((m) => m.StatGrid),
    'CardGrid': () => import('../common/CardGrid/index.js').then((m) => m.CardGrid),
    'StepIndicator': () => import('../common/StepIndicator/index.js').then((m) => m.StepIndicator),
    'DropdownMenu': () => import('../common/DropdownMenu/index.js').then((m) => m.DropdownMenu),

    // Sections
    'PageHeader': () => import('../sections/PageHeader/index.js').then((m) => m.PageHeader),
    'PageFooter': () => import('../sections/PageFooter/index.js').then((m) => m.PageFooter),
    'BannerSection': () => import('../sections/BannerSection/index.js').then((m) => m.BannerSection),
    'ContentSection': () => import('../sections/ContentSection/index.js').then((m) => m.ContentSection),
}));

// 一旦 fallback 匯入過 eager ComponentFactory，之後名稱解析優先讀其「即時」registry，
// 讓 ComponentFactory.register()/uninstall 的變動對 tool 頁面保持可見（與舊預設 factory 一致）
let eagerFactory = null;

export class LazyComponentFactory {
    /** 已載入類別快取:元件名稱 -> Class（僅存 loader 載入的內建元件） */
    static cache = new Map();
    /** 明示 register() 的覆寫，優先權最高 */
    static overrides = new Map();

    /**
     * 判斷名稱是否可解析(已覆寫、可即時解析、已快取或可延遲載入);
     * 供 DynamicToolRenderer 等呼叫端在 preload 前做存在性檢查。
     * @param {string} name
     * @returns {boolean}
     */
    static has(name) {
        if (this.overrides.has(name)) return true;
        if (eagerFactory && Object.prototype.hasOwnProperty.call(eagerFactory.registry, name)) return true;
        return this.cache.has(name) || loaders.has(name);
    }

    /**
     * 預先載入指定元件並填入快取;之後 create()/getComponentClass() 皆為同步。
     * 非延遲註冊表內的名稱改由 eager ComponentFactory 的即時 registry 解析
     * (支援外部 ComponentFactory.register() 的既有用法);解析結果不快取，
     * 以免 registry 之後的移除（如 CustomComponentRegistry.dispose）被舊快取遮蔽。
     * @param {string[]} names
     */
    static async preload(names) {
        const unique = [...new Set(Array.isArray(names) ? names : [])];
        const tasks = [];
        let needEager = false;
        for (const name of unique) {
            if (typeof name !== 'string' || this.overrides.has(name) || this.cache.has(name)) continue;
            if (eagerFactory && Object.prototype.hasOwnProperty.call(eagerFactory.registry, name)) continue;
            const loader = loaders.get(name);
            if (loader) {
                tasks.push(loader().then((componentClass) => {
                    if (componentClass) this.cache.set(name, componentClass);
                }));
            } else {
                needEager = true;
            }
        }
        if (needEager && !eagerFactory) {
            ({ ComponentFactory: eagerFactory } = await import('./ComponentFactory.js'));
        }
        await Promise.all(tasks);
    }

    /**
     * 根據元件名稱取得類別(覆寫 > eager registry 即時值 > 快取;未載入請先 preload)
     * @param {string} name
     * @returns {Class}
     */
    static getComponentClass(name) {
        const componentClass = this.overrides.get(name)
            || (eagerFactory && Object.prototype.hasOwnProperty.call(eagerFactory.registry, name)
                ? eagerFactory.registry[name]
                : null)
            || this.cache.get(name);
        if (!componentClass) {
            console.warn(`[ComponentFactory] Component "${name}" not found in registry.`);
            return null;
        }
        return componentClass;
    }

    /**
     * 建立元件實例
     * @param {string} name - 元件名稱
     * @param {Object} options - 建構函式選項
     * @returns {Object} 元件實例
     */
    static create(name, options = {}) {
        const ComponentClass = this.getComponentClass(name);
        if (!ComponentClass) return null;

        try {
            return new ComponentClass(options);
        } catch (e) {
            console.error(`[ComponentFactory] Failed to instantiate "${name}":`, e);
            return null;
        }
    }

    /**
     * 註冊新元件(同步寫入覆寫表，優先權最高)
     * @param {string} name
     * @param {Class} componentClass
     */
    static register(name, componentClass) {
        this.overrides.set(name, componentClass);
    }
}
