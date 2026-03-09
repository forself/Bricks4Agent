/**
 * PhotoWall Component
 * 照片牆元件 - 支援新增、刪除、預覽導航、多選下載
 */

import { PhotoCard } from '../../common/PhotoCard/PhotoCard.js';
import { ImageViewer } from '../../common/ImageViewer/ImageViewer.js';
import { ModalPanel } from '../Panel/ModalPanel.js';
import { ActionButton } from '../../common/ActionButton/ActionButton.js'; // 假設有的話，或是直接用 icon
import { UploadButton } from '../../common/UploadButton/UploadButton.js'; // 假如需要內部引用
import { DownloadButton } from '../../common/DownloadButton/DownloadButton.js'; // 用於樣式或按鈕
import SimpleZip from '../../utils/SimpleZip.js';

import Locale from '../../i18n/index.js';
export class PhotoWall {
    /**
     * @param {Object} options
     * @param {Array} options.photos - 初始照片陣列 [{id, src, alt}]
     * @param {boolean} options.readOnly - 唯讀模式
     * @param {Function} options.onAdd - 新增照片回調
     * @param {Function} options.onDelete - 刪除照片回調 (id)
     * @param {Function} options.onChange - 照片變更回調 (photos)
     */
    constructor(options = {}) {
        this.options = {
            photos: [],
            readOnly: false,
            onAdd: null,
            onDelete: null,
            onChange: null,
            ...options
        };

        this.photos = [...this.options.photos];
        this.selectedIds = new Set();
        this.element = this._createElement();

        // Trigger initial change notification to sync state
        this._notifyChange();
    }

    _notifyChange() {
        if (this.options.onChange) {
            this.options.onChange([...this.photos]);
        }
    }

    _createElement() {
        const wrapper = document.createElement('div');
        wrapper.className = 'photo-wall-wrapper';
        wrapper.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 16px;
            width: 100%;
        `;

        // 工具列 (下載按鈕)
        const toolbar = document.createElement('div');
        toolbar.style.cssText = `
            display: flex;
            justify-content: flex-end;
            align-items: center;
            height: 40px;
        `;

        // 下載按鈕
        this.downloadBtn = document.createElement('button');
        this.downloadBtn.textContent = Locale.t('photoWall.downloadSelected', { count: 0 });
        this.downloadBtn.className = 'photo-wall__download-btn';
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
        // 加入 icon
        this.downloadBtn.innerHTML = `
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
            </svg>
            <span>下載選取 (0)</span>
        `;

        this.downloadBtn.addEventListener('click', () => this._handleDownload());
        toolbar.appendChild(this.downloadBtn);
        wrapper.appendChild(toolbar);

        // 照片容器
        const container = document.createElement('div');
        container.className = 'photo-wall';
        container.style.cssText = `
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
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

        // 渲染照片列表
        this.photos.forEach((photo, index) => {
            const wrapper = document.createElement('div');
            wrapper.style.cssText = 'position: relative;';

            // 照片卡片
            const card = new PhotoCard({
                src: photo.src,
                alt: photo.alt,
                type: 'location', // 預設使用地點比例 (3:4) 比較適合照片牆，或可配置
                clickable: true,
                width: '100%'
            });

            // 覆寫點擊行為
            card.element.onclick = (e) => {
                e.stopPropagation();
                // 如果點擊的是 checkbox 區域，不觸發 viewer（雖然 checkbox 有自己的事件，但防萬一）
                this._openViewer(index);
            };

            wrapper.appendChild(card.element);

            // 勾選框 (右上角)
            const checkbox = document.createElement('div');
            const isSelected = this.selectedIds.has(photo.id);
            checkbox.className = 'photo-wall__select';
            checkbox.style.cssText = `
                position: absolute;
                top: 8px;
                right: 8px;
                width: 24px;
                height: 24px;
                background: ${isSelected ? 'var(--cl-primary)' : 'rgba(255, 255, 255, 0.8)'};
                border: 2px solid ${isSelected ? 'var(--cl-primary)' : 'var(--cl-border)'};
                border-radius: 4px;
                cursor: pointer;
                display: flex;
                align-items: center;
                justify-content: center;
                z-index: 10;
                transition: all 0.2s;
            `;

            if (isSelected) {
                checkbox.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>`;
            }

            checkbox.onclick = (e) => {
                e.stopPropagation();
                this._toggleSelect(photo.id);
            };

            wrapper.appendChild(checkbox);

            // 刪除按鈕 (改為左上角以免衝突，或保持右上但位移？用戶需求是右上勾選，刪除未指定但通常會有衝突)
            // 原需求：刪除在照片右上角。新需求：勾選也在右上角。
            // 調整：把刪除移到左上角，或右下角。
            // 為了美觀，將刪除移至 *左上角* 
            if (!this.options.readOnly) {
                const deleteBtn = document.createElement('div');
                deleteBtn.innerHTML = `
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <line x1="18" y1="6" x2="6" y2="18"></line>
                        <line x1="6" y1="6" x2="18" y2="18"></line>
                    </svg>
                `;
                deleteBtn.style.cssText = `
                    position: absolute;
                    top: 8px;
                    left: 8px;
                    width: 24px;
                    height: 24px;
                    background: var(--cl-danger);
                    color: var(--cl-text-inverse);
                    border-radius: 50%;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    cursor: pointer;
                    box-shadow: 0 2px 4px rgba(0,0,0,0.2);
                    z-index: 10;
                    opacity: 0;
                    transition: opacity 0.2s;
                `;

                // 懸停時顯示刪除按鈕
                wrapper.addEventListener('mouseenter', () => deleteBtn.style.opacity = '1');
                wrapper.addEventListener('mouseleave', () => deleteBtn.style.opacity = '0');

                deleteBtn.onclick = (e) => {
                    e.stopPropagation();
                    this._handleDelete(index);
                };

                wrapper.appendChild(deleteBtn);
            }

            this.container.appendChild(wrapper);
        });

        // 新增按鈕
        if (!this.options.readOnly) {
            const addBtn = document.createElement('div');
            addBtn.className = 'photo-wall__add';
            addBtn.style.cssText = `
                display: flex;
                flex-direction: column;
                align-items: center;
                justify-content: center;
                aspect-ratio: 3/4;
                background: var(--cl-bg-secondary);
                border: 2px dashed var(--cl-border-light);
                border-radius: 8px;
                cursor: pointer;
                transition: all 0.2s;
                color: var(--cl-text-placeholder);
            `;
            addBtn.innerHTML = `
                <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="12" y1="5" x2="12" y2="19"></line>
                    <line x1="5" y1="12" x2="19" y2="12"></line>
                </svg>
                <span style="font-size: 13px; margin-top: 8px;">新增照片</span>
            `;

            addBtn.addEventListener('mouseenter', () => {
                addBtn.style.background = 'var(--cl-bg-input)';
                addBtn.style.borderColor = 'var(--cl-grey-light)';
                addBtn.style.color = 'var(--cl-text-secondary)';
            });
            addBtn.addEventListener('mouseleave', () => {
                addBtn.style.background = 'var(--cl-bg-secondary)';
                addBtn.style.borderColor = 'var(--cl-border-light)';
                addBtn.style.color = 'var(--cl-text-placeholder)';
            });
            addBtn.addEventListener('click', () => {
                if (this.options.onAdd) {
                    this.options.onAdd();
                }
            });

            this.container.appendChild(addBtn);
        }
        this._updateDownloadBtn(); // Initial update for download button state
    }

    _toggleSelect(id) {
        if (this.selectedIds.has(id)) {
            this.selectedIds.delete(id);
        } else {
            this.selectedIds.add(id);
        }
        this.render(); // 簡單重繪更新 UI
        this._updateDownloadBtn();
    }

    _updateDownloadBtn() {
        const count = this.selectedIds.size;

        const textSpan = this.downloadBtn.querySelector('span');
        if (textSpan) textSpan.textContent = Locale.t('photoWall.downloadSelected', { count });

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

    async _handleDownload() {
        if (this.selectedIds.size === 0) return;

        const selectedPhotos = this.photos.filter(p => this.selectedIds.has(p.id));

        if (selectedPhotos.length === 1) {
            // 單張下載
            const photo = selectedPhotos[0];
            this._triggerDownload(photo.src, `photo_${photo.id}.png`);
        } else {
            // 多張下載 (ZIP)
            await this._downloadAsZip(selectedPhotos);
        }
    }

    async _downloadAsZip(photos) {
        // 設定 loading 狀態 (這裡簡化，可以加個 spinner)
        const originalText = this.downloadBtn.querySelector('span').textContent;
        this.downloadBtn.querySelector('span').textContent = Locale.t('photoWall.packing');
        this.downloadBtn.disabled = true;

        try {
            const zip = new SimpleZip();

            for (const photo of photos) {
                // 取得檔案內容 (Blob)
                const blob = await fetch(photo.src).then(r => r.blob());
                // 判斷副檔名
                let ext = 'png';
                if (blob.type === 'image/jpeg') ext = 'jpg';

                zip.addFile(`photo_${photo.id}.${ext}`, blob);
            }

            const zipBlob = await zip.generateAsync();
            this._triggerDownload(URL.createObjectURL(zipBlob), `photos_${Date.now()}.zip`);

        } catch (error) {
            console.error('ZIP 打包失敗:', error);
            ModalPanel.alert({ message: Locale.t('photoWall.packError') });
        } finally {
            this.downloadBtn.querySelector('span').textContent = originalText; // Restore original text
            this._updateDownloadBtn(); // Re-evaluate button state
        }
    }

    _triggerDownload(url, filename) {
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    }

    _handleDelete(index) {
        const photo = this.photos[index];

        // 第一層確認
        ModalPanel.confirm({
            title: Locale.t('photoWall.deleteConfirmTitle'),
            message: Locale.t('photoWall.deleteConfirmMessage'),
            confirmText: Locale.t('photoWall.confirmBtn'),
            cancelText: Locale.t('photoWall.cancelBtn'),
            onConfirm: () => {
                // 第二層確認：輸入 "是"
                // Delay to allow first modal to clear completely (prevent z-index/focus timing issues)
                setTimeout(() => {
                    ModalPanel.prompt({
                        title: Locale.t('photoWall.doubleConfirmTitle'),
                        message: Locale.t('photoWall.doubleConfirmMessage'),
                        placeholder: Locale.t('photoWall.doubleConfirmPlaceholder'),
                        validate: (value) => value === Locale.t('photoWall.doubleConfirmKeyword'),
                        confirmText: Locale.t('photoWall.doubleConfirmBtn'),
                        onConfirm: () => {
                            this.removePhoto(index);
                            this.selectedIds.delete(photo.id); // 移除選取狀態
                            this._updateDownloadBtn();
                            if (this.options.onDelete) {
                                this.options.onDelete(photo.id);
                            }
                        }
                    });
                }, 300);
            }
        });
    }

    _openViewer(initialIndex) {
        let currentIndex = initialIndex;

        const updateViewer = () => {
            const photo = this.photos[currentIndex];
            ImageViewer.open(photo.src, {
                onPrev: () => {
                    currentIndex = (currentIndex - 1 + this.photos.length) % this.photos.length;
                    updateViewer();
                },
                onNext: () => {
                    currentIndex = (currentIndex + 1) % this.photos.length;
                    updateViewer();
                }
            });
        };

        updateViewer();
    }

    // Public API

    /**
     * 加入照片
     */
    addPhoto(photo) {
        this.photos.push(photo);
        this.render();
        // 保持目前的選取狀態（已在 toggleSelect 中處理重繪）
        this._updateDownloadBtn();
        this._notifyChange();
    }

    /**
     * 移除照片
     */
    removePhoto(index) {
        this.photos.splice(index, 1);
        this.render();
        this._updateDownloadBtn();
        this._notifyChange();
    }

    /**
     * 取得所有照片資料
     */
    getPhotos() {
        return [...this.photos];
    }

    /**
     * 掛載
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }
}

export default PhotoWall;
