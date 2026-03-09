import { hasPathTraversalRisk } from '../../utils/security.js';
import Locale from '../../i18n/index.js';

import { ModalPanel } from '../../layout/Panel/index.js';

/**
 * UploadButton Component
 * 上傳按鈕元件 - 支援 8 種類型：XLS、Word、PDF、Image、Portrait、File、TXT、CSV
 */

export class UploadButton {
    static TYPES = {
        XLS: 'xls',
        WORD: 'word',
        PDF: 'pdf',
        IMAGE: 'image',
        PORTRAIT: 'portrait',
        FILE: 'file',
        TXT: 'txt',
        CSV: 'csv'
    };

    static ACCEPT_TYPES = {
        xls: '.xls,.xlsx,.xlsm,application/vnd.ms-excel,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        word: '.doc,.docx,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        pdf: '.pdf,application/pdf',
        image: 'image/*',
        portrait: 'image/*',
        file: '*/*',
        txt: '.txt,text/plain',
        csv: '.csv,text/csv,application/csv'
    };

    static ICONS = {
        xls: {
            color: 'var(--cl-brand-excel)',
            label: 'XLS',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-brand-excel)"/>
                <rect x="8" y="12" width="32" height="24" rx="2" fill="white" fill-opacity="0.9"/>
                <line x1="8" y1="20" x2="40" y2="20" stroke="var(--cl-brand-excel)" stroke-width="1.5"/>
                <line x1="8" y1="28" x2="40" y2="28" stroke="var(--cl-brand-excel)" stroke-width="1.5"/>
                <line x1="20" y1="12" x2="20" y2="36" stroke="var(--cl-brand-excel)" stroke-width="1.5"/>
                <line x1="32" y1="12" x2="32" y2="36" stroke="var(--cl-brand-excel)" stroke-width="1.5"/>
                <path d="M24 4L30 10H27V13H21V10H18L24 4Z" fill="white"/>
            </svg>`
        },
        word: {
            color: 'var(--cl-brand-word)',
            label: 'DOC',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-brand-word)"/>
                <rect x="10" y="12" width="28" height="24" rx="2" fill="white" fill-opacity="0.9"/>
                <line x1="14" y1="18" x2="34" y2="18" stroke="var(--cl-brand-word)" stroke-width="2" stroke-linecap="round"/>
                <line x1="14" y1="24" x2="30" y2="24" stroke="var(--cl-brand-word)" stroke-width="2" stroke-linecap="round"/>
                <line x1="14" y1="30" x2="26" y2="30" stroke="var(--cl-brand-word)" stroke-width="2" stroke-linecap="round"/>
                <path d="M24 4L30 10H27V13H21V10H18L24 4Z" fill="white"/>
            </svg>`
        },
        pdf: {
            color: 'var(--cl-danger)',
            label: 'PDF',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-danger)"/>
                <path d="M10 12H32L38 18V36H10V12Z" fill="white" fill-opacity="0.9"/>
                <path d="M32 12V18H38" fill="var(--cl-bg-danger-lighter)" stroke="var(--cl-bg-danger-lighter)" stroke-width="1"/>
                <text x="24" y="29" font-family="Arial" font-size="8" font-weight="bold" fill="var(--cl-danger)" text-anchor="middle">PDF</text>
                <path d="M24 4L30 10H27V13H21V10H18L24 4Z" fill="white"/>
            </svg>`
        },
        image: {
            color: 'var(--cl-purple-dark)',
            label: 'IMG',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-purple-dark)"/>
                <rect x="8" y="12" width="32" height="24" rx="3" fill="white" fill-opacity="0.9"/>
                <circle cx="16" cy="20" r="4" fill="var(--cl-warning)"/>
                <path d="M8 36L18 24L26 32L32 26L40 36H8Z" fill="var(--cl-purple-dark)"/>
                <path d="M24 4L30 10H27V13H21V10H18L24 4Z" fill="white"/>
            </svg>`
        },
        portrait: {
            color: 'var(--cl-cyan-dark)',
            label: 'PHOTO',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-cyan-dark)"/>
                <rect x="10" y="10" width="28" height="28" rx="3" fill="white" fill-opacity="0.9"/>
                <circle cx="24" cy="20" r="7" fill="var(--cl-cyan-dark)"/>
                <ellipse cx="24" cy="36" rx="10" ry="8" fill="var(--cl-cyan-dark)"/>
                <rect x="10" y="32" width="28" height="6" fill="white" fill-opacity="0.9"/>
                <path d="M14 38Q24 28 34 38Z" fill="var(--cl-cyan-dark)"/>
                <path d="M24 2L30 8H27V11H21V8H18L24 2Z" fill="white"/>
            </svg>`
        },
        file: {
            color: 'var(--cl-blue-grey)',
            label: 'FILE',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-blue-grey)"/>
                <path d="M12 10H30L36 16V38H12V10Z" fill="white" fill-opacity="0.9"/>
                <path d="M30 10V16H36" fill="var(--cl-border-muted)" stroke="var(--cl-border-muted)" stroke-width="1"/>
                <line x1="16" y1="22" x2="32" y2="22" stroke="var(--cl-blue-grey)" stroke-width="2" stroke-linecap="round"/>
                <line x1="16" y1="28" x2="28" y2="28" stroke="var(--cl-blue-grey)" stroke-width="2" stroke-linecap="round"/>
                <line x1="16" y1="34" x2="24" y2="34" stroke="var(--cl-blue-grey)" stroke-width="2" stroke-linecap="round"/>
                <path d="M24 2L30 8H27V11H21V8H18L24 2Z" fill="white"/>
            </svg>`
        },
        txt: {
            color: 'var(--cl-blue-grey-dark)',
            label: 'TXT',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-blue-grey-dark)"/>
                <rect x="10" y="10" width="28" height="28" rx="2" fill="white" fill-opacity="0.9"/>
                <line x1="14" y1="18" x2="34" y2="18" stroke="var(--cl-blue-grey-dark)" stroke-width="1.5"/>
                <line x1="14" y1="23" x2="34" y2="23" stroke="var(--cl-blue-grey-dark)" stroke-width="1.5"/>
                <line x1="14" y1="28" x2="34" y2="28" stroke="var(--cl-blue-grey-dark)" stroke-width="1.5"/>
                <line x1="14" y1="33" x2="26" y2="33" stroke="var(--cl-blue-grey-dark)" stroke-width="1.5"/>
                <path d="M24 2L30 8H27V11H21V8H18L24 2Z" fill="white"/>
            </svg>`
        },
        csv: {
            color: 'var(--cl-success)',
            label: 'CSV',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-success)"/>
                <rect x="8" y="12" width="32" height="24" rx="2" fill="white" fill-opacity="0.9"/>
                <!-- 表格格線 -->
                <line x1="8" y1="20" x2="40" y2="20" stroke="var(--cl-success)" stroke-width="1"/>
                <line x1="8" y1="28" x2="40" y2="28" stroke="var(--cl-success)" stroke-width="1"/>
                <line x1="18" y1="12" x2="18" y2="36" stroke="var(--cl-success)" stroke-width="1"/>
                <line x1="28" y1="12" x2="28" y2="36" stroke="var(--cl-success)" stroke-width="1"/>
                <!-- 逗號符號 -->
                <text x="13" y="18" font-family="Arial" font-size="6" fill="var(--cl-success)">,</text>
                <text x="23" y="18" font-family="Arial" font-size="6" fill="var(--cl-success)">,</text>
                <path d="M24 4L30 10H27V13H21V10H18L24 4Z" fill="white"/>
            </svg>`
        }
    };

    /**
     * 建立上傳按鈕
     * @param {Object} options - 設定選項
     * @param {string} options.type - 按鈕類型
     * @param {Function} options.onSelect - 選擇檔案回調 (files) => void
     * @param {Function} options.onUpload - 上傳回調 (files, progressCallback) => Promise
     * @param {string} options.size - 按鈕尺寸
     * @param {boolean} options.showLabel - 是否顯示標籤
     * @param {boolean} options.multiple - 是否允許多選
     * @param {string} options.tooltip - 滑鼠提示文字
     * @param {number} options.maxSize - 最大檔案大小 (bytes)
     * @param {string} options.customAccept - 自訂 accept 屬性
     */
    constructor(options = {}) {
        this.options = {
            type: 'file',
            onSelect: null,
            onUpload: null,
            size: 'medium',
            showLabel: false,
            multiple: false,
            tooltip: '',
            maxSize: 50 * 1024 * 1024, // 50MB
            customAccept: null,
            ...options
        };

        this.fileInput = null;
        this.element = this._createElement();
    }

    _getSizeValue() {
        const sizes = { small: 32, medium: 48, large: 64 };
        return sizes[this.options.size] || 48;
    }

    _createElement() {
        const { type, showLabel, tooltip, multiple, customAccept } = this.options;
        const iconConfig = UploadButton.ICONS[type] || UploadButton.ICONS.file;
        const size = this._getSizeValue();
        const acceptType = customAccept || UploadButton.ACCEPT_TYPES[type] || '*/*';

        // 建立容器
        const container = document.createElement('div');
        container.className = 'upload-btn-container';
        container.style.cssText = `
            display: inline-flex;
            flex-direction: column;
            align-items: center;
            gap: 4px;
            position: relative;
        `;

        // 建立隱藏的 file input
        this.fileInput = document.createElement('input');
        this.fileInput.type = 'file';
        this.fileInput.accept = acceptType;
        this.fileInput.multiple = multiple;
        this.fileInput.style.display = 'none';
        this.fileInput.addEventListener('change', (e) => this._handleFileSelect(e));

        // 建立按鈕
        const button = document.createElement('button');
        button.className = `upload-btn upload-btn--${type}`;
        button.setAttribute('type', 'button');
        button.setAttribute('title', tooltip || Locale.t('upload.uploadLabel', { label: iconConfig.label }));
        button.setAttribute('aria-label', Locale.t('upload.uploadAriaLabel', { label: iconConfig.label }));

        button.style.cssText = `
            width: ${size}px;
            height: ${size}px;
            padding: 0;
            border: 2px dashed ${iconConfig.color}60;
            border-radius: 8px;
            cursor: pointer;
            transition: all 0.2s ease;
            background: transparent;
            position: relative;
            overflow: hidden;
        `;

        button.innerHTML = iconConfig.svg;

        // SVG 填滿按鈕
        const svg = button.querySelector('svg');
        if (svg) {
            svg.style.cssText = `
                width: 100%;
                height: 100%;
                display: block;
            `;
        }

        // Hover 效果
        button.addEventListener('mouseenter', () => {
            button.style.transform = 'translateY(-2px)';
            button.style.borderStyle = 'solid';
            button.style.boxShadow = `0 4px 12px ${iconConfig.color}40`;
        });

        button.addEventListener('mouseleave', () => {
            button.style.transform = 'translateY(0)';
            button.style.borderStyle = 'dashed';
            button.style.boxShadow = 'none';
        });

        // 點擊開啟檔案選擇
        button.addEventListener('click', () => {
            console.log('[UploadButton] Button clicked, triggering file input');
            this.fileInput.click();
        });

        // 拖放支援
        button.addEventListener('dragover', (e) => {
            e.preventDefault();
            button.style.borderColor = iconConfig.color;
            button.style.background = `${iconConfig.color}10`;
        });

        button.addEventListener('dragleave', () => {
            button.style.borderColor = `${iconConfig.color}60`;
            button.style.background = 'transparent';
        });

        button.addEventListener('drop', (e) => {
            e.preventDefault();
            button.style.borderColor = `${iconConfig.color}60`;
            button.style.background = 'transparent';

            const files = Array.from(e.dataTransfer.files);
            this._processFiles(files);
        });

        container.appendChild(this.fileInput);
        container.appendChild(button);

        // 標籤
        if (showLabel) {
            const label = document.createElement('span');
            label.className = 'upload-btn-label';
            label.textContent = iconConfig.label;
            label.style.cssText = `
                font-size: 10px;
                font-weight: 600;
                color: ${iconConfig.color};
                text-transform: uppercase;
            `;
            container.appendChild(label);
        }

        this.button = button;
        return container;
    }

    _handleFileSelect(e) {
        console.log('[UploadButton] File input changed, files:', e.target.files);
        const files = Array.from(e.target.files);
        this._processFiles(files);
        // 重置 input 以便再次選擇相同檔案
        e.target.value = '';
    }

    // 常見檔案的 Magic Number
    static MAGIC_NUMBERS = {
        // 圖片
        'jpeg': { bytes: [0xFF, 0xD8, 0xFF], offset: 0 },
        'jpg': { bytes: [0xFF, 0xD8, 0xFF], offset: 0 },
        'png': { bytes: [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], offset: 0 },
        'gif': { bytes: [0x47, 0x49, 0x46, 0x38], offset: 0 }, // GIF87a 或 GIF89a
        'bmp': { bytes: [0x42, 0x4D], offset: 0 },
        'webp': { bytes: [0x52, 0x49, 0x46, 0x46], offset: 0 }, // RIFF
        // 文件
        'pdf': { bytes: [0x25, 0x50, 0x44, 0x46], offset: 0 }, // %PDF
        'zip': { bytes: [0x50, 0x4B, 0x03, 0x04], offset: 0 }, // PK.. (docx/xlsx/pptx 都是 zip)
        'docx': { bytes: [0x50, 0x4B, 0x03, 0x04], offset: 0 },
        'xlsx': { bytes: [0x50, 0x4B, 0x03, 0x04], offset: 0 },
        'pptx': { bytes: [0x50, 0x4B, 0x03, 0x04], offset: 0 },
        // 舊版 Office (Compound File Binary Format)
        'doc': { bytes: [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], offset: 0 },
        'xls': { bytes: [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], offset: 0 },
        'ppt': { bytes: [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], offset: 0 },
    };

    // 根據上傳類型取得允許的 Magic Number
    static ALLOWED_MAGIC_BY_TYPE = {
        'image': ['jpeg', 'jpg', 'png', 'gif', 'bmp', 'webp'],
        'portrait': ['jpeg', 'jpg', 'png'],
        'pdf': ['pdf'],
        'xls': ['xlsx', 'xls'],
        'word': ['docx', 'doc'],
        'csv': [], // 純文字無 Magic Number
        'txt': [], // 純文字無 Magic Number
        'file': [] // 通用檔案不檢查
    };

    async _processFiles(files) {
        const { onSelect, onUpload, maxSize, type } = this.options;
        const validFiles = [];

        for (const file of files) {
            // 1. 安全性檢查：路徑遍歷
            if (hasPathTraversalRisk(file.name)) {
                console.error(`Security Alert: File name "${file.name}" detected with Path Traversal risk.`);
                ModalPanel.alert({ message: Locale.t('upload.securityPathTraversal', { name: file.name }) });
                continue;
            }

            // 2. 檢查檔案大小
            if (file.size > maxSize) {
                console.warn(`檔案 ${file.name} 超過大小限制 (${this._formatSize(maxSize)})`);
                ModalPanel.alert({ message: Locale.t('upload.fileTooLarge', { name: file.name, max: this._formatSize(maxSize) }) });
                continue;
            }

            // 3. Magic Number 驗證 (針對圖片、PDF、Office 文件)
            const magicValid = await this._validateMagicNumber(file, type);
            if (!magicValid) {
                continue; // 警告已在驗證函數中顯示
            }

            // 4. UTF-8 編碼驗證 (針對 TXT/CSV)
            if (type === 'txt' || type === 'csv') {
                const utf8Valid = await this._validateUTF8(file);
                if (!utf8Valid) {
                    continue; // 警告已在驗證函數中顯示
                }
            }

            validFiles.push(file);
        }

        if (validFiles.length === 0) return;

        // 觸發選擇回調
        if (onSelect) {
            onSelect(validFiles, { type });
        }

        // 自動上傳
        if (onUpload) {
            this._setLoading(true);
            onUpload(validFiles, (progress) => this._updateProgress(progress))
                .then(() => {
                    this._setLoading(false);
                })
                .catch((err) => {
                    console.error('上傳失敗:', err);
                    this._setLoading(false);
                });
        }
    }

    /**
     * 驗證檔案的 Magic Number
     */
    async _validateMagicNumber(file, uploadType) {
        const allowedFormats = UploadButton.ALLOWED_MAGIC_BY_TYPE[uploadType] || [];
        
        // 如果此類型不需檢查 Magic Number，直接通過
        if (allowedFormats.length === 0) return true;

        // 取得檔案副檔名
        const ext = file.name.split('.').pop()?.toLowerCase();
        
        // 讀取檔案前 16 bytes
        const headerBytes = await this._readFileHeader(file, 16);
        if (!headerBytes) return true; // 讀取失敗時放行

        // 檢查是否符合任一允許的格式
        let matched = false;
        let expectedFormat = '';

        for (const format of allowedFormats) {
            const magic = UploadButton.MAGIC_NUMBERS[format];
            if (!magic) continue;

            const isMatch = magic.bytes.every((byte, i) => headerBytes[magic.offset + i] === byte);
            if (isMatch) {
                matched = true;
                expectedFormat = format;
                break;
            }
        }

        if (!matched) {
            console.error(`[Security] 檔案 "${file.name}" 的內容與預期格式不符`);
            console.error(`  - 檔案標頭: ${Array.from(headerBytes.slice(0, 8)).map(b => b.toString(16).padStart(2, '0')).join(' ')}`);
            ModalPanel.alert({ message: Locale.t('upload.formatMismatch', { name: file.name }) });
            return false;
        }

        // 額外檢查：副檔名是否與實際內容一致 (可選)
        if (ext && expectedFormat) {
            const extLower = ext.toLowerCase();
            // jpeg/jpg 視為相同
            const normalizedExt = extLower === 'jpeg' ? 'jpg' : extLower;
            const normalizedFormat = expectedFormat === 'jpeg' ? 'jpg' : expectedFormat;
            
            // docx/xlsx 都是 zip 格式，允許
            const zipFormats = ['docx', 'xlsx', 'pptx', 'zip'];
            if (zipFormats.includes(normalizedFormat) && zipFormats.includes(normalizedExt)) {
                return true;
            }

            if (normalizedExt !== normalizedFormat) {
                console.warn(`[Warning] 檔案 "${file.name}" 的副檔名 (.${ext}) 與實際內容 (${expectedFormat}) 不一致`);
                // 這裡可選擇是否阻擋，目前只警告
            }
        }

        return true;
    }

    /**
     * 讀取檔案標頭
     */
    _readFileHeader(file, bytes) {
        return new Promise((resolve) => {
            const reader = new FileReader();
            reader.onload = (e) => {
                resolve(new Uint8Array(e.target.result));
            };
            reader.onerror = () => {
                console.warn('無法讀取檔案標頭');
                resolve(null);
            };
            reader.readAsArrayBuffer(file.slice(0, bytes));
        });
    }

    /**
     * 驗證檔案是否為 UTF-8 編碼 (含或不含 BOM)
     */
    async _validateUTF8(file) {
        // 讀取檔案前 64KB 來驗證 (足以判斷編碼)
        const sampleSize = Math.min(file.size, 64 * 1024);
        const bytes = await this._readFileBytes(file, 0, sampleSize);
        
        if (!bytes) {
            console.warn('無法讀取檔案內容');
            return true; // 讀取失敗時放行
        }

        // 檢查 UTF-8 BOM (EF BB BF)
        let startIndex = 0;
        if (bytes.length >= 3 && bytes[0] === 0xEF && bytes[1] === 0xBB && bytes[2] === 0xBF) {
            console.log(`[UTF-8] 檔案 "${file.name}" 含 BOM`);
            startIndex = 3; // 跳過 BOM
        }

        // 檢查是否有其他編碼的 BOM (表示非 UTF-8)
        // UTF-16 LE: FF FE
        if (bytes.length >= 2 && bytes[0] === 0xFF && bytes[1] === 0xFE) {
            ModalPanel.alert({ message: Locale.t('upload.encodingUtf16LE', { name: file.name }) });
            return false;
        }
        // UTF-16 BE: FE FF
        if (bytes.length >= 2 && bytes[0] === 0xFE && bytes[1] === 0xFF) {
            ModalPanel.alert({ message: Locale.t('upload.encodingUtf16BE', { name: file.name }) });
            return false;
        }

        // 驗證 UTF-8 byte sequences
        const isValid = this._isValidUTF8(bytes, startIndex);
        
        if (!isValid) {
            console.error(`[Encoding] 檔案 "${file.name}" 不是有效的 UTF-8 編碼`);
            ModalPanel.alert({ message: Locale.t('upload.encodingInvalid', { name: file.name }) });
            return false;
        }

        console.log(`[UTF-8] 檔案 "${file.name}" 驗證通過`);
        return true;
    }

    /**
     * 驗證 byte array 是否為有效 UTF-8 序列
     */
    _isValidUTF8(bytes, startIndex = 0) {
        let i = startIndex;
        while (i < bytes.length) {
            const byte = bytes[i];
            let expectedContinuationBytes = 0;

            if ((byte & 0x80) === 0x00) {
                // 1-byte: 0xxxxxxx (ASCII)
                i++;
                continue;
            } else if ((byte & 0xE0) === 0xC0) {
                // 2-byte: 110xxxxx 10xxxxxx
                expectedContinuationBytes = 1;
            } else if ((byte & 0xF0) === 0xE0) {
                // 3-byte: 1110xxxx 10xxxxxx 10xxxxxx
                expectedContinuationBytes = 2;
            } else if ((byte & 0xF8) === 0xF0) {
                // 4-byte: 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx
                expectedContinuationBytes = 3;
            } else {
                // 無效的起始 byte
                return false;
            }

            // 檢查後續的 continuation bytes (10xxxxxx)
            for (let j = 1; j <= expectedContinuationBytes; j++) {
                if (i + j >= bytes.length) {
                    // 不完整的序列 (在檔案結尾可接受)
                    return true;
                }
                if ((bytes[i + j] & 0xC0) !== 0x80) {
                    // 無效的 continuation byte
                    return false;
                }
            }

            i += 1 + expectedContinuationBytes;
        }
        return true;
    }

    /**
     * 讀取檔案指定範圍的 bytes
     */
    _readFileBytes(file, start, end) {
        return new Promise((resolve) => {
            const reader = new FileReader();
            reader.onload = (e) => {
                resolve(new Uint8Array(e.target.result));
            };
            reader.onerror = () => {
                resolve(null);
            };
            reader.readAsArrayBuffer(file.slice(start, end));
        });
    }

    _formatSize(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }

    _setLoading(loading) {
        if (loading) {
            this.button.classList.add('upload-btn--loading');
            this.button.disabled = true;
        } else {
            this.button.classList.remove('upload-btn--loading');
            this.button.disabled = false;
        }
    }

    _updateProgress(progress) {
        // 可擴展為進度條顯示
        console.log(`上傳進度: ${progress}%`);
    }

    /**
     * 掛載到指定容器
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;

        if (target) {
            target.appendChild(this.element);
        }
        return this;
    }

    /**
     * 移除元件
     */
    destroy() {
        this.element?.remove();
    }

    /**
     * 建立上傳按鈕群組
     */
    static createGroup(buttons, groupOptions = {}) {
        const group = document.createElement('div');
        group.className = 'upload-btn-group';
        group.style.cssText = `
            display: inline-flex;
            gap: ${groupOptions.gap || '8px'};
            align-items: flex-start;
        `;

        buttons.forEach(btnOptions => {
            const btn = new UploadButton({ ...groupOptions, ...btnOptions });
            group.appendChild(btn.element);
        });

        return group;
    }
}

export default UploadButton;
