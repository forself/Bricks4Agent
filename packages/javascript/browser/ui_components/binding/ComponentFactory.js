/**
 * ComponentFactory
 * 負責維護元件註冊表並提供實例化方法。
 */

// 1. Viz Components
import { BarChart } from '../viz/BarChart.js';
import { BaseChart } from '../viz/BaseChart.js';
import { CanvasMap } from '../viz/CanvasMap.js';
import { FlameChart } from '../viz/FlameChart.js';
import { HierarchyChart } from '../viz/HierarchyChart.js';
import { LeafletMap } from '../viz/LeafletMap.js';
import { LineChart } from '../viz/LineChart.js';
import { MapEditor } from '../viz/MapEditor.js';
import { MapEditorV2 } from '../viz/MapEditorV2.js';
import { OrgChart } from '../viz/OrgChart.js';
import { PieChart } from '../viz/PieChart.js';
import { RelationChart } from '../viz/RelationChart.js';
import { RoseChart } from '../viz/RoseChart.js';
import { SankeyChart } from '../viz/SankeyChart.js';
import { SunburstChart } from '../viz/SunburstChart.js';
import { TimelineChart } from '../viz/TimelineChart.js';
import { OSMMapEditor } from '../viz/OSMMapEditor/OSMMapEditor.js';
import { DrawingBoard } from '../viz/DrawingBoard/DrawingBoard.js';
import { WebPainter } from '../viz/WebPainter/WebPainter.js';

// 2. Layout Components
import { DataTable } from '../layout/DataTable/DataTable.js';
import { DocumentWall } from '../layout/DocumentWall/DocumentWall.js';
import { FormRow } from '../layout/FormRow/FormRow.js';
import { FunctionMenu } from '../layout/FunctionMenu/FunctionMenu.js';
import { InfoPanel } from '../layout/InfoPanel/InfoPanel.js';
import { PanelManager } from '../layout/Panel/PanelManager.js';
import { PhotoWall } from '../layout/PhotoWall/PhotoWall.js';
import { SideMenu } from '../layout/SideMenu/SideMenu.js';
import { TabContainer } from '../layout/TabContainer/TabContainer.js';
import { WorkflowPanel } from '../layout/WorkflowPanel/WorkflowPanel.js';

// 3. Form Components
import { BatchUploader } from '../form/BatchUploader/BatchUploader.js';
import { Checkbox } from '../form/Checkbox/Checkbox.js';
import { DatePicker } from '../form/DatePicker/DatePicker.js';
import { Dropdown } from '../form/Dropdown/Dropdown.js';
import { FormField } from '../form/FormField/FormField.js';
import { MultiSelectDropdown } from '../form/MultiSelectDropdown/MultiSelectDropdown.js';
import { NumberInput } from '../form/NumberInput/NumberInput.js';
import { Radio } from '../form/Radio/Radio.js';
import { SearchForm } from '../form/SearchForm/SearchForm.js';
import { TextInput } from '../form/TextInput/TextInput.js';
import { TimePicker } from '../form/TimePicker/TimePicker.js';
import { ToggleSwitch } from '../form/ToggleSwitch/ToggleSwitch.js';

// 4. Input Components (Advanced)
import { AddressInput } from '../input/AddressInput/AddressInput.js';
import { AddressListInput } from '../input/AddressListInput/AddressListInput.js';
import { ChainedInput } from '../input/ChainedInput/ChainedInput.js';
import { DateTimeInput } from '../input/DateTimeInput/DateTimeInput.js';
import { ListInput } from '../input/ListInput/ListInput.js';
import { OrganizationInput } from '../input/OrganizationInput/OrganizationInput.js';
import { PersonInfoList } from '../input/PersonInfoList/PersonInfoList.js';
import { PhoneListInput } from '../input/PhoneListInput/PhoneListInput.js';
import { SocialMediaList } from '../input/SocialMediaList/SocialMediaList.js';
import { StudentInput } from '../input/StudentInput/StudentInput.js';

// 5. Common Components
import { ActionButton } from '../common/ActionButton/ActionButton.js';
import { AuthButton } from '../common/AuthButton/AuthButton.js';
import { Badge } from '../common/Badge/Badge.js';
import { BasicButton } from '../common/BasicButton/BasicButton.js';
import { Breadcrumb } from '../common/Breadcrumb/Breadcrumb.js';
import { ButtonGroup } from '../common/ButtonGroup/ButtonGroup.js';
import { ColorPicker } from '../common/ColorPicker/ColorPicker.js';
import { SimpleDialog } from '../common/Dialog/SimpleDialog.js';
import { Divider } from '../common/Divider/Divider.js';
import { DownloadButton } from '../common/DownloadButton/DownloadButton.js';
import { EditorButton } from '../common/EditorButton/EditorButton.js';
import { FeatureCard } from '../common/FeatureCard/FeatureCard.js';
import { ImageViewer } from '../common/ImageViewer/ImageViewer.js';
import { LoadingSpinner } from '../common/LoadingSpinner/LoadingSpinner.js';
import { Notification } from '../common/Notification/Notification.js';
import { Pagination } from '../common/Pagination/Pagination.js';
import { PhotoCard } from '../common/PhotoCard/PhotoCard.js';
import { Progress } from '../common/Progress/Progress.js';
import { SortButton } from '../common/SortButton/SortButton.js';
import { Tag } from '../common/Tag/Tag.js';
import { Tooltip } from '../common/Tooltip/Tooltip.js';
import { TreeList } from '../common/TreeList/TreeList.js';
import { UploadButton } from '../common/UploadButton/UploadButton.js';

// 6. Social Components
import { Avatar } from '../social/Avatar/Avatar.js';
import { ConnectionCard } from '../social/ConnectionCard/ConnectionCard.js';
import { FeedCard } from '../social/FeedCard/FeedCard.js';
import { StatCard } from '../social/StatCard/StatCard.js';
import { Timeline } from '../social/Timeline/Timeline.js';

// 7. Editor Components
import { WebTextEditor } from '../editor/WebTextEditor/WebTextEditor.js';

// 8. Data Components
import { RegionMap } from '../data/RegionMap/RegionMap.js';

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
