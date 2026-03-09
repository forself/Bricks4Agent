/**
 * DocumentWall Component
 * 文件牆元件 - 管理文件列表、多選與下載
 */

import { DocumentCard } from './DocumentCard.js';
import { ModalPanel } from '../Panel/ModalPanel.js';
import SimpleZip from '../../utils/SimpleZip.js';

import Locale from '../../i18n/index.js';
export class DocumentWall {
    /**
     * @param {Object} options
     * @param {Array} options.documents - 初始文件陣列 [{id, title, type, src, description}]
     * @param {boolean} options.readOnly - 唯讀模式 (不顯示選取/下載)
     * @param {Function} options.onDownload - 單檔下載回調 (doc)
     * @param {Function} options.onDescription - 說明回調 (doc)
     * @param {Function} options.onEdit - 編輯回調 (doc)
     * @param {Function} options.onDelete - 刪除回調 (docId)
     */
    constructor(options = {}) {
        this.options = {
            documents: [],
            readOnly: false,
            onDownload: null,
            onDescription: null,
            onEdit: null,
            onDelete: null,
            ...options
        };

        this.documents = [...this.options.documents];
        this.selectedIds = new Set();
        this.element = this._createElement();
    }

    _createElement() {
        const wrapper = document.createElement('div');
        wrapper.className = 'document-wall-wrapper';
        wrapper.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 16px;
            width: 100%;
        `;

        // 工具列 (下載按鈕)
        if (!this.options.readOnly) {
            const toolbar = document.createElement('div');
            toolbar.style.cssText = `
                display: flex;
                justify-content: flex-end;
                align-items: center;
                height: 40px;
            `;

            this.downloadBtn = document.createElement('button');
            this.downloadBtn.textContent = Locale.t('documentWall.downloadSelected', { count: 0 });
            this.downloadBtn.style.cssText = `
                padding: 8px 16px;
                border: 1px solid var(--cl-border);
                background: var(--cl-bg);
                color: var(--cl-text-secondary);
                border-radius: 4px;
                cursor: not-allowed;
                font-size: 14px;
                display: flex;
                align-items: center;
                gap: 6px;
                opacity: 0.6;
                transition: all 0.2s;
            `;
            // SVG Icon
            this.downloadBtn.innerHTML = `
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                    <polyline points="7 10 12 15 17 10"></polyline>
                    <line x1="12" y1="15" x2="12" y2="3"></line>
                </svg>
                <span>下載選取 (0)</span>
            `;

            this.downloadBtn.onclick = () => this._handleBatchDownload();
            toolbar.appendChild(this.downloadBtn);
            wrapper.appendChild(toolbar);
        }

        // 文件網格容器
        const container = document.createElement('div');
        container.className = 'document-wall';
        container.style.cssText = `
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
            gap: 16px;
            width: 100%;
        `;

        this.container = container;
        wrapper.appendChild(container);

        this.render();

        return wrapper;
    }

    render() {
        this.container.innerHTML = '';

        this.documents.forEach((doc, index) => {
            const isSelected = this.selectedIds.has(doc.id);
            const card = new DocumentCard({
                title: doc.title,
                type: doc.type,
                src: doc.src, // 傳遞 src 用於預覽
                selected: isSelected,
                onSelect: () => this._toggleSelect(doc.id),
                onEdit: () => {
                    if (this.options.onEdit) {
                        this.options.onEdit(doc);
                    }
                },
                onDescription: () => {
                    if (this.options.onDescription) {
                        this.options.onDescription(doc);
                    } else {
                        ModalPanel.alert({ message: `${Locale.t('documentWall.descriptionPrefix')}${doc.description || Locale.t('documentWall.noDescription')}` });
                    }
                },
                onDownload: () => {
                    // 單檔下載
                    if (this.options.onDownload) {
                        this.options.onDownload(doc);
                    } else {
                        this._triggerDownload(doc.src || '#', doc.title);
                    }
                },
                // 傳遞刪除回調 (只有非唯讀模式且有 onDelete 時)
                onDelete: (!this.options.readOnly && this.options.onDelete) ? () => this._handleDelete(index) : null
            });

            this.container.appendChild(card.element);
        });

        this._updateDownloadBtn();
    }

    _handleDelete(index) {
        const doc = this.documents[index];

        // 使用 ModalPanel 進行雙重確認
        ModalPanel.confirm({
            title: Locale.t('documentWall.deleteConfirmTitle'),
            message: Locale.t('documentWall.deleteConfirmMessage', { title: doc.title }),
            confirmText: Locale.t('documentWall.confirmBtn'),
            cancelText: Locale.t('documentWall.cancelBtn'),
            onConfirm: () => {
                // 第二層確認
                setTimeout(() => {
                    ModalPanel.prompt({
                        title: Locale.t('documentWall.doubleConfirmTitle'),
                        message: Locale.t('documentWall.doubleConfirmMessage'),
                        placeholder: Locale.t('documentWall.doubleConfirmPlaceholder'),
                        validate: (value) => value === Locale.t('documentWall.doubleConfirmKeyword'),
                        confirmText: Locale.t('documentWall.doubleConfirmBtn'),
                        onConfirm: () => {
                            this.removeDocument(index);
                            if (this.options.onDelete) {
                                this.options.onDelete(doc.id);
                            }
                        }
                    });
                }, 300);
            }
        });
    }

    removeDocument(index) {
        const doc = this.documents[index];
        this.documents.splice(index, 1);
        if (doc) this.selectedIds.delete(doc.id);
        this.render();
    }

    _toggleSelect(id) {
        if (this.selectedIds.has(id)) {
            this.selectedIds.delete(id);
        } else {
            this.selectedIds.add(id);
        }
        // 為求簡單，重新 render 全部 (因為需要更新按鈕狀態與卡片選取狀態)
        // 優化：可以只更新對應卡片的 updateSelectState
        this.render();
    }

    _updateDownloadBtn() {
        if (!this.downloadBtn) return;

        const count = this.selectedIds.size;
        const textSpan = this.downloadBtn.querySelector('span');
        if (textSpan) textSpan.textContent = Locale.t('documentWall.downloadSelected', { count });

        if (count > 0) {
            this.downloadBtn.style.opacity = '1';
            this.downloadBtn.style.cursor = 'pointer';
            this.downloadBtn.style.background = 'var(--cl-primary)';
            this.downloadBtn.style.color = 'var(--cl-text-inverse)';
            this.downloadBtn.style.borderColor = 'var(--cl-primary)';
            this.downloadBtn.disabled = false;
        } else {
            this.downloadBtn.style.opacity = '0.6';
            this.downloadBtn.style.cursor = 'not-allowed';
            this.downloadBtn.style.background = 'var(--cl-bg)';
            this.downloadBtn.style.color = 'var(--cl-text-secondary)';
            this.downloadBtn.style.borderColor = 'var(--cl-border)';
            this.downloadBtn.disabled = true;
        }
    }

    async _handleBatchDownload() {
        if (this.selectedIds.size === 0) return;

        const selectedDocs = this.documents.filter(d => this.selectedIds.has(d.id));

        // 進入 loading 狀態
        const originalText = this.downloadBtn.querySelector('span').textContent;
        this.downloadBtn.querySelector('span').textContent = Locale.t('documentWall.packing');
        this.downloadBtn.disabled = true;

        try {
            const zip = new SimpleZip();

            // 由於 src 可能是假連結，這裡做個簡單與 PhotoWall 類似的 fetch 處理
            // 如果是真實連結，應該 fetch blob
            for (const doc of selectedDocs) {
                if (doc.src?.startsWith('http')) {
                    try {
                        const blob = await fetch(doc.src).then(r => r.blob());
                        zip.addFile(doc.title, blob); // 假設 title 包含副檔名或自動偵測
                    } catch (e) {
                        console.warn(`無法載入文件 "${doc.title}":`, e);
                        // Fallback for demo: create text file
                        zip.addFile(doc.title + '.txt', `Mock content for ${doc.title}`);
                    }
                } else {
                    // Fallback
                    zip.addFile(doc.title + '.txt', `Mock content for ${doc.title}\nDescription: ${doc.description}`);
                }
            }

            const zipBlob = await zip.generateAsync();
            this._triggerDownload(URL.createObjectURL(zipBlob), `documents_${Date.now()}.zip`);

        } catch (error) {
            console.error('Batch download failed', error);
            ModalPanel.alert({ message: Locale.t('documentWall.packError') });
        } finally {
            this.downloadBtn.querySelector('span').textContent = originalText;
            this._updateDownloadBtn();
        }
    }

    _triggerDownload(url, filename) {
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }
}
