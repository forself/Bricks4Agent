/**
 * ComponentFactory
 * 負責維護元件註冊表並提供實例化方法。
 */

// 1. Viz Components
import {
    BarChart,
    BaseChart,
    CanvasMap,
    FlameChart,
    HierarchyChart,
    LeafletMap,
    LineChart,
    MapEditor,
    MapEditorV2,
    OrgChart,
    PieChart,
    RelationChart,
    RoseChart,
    SankeyChart,
    SunburstChart,
    TimelineChart,
    OSMMapEditor,
    DrawingBoard,
    WebPainter
} from '../viz/index.js';

// 2. Layout Components
import {
    DataTable,
    DocumentWall,
    FormRow,
    FunctionMenu,
    InfoPanel,
    PanelManager,
    PhotoWall,
    SideMenu,
    TabContainer,
    WorkflowPanel
} from '../layout/index.js';

// 3. Form Components
import {
    BatchUploader,
    Checkbox,
    DatePicker,
    Dropdown,
    FormField,
    MultiSelectDropdown,
    NumberInput,
    Radio,
    SearchForm,
    TextInput,
    TimePicker,
    ToggleSwitch
} from '../form/index.js';

// 4. Input Components (Advanced)
import {
    AddressInput,
    AddressListInput,
    ChainedInput,
    DateTimeInput,
    ListInput,
    OrganizationInput,
    PersonInfoList,
    PhoneListInput,
    SocialMediaList,
    StudentInput
} from '../input/index.js';

// 5. Common Components
import {
    ActionButton,
    AuthButton,
    Badge,
    BasicButton,
    Breadcrumb,
    ButtonGroup,
    ColorPicker,
    SimpleDialog,
    Divider,
    DownloadButton,
    EditorButton,
    FeatureCard,
    ImageViewer,
    LoadingSpinner,
    Notification,
    Pagination,
    PhotoCard,
    Progress,
    SortButton,
    Tag,
    Tooltip,
    TreeList,
    UploadButton
} from '../common/index.js';

// 6. Social Components
import {
    Avatar,
    ConnectionCard,
    FeedCard,
    StatCard,
    Timeline
} from '../social/index.js';

// 7. Editor Components
import { WebTextEditor } from '../editor/index.js';

// 8. Data Components
import { RegionMap } from '../data/index.js';

export class ComponentFactory {
    static registry = {
        // Viz
        'BarChart': BarChart,
        'BaseChart': BaseChart,
        'CanvasMap': CanvasMap,
        'FlameChart': FlameChart,
        'HierarchyChart': HierarchyChart,
        'LeafletMap': LeafletMap,
        'LineChart': LineChart,
        'MapEditor': MapEditor,
        'MapEditorV2': MapEditorV2,
        'OrgChart': OrgChart,
        'PieChart': PieChart,
        'RelationChart': RelationChart,
        'RoseChart': RoseChart,
        'SankeyChart': SankeyChart,
        'SunburstChart': SunburstChart,
        'TimelineChart': TimelineChart,
        'OSMMapEditor': OSMMapEditor,
        'DrawingBoard': DrawingBoard,
        'WebPainter': WebPainter,

        // Layout
        'DataTable': DataTable,
        'DocumentWall': DocumentWall,
        'FormRow': FormRow,
        'FunctionMenu': FunctionMenu,
        'InfoPanel': InfoPanel,
        'PanelManager': PanelManager,
        'PhotoWall': PhotoWall,
        'SideMenu': SideMenu,
        'TabContainer': TabContainer,
        'WorkflowPanel': WorkflowPanel,

        // Form
        'BatchUploader': BatchUploader,
        'Checkbox': Checkbox,
        'DatePicker': DatePicker,
        'Dropdown': Dropdown,
        'FormField': FormField,
        'MultiSelectDropdown': MultiSelectDropdown,
        'NumberInput': NumberInput,
        'Radio': Radio,
        'SearchForm': SearchForm,
        'TextInput': TextInput,
        'TimePicker': TimePicker,
        'ToggleSwitch': ToggleSwitch,

        // Input
        'AddressInput': AddressInput,
        'AddressListInput': AddressListInput,
        'ChainedInput': ChainedInput,
        'DateTimeInput': DateTimeInput,
        'ListInput': ListInput,
        'OrganizationInput': OrganizationInput,
        'PersonInfoList': PersonInfoList,
        'PhoneListInput': PhoneListInput,
        'SocialMediaList': SocialMediaList,
        'StudentInput': StudentInput,

        // Common
        'ActionButton': ActionButton,
        'AuthButton': AuthButton,
        'Badge': Badge,
        'BasicButton': BasicButton,
        'Breadcrumb': Breadcrumb,
        'ButtonGroup': ButtonGroup,
        'ColorPicker': ColorPicker,
        'SimpleDialog': SimpleDialog,
        'Divider': Divider,
        'DownloadButton': DownloadButton,
        'EditorButton': EditorButton,
        'FeatureCard': FeatureCard,
        'ImageViewer': ImageViewer,
        'LoadingSpinner': LoadingSpinner,
        'Notification': Notification,
        'Pagination': Pagination,
        'PhotoCard': PhotoCard,
        'Progress': Progress,
        'SortButton': SortButton,
        'Tag': Tag,
        'Tooltip': Tooltip,
        'TreeList': TreeList,
        'UploadButton': UploadButton,

        // Social
        'Avatar': Avatar,
        'ConnectionCard': ConnectionCard,
        'FeedCard': FeedCard,
        'StatCard': StatCard,
        'Timeline': Timeline,

        // Editor
        'WebTextEditor': WebTextEditor,

        // Data
        'RegionMap': RegionMap
    };

    /**
     * 根據元件名稱取得類別
     * @param {string} name 
     * @returns {Class}
     */
    static getComponentClass(name) {
        const componentClass = this.registry[name];
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
     * 註冊新元件
     * @param {string} name 
     * @param {Class} componentClass 
     */
    static register(name, componentClass) {
        this.registry[name] = componentClass;
    }
}
