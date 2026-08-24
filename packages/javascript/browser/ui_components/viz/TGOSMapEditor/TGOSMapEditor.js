/**
 * TGOSMapEditor - TGOS(臺灣通用電子地圖)地圖編輯器
 * 與 OSMMapEditor 能力完全相同(繪圖/標註/測量/GeoJSON/地圖截圖→底圖),
 * 唯一差別是「地圖來源」:預設臺灣通用電子地圖(內政部國土測繪中心 WMTS,
 * 即 TGOS 平臺提供的同源底圖),對應舊系統 MapEditor 未完成的 TGOS 整合。
 *
 * 圖磚來源:
 * - emap  :臺灣通用電子地圖(EMAP)
 * - photo :臺灣通用正射影像(PHOTO2)
 * 政網部署若需走 TGOS MAP API(需 appid/key),依資安決策「TGOS 後端轉發」:
 * 以 options.tileLayers 指向後端代理端點即可,例如
 *   new TGOSMapEditor({ tileLayers: { tgos: { url: '/api/tgos/wmts/{z}/{y}/{x}', name: 'TGOS(代理)' } }, tileLayer: 'tgos' })
 * 金鑰只存在後端,前端零外洩。
 */

import { OSMMapEditor } from '../OSMMapEditor/index.js';

export class TGOSMapEditor extends OSMMapEditor {

    /** 臺灣官方圖磚源(取代 OSM 系列) */
    static TILE_LAYERS = {
        emap: {
            url: 'https://wmts.nlsc.gov.tw/wmts/EMAP/default/GoogleMapsCompatible/{z}/{y}/{x}',
            attribution: '© <a href="https://maps.nlsc.gov.tw/" target="_blank" rel="noopener noreferrer">內政部國土測繪中心</a>',
            name: '臺灣通用電子地圖',
            maxZoom: 19
        },
        photo: {
            url: 'https://wmts.nlsc.gov.tw/wmts/PHOTO2/default/GoogleMapsCompatible/{z}/{y}/{x}',
            attribution: '© <a href="https://maps.nlsc.gov.tw/" target="_blank" rel="noopener noreferrer">內政部國土測繪中心</a>',
            name: '通用正射影像',
            maxZoom: 19
        }
    };

    static DEFAULT_TILE_KEY = 'emap';
    static MAP_TITLE = 'TGOS 臺灣通用電子地圖';
    static LEAFLET_BASE_LAYER = 'nlsc';   // LeafletMap 初始底圖即 NLSC,離線政網不會先打 OSM
}

export default TGOSMapEditor;
