import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { BarChart } from '../../ui_components/viz/BarChart.js';
import { LineChart } from '../../ui_components/viz/LineChart.js';
import { PieChart } from '../../ui_components/viz/PieChart.js';
import { OrgChart } from '../../ui_components/viz/OrgChart.js';
import { RelationChart } from '../../ui_components/viz/RelationChart.js';
import { CanvasMap } from '../../ui_components/viz/CanvasMap.js';
import { MapEditor } from '../../ui_components/viz/MapEditor.js';
import { MapEditorV2 } from '../../ui_components/viz/MapEditorV2.js';
import { WebPainter } from '../../ui_components/viz/WebPainter/index.js';
import { DrawingBoard } from '../../ui_components/viz/DrawingBoard/index.js';
import { OSMMapEditor } from '../../ui_components/viz/OSMMapEditor/index.js';
import { LeafletMap } from '../../ui_components/viz/LeafletMap.js';
import { WebTextEditor } from '../../ui_components/editor/WebTextEditor/index.js';
import { resetUid } from '../../ui_components/utils/uid.js';

let container;
beforeEach(() => {
    // jsdom lacks ResizeObserver (BaseChart depends on it); stub it.
    globalThis.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
    container = document.createElement('div');
    document.body.appendChild(container);
    resetUid();
});
afterEach(() => { container.remove(); });

describe('viz SVG 圖表在 jsdom 可建構', () => {
    const svgCharts = { BarChart, LineChart, PieChart };
    for (const [name, Cls] of Object.entries(svgCharts)) {
        it(`${name} 建構並掛上 <svg>`, () => {
            const instance = new Cls({ container });
            expect(instance).toBeTruthy();
            expect(container.querySelector('svg')).not.toBeNull();
        });
    }

    it('OrgChart / RelationChart 建構不丟例外', () => {
        expect(() => new OrgChart({ container })).not.toThrow();
        expect(() => new RelationChart({ container })).not.toThrow();
    });
});

describe('viz/editor/map 模組面可載入(canvas/Leaflet 需真實瀏覽器)', () => {
    it('canvas + map + editor class 皆為可建構型別', () => {
        for (const Cls of [CanvasMap, MapEditor, MapEditorV2, WebPainter, DrawingBoard, OSMMapEditor, LeafletMap, WebTextEditor]) {
            expect(typeof Cls).toBe('function');
        }
    });
});

describe('WebTextEditor', () => {
    it('instanceId 為決定性(無 random),且可注入', () => {
        resetUid();
        const ed = new WebTextEditor({ container });
        expect(ed.instanceId).toBe('wte-1');
        const injected = new WebTextEditor({ container, id: 'wte-fixed' });
        expect(injected.instanceId).toBe('wte-fixed');
    });
});
