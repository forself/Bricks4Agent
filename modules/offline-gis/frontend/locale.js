import Locale from '../../../packages/javascript/browser/ui_components/i18n/index.js';
Locale.register('zh-TW', 'offlineGis', {
    search: '查詢本地地址', empty: '沒有候選地址', marker: '新增標記', pan: '平移', circle: '圓形範圍（點中心及邊界）',
    polygon: '多邊形範圍（雙擊完成）', clear: '清除範圍', density: '密度格網（非統計顯著性熱點）',
    play: '播放／暫停', time: '時間上限', from: '時間下限', zoomIn: '放大', zoomOut: '縮小', map: '離線 GIS 地圖',
    missing: '部分本地圖磚缺少或無權限', ready: '就緒', error: '操作失敗', count: '範圍內筆數',
    label: '標記名稱', fixture: 'FIXTURE：合成測試圖資與地址，非完整臺灣資料', attribution: '© OpenStreetMap contributors · ODbL（使用 OSM 資料時）',
});
Locale.register('en', 'offlineGis', {
    search: 'Search local addresses', empty: 'No address candidates', marker: 'Add marker', pan: 'Pan', circle: 'Circle (center then edge)',
    polygon: 'Polygon (double click to finish)', clear: 'Clear selection', density: 'Density grid (not statistical hotspots)',
    play: 'Play / pause', time: 'Time upper bound', from: 'Time lower bound', zoomIn: 'Zoom in', zoomOut: 'Zoom out', map: 'Offline GIS map',
    missing: 'Some local tiles are missing or unauthorized', ready: 'Ready', error: 'Operation failed', count: 'Points in selection',
    label: 'Marker label', fixture: 'FIXTURE: synthetic map and addresses, not complete Taiwan data', attribution: '© OpenStreetMap contributors · ODbL (when using OSM data)',
});
export const t = key => Locale.t(`offlineGis.${key}`);
