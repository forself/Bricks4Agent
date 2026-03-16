/**
 * WebTextEditor - 輕量級富文本編輯器
 * 基於 document.execCommand 與 contenteditable 實作
 *
 * 功能:
 * - 搜尋和取代 (Ctrl+F/Ctrl+H)
 * - 匯出功能 (PDF, Word, Markdown)
 * - 目錄自動生成
 * - 行間距控制
 * - 字數統計
 * - WebPainter 整合
 */

import { EditorButton } from '../../common/EditorButton/index.js';
import { ButtonGroup } from '../../common/ButtonGroup/index.js';
import { ColorPicker } from '../../common/ColorPicker/index.js';
import { UploadButton } from '../../common/UploadButton/index.js';
import { SimpleDialog } from '../../common/Dialog/index.js';
import { escapeHtml, sanitizeUrl } from '../../utils/security.js';
import { WebPainter } from '../../viz/WebPainter/index.js';
import { NumberInput } from '../../form/NumberInput/index.js';

import { ModalPanel } from '../../layout/Panel/index.js';
import Locale from '../../i18n/index.js';

export class WebTextEditor {
    constructor(options = {}) {
        this.container = typeof options.container === 'string'
            ? document.querySelector(options.container)
            : options.container;

        if (!this.container) throw new Error('WebTextEditor:Container not found');

        this.options = {
            placeholder: Locale.t('webTextEditor.placeholder'),
            height: '400px',
            content: '',
            readOnly: false,
            onChange: null,
            ...options
        };

        this.features = {
            ...options.features
        };

        // 生成唯一實例 ID，防止多編輯器 ID 衝突
        this.instanceId = 'wte-' + Math.random().toString(36).substr(2, 9);

        // Fixing Pattern State
        this.fixingId = this.instanceId + '-fixing';
        this.spanCounter = 0;

        // Overlay & Resizing State
        this.selectedObject = null; // Currently selected img/element
        this.overlay = null;
        this.isResizing = false;

        // 浮動工具列相關
        this.floatingToolbar = null;
        this.isFloatingToolbarVisible = false;

        // 搜尋取代相關狀態
        this.searchDialog = null;
        this.searchMatches = [];
        this.currentMatchIndex = -1;


        // 用於表格調整欄寬的狀態
        this._tableResizing = {
            isResizing: false,
            table: null,
            cell: null,
            startX: 0,
            startWidth: 0
        };

        // 歷史記錄 (Undo/Redo)
        this.history = [];
        this.historyIndex = -1;
        this.maxHistory = 50;

        // 全螢幕狀態
        this.isFullscreen = false;

        // 自動儲存冷卻
        this._autoSaveTimer = null;
        this._init();
    }

    _init() {
        this._createUI();
        this._setupEventListeners();
        if (this.options.content) {
            this.setContent(this.options.content);
        }
        // 初始記錄
        this._saveHistory();
        // 檢查是否有草稿
        this._checkDraft();
    }

    _createUI() {
        // 主容器樣式
        this.container.classList.add('web-text-editor');
        this.container.style.cssText = `
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-lg);
            overflow: hidden;
            display: flex;
            flex-direction: column;
            background: var(--cl-bg);
            font-family: var(--cl-font-family);
            height: ${this.options.height};
        `;

        // 1. 工具列
        this._createToolbar();

        // 2. 編輯區
        this.editor = document.createElement('div');
        this.editor.className = 'wte-content';
        this.editor.contentEditable = !this.options.readOnly;
        this.editor.style.cssText = `
            flex: 1;
            padding: 20px;
            overflow-y: auto;
            outline: none;
            line-height: 1.6;
            font-size: var(--cl-font-size-xl);
            position: relative;
        `;
        
        // Placeholder 實作 (CSS 偽元素較佳，但用 JS 控制 class 也可)
        if (!this.options.content) {
            this.editor.dataset.placeholder = this.options.placeholder;
        }

        // 注入基礎 CSS
        const style = document.createElement('style');
        style.textContent = `
            .wte-content:empty:before {
                content: attr(data-placeholder);
                color: var(--cl-text-placeholder);
                pointer-events: none;
            }
            .wte-content h1 { font-size: 2em; margin: 0.5em 0; border-bottom: 2px solid var(--cl-border-light); padding-bottom: 5px; }
            .wte-content h2 { font-size: 1.5em; margin: 0.5em 0; color: var(--cl-text); }
            .wte-content blockquote { border-left: 4px solid var(--cl-border); padding-left: 10px; color: var(--cl-text-secondary); margin: 10px 0; }
            .wte-content img { max-width: 100%; border-radius: var(--cl-radius-sm); box-shadow: var(--cl-shadow-md); }
            .wte-content ul, .wte-content ol { padding-left: 20px; }
            .wte-content a { color: var(--cl-primary); text-decoration: underline; }
            /* 表格样式 */
            .wte-content table { border-collapse: collapse; width: 100%; margin: 15px 0; }
            .wte-content table td, .wte-content table th { border: 1px solid var(--cl-border); padding: 8px; text-align: left; }
            .wte-content table th { background: var(--cl-bg-secondary); font-weight: bold; }
            .wte-content table tr:hover { background: var(--cl-bg-disabled); }
            /* 分页符样式 */
            .wte-page-break { 
                margin: 20px 0; padding: 10px; border-top: 2px dashed var(--cl-text-placeholder); 
                position: relative; text-align: center; color: var(--cl-text-placeholder); font-size: var(--cl-font-size-sm);
                page-break-after: always;
            }
            .wte-page-break::before { content: Locale.t('webTextEditor.pageBreakLine'); }
            
            /* 分章節顯示 */
            .wte-header, .wte-footer { 
                padding: 10px 20px; font-size: var(--cl-font-size-md); color: var(--cl-text-muted); border: 1px dashed var(--cl-border-light); 
                margin: 5px 0; background: var(--cl-bg-input); position: relative;
            }
            .wte-header::after { content: '" + Locale.t('webTextEditor.headerArea') + "'; position: absolute; right: 10px; top: 5px; font-size: var(--cl-font-size-2xs); opacity: 0.5; }
            .wte-footer::after { content: '" + Locale.t('webTextEditor.footerArea') + "'; position: absolute; right: 10px; bottom: 5px; font-size: var(--cl-font-size-2xs); opacity: 0.5; }
            
            .wte-page-number { font-weight: bold; color: var(--cl-text); }
            
            /* 全螢幕樣式 */
            .web-text-editor.fullscreen {
                position: fixed; top: 0; left: 0; width: 100vw !important; height: 100vh !important;
                z-index: 10000; border-radius: 0;
            }
        `;
        this.container.appendChild(style);
        this.container.appendChild(this.editor);

        // 3. 狀態列 (Status Bar) - 增強版字數統計
        this.statusBar = document.createElement('div');
        this.statusBar.className = 'wte-status-bar';
        this.statusBar.style.cssText = `
            padding: 8px 15px; background: var(--cl-bg-disabled); border-top: 1px solid var(--cl-border-light);
            font-size: var(--cl-font-size-sm); color: var(--cl-text-secondary); display: flex; justify-content: space-between; align-items: center;
        `;
        this.statusBar.innerHTML = `
            <div class="wte-stats">
                字數: <span id="${this.instanceId}-word-count">0</span> |
                字元(含空格): <span id="${this.instanceId}-char-count">0</span> |
                字元(不含空格): <span id="${this.instanceId}-char-no-space">0</span> |
                段落: <span id="${this.instanceId}-para-count">0</span>
            </div>
            <div class="wte-status-info" id="${this.instanceId}-save-status">已存檔</div>
        `;
        this.container.appendChild(this.statusBar);
    }

    _createToolbar() {
        // --- 1. 定義分類內容 (使用 EditorButton) ---
        const T = EditorButton.TYPES;

        const tabConfigs = {
            [Locale.t('webTextEditor.tabCommon')]: [
                // 歷史
                [
                    { type: T.UNDO, command: 'undo', customAction: () => this.undo() },
                    { type: T.REDO, command: 'redo', customAction: () => this.redo() },
                ],
                // 格式
                [
                    { type: T.PARAGRAPH, command: 'formatBlock', value: 'P' },
                    { type: T.HEADING1, command: 'formatBlock', value: 'H1' },
                    { type: T.HEADING2, command: 'formatBlock', value: 'H2' },
                    { type: T.HEADING3, command: 'formatBlock', value: 'H3' },
                ],
                // 字體樣式
                [
                    { type: T.BOLD, command: 'bold', toggleable: true },
                    { type: T.ITALIC, command: 'italic', toggleable: true },
                    { type: T.UNDERLINE, command: 'underline', toggleable: true },
                    { type: T.STRIKETHROUGH, command: 'strikeThrough', toggleable: true },
                ],
                // 對齊
                [
                    { type: T.ALIGN_LEFT, command: 'justifyLeft' },
                    { type: T.ALIGN_CENTER, command: 'justifyCenter' },
                    { type: T.ALIGN_RIGHT, command: 'justifyRight' },
                ],
                // 行間距控制
                ['LINE_SPACING'],
                // 控制項
                ['COLOR_PICKER', 'FONT_SIZE', 'REMOVE_FORMAT']
            ],
            [Locale.t('webTextEditor.tabInsert')]: [
                [
                    { type: T.INSERT_DRAWING, command: 'insertWebPainter', customAction: () => this._insertWebPainter() },
                    { type: T.INSERT_TABLE, command: 'insertTable', customAction: () => this._insertTable() },
                ],
                [
                    'UPLOAD_IMAGE',
                    { type: T.INSERT_LINK, command: 'createLink', customAction: () => this._insertLink() },
                ],
                [
                    { type: T.PAGE_BREAK, command: 'insertPageBreak', customAction: () => this._insertPageBreak() },
                    { type: T.HORIZONTAL_LINE, command: 'insertHorizontalRule' },
                ],
                [
                    { type: T.INSERT_TOC, command: 'insertTOC', customAction: () => this._insertTableOfContents() },
                ]
            ],
            [Locale.t('webTextEditor.tabLayout')]: [
                [
                    { type: 'custom', label: Locale.t('webTextEditor.marginNarrow'), icon: 'margin', command: 'margin-narrow', customAction: () => this._setMargins('20px 40px') },
                    { type: 'custom', label: Locale.t('webTextEditor.marginNormal'), icon: 'margin', command: 'margin-normal', customAction: () => this._setMargins('40px 80px') },
                    { type: 'custom', label: Locale.t('webTextEditor.marginWide'), icon: 'margin', command: 'margin-wide', customAction: () => this._setMargins('60px 120px') },
                ],
                [
                    { type: T.HEADER, command: 'header', customAction: () => this._toggleHeaderFooter('header') },
                    { type: T.FOOTER, command: 'footer', customAction: () => this._toggleHeaderFooter('footer') },
                    { type: T.PAGE_NUMBER, command: 'page-number', customAction: () => this._togglePageNumbers() },
                ],
                [
                    { type: T.FULLSCREEN, command: 'fullscreen', customAction: () => this.toggleFullscreen() },
                ]
            ],
            [Locale.t('webTextEditor.tabTools')]: [
                // 搜尋與取代
                [
                    { type: T.SEARCH, command: 'search', customAction: () => this._openSearchDialog(false) },
                    { type: T.REPLACE, command: 'replace', customAction: () => this._openSearchDialog(true) },
                ],
                // 匯出功能
                [
                    { type: T.EXPORT_PDF, command: 'exportPDF', customAction: () => this._exportToPDF() },
                    { type: T.EXPORT_WORD, command: 'exportWord', customAction: () => this._exportToWord() },
                    { type: T.EXPORT_MD, command: 'exportMD', customAction: () => this._exportToMarkdown() },
                ],
                // 目錄功能
                [
                    { type: T.GENERATE_TOC, command: 'generateTOC', customAction: () => this._showTableOfContents() },
                ],
                // 管理
                [
                    { type: T.CLEAR_ALL, command: 'clearAll', customAction: () => this.clearAll() },
                ]
            ]
        };

        // --- 2. 建立 UI 結構 ---
        const toolbarContainer = document.createElement('div');
        toolbarContainer.className = 'wte-toolbar-container';
        toolbarContainer.style.cssText = `background: var(--cl-bg); border-bottom: 1px solid var(--cl-border); display: flex; flex-direction: column;`;

        const tabList = document.createElement('div');
        tabList.className = 'wte-tab-list';
        tabList.style.cssText = `display: flex; background: var(--cl-bg-secondary); padding: 0 10px; border-bottom: 1px solid var(--cl-border); gap: 2px;`;

        const tabContent = document.createElement('div');
        tabContent.className = 'wte-tab-content';
        tabContent.style.cssText = `padding: 10px; background: var(--cl-bg); display: flex; align-items: center; min-height: 45px;`;

        this.tabPanels = {};
        this.buttons = {};
        this.editorButtons = {}; // 儲存 EditorButton 實例以便更新狀態

        Object.keys(tabConfigs).forEach((tabName, index) => {
            const tabBtn = document.createElement('button');
            tabBtn.textContent = tabName;
            tabBtn.style.cssText = `
                border: none; background: transparent; padding: 8px 16px; cursor: pointer;
                font-size: var(--cl-font-size-md); color: var(--cl-text-secondary); position: relative; top: 1px;
                border: 1px solid transparent; border-bottom: none; border-radius: var(--cl-radius-sm) var(--cl-radius-sm) 0 0;
            `;

            const panel = document.createElement('div');
            panel.style.cssText = `display: none; align-items: center; gap: 10px; flex-wrap: wrap;`;
            this.tabPanels[tabName] = panel;

            // 建立按鈕
            tabConfigs[tabName].forEach((group, gIndex) => {
                if (gIndex > 0) this._addSeparator(panel);

                group.forEach(def => {
                    if (typeof def === 'string') {
                        if (def === 'UPLOAD_IMAGE') {
                            const ubtn = new UploadButton({ type: UploadButton.TYPES.IMAGE, tooltip: Locale.t('webTextEditor.insertImage'), onSelect: (files) => this._insertImage(files[0]) });
                            ubtn.mount(panel);
                        } else if (def === 'COLOR_PICKER') {
                             const cp = new ColorPicker({ value: 'var(--cl-text-dark)', onChange: (c) => { this.editor.focus(); document.execCommand('foreColor', false, c); }});
                             cp.element.style.marginTop = '0';
                             cp.mount(panel);
                        } else if (def === 'FONT_SIZE') {
                             const fs = new NumberInput({ value: 16, min: 10, max: 72, width: '70px', onChange: (v) => { this.editor.focus(); this._applyFontSize(v); }});
                             fs.element.style.marginTop = '0';
                             fs.mount(panel);
                             this.fontSizeInput = fs;
                        } else if (def === 'LINE_SPACING') {
                             // 行間距下拉選單
                             const lineSpacingSelect = document.createElement('select');
                             lineSpacingSelect.title = Locale.t('webTextEditor.lineSpacing');
                             lineSpacingSelect.style.cssText = `
                                 padding: 4px 8px; border: 1px solid var(--cl-border); border-radius: var(--cl-radius-sm);
                                 font-size: var(--cl-font-size-md); cursor: pointer; background: var(--cl-bg);
                             `;
                             const spacingOptions = [
                                 { value: '1', label: '1.0' },
                                 { value: '1.15', label: '1.15' },
                                 { value: '1.5', label: '1.5' },
                                 { value: '2', label: '2.0' },
                                 { value: '2.5', label: '2.5' },
                                 { value: '3', label: '3.0' }
                             ];
                             spacingOptions.forEach(opt => {
                                 const option = document.createElement('option');
                                 option.value = opt.value;
                                 option.textContent = Locale.t('webTextEditor.lineSpacingLabel', { label: opt.label });
                                 if (opt.value === '1.5') option.selected = true;
                                 lineSpacingSelect.appendChild(option);
                             });
                             lineSpacingSelect.onchange = (e) => {
                                 this.editor.focus();
                                 this._applyLineSpacing(e.target.value);
                             };
                             panel.appendChild(lineSpacingSelect);
                             this.lineSpacingSelect = lineSpacingSelect;
                        } else if (def === 'REMOVE_FORMAT') {
                             const btn = new EditorButton({
                                 type: T.REMOVE_FORMAT,
                                 theme: 'light',
                                 onClick: () => {
                                     this.editor.focus();
                                     document.execCommand('removeFormat');
                                     document.execCommand('unlink'); // 同時移除連結
                                 }
                             });
                             btn.mount(panel);
                        }
                        return;
                    }

                    // 使用 EditorButton
                    const btnOptions = {
                        type: def.type,
                        theme: 'light',
                        onClick: () => {
                            this.editor.focus();
                            if (def.customAction) def.customAction();
                            else document.execCommand(def.command, false, def.value || null);
                            this._updateToolbarState();
                        }
                    };

                    // 自訂按鈕標籤
                    if (def.type === 'custom' && def.label) {
                        btnOptions.label = def.label;
                    }

                    const btn = new EditorButton(btnOptions);
                    this.buttons[def.command] = { element: btn.element, value: def.value };

                    // 如果是可切換狀態的按鈕，保存實例
                    if (def.toggleable) {
                        this.editorButtons[def.command] = btn;
                    }

                    btn.mount(panel);
                });
            });

            tabBtn.onclick = () => {
                Object.values(this.tabPanels).forEach(p => p.style.display = 'none');
                tabList.querySelectorAll('button').forEach(b => {
                    b.style.background = 'transparent';
                    b.style.border = '1px solid transparent';
                    b.style.borderBottom = 'none';
                    b.style.color = 'var(--cl-text-secondary)';
                });
                panel.style.display = 'flex';
                tabBtn.style.background = 'var(--cl-bg)';
                tabBtn.style.border = '1px solid var(--cl-border)';
                tabBtn.style.borderBottom = '1px solid var(--cl-bg)';
                tabBtn.style.color = 'var(--cl-text-dark)';
            };

            tabList.appendChild(tabBtn);
            tabContent.appendChild(panel);

            if (index === 0) tabBtn.click();
        });

        toolbarContainer.appendChild(tabList);
        toolbarContainer.appendChild(tabContent);
        this.container.appendChild(toolbarContainer);

        // Initialize Floating Toolbar (Hidden by default)
        this._createFloatingToolbar();
        // Initialize Object Overlay (Resizing & Layout)
        this._initObjectOverlay();
    }

    _addSeparator(parent) {
        const sep = document.createElement('div');
        sep.style.cssText = `width: 1px; height: 20px; background: var(--cl-border); margin: 0 5px;`;
        parent.appendChild(sep);
    }

    _setupEventListeners() {
        // 更新按鈕狀態 (當游標移動時)
        this.editor.addEventListener('keyup', () => this._handleChange());
        this.editor.addEventListener('mouseup', () => this._handleChange());
        // 監聽點擊以處理圖片選取和編輯
        this.editor.addEventListener('click', (e) => {
            const target = e.target;
            
            // 處理 WebPainter 圖片點擊
            if (target.tagName === 'IMG' && target.classList.contains('web-painter-embed')) {
                this._selectObject(target);
            } 
            // 處理表格選取
            else if (target.tagName === 'TD' || target.tagName === 'TH') {
                // 如果是按住 Alt 點擊或是點擊 Cell 的邊緣（這裡簡單判斷點擊目標）
                // 為了讓使用者可以編輯文字，我們預設「單擊儲存格內部」是編輯文字
                // 但如果表格已經被選取了，就不重複選取
                if (this.selectedObject === target.closest('table')) {
                    // 保持選取，但不阻止編輯
                } else if (e.altKey) { // 改為按住 Alt 鍵點擊選取表格物件模式
                    const table = target.closest('table');
                    if (table) this._selectObject(table);
                    e.preventDefault();
                }
            } 
            else if (target.tagName === 'TABLE') {
                this._selectObject(target);
            }
            else {
                // 點擊其他地方，取消選取物件
                this._deselectObject();
            }
        });

        // 監聽捲動與縮放以更新 Overlay 位置
        window.addEventListener('scroll', () => this._updateOverlayPosition(), true);
        window.addEventListener('resize', () => this._updateOverlayPosition());

        // 處理鍵盤事件 (Delete 刪除物件, Escape 隱藏工具列)
        this.editor.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                this._deselectObject();
                this._hideFloatingToolbar();
                return;
            }

            if (this.selectedObject && (e.key === 'Delete' || e.key === 'Backspace')) {
                // 如果是在表格或儲存格內打字，不觸發整張刪除
                const sel = window.getSelection();
                if (sel.rangeCount > 0) {
                    let node = sel.getRangeAt(0).commonAncestorContainer;
                    if (node.nodeType === 3) node = node.parentElement;
                    if (node.closest('td') || node.closest('th')) {
                        return; // 正常打字中，跳過物件選取模式的刪除
                    }
                }

                e.preventDefault();
                this.selectedObject.parentNode.removeChild(this.selectedObject);
                this._deselectObject();
            }
        });

        // 監聽表格拖曳事件
        this.editor.addEventListener('mousedown', (e) => this._onTableMouseDown(e));
        this.editor.addEventListener('mousemove', (e) => this._onTableMouseMove(e));
        document.addEventListener('mouseup', () => this._onTableMouseUp());

        // 處理全域 Escape 監聽及搜尋快捷鍵
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                if (this.searchDialog && this.searchDialog.style.display !== 'none') {
                    this._closeSearchDialog();
                    return;
                }
                if (this.isFullscreen) this.toggleFullscreen(); // 如果全螢幕，ESC 則結束全螢幕
                this._deselectObject();
                this._hideFloatingToolbar();
            }
            // Ctrl+F 開啟搜尋對話框
            if (e.ctrlKey && e.key.toLowerCase() === 'f') {
                // 檢查 focus 是否在編輯器內
                if (this.editor.contains(document.activeElement) || document.activeElement === this.editor) {
                    e.preventDefault();
                    this._openSearchDialog(false);
                }
            }
            // Ctrl+H 開啟取代對話框
            if (e.ctrlKey && e.key.toLowerCase() === 'h') {
                if (this.editor.contains(document.activeElement) || document.activeElement === this.editor) {
                    e.preventDefault();
                    this._openSearchDialog(true);
                }
            }
        });

        // 監聽鍵盤鬆開 (Markdown 快捷鍵 & 字數統計觸發)
        this.editor.addEventListener('keyup', (e) => this._onKeyUp(e));
        this.editor.addEventListener('input', () => this._onContentChange());

        // 處理貼上 (過濾格式，防止貼入垃圾 HTML)
        this.editor.addEventListener('paste', (e) => {
            e.preventDefault();
            const text = (e.originalEvent || e).clipboardData.getData('text/plain');
            document.execCommand('insertText', false, text);
        });

        // 追蹤選取範圍 (處理 Focus 丟失問題)
        document.addEventListener('selectionchange', () => {
            const sel = window.getSelection();
            if (sel.rangeCount > 0) {
                const range = sel.getRangeAt(0);
                // 只有當選取範圍在編輯器內時才更新 savedRange
                if (this.editor.contains(range.commonAncestorContainer) || this.editor === range.commonAncestorContainer) {
                    this.savedRange = range.cloneRange();
                }
            }
        });
    }

    _handleChange() {
        this._updateToolbarState();
        this._updateFloatingToolbar(); // 檢查是否需要顯示浮動工具列
        
        if (this.options.onChange) {
            this.options.onChange(this.getHTML());
        }
    }

    _updateToolbarState() {
        Object.keys(this.buttons).forEach(command => {
            const btnObj = this.buttons[command];
            let active = false;

            try {
                if (command === 'formatBlock') {
                    // Block 型態比較特殊
                    active = document.queryCommandValue(command).toLowerCase() === btnObj.value.toLowerCase();
                } else {
                    active = document.queryCommandState(command);
                }
            } catch(e) { /* 忽略不支援的 command */ }

            // 使用 EditorButton 的 active 屬性 (如果有儲存實例)
            if (this.editorButtons && this.editorButtons[command]) {
                this.editorButtons[command].active = active;
            } else if (btnObj.element) {
                // 向後相容：直接修改元素樣式
                if (active) {
                    btnObj.element.style.background = 'var(--cl-primary-light)';
                    btnObj.element.style.color = 'var(--cl-primary)';
                } else {
                    btnObj.element.style.background = 'transparent';
                    btnObj.element.style.color = 'var(--cl-text)';
                }
            }
        });

        // 更新字體大小顯示
        if (this.fontSizeInput) {
            const sel = window.getSelection();
            if (sel.rangeCount > 0) {
                let node = sel.anchorNode;
                // 如果是 Text Node，找父元素
                if (node.nodeType === 3) node = node.parentNode;
                
                // 確保在編輯器內
                if (this.editor.contains(node) || this.editor === node) {
                    const computed = window.getComputedStyle(node).fontSize;
                    const size = Number.parseInt(computed, 10);
                    
                    if (!isNaN(size) && this.fontSizeInput.getValue() !== size) {
                        // 暫時禁用 onChange 防止循環更新
                        const originalOnChange = this.fontSizeInput.options.onChange;
                        this.fontSizeInput.options.onChange = null;
                        this.fontSizeInput.setValue(size);
                        this.fontSizeInput.options.onChange = originalOnChange;
                    }
                }
            }
        }
    }

    _applyFontSize(size) {
        // 還原選取範圍 (如果因為點擊 Input 而丟失 Focus)
        this._restoreSelection();
        this.editor.focus();

        // 使用替身術：先設為特定的 font size 7
        document.execCommand('fontSize', false, '7');
        
        // 找到這些 font tag 並替換為 span style
        const fonts = this.editor.querySelectorAll('font[size="7"]');
        fonts.forEach(font => {
            const span = document.createElement('span');
            span.style.fontSize = size + 'px';
            
            // 保持原本內容
            while(font.firstChild) {
                span.appendChild(font.firstChild);
            }
            
            if (font.parentNode) {
                font.parentNode.replaceChild(span, font);
            }
        });
    }

    async _insertLink() {
        // 儲存當前選取，因為 SimpleDialog 會搶走 Focus
        this._saveCurrentSelection();
        
        const url = await SimpleDialog.prompt(Locale.t('webTextEditor.promptLink'), 'https://');
        if (url) {
            const safeUrl = sanitizeUrl(url);
            if (!safeUrl) return;

            // 對話框關閉後，還原選取範圍
            this._restoreSelection();
            this.editor.focus();
            document.execCommand('createLink', false, safeUrl);
            this._saveCurrentSelection(); // 完成後更新
        }
    }

    async _insertImage(file) {
        if (!file) return;
        
        // Security Check: Validate MIME type
        if (!file.type.startsWith('image/')) {
            console.warn('Invalid file type:', file.type);
            ModalPanel.alert({ message: "只允許上傳圖片檔案" });
            return;
        }

        // 讀取圖片並取得原始尺寸與 Base64
        const reader = new FileReader();
        reader.onload = (e) => {
            const result = e.target.result;
            const imgObj = new Image();
            imgObj.onload = () => {
                const id = this.instanceId + '-img-' + Date.now();
                // 構造 WebPainter 初始資料，將圖片設為背景
                const defaultData = {
                    width: imgObj.width,
                    height: imgObj.height,
                    backgroundImage: result, // 這裡存 Base64，雖然大但方便自包含
                    elements: [],
                    layers: [{ id: 'layer-1', name: Locale.t('webTextEditor.defaultDrawingLayer'), visible: true }]
                };

                // 插入圖片，並標記為 web-painter-embed
                const imgHtml = `<img src="${result}" class="web-painter-embed" id="${id}" 
                    style="max-width: 100%; display: block; margin: 10px auto; cursor: pointer;" 
                    title="點擊編輯圖片/繪圖" />`;
                
                document.execCommand('insertHTML', false, imgHtml);
                document.execCommand('insertHTML', false, '<p><br/></p>');

                // 設定 dataset
                setTimeout(() => {
                    const img = this.editor.querySelector('#' + id);
                    if (img) {
                        img.dataset.webPainterData = JSON.stringify(defaultData);
                    }
                }, 0);
            };
            imgObj.src = result;
        };
        reader.readAsDataURL(file);
    }

    // --- WebPainter 整合 ---

    _insertWebPainter() {
        const id = this.instanceId + '-wp-' + Date.now();
        // 使用英文避免 btoa 中文亂碼問題，並加上圖示
        const svg = `
        <svg xmlns="http://www.w3.org/2000/svg" width="400" height="300" viewBox="0 0 400 300" style="background:var(--cl-bg-tertiary);border:1px solid var(--cl-border-medium);">
            <rect width="100%" height="100%" fill="var(--cl-bg-tertiary)"/>
            <text x="50%" y="45%" dominant-baseline="middle" text-anchor="middle" font-size="60">🎨</text>
            <text x="50%" y="65%" dominant-baseline="middle" text-anchor="middle" fill="var(--cl-text-secondary)" font-family="sans-serif" font-size="20">Click to Edit Illustration</text>
        </svg>`.trim();
        const placeholder = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svg)));

        const imgHtml = `<img src="${placeholder}" class="web-painter-embed" id="${id}" style="cursor: pointer; border: 2px dashed var(--cl-border-dark); max-width: 100%; display: block; margin: 10px auto;" title="點擊編輯插圖" />`;
        
        document.execCommand('insertHTML', false, imgHtml);
        document.execCommand('insertHTML', false, '<p><br/></p>'); // 換行

        // 初始化數據
        setTimeout(() => {
            const img = this.editor.querySelector('#' + id);
            if (img) {
                // 預設 800x600 的畫布資料
                const defaultData = { width: 800, height: 600, elements: [], layers: [] };
                img.dataset.webPainterData = JSON.stringify(defaultData);
            }
        }, 0);
    }

    _openWebPainterModal(imgElement) {
        // 隱藏選取框，避免在 Modal 後方顯示
        this._deselectObject();
        
        // 建立全螢幕 Modal
        const overlay = document.createElement('div');
        overlay.style.cssText = `position:fixed;top:0;left:0;width:100vw;height:100vh;background:var(--cl-bg-overlay-strong);z-index:9999;display:flex;justify-content:center;align-items:center;backdrop-filter: blur(5px);`;
        
        const modal = document.createElement('div');
        modal.style.cssText = `width:95vw;height:95vh;background: var(--cl-bg);border-radius:var(--cl-radius-lg);display:flex;flex-direction:column;overflow:hidden;box-shadow: var(--cl-shadow-lg);`;
        
        // Modal Header
        const header = document.createElement('div');
        header.style.cssText = `padding:10px 20px;background:var(--cl-bg-secondary);border-bottom:1px solid var(--cl-border);display:flex;justify-content:space-between;align-items:center;`;
        header.innerHTML = '<span style="font-weight:bold;font-size:var(--cl-font-size-2xl);">🎨 繪圖板編輯模式</span>';

        const btnGroup = document.createElement('div');
        btnGroup.style.gap = '10px';
        btnGroup.style.display = 'flex';

        const cancelBtn = document.createElement('button');
        cancelBtn.textContent = Locale.t('webTextEditor.cancelBtn');
        cancelBtn.style.cssText = `padding:8px 16px;border:1px solid var(--cl-border-dark);background: var(--cl-bg);border-radius:var(--cl-radius-sm);cursor:pointer;font-size:var(--cl-font-size-lg);font-family:var(--cl-font-family);`;
        cancelBtn.onclick = () => document.body.removeChild(overlay);
        
        const saveBtn = document.createElement('button');
        saveBtn.textContent = Locale.t('webTextEditor.doneBtn');
        saveBtn.style.cssText = `background:var(--cl-success);color:var(--cl-text-inverse);border:none;padding:8px 20px;border-radius:var(--cl-radius-sm);cursor:pointer;font-weight:bold;font-size:var(--cl-font-size-lg);font-family:var(--cl-font-family);`;
        
        btnGroup.appendChild(cancelBtn);
        btnGroup.appendChild(saveBtn);
        header.appendChild(btnGroup);
        
        // Modal Body (Painter Container)
        const body = document.createElement('div');
        body.style.cssText = `flex:1;position:relative;background:var(--cl-border-light);overflow:hidden;`;
        
        // WebPainter Wrapper (to center it)
        const painterWrapper = document.createElement('div');
        painterWrapper.style.cssText = `width:100%;height:100%;display:flex;justify-content:center;align-items:center;`;
        body.appendChild(painterWrapper);

        modal.appendChild(header);
        modal.appendChild(body);
        overlay.appendChild(modal);
        document.body.appendChild(overlay);
        
        // 初始化 WebPainter
        // 讀取舊資料
        let currentData = { width: 800, height: 600 };
        try {
            if (imgElement.dataset.webPainterData) {
                currentData = JSON.parse(imgElement.dataset.webPainterData);
            }
        } catch(e) { 
            console.error('Load data failed', e);
        }

        // 我們創建一個容器給 painter，並確保它適應視窗
        const painterContainer = document.createElement('div');
        painterContainer.style.height = '100%';
        painterContainer.style.width = '100%';
        painterWrapper.appendChild(painterContainer);

        const painter = new WebPainter({
            container: painterContainer,
            width: currentData.width || 800,
            height: currentData.height || 600,
            features: {
                header: true,      // 顯示工具列 (WebPainter 中 header 即 toolbar)
                export: false,     // 隱藏匯出 (使用 Modal 的完成按鈕)
                upload: true,      // 啟用所有其他功能
                tools: true,
                settings: true,
                zoom: true,
                layers: true,
                delete: true,
                clear: true
            }
        });
        
        // 稍微延遲載入資料以確保 DOM ready
        setTimeout(() => {
            painter.loadData(currentData);
        }, 50);

        // 儲存邏輯
        saveBtn.onclick = async () => {
            try {
                // 1. 取得狀態資料
                const newData = painter.getData();
                imgElement.dataset.webPainterData = JSON.stringify(newData);
                
                // 2. 產生圖片預覽 (Snapshot)
                // 我們可以暫時將 painter 的 canvas 轉為 image
                const dataUrl = painter.canvas.toDataURL('image/png');
                imgElement.src = dataUrl;
                imgElement.style.border = '1px solid var(--cl-border)'; // 移除 dashed placeholder 樣式
                
                // 3. 關閉視窗
                document.body.removeChild(overlay);
                this._handleChange();
            } catch(e) {
                console.error(e);
                ModalPanel.alert({ message: "更新失敗，請檢查 console" });
            }
        };
    }

    // --- 表格功能 ---

    async _insertTable() {
        // 先儲存當前選取區
        this._saveCurrentSelection();
        
        const rows = await SimpleDialog.prompt(Locale.t('webTextEditor.promptRows'), '3');
        if (rows === null || rows === '' || isNaN(rows)) return;
        
        const cols = await SimpleDialog.prompt(Locale.t('webTextEditor.promptCols'), '3');
        if (cols === null || cols === '' || isNaN(cols)) return;
        
        const r = Math.min(Math.max(Number.parseInt(rows, 10), 1), 20);
        const c = Math.min(Math.max(Number.parseInt(cols, 10), 1), 10);
        
        // 生成表格 HTML
        let html = '<table class="wte-table"><thead><tr>';
        for (let i = 0; i < c; i++) {
            html += `<th>標題 ${i + 1}</th>`;
        }
        html += '</tr></thead><tbody>';
        
        for (let i = 0; i < r - 1; i++) {
            html += '<tr>';
            for (let j = 0; j < c; j++) {
                html += '<td>單元格</td>';
            }
            html += '</tr>';
        }
        html += '</tbody></table><p><br/></p>';
        
        // 還原選取區並插入
        this._restoreSelection();
        document.execCommand('insertHTML', false, html);
        this._handleChange();
    }

    // --- 分頁符功能 ---

    _insertPageBreak() {
        const html = '<div class="wte-page-break" contenteditable="false"></div><p><br/></p>';
        document.execCommand('insertHTML', false, html);
    }

    // --- Fixing Span Logic ---

    _commitFixing() {
        const el = this.editor.querySelector('#' + this.fixingId);
        if (el) {
            // Commit: Change ID to unique permanent ID scoped to this instance
            this.spanCounter++;
            el.id = `${this.instanceId}-s-${Date.now()}-${this.spanCounter}`;
        }
    }

    _wrapFixing(range) {
        // 先提交前一個（如果有）
        this._commitFixing();

        try {
            // 如果選取範圍已經在 #fixing 內部，就不需要再包一層
            let parent = range.commonAncestorContainer;
            if (parent.nodeType === 3) parent = parent.parentNode;
            if (parent.id === this.fixingId) return parent;

            // 1. 切割 DOM (Extract)
            const fragment = range.extractContents();
            
            // 2. 清理：移除 fragment 中所有元素的 ID
            // 防止因為從既有的 s-xxx 區塊切割出來而導致 ID 重複
            const elementsWithId = fragment.querySelectorAll('[id]');
            elementsWithId.forEach(el => el.removeAttribute('id'));
            
            for (let i = 0; i < fragment.childNodes.length; i++) {
                const node = fragment.childNodes[i];
                if (node.nodeType === 1 && node.hasAttribute('id')) {
                    node.removeAttribute('id');
                }
            }

            // 3. 包裹 (Wrap)
            const span = document.createElement('span');
            span.id = this.fixingId;
            span.appendChild(fragment);
            range.insertNode(span);
            
            // 4. 重選 (Re-select)
            const sel = window.getSelection();
            sel.removeAllRanges();
            const newRange = document.createRange();
            newRange.selectNodeContents(span);
            sel.addRange(newRange);
            
            return span;
        } catch (e) {
            console.warn('Wrap fixing failed:', e);
            return null;
        }
    }

    // --- Floating Toolbar ---

    _createFloatingToolbar() {
        const toolbar = document.createElement('div');
        // ... styles (unchanged) ...
        toolbar.className = 'web-text-editor-floating-toolbar';
        toolbar.style.cssText = `
            position: absolute;
            z-index: 10000;
            background: var(--cl-bg-dark);
            padding: 6px 10px;
            border-radius: var(--cl-radius-md);
            box-shadow: var(--cl-shadow-md);
            display: none;
            align-items: center;
            gap: 6px;
            transition: opacity var(--cl-transition-fast), transform var(--cl-transition-fast);
            white-space: nowrap;
        `;
        
        toolbar.onmousedown = (e) => {
             if (['INPUT', 'SELECT', 'OPTION'].includes(e.target.tagName)) return;
             e.preventDefault();
        };

        // Helper: Create Style Toggle Button
        const createStyleBtn = (icon, prop, valOn, valOff, title) => {
            const btn = document.createElement('button');
            btn.innerHTML = icon;
            btn.title = title || '';
            btn.style.cssText = `
                background: transparent; border: none; color: var(--cl-border); font-size: 15px; cursor: pointer;
                padding: 4px; min-width: 26px; height: 26px; border-radius: var(--cl-radius-sm);
                display: flex; align-items: center; justify-content: center; transition: background var(--cl-transition-fast);
            `;
            btn.onmouseover = () => btn.style.background = 'var(--cl-bg-inverse-soft-hover)';
            btn.onmouseout = () => btn.style.background = 'transparent';
            btn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
            
            btn.onclick = (e) => {
                e.preventDefault(); e.stopPropagation();
                
                const el = this.editor.querySelector('#' + this.fixingId);
                if (el) {
                    // Toggle Logic
                    if (el.style[prop] === valOn) {
                        el.style[prop] = valOff;
                        btn.style.color = 'var(--cl-border)';
                    } else {
                        el.style[prop] = valOn;
                        btn.style.color = 'var(--cl-success)'; // Active Color
                    }
                }
            };
            return btn;
        };

        // 1. Font Family
        const fontSelect = document.createElement('select');
        fontSelect.title = "字型";
        fontSelect.style.cssText = `
            background: var(--cl-bg-dark); color: var(--cl-bg); border: 1px solid var(--cl-text-secondary); border-radius: var(--cl-radius-sm);
            font-size: var(--cl-font-size-sm); height: 26px; width: 80px; outline: none;
        `;
        ['Microsoft JhengHei', 'Arial', 'Times New Roman', 'Verdana'].forEach(f => {
            const opt = document.createElement('option');
            opt.value = f;
            opt.innerText = f;
            fontSelect.appendChild(opt);
        });
        fontSelect.onmousedown = (e) => e.stopPropagation();
        fontSelect.onchange = (e) => {
            const el = this.editor.querySelector('#' + this.fixingId);
            if(el) el.style.fontFamily = e.target.value;
        };
        toolbar.appendChild(fontSelect);
        this.floatingFontSelect = fontSelect;

        // 2. Font Size
        const sizeInput = document.createElement('input');
        sizeInput.type = 'number';
        sizeInput.min = 12; sizeInput.max = 72; sizeInput.value = 16;
        sizeInput.title = "字體大小 (px)";
        sizeInput.style.cssText = `
            width: 45px; background: var(--cl-bg-dark); color: var(--cl-bg); border: 1px solid var(--cl-text-secondary);
            border-radius: var(--cl-radius-sm); padding: 0 4px; text-align: center; font-size: var(--cl-font-size-sm); height: 26px;
        `;
        sizeInput.onmousedown = (e) => e.stopPropagation();
        sizeInput.onclick = (e) => e.stopPropagation();
        sizeInput.oninput = (e) => {
             const el = this.editor.querySelector('#' + this.fixingId);
             if(el) el.style.fontSize = e.target.value + 'px';
        };
        toolbar.appendChild(sizeInput);
        this.floatingSizeInput = sizeInput;

        // Separator
        const sep = () => {
            const s = document.createElement('div');
            s.style.cssText = 'width:1px;height:18px;background:var(--cl-text-secondary);margin:0 2px;';
            return s;
        };
        toolbar.appendChild(sep());

        // 3. Styles (Direct DOM manipulation)
        toolbar.appendChild(createStyleBtn('<b>B</b>', 'fontWeight', 'bold', 'normal', Locale.t('webTextEditor.bold')));
        toolbar.appendChild(createStyleBtn('<i>I</i>', 'fontStyle', 'italic', 'normal', Locale.t('webTextEditor.italic')));
        toolbar.appendChild(createStyleBtn('<u>U</u>', 'textDecoration', 'underline', 'none', Locale.t('webTextEditor.underline')));
        // 刪除線比較特別，通常是 text-decoration: line-through。
        // 如果和 underline 共存，需要處理 textDecorationLine。但簡單起見，這裡互斥，或改用 createBtn 自定義
        const strikeBtn = document.createElement('button');
        strikeBtn.innerHTML = '<s>S</s>';
        strikeBtn.style.cssText = createStyleBtn('','','','').style.cssText; // Reuse styles
        strikeBtn.onmouseover = () => strikeBtn.style.background = 'var(--cl-bg-inverse-soft-hover)';
        strikeBtn.onmouseout = () => strikeBtn.style.background = 'transparent';
        strikeBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        strikeBtn.onclick = (e) => {
             const el = this.editor.querySelector('#' + this.fixingId);
             if(el) el.style.textDecoration = el.style.textDecoration === 'line-through' ? 'none' : 'line-through';
        };
        toolbar.appendChild(strikeBtn);

        toolbar.appendChild(sep());

        // 4. Color
        const colorWrapper = document.createElement('div');
        colorWrapper.innerHTML = '<span style="font-size:var(--cl-font-size-xl);">🎨</span>';
        colorWrapper.style.cssText = 'position:relative; width: 26px; height: 26px; display:flex; align-items:center; justify-content:center; cursor:pointer;';
        const colorInput = document.createElement('input');
        colorInput.type = 'color';
        colorInput.style.cssText = `position:absolute; top:0; left:0; width:100%; height:100%; opacity:0; cursor:pointer;`;
        colorInput.onmousedown = (e) => e.stopPropagation();
        colorInput.oninput = (e) => {
            const el = this.editor.querySelector('#' + this.fixingId);
            if(el) el.style.color = e.target.value;
        };
        colorWrapper.appendChild(colorInput);
        toolbar.appendChild(colorWrapper);

        toolbar.appendChild(sep());

        // 5. Actions: Link
        const linkBtn = document.createElement('button');
        linkBtn.innerHTML = '🔗';
        linkBtn.title = "轉換為連結";
        linkBtn.style.cssText = strikeBtn.style.cssText;
        linkBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        linkBtn.onclick = async (e) => {
            e.preventDefault(); e.stopPropagation();
            const el = this.editor.querySelector('#' + this.fixingId);
            if (!el) return;

            // SimpleDialog 會非同步，但因為我們操作的是實體 #fixing，不怕丟 focus
            const url = await SimpleDialog.prompt(Locale.t('webTextEditor.promptLink'), 'https://');
            if (url) {
                // Security Sanitize
                const safeUrl = sanitizeUrl(url);
                if (!safeUrl) {
                    ModalPanel.alert({ message: "連結含有不安全的內容" });
                    return;
                }

                // 將 span 轉為 a tag
                const a = document.createElement('a');
                a.href = safeUrl;
                a.id = this.fixingId; // 保持 ID 以便繼續編輯
                a.target = '_blank';
                // 複製樣式
                a.style.cssText = el.style.cssText;
                // 移動內容
                while (el.firstChild) a.appendChild(el.firstChild);
                el.parentNode.replaceChild(a, el);
            }
        };
        toolbar.appendChild(linkBtn);
        
        // 6. Clear Format (Unwrap)
        const clearBtn = document.createElement('button');
        clearBtn.innerHTML = '🧹';
        clearBtn.title = "清除格式";
        clearBtn.style.cssText = strikeBtn.style.cssText;
        clearBtn.onmousedown = (e) => { e.preventDefault(); e.stopPropagation(); };
        clearBtn.onclick = (e) => {
            const el = this.editor.querySelector('#' + this.fixingId);
            if (el) {
                // Unwrap
                const parent = el.parentNode;
                while (el.firstChild) parent.insertBefore(el.firstChild, el);
                parent.removeChild(el);
                this._hideFloatingToolbar(); // 隱藏工具列因為 fixing 沒了
            }
        };
        toolbar.appendChild(clearBtn);

        document.body.appendChild(toolbar); 
        this.floatingToolbar = toolbar;
    }

    _updateFloatingToolbar() {
        if (!this.floatingToolbar) return;

        const sel = window.getSelection();
        // 條件：有選取文字，且選取範圍在編輯器內，且不是空的
        if (sel.rangeCount > 0 && !sel.isCollapsed) {
            const range = sel.getRangeAt(0);
            if (this.editor.contains(range.commonAncestorContainer) || this.editor === range.commonAncestorContainer) {
                
                // 核心改變：自動包裹 #fixing span
                const fixingSpan = this._wrapFixing(range);
                if (fixingSpan) {
                    this._showFloatingToolbar(fixingSpan); // Use span for positioning

                    // Sync UI State from the span's style
                    const style = fixingSpan.style;
                    if(this.floatingSizeInput) this.floatingSizeInput.value = Number.parseInt(style.fontSize, 10) || 16;
                    // ... sync others if needed ...
                }
                return;
            }
        }
        
        // 如果沒有選取，提交變更並隱藏
        this._commitFixing();
        this._hideFloatingToolbar();
    }

    _saveCurrentSelection() {
        const sel = window.getSelection();
        if (sel.rangeCount > 0) {
            const range = sel.getRangeAt(0);
            if (this.editor.contains(range.commonAncestorContainer) || this.editor === range.commonAncestorContainer) {
                this.savedRange = range.cloneRange();
            }
        }
    }

    _restoreSelection() {
        if (this.savedRange) {
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(this.savedRange);
        }
    }

    _showFloatingToolbar(targetNode) {
        // targetNode 應該是我們的 #fixing span
        if (!this.isFloatingToolbarVisible) {
            this.floatingToolbar.style.display = 'flex';
            this.floatingToolbar.style.opacity = '0';
            this.floatingToolbar.style.transform = 'translateY(10px)';
            
            requestAnimationFrame(() => {
                this.floatingToolbar.style.opacity = '1';
                this.floatingToolbar.style.transform = 'translateY(0)';
            });
            this.isFloatingToolbarVisible = true;
        }

        const rect = targetNode.getBoundingClientRect();
        const toolbarRect = this.floatingToolbar.getBoundingClientRect();
        
        let top = rect.top - toolbarRect.height - 10 + window.scrollY;
        let left = rect.left + (rect.width / 2) - (toolbarRect.width / 2) + window.scrollX;

        if (top < window.scrollY + 10) {
            top = rect.bottom + 10 + window.scrollY;
        }

        this.floatingToolbar.style.top = `${top}px`;
        this.floatingToolbar.style.left = `${left}px`;
    }

    _hideFloatingToolbar() {
        if (this.isFloatingToolbarVisible) {
            this.floatingToolbar.style.opacity = '0';
            this.floatingToolbar.style.transform = 'translateY(10px)';
            setTimeout(() => {
                 // 檢查狀態避免快速切換時閃爍
                 if (this.floatingToolbar.style.opacity === '0') {
                    this.floatingToolbar.style.display = 'none';
                 }
            }, 200);
            this.isFloatingToolbarVisible = false;
        }
    }

    // --- Object Selection Overlay ---

    _initObjectOverlay() {
        const overlay = document.createElement('div');
        overlay.className = 'wte-object-overlay';
        overlay.style.cssText = `
            position: absolute; display: none; border: 2px solid var(--cl-primary);
            pointer-events: none;
            z-index: 10001; box-sizing: border-box;
        `;

        // Resize Handles (仅用于图片)
        this.overlayHandles = [];
        ['nw', 'ne', 'sw', 'se'].forEach(pos => {
            const handle = document.createElement('div');
            handle.className = `resize-handle ${pos}`;
            handle.style.cssText = `
                position: absolute; width: 10px; height: 10px; background: var(--cl-primary);
                border: 1px solid var(--cl-bg); border-radius: var(--cl-radius-round); pointer-events: auto;
                cursor: ${pos}-resize; display: none;
            `;
            if (pos.includes('n')) handle.style.top = '-6px'; else handle.style.bottom = '-6px';
            if (pos.includes('w')) handle.style.left = '-6px'; else handle.style.right = '-6px';
            
            handle.addEventListener('mousedown', (e) => this._resizeStart(e, pos));
            overlay.appendChild(handle);
            this.overlayHandles.push(handle);
        });

        // 动态工具栏 (内容由 _updateObjectToolbar 填充)
        const toolbar = document.createElement('div');
        toolbar.className = 'wte-obj-toolbar';
        toolbar.style.cssText = `
            position: absolute; top: -45px; left: 50%; transform: translateX(-50%);
            background: var(--cl-bg-dark); padding: 6px; border-radius: var(--cl-radius-md);
            display: flex; gap: 6px; pointer-events: auto; white-space: nowrap;
            box-shadow: var(--cl-shadow-md);
        `;

        overlay.appendChild(toolbar);
        document.body.appendChild(overlay);
        this.overlay = overlay;
        this.overlayToolbar = toolbar;
    }

    _selectObject(obj) {
        if (this.selectedObject === obj) return;
        this.selectedObject = obj;
        
        // 更新工具栏按钮
        this._updateObjectToolbar();
        
        // 先显示 Overlay，再更新位置 (顺序重要！)
        this.overlay.style.display = 'block';
        this._updateOverlayPosition();
    }

    _updateObjectToolbar() {
        if (!this.selectedObject || !this.overlayToolbar) return;
        
        // 清空工具栏
        this.overlayToolbar.innerHTML = '';
        
        const isTable = this.selectedObject.tagName === 'TABLE';
        const isImage = this.selectedObject.tagName === 'IMG';
        
        // 控制 Resize Handles 显示 (仅图片需要)
        if (this.overlayHandles) {
            this.overlayHandles.forEach(h => {
                h.style.display = isImage ? 'block' : 'none';
            });
        }
        
        // Helper function
        const addBtn = (icon, title, action) => {
            const btn = document.createElement('button');
            btn.innerHTML = icon;
            btn.title = title;
            btn.style.cssText = `
                background: transparent; border: none; color: var(--cl-bg); cursor: pointer;
                padding: 4px 8px; display: flex; align-items: center; gap: 4px;
                border-radius: var(--cl-radius-sm); font-size: var(--cl-font-size-sm); white-space: nowrap;
            `;
            btn.onmouseover = () => btn.style.background = 'var(--cl-bg-inverse-soft-hover)';
            btn.onmouseout = () => btn.style.background = 'transparent';
            btn.onmousedown = (e) => e.stopPropagation();
            btn.onclick = (e) => { e.preventDefault(); action(); };
            this.overlayToolbar.appendChild(btn);
            return btn;
        };
        
        const addSep = () => {
            const sep = document.createElement('div');
            sep.style.cssText = 'width:1px;background:var(--cl-text-secondary);margin:0 4px;';
            this.overlayToolbar.appendChild(sep);
        };
        
        if (isImage) {
            // 图片工具
            addBtn('🎨', Locale.t('webTextEditor.editContent'), () => this._openWebPainterModal(this.selectedObject));
            addSep();
            addBtn('⬅️', Locale.t('webTextEditor.alignLeft'), () => this._setObjStyle({ display: 'block', float: 'left', margin: '10px 15px 10px 0' }));
            addBtn('⏺️', Locale.t('webTextEditor.alignCenter'), () => this._setObjStyle({ display: 'block', float: 'none', margin: '15px auto' }));
            addBtn('➡️', Locale.t('webTextEditor.alignRight'), () => this._setObjStyle({ display: 'block', float: 'right', margin: '10px 0 10px 15px' }));
            addBtn('🔲', Locale.t('webTextEditor.alignFull'), () => this._setObjStyle({ display: 'block', float: 'none', width: '100%', margin: '15px 0', height: 'auto' }));
        } else if (isTable) {
            // 表格工具
            addBtn('➕行', Locale.t('webTextEditor.addRow'), () => this._addTableRow());
            addBtn('➕列', Locale.t('webTextEditor.addCol'), () => this._addTableColumn());
            addSep();
            addBtn('➖行', Locale.t('webTextEditor.deleteRow'), () => this._deleteTableRow());
            addBtn('➖列', Locale.t('webTextEditor.deleteCol'), () => this._deleteTableColumn());
            addSep();
            addBtn('🗑️', Locale.t('webTextEditor.deleteTable'), () => {
                if (confirm(Locale.t('webTextEditor.confirmDeleteTable'))) {
                    this.selectedObject.remove();
                    this._deselectObject();
                }
            });
        }
    }

    _deselectObject() {
        if (!this.selectedObject) return;
        this.selectedObject = null;
        if (this.overlay) this.overlay.style.display = 'none';
    }

    _updateOverlayPosition() {
        if (!this.selectedObject || !this.overlay || this.overlay.style.display === 'none') return;
        
        const rect = this.selectedObject.getBoundingClientRect();
        const scrollTop = window.scrollY;
        const scrollLeft = window.scrollX;

        this.overlay.style.top = (rect.top + scrollTop) + 'px';
        this.overlay.style.left = (rect.left + scrollLeft) + 'px';
        this.overlay.style.width = rect.width + 'px';
        this.overlay.style.height = rect.height + 'px';
    }

    _setObjStyle(styles) {
        if (!this.selectedObject) return;
        Object.assign(this.selectedObject.style, styles);
        this._updateOverlayPosition();
        // Trigger Change
        this._saveCurrentSelection(); 
        if(this.features.onChange) this.features.onChange(this.getHTML());
    }

    _resizeStart(e, handlePos) {
        e.preventDefault();
        e.stopPropagation();
        this.isResizing = true;
        
        const startX = e.clientX;
        const startWidth = this.selectedObject.offsetWidth;
        // Keep aspect ratio by only updating width and letting height be auto?
        // Usually web images are width-driven.
        
        const onMove = (moveEvent) => {
            if (!this.isResizing) return;
            const dx = moveEvent.clientX - startX;
            
            // 簡單處理：右側把手(e)增加寬度，左側(w)減少寬度
            // 更精確的處理需要看 handlePos
            let newWidth = startWidth;
            if (handlePos.includes('e')) newWidth += dx;
            else if (handlePos.includes('w')) newWidth -= dx;
            
            // Min width
            if (newWidth < 50) newWidth = 50;
            
            this.selectedObject.style.width = newWidth + 'px';
            this.selectedObject.style.height = 'auto'; // Keep aspect ratio
            this._updateOverlayPosition();
        };

        const onUp = () => {
            this.isResizing = false;
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            // Trigger change
            if(this.features.onChange) this.features.onChange(this.getHTML());
        };

        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    }

    setContent(html) {
        this.editor.innerHTML = html;
    }

    getHTML() {
        return this.editor.innerHTML;
    }

    getText() {
        return this.editor.innerText;
    }

    clear() {
        this.editor.innerHTML = '';
        this.editor.focus();
    }

    // --- 儲存/讀取 API ---

    /**
     * 匯出編輯器完整狀態 (包含 HTML 與所有嵌入的 WebPainter 資料)
     * @returns {Object} 可序列化的資料物件
     */
    save() {
        const data = {
            version: '1.0',
            timestamp: new Date().toISOString(),
            html: this.editor.innerHTML
        };
        
        // 另外儲存所有 WebPainter 圖片的資料 (方便分離處理)
        const painterData = {};
        const imgs = this.editor.querySelectorAll('img.web-painter-embed');
        imgs.forEach(img => {
            if (img.id && img.dataset.webPainterData) {
                painterData[img.id] = img.dataset.webPainterData;
            }
        });
        data.painterData = painterData;
        
        return data;
    }

    /**
     * 從儲存的資料還原編輯器狀態
     * @param {Object|string} data - save() 輸出的資料物件或 JSON 字串
     */
    load(data) {
        try {
            const state = typeof data === 'string' ? JSON.parse(data) : data;
            
            if (!state || !state.html) {
                console.warn('Invalid editor data');
                return false;
            }
            
            // 還原 HTML
            this.editor.innerHTML = state.html;
            
            // 還原 WebPainter 資料 (如果 dataset 在 HTML 中還原不完整)
            if (state.painterData) {
                Object.keys(state.painterData).forEach(id => {
                    const img = this.editor.querySelector('#' + id);
                    if (img && !img.dataset.webPainterData) {
                        img.dataset.webPainterData = state.painterData[id];
                    }
                });
            }
            
            return true;
        } catch (e) {
            console.error('Failed to load editor data:', e);
            return false;
        }
    }

    /**
     * 將編輯器內容儲存到本地檔案 (JSON 格式)
     * @param {string} filename - 檔名 (預設 'document.json')
     */
    saveToFile(filename = 'document.json') {
        const data = this.save();
        const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    }

    /**
     * 從本地檔案讀取並還原編輯器內容
     * @returns {Promise<boolean>} 是否成功
     */
    loadFromFile() {
        return new Promise((resolve) => {
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = '.json';
            input.onchange = (e) => {
                const file = e.target.files[0];
                if (!file) {
                    resolve(false);
                    return;
                }
                const reader = new FileReader();
                reader.onload = (evt) => {
                    try {
                        const data = JSON.parse(evt.target.result);
                        resolve(this.load(data));
                    } catch (err) {
                        console.error('Failed to parse file:', err);
                        resolve(false);
                    }
                };
                reader.readAsText(file);
            };
            input.click();
        });
    }

    // --- 表格操作方法 ---

    _addTableRow() {
        if (!this.selectedObject || this.selectedObject.tagName !== 'TABLE') return;
        const table = this.selectedObject;
        const tbody = table.querySelector('tbody') || table;
        const lastRow = tbody.querySelector('tr:last-child');
        if (!lastRow) return;
        
        const newRow = lastRow.cloneNode(true);
        newRow.querySelectorAll('td, th').forEach(cell => cell.textContent = Locale.t('webTextEditor.newCell'));
        tbody.appendChild(newRow);
        this._updateOverlayPosition();
    }

    _addTableColumn() {
        if (!this.selectedObject || this.selectedObject.tagName !== 'TABLE') return;
        const table = this.selectedObject;
        
        // 添加到 thead
        const thead = table.querySelector('thead tr');
        if (thead) {
            const th = document.createElement('th');
            th.textContent = Locale.t('webTextEditor.newHeader');
            thead.appendChild(th);
        }
        
        // 添加到 tbody 的每一行
        const rows = table.querySelectorAll('tbody tr');
        rows.forEach(row => {
            const td = document.createElement('td');
            td.textContent = Locale.t('webTextEditor.newCell');
            row.appendChild(td);
        });
        this._updateOverlayPosition();
    }

    _deleteTableRow() {
        if (!this.selectedObject || this.selectedObject.tagName !== 'TABLE') return;
        const table = this.selectedObject;
        const tbody = table.querySelector('tbody') || table;
        const rows = tbody.querySelectorAll('tr');
        if (rows.length > 1) {
            rows[rows.length - 1].remove();
            this._updateOverlayPosition();
        } else {
            ModalPanel.alert({ message: "至少需要保留一行" });
        }
    }

    _deleteTableColumn() {
        if (!this.selectedObject || this.selectedObject.tagName !== 'TABLE') return;
        const table = this.selectedObject;
        
        // 从 thead 删除
        const thead = table.querySelector('thead tr');
        if (thead && thead.children.length > 1) {
            thead.removeChild(thead.lastElementChild);
        }
        
        // 从 tbody 的每一行删除
        const rows = table.querySelectorAll('tbody tr');
        rows.forEach(row => {
            if (row.children.length > 1) {
                row.removeChild(row.lastElementChild);
            }
        });
        this._updateOverlayPosition();
    }

    // --- 表格調整欄寬邏輯 ---

    _onTableMouseMove(e) {
        if (this._tableResizing.isResizing) {
            const dx = e.pageX - this._tableResizing.startX;
            const newWidth = Math.max(30, this._tableResizing.startWidth + dx);
            this._tableResizing.cell.style.width = newWidth + 'px';
            this._updateOverlayPosition();
            return;
        }

        // 檢查是否在儲存格右邊緣
        const target = e.target.closest('td, th');
        if (target) {
            const rect = target.getBoundingClientRect();
            const x = e.clientX - rect.left;
            // 如果在右邊緣 8px 內
            if (rect.width - x < 10) {
                target.style.cursor = 'col-resize';
                this._tableResizing.canResize = true;
                this._tableResizing.hoverCell = target;
            } else {
                target.style.cursor = '';
                this._tableResizing.canResize = false;
            }
        }
    }

    _onTableMouseDown(e) {
        if (this._tableResizing.canResize && this._tableResizing.hoverCell) {
            e.preventDefault();
            const cell = this._tableResizing.hoverCell;
            this._tableResizing.isResizing = true;
            this._tableResizing.cell = cell;
            this._tableResizing.startX = e.pageX;
            this._tableResizing.startWidth = cell.offsetWidth;
            
            // 鎖定表格總寬度，避免自動收縮
            const table = cell.closest('table');
            if (table && !table.style.width) {
                table.style.width = table.offsetWidth + 'px';
            }
            
            document.body.style.cursor = 'col-resize';
        }
    }

    _onTableMouseUp() {
        if (this._tableResizing.isResizing) {
            this._tableResizing.isResizing = false;
            document.body.style.cursor = '';
            this._handleChange();
        }
    }

    /**
     * 更新內容時的處理 (即時統計、自動儲存)
     */
    _onContentChange() {
        this._updateStats();
        
        // 自動儲存 (Debounced 5s)
        if (this._autoSaveTimer) clearTimeout(this._autoSaveTimer);
        this._autoSaveTimer = setTimeout(() => this._autoSave(), 5000);

        if (this.options.onChange) {
            this.options.onChange(this.getHTML());
        }
    }

    _updateStats() {
        const text = this.editor.innerText || '';
        // 字數計算: 中文字符單獨計算，英文單詞以空格分隔
        const chineseChars = (text.match(/[\u4e00-\u9fa5]/g) || []).length;
        const englishWords = text.replace(/[\u4e00-\u9fa5]/g, ' ').trim().split(/\s+/).filter(w => w.length > 0).length;
        const wordCount = chineseChars + englishWords;

        // 字元數 (含空格)
        const charCount = text.length;
        // 字元數 (不含空格)
        const charNoSpace = text.replace(/\s/g, '').length;
        // 段落數
        const paragraphs = this.editor.querySelectorAll('p, h1, h2, h3, h4, h5, h6, li, blockquote');
        const paraCount = Array.from(paragraphs).filter(p => p.textContent.trim().length > 0).length || (text.trim() ? 1 : 0);

        const wordEl = document.getElementById(`${this.instanceId}-word-count`);
        const charEl = document.getElementById(`${this.instanceId}-char-count`);
        const charNoSpaceEl = document.getElementById(`${this.instanceId}-char-no-space`);
        const paraEl = document.getElementById(`${this.instanceId}-para-count`);

        if (wordEl) wordEl.textContent = wordCount;
        if (charEl) charEl.textContent = charCount;
        if (charNoSpaceEl) charNoSpaceEl.textContent = charNoSpace;
        if (paraEl) paraEl.textContent = paraCount;
    }

    _onKeyUp(e) {
        // Markdown 快捷鍵偵測 (僅在輸入空格時觸發)
        if (e.key === ' ') {
            this._checkMarkdownShortcuts();
        }
        
        // 快捷鍵 Ctrl + Z / Y
        if (e.ctrlKey && e.key.toLowerCase() === 'z') {
            e.preventDefault();
            this.undo();
        } else if (e.ctrlKey && (e.key.toLowerCase() === 'y' || (e.shiftKey && e.key.toLowerCase() === 'z'))) {
            e.preventDefault();
            this.redo();
        }

        // 基本變動記錄 (除了導航鍵)
        const navKeys = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Control', 'Shift', 'Alt', 'Meta'];
        if (!navKeys.includes(e.key)) {
            this._saveHistoryDebounced();
        }
    }

    _checkMarkdownShortcuts() {
        const selection = window.getSelection();
        if (!selection.rangeCount) return;
        const range = selection.getRangeAt(0);
        const node = range.startContainer;
        if (node.nodeType !== 3) return; // 必須是文字節點

        const text = node.textContent;
        const pos = range.startOffset;
        const lineText = text.substring(0, pos);

        // 定義捷徑
        const shortcuts = [
            { pattern: /^#\s$/, command: 'formatBlock', value: 'H1' },
            { pattern: /^##\s$/, command: 'formatBlock', value: 'H2' },
            { pattern: /^>\s$/, command: 'formatBlock', value: 'blockquote' },
            { pattern: /^\*\s$/, command: 'insertUnorderedList' },
            { pattern: /^1\.\s$/, command: 'insertOrderedList' },
            { pattern: /^---\s$/, command: 'insertHorizontalRule' }
        ];

        for (const { pattern, command, value } of shortcuts) {
            if (pattern.test(lineText)) {
                // 刪除觸發 Pattern 的文字
                range.setStart(node, 0);
                range.setEnd(node, pos);
                range.deleteContents();
                
                // 執行指令
                document.execCommand(command, false, value || null);
                this._saveHistory(); // 變更格式後立即存歷史
                break;
            }
        }
    }

    // --- 歷史記錄 (Undo/Redo) ---
    
    _saveHistoryDebounced() {
        if (this._historyTimer) clearTimeout(this._historyTimer);
        this._historyTimer = setTimeout(() => this._saveHistory(), 1000);
    }

    _saveHistory() {
        const html = this.getHTML();
        // 如果跟上一個相同則不記錄
        if (this.historyIndex >= 0 && this.history[this.historyIndex] === html) return;

        // 刪除當前索引之後的版本 (如果我們是在 Undo 後進行編輯)
        this.history = this.history.slice(0, this.historyIndex + 1);
        this.history.push(html);
        
        if (this.history.length > this.maxHistory) {
            this.history.shift();
        } else {
            this.historyIndex++;
        }
        this._updateToolbarState();
    }

    undo() {
        if (this.historyIndex > 0) {
            this._saveHistory(); // 確保 Undo 前的當前狀態有被儲存
            this.historyIndex--;
            this.editor.innerHTML = this.history[this.historyIndex];
            this._onContentChange();
        }
    }

    redo() {
        if (this.historyIndex < this.history.length - 1) {
            this.historyIndex++;
            this.editor.innerHTML = this.history[this.historyIndex];
            this._onContentChange();
        }
    }

    // --- 功能擴充 ---

    toggleFullscreen() {
        this.isFullscreen = !this.isFullscreen;
        if (this.isFullscreen) {
            this.container.classList.add('fullscreen');
            document.body.style.overflow = 'hidden';
            this.editor.style.height = 'calc(100vh - 100px)';
        } else {
            this.container.classList.remove('fullscreen');
            document.body.style.overflow = '';
            this.editor.style.height = '';
        }
        this._updateOverlayPosition();
    }

    _autoSave() {
        const data = this.save();
        localStorage.setItem(`${this.instanceId}-draft`, JSON.stringify(data));
        const statusEl = document.getElementById(`${this.instanceId}-save-status`);
        if (statusEl) {
            const now = new Date();
            statusEl.textContent = Locale.t('webTextEditor.autoSaved', { time: `${now.getHours()}:${String(now.getMinutes()).padStart(2, '0')}` });
        }
    }

    /**
     * 設定編輯區邊界
     */
    _setMargins(padding) {
        this.editor.style.padding = padding;
        this._updateOverlayPosition();
    }

    /**
     * 切換頁首/頁尾區塊
     */
    _toggleHeaderFooter(type) {
        const className = `wte-${type}`;
        let el = this.editor.querySelector(`.${className}`);
        if (el) {
            // 如果已存在且內容為空則移除，否則跳轉
            if (!el.textContent.trim()) el.remove();
            else el.focus();
        } else {
            el = document.createElement('div');
            el.className = className;
            el.contentEditable = true;
            el.innerHTML = type === 'header' ? Locale.t('webTextEditor.headerPlaceholder') : Locale.t('webTextEditor.footerPlaceholder');
            
            if (type === 'header') {
                this.editor.insertBefore(el, this.editor.firstChild);
            } else {
                this.editor.appendChild(el);
            }
            el.focus();
        }
    }

    /**
     * 插入/切換頁碼 (簡單模擬)
     */
    _togglePageNumbers() {
        let footer = this.editor.querySelector('.wte-footer');
        if (!footer) {
            this._toggleHeaderFooter('footer');
            footer = this.editor.querySelector('.wte-footer');
        }
        
        const existingNum = footer.querySelector('.wte-page-number');
        if (existingNum) {
            existingNum.remove();
        } else {
            const span = document.createElement('span');
            span.className = 'wte-page-number';
            span.contentEditable = false;
            span.innerHTML = ' | 第 1 頁';
            footer.appendChild(span);
        }
    }

    /**
     * 程序化清空內容 (不帶確認)
     */
    clear() {
        this._saveHistory(); // 清空前存一個歷史
        this.editor.innerHTML = '<p><br></p>';
        this._handleChange();
        this.editor.focus();
    }

    /**
     * UI 觸發的清空 (帶確認)
     */
    async clearAll() {
        if (await SimpleDialog.confirm(Locale.t('webTextEditor.confirmClearAll'))) {
            this.clear();
        }
    }

    // =====================================================
    // 搜尋和取代功能
    // =====================================================

    /**
     * 開啟搜尋/取代對話框
     * @param {boolean} showReplace - 是否顯示取代欄位
     */
    _openSearchDialog(showReplace = false) {
        // 如果對話框已存在，只切換顯示模式
        if (this.searchDialog) {
            this.searchDialog.style.display = 'flex';
            const replaceRow = this.searchDialog.querySelector('.wte-replace-row');
            const replaceActions = this.searchDialog.querySelector('.wte-replace-actions');
            if (replaceRow) replaceRow.style.display = showReplace ? 'flex' : 'none';
            if (replaceActions) replaceActions.style.display = showReplace ? 'flex' : 'none';
            this.searchDialog.querySelector('input[name="search"]').focus();
            this.searchDialog.querySelector('input[name="search"]').select();
            return;
        }

        // 建立搜尋對話框
        const dialog = document.createElement('div');
        dialog.className = 'wte-search-dialog';
        dialog.style.cssText = `
            position: absolute; top: 60px; right: 20px; z-index: 10002;
            background: var(--cl-bg); border: 1px solid var(--cl-border); border-radius: var(--cl-radius-lg);
            box-shadow: var(--cl-shadow-md); padding: 15px;
            display: flex; flex-direction: column; gap: 10px; min-width: 350px;
        `;

        dialog.innerHTML = `
            <div class="wte-search-header" style="display:flex;justify-content:space-between;align-items:center;margin-bottom:5px;">
                <span style="font-weight:bold;font-size:var(--cl-font-size-lg);">搜尋與取代</span>
                <button class="wte-search-close" style="background:none;border:none;font-size:var(--cl-font-size-2xl);cursor:pointer;color:var(--cl-text-placeholder);">&times;</button>
            </div>
            <div style="display:flex;gap:8px;align-items:center;">
                <input type="text" name="search" placeholder="搜尋文字..." style="flex:1;padding:8px 12px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-lg);">
                <button class="wte-btn-prev" title="上一個" style="padding:6px 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background: var(--cl-bg);cursor:pointer;">▲</button>
                <button class="wte-btn-next" title="下一個" style="padding:6px 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background: var(--cl-bg);cursor:pointer;">▼</button>
            </div>
            <div class="wte-replace-row" style="display:${showReplace ? 'flex' : 'none'};gap:8px;align-items:center;">
                <input type="text" name="replace" placeholder="取代為..." style="flex:1;padding:8px 12px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-lg);">
            </div>
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;">
                <label style="font-size:var(--cl-font-size-sm);display:flex;align-items:center;gap:4px;cursor:pointer;">
                    <input type="checkbox" name="caseSensitive"> 區分大小寫
                </label>
                <label style="font-size:var(--cl-font-size-sm);display:flex;align-items:center;gap:4px;cursor:pointer;">
                    <input type="checkbox" name="wholeWord"> 全字匹配
                </label>
                <span class="wte-match-count" style="font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);margin-left:auto;">0 個結果</span>
            </div>
            <div class="wte-replace-actions" style="display:${showReplace ? 'flex' : 'none'};gap:8px;justify-content:flex-end;">
                <button class="wte-btn-replace" style="padding:6px 12px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background: var(--cl-bg);cursor:pointer;">取代</button>
                <button class="wte-btn-replace-all" style="padding:6px 12px;border:none;border-radius:var(--cl-radius-sm);background:var(--cl-primary-dark);color:var(--cl-text-inverse);cursor:pointer;">全部取代</button>
            </div>
        `;

        // 設定事件監聽
        const searchInput = dialog.querySelector('input[name="search"]');
        const replaceInput = dialog.querySelector('input[name="replace"]');
        const caseSensitive = dialog.querySelector('input[name="caseSensitive"]');
        const wholeWord = dialog.querySelector('input[name="wholeWord"]');
        const matchCount = dialog.querySelector('.wte-match-count');

        // 搜尋輸入變更時自動搜尋
        let searchTimeout;
        searchInput.addEventListener('input', () => {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => {
                this._performSearch(searchInput.value, caseSensitive.checked, wholeWord.checked);
                matchCount.textContent = Locale.t('webTextEditor.matchCount', { count: this.searchMatches.length });
            }, 200);
        });

        // 選項變更時重新搜尋
        [caseSensitive, wholeWord].forEach(el => {
            el.addEventListener('change', () => {
                this._performSearch(searchInput.value, caseSensitive.checked, wholeWord.checked);
                matchCount.textContent = Locale.t('webTextEditor.matchCount', { count: this.searchMatches.length });
            });
        });

        // Enter 鍵跳到下一個
        searchInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                if (e.shiftKey) this._searchPrev();
                else this._searchNext();
            }
        });

        // 按鈕事件
        dialog.querySelector('.wte-search-close').onclick = () => this._closeSearchDialog();
        dialog.querySelector('.wte-btn-prev').onclick = () => this._searchPrev();
        dialog.querySelector('.wte-btn-next').onclick = () => this._searchNext();
        dialog.querySelector('.wte-btn-replace').onclick = () => this._replaceCurrent(replaceInput.value);
        dialog.querySelector('.wte-btn-replace-all').onclick = () => {
            const count = this._replaceAll(searchInput.value, replaceInput.value, caseSensitive.checked, wholeWord.checked);
            matchCount.textContent = Locale.t('webTextEditor.replacedCount', { count: count });
            this._performSearch(searchInput.value, caseSensitive.checked, wholeWord.checked);
        };

        this.container.appendChild(dialog);
        this.searchDialog = dialog;
        searchInput.focus();

        // 如果有選取文字，自動填入搜尋框
        const sel = window.getSelection();
        if (sel.rangeCount > 0 && !sel.isCollapsed) {
            const selectedText = sel.toString().trim();
            if (selectedText && selectedText.length < 100) {
                searchInput.value = selectedText;
                this._performSearch(selectedText, caseSensitive.checked, wholeWord.checked);
                matchCount.textContent = Locale.t('webTextEditor.matchCount', { count: this.searchMatches.length });
            }
        }
    }

    /**
     * 關閉搜尋對話框
     */
    _closeSearchDialog() {
        if (this.searchDialog) {
            this.searchDialog.style.display = 'none';
            this._clearSearchHighlights();
        }
    }

    /**
     * 執行搜尋並高亮所有匹配項
     */
    _performSearch(searchText, caseSensitive = false, wholeWord = false) {
        this._clearSearchHighlights();
        this.searchMatches = [];
        this.currentMatchIndex = -1;

        if (!searchText || searchText.length === 0) return;

        // 建立正則表達式
        let pattern = searchText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); // 跳脫特殊字符
        if (wholeWord) {
            pattern = `\\b${pattern}\\b`;
        }
        const flags = caseSensitive ? 'g' : 'gi';
        const regex = new RegExp(pattern, flags);

        // 遍歷文字節點並標記匹配
        const walker = document.createTreeWalker(
            this.editor,
            NodeFilter.SHOW_TEXT,
            null,
            false
        );

        const textNodes = [];
        let node;
        while (node = walker.nextNode()) {
            // 跳過已標記的高亮區域
            if (node.parentElement.classList.contains('wte-search-highlight')) continue;
            textNodes.push(node);
        }

        textNodes.forEach(textNode => {
            const text = textNode.textContent;
            let match;
            const matches = [];

            while ((match = regex.exec(text)) !== null) {
                matches.push({ index: match.index, length: match[0].length });
            }

            if (matches.length > 0) {
                // 從後往前處理，避免索引偏移問題
                const parent = textNode.parentNode;
                let currentNode = textNode;

                for (let i = matches.length - 1; i >= 0; i--) {
                    const m = matches[i];
                    const before = currentNode.textContent.substring(0, m.index);
                    const matched = currentNode.textContent.substring(m.index, m.index + m.length);
                    const after = currentNode.textContent.substring(m.index + m.length);

                    // 建立高亮 span
                    const highlightSpan = document.createElement('span');
                    highlightSpan.className = 'wte-search-highlight';
                    highlightSpan.style.cssText = 'background: var(--cl-warning); padding: 0 2px; border-radius: var(--cl-radius-xs);';
                    highlightSpan.textContent = matched;

                    // 替換節點
                    if (after) {
                        const afterNode = document.createTextNode(after);
                        parent.insertBefore(afterNode, currentNode.nextSibling);
                    }
                    parent.insertBefore(highlightSpan, currentNode.nextSibling);
                    currentNode.textContent = before;

                    this.searchMatches.unshift(highlightSpan); // 加到陣列前端保持順序
                }
            }
        });

        // 自動跳到第一個匹配
        if (this.searchMatches.length > 0) {
            this._searchNext();
        }
    }

    /**
     * 清除所有搜尋高亮
     */
    _clearSearchHighlights() {
        const highlights = this.editor.querySelectorAll('.wte-search-highlight');
        highlights.forEach(span => {
            const parent = span.parentNode;
            const text = document.createTextNode(span.textContent);
            parent.replaceChild(text, span);
            parent.normalize(); // 合併相鄰文字節點
        });
        this.searchMatches = [];
        this.currentMatchIndex = -1;
    }

    /**
     * 跳到下一個匹配項
     */
    _searchNext() {
        if (this.searchMatches.length === 0) return;

        // 移除當前高亮標記
        if (this.currentMatchIndex >= 0 && this.searchMatches[this.currentMatchIndex]) {
            this.searchMatches[this.currentMatchIndex].style.background = 'var(--cl-warning)';
        }

        this.currentMatchIndex = (this.currentMatchIndex + 1) % this.searchMatches.length;

        // 設定當前匹配高亮
        const current = this.searchMatches[this.currentMatchIndex];
        if (current) {
            current.style.background = 'var(--cl-warning)';
            current.scrollIntoView({ behavior: 'smooth', block: 'center' });

            // 更新計數顯示
            const countEl = this.searchDialog?.querySelector('.wte-match-count');
            if (countEl) {
                countEl.textContent = `${this.currentMatchIndex + 1} / ${this.searchMatches.length}`;
            }
        }
    }

    /**
     * 跳到上一個匹配項
     */
    _searchPrev() {
        if (this.searchMatches.length === 0) return;

        // 移除當前高亮標記
        if (this.currentMatchIndex >= 0 && this.searchMatches[this.currentMatchIndex]) {
            this.searchMatches[this.currentMatchIndex].style.background = 'var(--cl-warning)';
        }

        this.currentMatchIndex = (this.currentMatchIndex - 1 + this.searchMatches.length) % this.searchMatches.length;

        // 設定當前匹配高亮
        const current = this.searchMatches[this.currentMatchIndex];
        if (current) {
            current.style.background = 'var(--cl-warning)';
            current.scrollIntoView({ behavior: 'smooth', block: 'center' });

            // 更新計數顯示
            const countEl = this.searchDialog?.querySelector('.wte-match-count');
            if (countEl) {
                countEl.textContent = `${this.currentMatchIndex + 1} / ${this.searchMatches.length}`;
            }
        }
    }

    /**
     * 取代當前匹配項
     */
    _replaceCurrent(replaceText) {
        if (this.currentMatchIndex < 0 || this.searchMatches.length === 0) return;

        const current = this.searchMatches[this.currentMatchIndex];
        if (current) {
            const textNode = document.createTextNode(replaceText);
            current.parentNode.replaceChild(textNode, current);
            this.searchMatches.splice(this.currentMatchIndex, 1);

            // 更新計數
            const countEl = this.searchDialog?.querySelector('.wte-match-count');
            if (countEl) {
                countEl.textContent = Locale.t('webTextEditor.matchCount', { count: this.searchMatches.length });
            }

            // 跳到下一個
            if (this.searchMatches.length > 0) {
                this.currentMatchIndex = Math.min(this.currentMatchIndex, this.searchMatches.length - 1);
                this._searchNext();
            } else {
                this.currentMatchIndex = -1;
            }

            this._handleChange();
        }
    }

    /**
     * 全部取代
     */
    _replaceAll(searchText, replaceText, caseSensitive = false, wholeWord = false) {
        if (!searchText) return 0;

        let count = 0;
        let pattern = searchText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        if (wholeWord) {
            pattern = `\\b${pattern}\\b`;
        }
        const flags = caseSensitive ? 'g' : 'gi';
        const regex = new RegExp(pattern, flags);

        // 使用 innerHTML 進行全域取代 (簡單方法，但要注意 HTML 標籤)
        // 更安全的方法是遍歷文字節點
        const walker = document.createTreeWalker(
            this.editor,
            NodeFilter.SHOW_TEXT,
            null,
            false
        );

        const textNodes = [];
        let node;
        while (node = walker.nextNode()) {
            textNodes.push(node);
        }

        textNodes.forEach(textNode => {
            const originalText = textNode.textContent;
            const newText = originalText.replace(regex, () => {
                count++;
                return replaceText;
            });
            if (originalText !== newText) {
                textNode.textContent = newText;
            }
        });

        this._clearSearchHighlights();
        this._handleChange();
        return count;
    }

    // =====================================================
    // 匯出功能
    // =====================================================

    /**
     * 匯出為 PDF (使用 window.print)
     */
    _exportToPDF() {
        // 建立列印專用的視窗
        const printWindow = window.open('', '_blank');
        if (!printWindow) {
            ModalPanel.alert({ message: Locale.t('webTextEditor.printError') });
            return;
        }

        const content = this.editor.innerHTML;
        const styles = `
            <style>
                @media print {
                    body {
                        font-family: var(--cl-font-family);
                        font-size: 12pt;
                        line-height: 1.6;
                        color: var(--cl-text-dark);
                        padding: 20mm;
                    }
                    h1 { font-size: 24pt; margin: 0.5em 0; border-bottom: 2px solid var(--cl-text); }
                    h2 { font-size: 18pt; margin: 0.5em 0; }
                    h3 { font-size: 14pt; margin: 0.5em 0; }
                    blockquote { border-left: 4px solid var(--cl-text-placeholder); padding-left: 10px; color: var(--cl-text-secondary); }
                    table { border-collapse: collapse; width: 100%; margin: 15px 0; }
                    table td, table th { border: 1px solid var(--cl-text); padding: 8px; }
                    table th { background: var(--cl-bg-subtle); }
                    img { max-width: 100%; }
                    .wte-page-break { page-break-after: always; }
                    .wte-toc { page-break-after: always; }
                    .wte-search-highlight { background: none !important; }
                    @page { margin: 20mm; }
                }
            </style>
        `;

        printWindow.document.write(`
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>列印文件</title>
                ${styles}
            </head>
            <body>
                ${content}
            </body>
            </html>
        `);

        printWindow.document.close();
        printWindow.focus();

        // 延遲呼叫 print 確保內容已渲染
        setTimeout(() => {
            printWindow.print();
            printWindow.close();
        }, 500);
    }

    /**
     * 匯出為 Word (DOCX)
     * 使用 HTML 包裝技巧建立簡易的 Word 檔案
     */
    _exportToWord() {
        const content = this.editor.innerHTML;

        // Word 檔案的 HTML 模板
        const template = `
            <html xmlns:o="urn:schemas-microsoft-com:office:office"
                  xmlns:w="urn:schemas-microsoft-com:office:word"
                  xmlns="http://www.w3.org/TR/REC-html40">
            <head>
                <meta charset="UTF-8">
                <title>Document</title>
                <style>
                    body {
                        font-family: var(--cl-font-family-cjk);
                        font-size: 12pt;
                        line-height: 1.6;
                    }
                    h1 { font-size: 24pt; }
                    h2 { font-size: 18pt; }
                    h3 { font-size: 14pt; }
                    table { border-collapse: collapse; width: 100%; }
                    table td, table th { border: 1px solid var(--cl-text-dark); padding: 8px; }
                    blockquote { border-left: 4px solid var(--cl-border-dark); padding-left: 10px; color: var(--cl-text-secondary); }
                </style>
            </head>
            <body>
                ${content}
            </body>
            </html>
        `;

        const blob = new Blob(['\ufeff' + template], {
            type: 'application/msword'
        });

        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'document.doc';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    /**
     * 匯出為 Markdown
     */
    _exportToMarkdown() {
        const markdown = this._htmlToMarkdown(this.editor);

        const blob = new Blob([markdown], { type: 'text/markdown;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'document.md';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    /**
     * HTML 轉 Markdown 轉換器
     */
    _htmlToMarkdown(element) {
        let markdown = '';

        const processNode = (node, listLevel = 0) => {
            if (node.nodeType === Node.TEXT_NODE) {
                return node.textContent;
            }

            if (node.nodeType !== Node.ELEMENT_NODE) return '';

            const tag = node.tagName.toLowerCase();
            let result = '';
            let childContent = '';

            // 處理子節點
            for (const child of node.childNodes) {
                childContent += processNode(child, listLevel);
            }

            switch (tag) {
                case 'h1':
                    result = `# ${childContent.trim()}\n\n`;
                    break;
                case 'h2':
                    result = `## ${childContent.trim()}\n\n`;
                    break;
                case 'h3':
                    result = `### ${childContent.trim()}\n\n`;
                    break;
                case 'h4':
                    result = `#### ${childContent.trim()}\n\n`;
                    break;
                case 'h5':
                    result = `##### ${childContent.trim()}\n\n`;
                    break;
                case 'h6':
                    result = `###### ${childContent.trim()}\n\n`;
                    break;
                case 'p':
                    result = `${childContent.trim()}\n\n`;
                    break;
                case 'br':
                    result = '\n';
                    break;
                case 'strong':
                case 'b':
                    result = `**${childContent}**`;
                    break;
                case 'em':
                case 'i':
                    result = `*${childContent}*`;
                    break;
                case 'u':
                    result = `<u>${childContent}</u>`;
                    break;
                case 's':
                case 'strike':
                case 'del':
                    result = `~~${childContent}~~`;
                    break;
                case 'a':
                    const href = node.getAttribute('href') || '';
                    result = `[${childContent}](${href})`;
                    break;
                case 'img':
                    const src = node.getAttribute('src') || '';
                    const alt = node.getAttribute('alt') || 'image';
                    result = `![${alt}](${src})\n\n`;
                    break;
                case 'blockquote':
                    result = `> ${childContent.trim().replace(/\n/g, '\n> ')}\n\n`;
                    break;
                case 'ul':
                    result = childContent + '\n';
                    break;
                case 'ol':
                    result = childContent + '\n';
                    break;
                case 'li':
                    const parent = node.parentElement;
                    const isOrdered = parent && parent.tagName.toLowerCase() === 'ol';
                    const indent = '  '.repeat(listLevel);
                    if (isOrdered) {
                        const index = Array.from(parent.children).indexOf(node) + 1;
                        result = `${indent}${index}. ${childContent.trim()}\n`;
                    } else {
                        result = `${indent}- ${childContent.trim()}\n`;
                    }
                    break;
                case 'hr':
                    result = '\n---\n\n';
                    break;
                case 'table':
                    result = this._tableToMarkdown(node) + '\n\n';
                    break;
                case 'code':
                    result = `\`${childContent}\``;
                    break;
                case 'pre':
                    result = `\`\`\`\n${childContent.trim()}\n\`\`\`\n\n`;
                    break;
                case 'div':
                    if (node.classList.contains('wte-toc')) {
                        result = `[TOC]\n\n`;
                    } else if (node.classList.contains('wte-page-break')) {
                        result = '\n---\n\n';
                    } else {
                        result = childContent;
                    }
                    break;
                case 'span':
                    result = childContent;
                    break;
                default:
                    result = childContent;
            }

            return result;
        };

        for (const child of element.childNodes) {
            markdown += processNode(child);
        }

        // 清理多餘的空行
        markdown = markdown.replace(/\n{3,}/g, '\n\n').trim();

        return markdown;
    }

    /**
     * 表格轉 Markdown
     */
    _tableToMarkdown(table) {
        const rows = table.querySelectorAll('tr');
        if (rows.length === 0) return '';

        let md = '';
        let isHeader = true;

        rows.forEach((row, index) => {
            const cells = row.querySelectorAll('th, td');
            const cellContents = Array.from(cells).map(cell => cell.textContent.trim());
            md += '| ' + cellContents.join(' | ') + ' |\n';

            // 在第一行後加入分隔線
            if (isHeader && (row.querySelector('th') || index === 0)) {
                md += '| ' + cellContents.map(() => '---').join(' | ') + ' |\n';
                isHeader = false;
            }
        });

        return md;
    }

    // =====================================================
    // 目錄自動生成功能
    // =====================================================

    /**
     * 掃描文件中的標題並生成目錄結構
     */
    _scanHeadings() {
        const headings = this.editor.querySelectorAll('h1, h2, h3, h4, h5, h6');
        const toc = [];

        headings.forEach((heading, index) => {
            // 跳過目錄區塊內的標題
            if (heading.closest('.wte-toc')) return;

            const level = parseInt(heading.tagName.charAt(1));
            const text = heading.textContent.trim();

            // 為標題加上 ID 以便跳轉
            if (!heading.id) {
                heading.id = `wte-heading-${this.instanceId}-${index}`;
            }

            toc.push({
                level,
                text,
                id: heading.id
            });
        });

        return toc;
    }

    /**
     * 顯示目錄面板
     */
    _showTableOfContents() {
        const toc = this._scanHeadings();

        if (toc.length === 0) {
            ModalPanel.alert({ message: Locale.t('webTextEditor.noHeadings') });
            return;
        }

        // 建立目錄面板
        const overlay = document.createElement('div');
        overlay.style.cssText = `
            position: fixed; top: 0; left: 0; width: 100vw; height: 100vh;
            background: var(--cl-bg-overlay); z-index: 10003;
            display: flex; justify-content: center; align-items: center;
        `;

        const panel = document.createElement('div');
        panel.style.cssText = `
            background: var(--cl-bg); border-radius: var(--cl-radius-xl); padding: 20px;
            max-width: 500px; width: 90%; max-height: 80vh; overflow-y: auto;
            box-shadow: var(--cl-shadow-lg);
        `;

        let tocHtml = `
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:15px;">
                <h3 style="margin:0;">文件目錄</h3>
                <button class="close-btn" style="background:none;border:none;font-size:var(--cl-font-size-3xl);cursor:pointer;">&times;</button>
            </div>
            <div class="toc-list" style="margin-bottom:15px;">
        `;

        toc.forEach(item => {
            const indent = (item.level - 1) * 20;
            tocHtml += `
                <div class="toc-item" data-id="${item.id}"
                     style="padding:8px 10px;margin-left:${indent}px;cursor:pointer;border-radius:var(--cl-radius-sm);transition:background var(--cl-transition);"
                     onmouseover="this.style.background='var(--cl-bg-secondary)'"
                     onmouseout="this.style.background='transparent'">
                    <span style="color:var(--cl-text-secondary);font-size:var(--cl-font-size-sm);margin-right:8px;">H${item.level}</span>
                    ${escapeHtml(item.text)}
                </div>
            `;
        });

        tocHtml += `
            </div>
            <div style="display:flex;gap:10px;justify-content:flex-end;border-top:1px solid var(--cl-border-light);padding-top:15px;">
                <button class="insert-btn" style="padding:8px 16px;border:1px solid var(--cl-primary-dark);color:var(--cl-primary-dark);background: var(--cl-bg);border-radius:var(--cl-radius-sm);cursor:pointer;">
                    插入目錄到文件
                </button>
            </div>
        `;

        panel.innerHTML = tocHtml;

        // 事件處理
        panel.querySelector('.close-btn').onclick = () => document.body.removeChild(overlay);
        overlay.onclick = (e) => {
            if (e.target === overlay) document.body.removeChild(overlay);
        };

        // 點擊目錄項目跳轉
        panel.querySelectorAll('.toc-item').forEach(item => {
            item.onclick = () => {
                const id = item.dataset.id;
                const heading = document.getElementById(id);
                if (heading) {
                    document.body.removeChild(overlay);
                    heading.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    // 高亮效果
                    heading.style.background = 'var(--cl-canvas-highlight)';
                    setTimeout(() => heading.style.background = '', 2000);
                }
            };
        });

        // 插入目錄按鈕
        panel.querySelector('.insert-btn').onclick = () => {
            document.body.removeChild(overlay);
            this._insertTableOfContents();
        };

        overlay.appendChild(panel);
        document.body.appendChild(overlay);
    }

    /**
     * 插入目錄到文件開頭
     */
    _insertTableOfContents() {
        const toc = this._scanHeadings();

        if (toc.length === 0) {
            ModalPanel.alert({ message: Locale.t('webTextEditor.noHeadingsForToc') });
            return;
        }

        // 移除舊的目錄
        const existingToc = this.editor.querySelector('.wte-toc');
        if (existingToc) {
            existingToc.remove();
        }

        // 建立目錄 HTML
        let tocHtml = `
            <div class="wte-toc" contenteditable="false" style="
                background: var(--cl-bg-tertiary); border: 1px solid var(--cl-border-subtle); border-radius: var(--cl-radius-lg);
                padding: 20px; margin-bottom: 20px;
            ">
                <div style="font-weight:bold;font-size:var(--cl-font-size-2xl);margin-bottom:15px;border-bottom:2px solid var(--cl-border-medium);padding-bottom:10px;">
                    目錄
                </div>
        `;

        toc.forEach(item => {
            const indent = (item.level - 1) * 20;
            tocHtml += `
                <div class="wte-toc-item" style="padding:5px 0;margin-left:${indent}px;">
                    <a href="#${item.id}" style="color:var(--cl-primary-dark);text-decoration:none;"
                       onclick="event.preventDefault();document.getElementById('${item.id}').scrollIntoView({behavior:'smooth',block:'center'});">
                        ${escapeHtml(item.text)}
                    </a>
                </div>
            `;
        });

        tocHtml += '</div>';

        // 插入到文件開頭
        const firstChild = this.editor.firstChild;
        const tocElement = document.createElement('div');
        tocElement.innerHTML = tocHtml;
        const tocNode = tocElement.firstElementChild;

        if (firstChild) {
            this.editor.insertBefore(tocNode, firstChild);
        } else {
            this.editor.appendChild(tocNode);
        }

        this._handleChange();
        ModalPanel.alert({ message: Locale.t('webTextEditor.tocInserted') });
    }

    // =====================================================
    // 行間距控制
    // =====================================================

    /**
     * 套用行間距到選取的段落或整份文件
     */
    _applyLineSpacing(spacing) {
        this._restoreSelection();

        const selection = window.getSelection();
        if (selection.rangeCount > 0 && !selection.isCollapsed) {
            // 有選取範圍，套用到選取的段落
            const range = selection.getRangeAt(0);
            const container = range.commonAncestorContainer;

            // 找到包含選取範圍的段落元素
            let element = container.nodeType === 3 ? container.parentElement : container;
            while (element && element !== this.editor && !['P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'LI', 'BLOCKQUOTE'].includes(element.tagName)) {
                element = element.parentElement;
            }

            if (element && element !== this.editor) {
                element.style.lineHeight = spacing;
            } else {
                // 無法確定段落，套用到整個編輯區
                this.editor.style.lineHeight = spacing;
            }
        } else {
            // 沒有選取，套用到整份文件
            this.editor.style.lineHeight = spacing;
        }

        this._handleChange();
    }

    // =====================================================
    // 草稿恢復
    // =====================================================

    /**
     * 檢查是否有本地草稿
     */
    _checkDraft() {
        const draftKey = `${this.instanceId}-draft`;
        const draft = localStorage.getItem(draftKey);

        if (draft && this.editor.innerHTML.trim() === '' || this.editor.innerHTML === '<p><br></p>') {
            // 只在編輯器為空時提示恢復
            try {
                const data = JSON.parse(draft);
                if (data.html && data.timestamp) {
                    const date = new Date(data.timestamp);
                    const timeStr = `${date.getFullYear()}/${date.getMonth() + 1}/${date.getDate()} ${date.getHours()}:${String(date.getMinutes()).padStart(2, '0')}`;

                    SimpleDialog.confirm(Locale.t('webTextEditor.draftFound', { time: timeStr })).then(confirmed => {
                        if (confirmed) {
                            this.load(data);
                            this._updateStats();
                        } else {
                            localStorage.removeItem(draftKey);
                        }
                    });
                }
            } catch (e) {
                console.warn('Failed to parse draft:', e);
            }
        }
    }
}
