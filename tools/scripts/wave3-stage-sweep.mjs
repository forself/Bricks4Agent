// Wave 3 SVG→Canvas migration acceptance sweep (real Edge).
// Prerequisite: serve repository root at http://127.0.0.1:8124.
import pkg from '../../../tim-web/poc/node_modules/playwright-core/index.js';
const { chromium } = pkg;

const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ viewport: { width: 1200, height: 900 }, deviceScaleFactor: 1.25 });
const browserErrors = [];
page.on('pageerror', error => browserErrors.push(`pageerror: ${error.message}`));
page.on('console', message => {
    if (message.type() === 'error') browserErrors.push(`console: ${message.text()}`);
});

const results = [];
const check = (name, pass, detail = '') => results.push({ name, pass: Boolean(pass), detail });

try {
    await page.goto('http://127.0.0.1:8124/tools/theme-studio/index.html');
    await page.waitForFunction(
        globalName => window[globalName] === true,
        '__studioReady',
        { timeout: 15000 }
    );

    const snapshot = await page.evaluate(async () => {
        const [iconModule, actionModule, authModule, basicModule, downloadModule, editorModule, sortModule, uploadModule, panelModule, documentWallModule, drawingModule, painterModule, osmModule, webTextEditorModule] = await Promise.all([
            import('/packages/javascript/browser/ui_components/common/Icon/Icon.js'),
            import('/packages/javascript/browser/ui_components/common/ActionButton/ActionButton.js'),
            import('/packages/javascript/browser/ui_components/common/AuthButton/AuthButton.js'),
            import('/packages/javascript/browser/ui_components/common/BasicButton/BasicButton.js'),
            import('/packages/javascript/browser/ui_components/common/DownloadButton/DownloadButton.js'),
            import('/packages/javascript/browser/ui_components/common/EditorButton/EditorButton.js'),
            import('/packages/javascript/browser/ui_components/common/SortButton/SortButton.js'),
            import('/packages/javascript/browser/ui_components/common/UploadButton/UploadButton.js'),
            import('/packages/javascript/browser/ui_components/layout/Panel/BasePanel.js'),
            import('/packages/javascript/browser/ui_components/layout/DocumentWall/DocumentWall.js'),
            import('/packages/javascript/browser/ui_components/viz/DrawingBoard/DrawingBoard.js'),
            import('/packages/javascript/browser/ui_components/viz/WebPainter/WebPainter.js'),
            import('/packages/javascript/browser/ui_components/viz/OSMMapEditor/OSMMapEditor.js'),
            import('/packages/javascript/browser/ui_components/editor/WebTextEditor/WebTextEditor.js')
        ]);
        const { Icon } = iconModule;
        const { ActionButton } = actionModule;
        const { AuthButton } = authModule;
        const { BasicButton } = basicModule;
        const { DownloadButton } = downloadModule;
        const { EditorButton } = editorModule;
        const { SortButton } = sortModule;
        const { UploadButton } = uploadModule;
        const { BasePanel } = panelModule;
        const { DocumentWall } = documentWallModule;
        const { DrawingBoard } = drawingModule;
        const { WebPainter } = painterModule;
        const { OSMMapEditor } = osmModule;
        const { WebTextEditor } = webTextEditorModule;

        const trackedIcons = [];
        const originalIconMount = Icon.prototype.mount;
        Icon.prototype.mount = function (...args) {
            trackedIcons.push(this);
            return originalIconMount.apply(this, args);
        };

        const host = document.createElement('section');
        host.id = 'wave3-sweep';
        host.style.cssText = 'position:absolute;left:0;top:0;width:1000px;padding:12px;color:rgb(30,40,50);background:var(--cl-bg);z-index:99999';
        document.body.appendChild(host);
        const owned = [];
        const mount = component => {
            component.mount(host);
            owned.push(component);
            return component;
        };
        const signature = canvas => {
            const data = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
            let hash = 2166136261;
            let alpha = 0;
            for (let i = 0; i < data.length; i += 4) {
                alpha += data[i + 3];
                hash ^= data[i + 3] ? (data[i] + data[i + 1] * 3 + data[i + 2] * 7 + i) : 0;
                hash = Math.imul(hash, 16777619);
            }
            return `${hash >>> 0}:${alpha}`;
        };

        const registry = Icon.names().map(name => mount(new Icon({ name, size: 24 })));
        const registryNonempty = registry.every(icon => {
            const canvas = icon.element.querySelector('canvas');
            return signature(canvas).split(':')[1] !== '0';
        });

        const editorEntries = Object.entries(EditorButton.CONFIG).filter(([, config]) => config.icon || config.iconGlyph);
        const invalidEditorNames = editorEntries
            .filter(([, config]) => config.icon && !Icon.has(config.icon))
            .map(([type, config]) => `${type}:${config.icon}`);
        const editorButtons = editorEntries.map(([type]) => mount(new EditorButton({ type, iconOnly: true })));

        const customName = mount(new EditorButton({ type: 'custom', label: 'Flag', iconName: 'flag', iconOnly: true }));
        const customPath = mount(new EditorButton({
            type: 'custom',
            label: 'Diamond',
            iconPath: 'M12 2l10 10-10 10L2 12z',
            iconOnly: true
        }));
        const legacyMarkup = mount(new EditorButton({
            type: 'custom',
            label: 'Rejected',
            icon: '<svg><path></path></svg>',
            iconOnly: true
        }));

        const basicTypes = ['delete', 'yes', 'no', 'done', 'clear', 'apply', 'copy', 'addRow', 'selectAll', 'deselectAll', 'prev', 'expandAll', 'collapseAll'];
        const basicButtons = basicTypes.map(type => mount(new BasicButton({ type })));
        const basicNames = basicButtons.map(button => button.element.querySelector('.cl-icon')?.className || '');

        const actionAdd = mount(new ActionButton({ type: 'add' }));
        const actionDelete = mount(new ActionButton({ type: 'delete' }));

        const sort = mount(new SortButton({ state: 'none' }));
        const sortInitialAsc = mount(new SortButton({ state: 'asc' }));
        const sortSignatures = [];
        const sortAria = [];
        for (const state of ['none', 'desc', 'asc']) {
            sort.setState(state);
            sortSignatures.push(signature(sort.element.querySelector('canvas')));
            sortAria.push([sort.element.getAttribute('aria-label'), sort.element.getAttribute('aria-sort')]);
        }

        const downloads = Object.keys(DownloadButton.ICONS).map(type => mount(new DownloadButton({ type })));
        const uploads = Object.keys(UploadButton.ICONS).map(type => mount(new UploadButton({ type })));

        const groups = [
            ActionButton.createGroup([{ type: 'add' }]),
            AuthButton.createAuthGroup({ isLoggedIn: false }),
            BasicButton.createGroup([{ type: 'confirm' }]),
            DownloadButton.createGroup([{ type: 'pdf' }]),
            UploadButton.createGroup([{ type: 'file' }])
        ];
        groups.forEach(group => host.appendChild(group));
        const factoryBefore = groups.map(group => ({
            element: group instanceof HTMLElement,
            count: group._components?.length,
            enumerable: Object.keys(group).includes('_components'),
            destroy: typeof group.destroy
        }));
        groups.forEach(group => { group.destroy(); group.destroy(); });
        const factoryAfter = groups.map(group => ({ connected: group.isConnected, count: group._components?.length }));

        const lifecycleResult = {};
        const lifecycleContainer = () => {
            const container = document.createElement('div');
            host.appendChild(container);
            return container;
        };
        const aliveSince = start => trackedIcons.slice(start).filter(icon => typeof icon._offTheme === 'function').length;

        let start = trackedIcons.length;
        const drawingHost = lifecycleContainer();
        const drawing = new DrawingBoard({ container: drawingHost, width: 240, height: 160 });
        drawing.destroy();
        lifecycleResult.drawing = { alive: aliveSince(start), connected: drawing.element?.isConnected };
        drawingHost.remove();

        start = trackedIcons.length;
        const painterHost = lifecycleContainer();
        const painter = new WebPainter({ container: painterHost, width: 240, height: 160 });
        painter.destroy();
        lifecycleResult.painter = { alive: aliveSince(start), connected: painter.element?.isConnected, keydown: painter._handleKeyDown };
        painterHost.remove();

        start = trackedIcons.length;
        const osmHost = lifecycleContainer();
        const osm = new OSMMapEditor({ container: osmHost, width: 240, height: 160 });
        const osmMapContainer = osm.mapContainer;
        osm.destroy();
        lifecycleResult.osm = { alive: aliveSince(start), connected: osm.element?.isConnected, mapConnected: osmMapContainer?.isConnected };
        osmHost.remove();

        const panel = new BasePanel({ title: 'Lifecycle', autoClose: true });
        panel.mount(host);
        panel.destroy();
        await new Promise(resolve => setTimeout(resolve, 10));
        lifecycleResult.panel = { timer: panel._outsideClickTimer, handler: panel._handleOutsideClick, connected: panel.element?.isConnected };

        start = trackedIcons.length;
        const documentWall = new DocumentWall();
        documentWall.mount(host);
        const documentWallCanvasBefore = documentWall.downloadBtn?.querySelectorAll('canvas').length;
        const documentWallLabelBefore = documentWall.downloadLabel?.textContent;
        documentWall.destroy();
        lifecycleResult.documentWall = {
            canvasBefore: documentWallCanvasBefore,
            labelBefore: documentWallLabelBefore,
            alive: aliveSince(start),
            connected: documentWall.element?.isConnected
        };

        start = trackedIcons.length;
        const webTextEditorHost = lifecycleContainer();
        const webTextEditor = new WebTextEditor({ container: webTextEditorHost, height: '240px' });
        webTextEditor.destroy();
        lifecycleResult.webTextEditor = {
            alive: aliveSince(start),
            globalListeners: webTextEditor._globalListeners.length,
            components: webTextEditor._componentInstances.size,
            children: webTextEditorHost.childElementCount
        };
        webTextEditorHost.remove();

        // Let Icon connection observers redraw components whose internal icon
        // mounted before the outer component entered the document.
        await new Promise(resolve => requestAnimationFrame(() => resolve()));

        const result = {
            registryCount: registry.length,
            registryNonempty,
            invalidEditorNames,
            editorCount: editorButtons.length,
            editorCanvasCount: editorButtons.filter(button => button.element.querySelector('canvas')).length,
            customName: customName.element.querySelector('.cl-icon--flag') !== null,
            customPathNonempty: signature(customPath.element.querySelector('canvas')).split(':')[1] !== '0',
            legacyMarkupFallback: legacyMarkup.element.querySelector('.cl-icon--help') !== null,
            basicHasFallback: basicNames.some(name => name.includes('cl-icon--help')),
            actionNames: [actionAdd._icon?.options.name, actionDelete._icon?.options.name],
            sortSignatures,
            sortAria,
            sortInitialAsc: [sortInitialAsc.element.getAttribute('aria-label'), sortInitialAsc.element.getAttribute('aria-sort')],
            downloadBadges: downloads.map(button => button.element.querySelector('.download-btn__format')?.textContent),
            downloadCanvas: downloads.every(button => button.element.querySelectorAll('canvas').length === 1),
            uploadBadges: uploads.map(button => button.element.querySelector('.upload-btn__format')?.textContent),
            uploadCanvas: uploads.every(button => button.element.querySelectorAll('canvas').length === 1),
            factoryBefore,
            factoryAfter,
            lifecycleResult,
            svgCount: host.querySelectorAll('svg').length
        };

        owned.forEach(component => component.destroy());
        result.remainingCanvases = host.querySelectorAll('canvas').length;
        host.remove();
        Icon.prototype.mount = originalIconMount;
        return result;
    });

    check('Icon registry 全量 Path2D 可繪製', snapshot.registryCount >= 90 && snapshot.registryNonempty, JSON.stringify(snapshot));
    check('EditorButton CONFIG 無未知 icon name', snapshot.invalidEditorNames.length === 0, snapshot.invalidEditorNames.join(', '));
    check('EditorButton 全量配置建立 Canvas', snapshot.editorCount > 50 && snapshot.editorCanvasCount === snapshot.editorCount, JSON.stringify(snapshot));
    check('自訂 iconName 契約', snapshot.customName, JSON.stringify(snapshot));
    check('自訂 iconPath 契約', snapshot.customPathNonempty, JSON.stringify(snapshot));
    check('舊 SVG markup 安全拒絕並 fallback', snapshot.legacyMarkupFallback, JSON.stringify(snapshot));
    check('BasicButton 13 個操作無 help 佔位', !snapshot.basicHasFallback, JSON.stringify(snapshot));
    check('ActionButton 新增/刪除語意正確', snapshot.actionNames.join(',') === 'add-circle,delete', JSON.stringify(snapshot.actionNames));
    check('SortButton 三態 glyph 可區分', new Set(snapshot.sortSignatures).size === 3, JSON.stringify(snapshot.sortSignatures));
    check('SortButton 三態 aria 可區分', new Set(snapshot.sortAria.map(row => row.join(':'))).size === 3, JSON.stringify(snapshot.sortAria));
    check('SortButton 非 none 初始 aria 正確', snapshot.sortInitialAsc.join(':') === '升冪排序:ascending', JSON.stringify(snapshot.sortInitialAsc));
    check('DownloadButton 七格式 badge + Canvas', snapshot.downloadBadges.length === 7 && snapshot.downloadBadges.every(Boolean) && snapshot.downloadCanvas, JSON.stringify(snapshot));
    check('UploadButton 八格式 badge + Canvas', snapshot.uploadBadges.length === 8 && snapshot.uploadBadges.every(Boolean) && snapshot.uploadCanvas, JSON.stringify(snapshot));
    check('五個 factory 保持 HTMLElement 相容 API', snapshot.factoryBefore.every(item => item.element && item.count === 1 && !item.enumerable && item.destroy === 'function'), JSON.stringify(snapshot.factoryBefore));
    check('五個 factory destroy 冪等且清空', snapshot.factoryAfter.every(item => !item.connected && item.count === 0), JSON.stringify(snapshot.factoryAfter));
    check('DrawingBoard destroy 清除所有 Icon 訂閱', snapshot.lifecycleResult.drawing.alive === 0 && !snapshot.lifecycleResult.drawing.connected, JSON.stringify(snapshot.lifecycleResult.drawing));
    check('WebPainter destroy 清除子元件與 keydown', snapshot.lifecycleResult.painter.alive === 0 && !snapshot.lifecycleResult.painter.connected && !snapshot.lifecycleResult.painter.keydown, JSON.stringify(snapshot.lifecycleResult.painter));
    check('OSMMapEditor destroy 清除繼承/地圖子元件', snapshot.lifecycleResult.osm.alive === 0 && !snapshot.lifecycleResult.osm.connected && !snapshot.lifecycleResult.osm.mapConnected, JSON.stringify(snapshot.lifecycleResult.osm));
    check('BasePanel immediate destroy 不殘留 autoClose timer', !snapshot.lifecycleResult.panel.timer && !snapshot.lifecycleResult.panel.handler && !snapshot.lifecycleResult.panel.connected, JSON.stringify(snapshot.lifecycleResult.panel));
    check('DocumentWall label 不覆蓋 Canvas 且 destroy 完整', snapshot.lifecycleResult.documentWall.canvasBefore === 1 && Boolean(snapshot.lifecycleResult.documentWall.labelBefore) && snapshot.lifecycleResult.documentWall.alive === 0 && !snapshot.lifecycleResult.documentWall.connected, JSON.stringify(snapshot.lifecycleResult.documentWall));
    check('WebTextEditor destroy 清除工具列與全域監聽', snapshot.lifecycleResult.webTextEditor.alive === 0 && snapshot.lifecycleResult.webTextEditor.globalListeners === 0 && snapshot.lifecycleResult.webTextEditor.components === 0 && snapshot.lifecycleResult.webTextEditor.children === 0, JSON.stringify(snapshot.lifecycleResult.webTextEditor));
    check('Wave 3 DOM 無 SVG', snapshot.svgCount === 0, `svgCount=${snapshot.svgCount}`);
    check('所有受測元件 destroy 後無 Canvas 殘留', snapshot.remainingCanvases === 0, `remaining=${snapshot.remainingCanvases}`);
    check('全程無瀏覽器錯誤', browserErrors.length === 0, browserErrors.join(' | '));
} catch (error) {
    check('Wave 3 harness 可完整執行', false, error.stack || error.message);
} finally {
    await browser.close();
}

let failed = 0;
for (const result of results) {
    if (!result.pass) failed++;
    console.log(`  ${result.pass ? 'ok ' : 'FAIL '} ${result.name}${result.pass ? '' : ` — ${result.detail}`}`);
}
console.log(`\n結果: ${results.length - failed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
