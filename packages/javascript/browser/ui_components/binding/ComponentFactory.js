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
import { LineChart } from '../viz/LineChart.js';
import { OrgChart } from '../viz/OrgChart.js';
import { PieChart } from '../viz/PieChart.js';
import { RelationChart } from '../viz/RelationChart.js';
import { RoseChart } from '../viz/RoseChart.js';
import { SankeyChart } from '../viz/SankeyChart.js';
import { SunburstChart } from '../viz/SunburstChart.js';
import { TimelineChart } from '../viz/TimelineChart.js';
import { OSMMapEditor } from '../viz/OSMMapEditor/OSMMapEditor.js';

// 2. Layout Components
import { DocumentWall } from '../layout/DocumentWall/DocumentWall.js';
import { PanelManager } from '../layout/Panel/PanelManager.js';
import { PhotoWall } from '../layout/PhotoWall/PhotoWall.js';
import { WorkflowPanel } from '../layout/WorkflowPanel/WorkflowPanel.js';

// 3. Form Components
import { Checkbox } from '../form/Checkbox/Checkbox.js';
import { DatePicker } from '../form/DatePicker/DatePicker.js';
import { Dropdown } from '../form/Dropdown/Dropdown.js';
import { NumberInput } from '../form/NumberInput/NumberInput.js';
import { Radio } from '../form/Radio/Radio.js';
import { TextInput } from '../form/TextInput/TextInput.js';
import { TimePicker } from '../form/TimePicker/TimePicker.js';

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
// BasicButton assumes a default export or named export, checking inventory
import { BasicButton } from '../common/BasicButton/BasicButton.js';
import { DownloadButton } from '../common/DownloadButton/DownloadButton.js';
import { ImageViewer } from '../common/ImageViewer/ImageViewer.js';
import { PhotoCard } from '../common/PhotoCard/PhotoCard.js';
import { SortButton } from '../common/SortButton/SortButton.js';
import { TreeList } from '../common/TreeList/TreeList.js';
import { UploadButton } from '../common/UploadButton/UploadButton.js';

// 6. Data Components
import { RegionMap } from '../data/RegionMap/RegionMap.js';

export class ComponentFactory {
    static registry = {
        // Viz
        'BarChart': BarChart,
        'BaseChart': BaseChart,
        'CanvasMap': CanvasMap,
        'FlameChart': FlameChart,
        'HierarchyChart': HierarchyChart,
        'LineChart': LineChart,
        'OrgChart': OrgChart,
        'PieChart': PieChart,
        'RelationChart': RelationChart,
        'RoseChart': RoseChart,
        'SankeyChart': SankeyChart,
        'SunburstChart': SunburstChart,
        'TimelineChart': TimelineChart,
        'OSMMapEditor': OSMMapEditor,

        // Layout
        'DocumentWall': DocumentWall,
        'PanelManager': PanelManager,
        'PhotoWall': PhotoWall,
        'WorkflowPanel': WorkflowPanel,

        // Form
        'Checkbox': Checkbox,
        'DatePicker': DatePicker,
        'Dropdown': Dropdown,
        'NumberInput': NumberInput,
        'Radio': Radio,
        'TextInput': TextInput,
        'TimePicker': TimePicker,

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
        'BasicButton': BasicButton,
        'DownloadButton': DownloadButton,
        'ImageViewer': ImageViewer,
        'PhotoCard': PhotoCard,
        'SortButton': SortButton,
        'TreeList': TreeList,
        'UploadButton': UploadButton,
        
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
