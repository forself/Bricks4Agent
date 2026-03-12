/**
 * BatchUploader Component
 * A flexible batch file upload component with drag & drop support
 */

export class BatchUploader {
    /**
     * @param {Object} options - Configuration options
     * @param {HTMLElement|string} options.container - Container element or selector
     * @param {string} options.apiEndpoint - API endpoint for file upload
     * @param {number} [options.ruleId] - Upload rule ID
     * @param {string} [options.tableName] - Associated table name
     * @param {string} [options.tablePk] - Table primary key
     * @param {string} [options.identify] - Custom identifier
     * @param {number} [options.maxFiles=10] - Maximum number of files
     * @param {number} [options.maxFileSize=10485760] - Maximum file size in bytes (default 10MB)
     * @param {string[]} [options.allowedExtensions] - Allowed file extensions (e.g., ['.jpg', '.png'])
     * @param {boolean} [options.autoUpload=false] - Auto upload when files are added
     * @param {boolean} [options.multiple=true] - Allow multiple file selection
     * @param {string} [options.uploadMode='sequential'] - 'sequential' or 'parallel'
     * @param {Object} [options.headers] - Custom headers for upload request
     * @param {Function} [options.onFileAdded] - Callback when file is added
     * @param {Function} [options.onFileRemoved] - Callback when file is removed
     * @param {Function} [options.onProgress] - Progress callback (file, progress)
     * @param {Function} [options.onFileComplete] - Single file complete callback
     * @param {Function} [options.onComplete] - All files complete callback
     * @param {Function} [options.onError] - Error callback
     * @param {Object} [options.labels] - Custom labels for UI elements
     */
    constructor(options = {}) {
        this.options = {
            container: null,
            apiEndpoint: '/api/files/upload',
            ruleId: null,
            tableName: null,
            tablePk: null,
            identify: null,
            maxFiles: 10,
            maxFileSize: 10485760, // 10MB
            allowedExtensions: null,
            autoUpload: false,
            multiple: true,
            uploadMode: 'sequential',
            headers: {},
            onFileAdded: null,
            onFileRemoved: null,
            onProgress: null,
            onFileComplete: null,
            onComplete: null,
            onError: null,
            labels: {
                dropzone: 'Drag files here or click to select',
                browse: 'Browse Files',
                upload: 'Upload All',
                clear: 'Clear All',
                remove: 'Remove',
                retry: 'Retry',
                uploading: 'Uploading...',
                pending: 'Pending',
                success: 'Uploaded',
                error: 'Failed',
                maxFilesError: 'Maximum number of files reached',
                fileSizeError: 'File size exceeds limit',
                fileTypeError: 'File type not allowed'
            },
            ...options
        };

        this.files = [];
        this.isUploading = false;
        this.uploadedCount = 0;
        this.failedCount = 0;

        this._init();
    }

    /**
     * Initialize the component
     */
    _init() {
        const container = typeof this.options.container === 'string'
            ? document.querySelector(this.options.container)
            : this.options.container;

        if (!container) {
            throw new Error('BatchUploader: Container not found');
        }

        this.container = container;
        this.element = this._createElement();
        this.container.appendChild(this.element);

        this._bindEvents();
    }

    /**
     * Create the main element structure
     */
    _createElement() {
        const { labels, multiple } = this.options;

        const wrapper = document.createElement('div');
        wrapper.className = 'batch-uploader';
        wrapper.style.cssText = `
            font-family: var(--cl-font-family);
            width: 100%;
        `;

        // Dropzone
        const dropzone = document.createElement('div');
        dropzone.className = 'batch-uploader__dropzone';
        dropzone.style.cssText = `
            border: 2px dashed var(--cl-border);
            border-radius: var(--cl-radius-lg);
            padding: 40px 20px;
            text-align: center;
            cursor: pointer;
            transition: all var(--cl-transition-slow);
            background: var(--cl-bg-input);
        `;

        const dropzoneContent = document.createElement('div');
        dropzoneContent.innerHTML = `
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" style="margin-bottom: 12px;">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" stroke="var(--cl-text-placeholder)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <polyline points="17,8 12,3 7,8" stroke="var(--cl-text-placeholder)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <line x1="12" y1="3" x2="12" y2="15" stroke="var(--cl-text-placeholder)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            <p style="margin: 0 0 12px 0; color: var(--cl-text-secondary); font-size: var(--cl-font-size-lg);">${labels.dropzone}</p>
            <button type="button" class="batch-uploader__browse-btn" style="
                padding: 8px 20px;
                background: var(--cl-primary);
                color: var(--cl-text-inverse);
                border: none;
                border-radius: var(--cl-radius-sm);
                cursor: pointer;
                font-size: var(--cl-font-size-lg);
                transition: background var(--cl-transition);
            ">${labels.browse}</button>
        `;
        dropzone.appendChild(dropzoneContent);

        // Hidden file input
        const fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.className = 'batch-uploader__input';
        fileInput.multiple = multiple;
        fileInput.style.display = 'none';
        if (this.options.allowedExtensions) {
            fileInput.accept = this.options.allowedExtensions.join(',');
        }

        // File list container
        const fileList = document.createElement('div');
        fileList.className = 'batch-uploader__file-list';
        fileList.style.cssText = `
            margin-top: 16px;
        `;

        // Actions bar
        const actions = document.createElement('div');
        actions.className = 'batch-uploader__actions';
        actions.style.cssText = `
            display: none;
            justify-content: flex-end;
            gap: 8px;
            margin-top: 16px;
        `;

        const clearBtn = document.createElement('button');
        clearBtn.type = 'button';
        clearBtn.className = 'batch-uploader__clear-btn';
        clearBtn.textContent = labels.clear;
        clearBtn.style.cssText = `
            padding: 8px 16px;
            background: var(--cl-bg-secondary);
            color: var(--cl-text-secondary);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-sm);
            cursor: pointer;
            font-size: var(--cl-font-size-lg);
        `;

        const uploadBtn = document.createElement('button');
        uploadBtn.type = 'button';
        uploadBtn.className = 'batch-uploader__upload-btn';
        uploadBtn.textContent = labels.upload;
        uploadBtn.style.cssText = `
            padding: 8px 20px;
            background: var(--cl-success);
            color: var(--cl-text-inverse);
            border: none;
            border-radius: var(--cl-radius-sm);
            cursor: pointer;
            font-size: var(--cl-font-size-lg);
        `;

        actions.appendChild(clearBtn);
        actions.appendChild(uploadBtn);

        // Summary bar
        const summary = document.createElement('div');
        summary.className = 'batch-uploader__summary';
        summary.style.cssText = `
            display: none;
            padding: 12px;
            background: var(--cl-bg-secondary);
            border-radius: var(--cl-radius-sm);
            margin-top: 16px;
            font-size: var(--cl-font-size-lg);
            color: var(--cl-text-secondary);
        `;

        wrapper.appendChild(dropzone);
        wrapper.appendChild(fileInput);
        wrapper.appendChild(fileList);
        wrapper.appendChild(actions);
        wrapper.appendChild(summary);

        this.dropzone = dropzone;
        this.fileInput = fileInput;
        this.fileList = fileList;
        this.actionsBar = actions;
        this.clearBtn = clearBtn;
        this.uploadBtn = uploadBtn;
        this.summaryBar = summary;

        return wrapper;
    }

    /**
     * Bind event handlers
     */
    _bindEvents() {
        // Browse button click
        const browseBtn = this.dropzone.querySelector('.batch-uploader__browse-btn');
        browseBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            this.fileInput.click();
        });

        // Dropzone click
        this.dropzone.addEventListener('click', () => {
            this.fileInput.click();
        });

        // File input change
        this.fileInput.addEventListener('change', (e) => {
            this._handleFiles(e.target.files);
            this.fileInput.value = ''; // Reset for re-selection
        });

        // Drag and drop events
        this.dropzone.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.dropzone.style.borderColor = 'var(--cl-primary)';
            this.dropzone.style.background = 'var(--cl-primary-light)';
        });

        this.dropzone.addEventListener('dragleave', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.dropzone.style.borderColor = 'var(--cl-border)';
            this.dropzone.style.background = 'var(--cl-bg-input)';
        });

        this.dropzone.addEventListener('drop', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.dropzone.style.borderColor = 'var(--cl-border)';
            this.dropzone.style.background = 'var(--cl-bg-input)';
            this._handleFiles(e.dataTransfer.files);
        });

        // Action buttons
        this.clearBtn.addEventListener('click', () => this.clear());
        this.uploadBtn.addEventListener('click', () => this.upload());

        // Button hover effects
        browseBtn.addEventListener('mouseenter', () => {
            browseBtn.style.background = 'var(--cl-primary-dark)';
        });
        browseBtn.addEventListener('mouseleave', () => {
            browseBtn.style.background = 'var(--cl-primary)';
        });

        this.uploadBtn.addEventListener('mouseenter', () => {
            this.uploadBtn.style.background = 'var(--cl-success-dark)';
        });
        this.uploadBtn.addEventListener('mouseleave', () => {
            this.uploadBtn.style.background = 'var(--cl-success)';
        });
    }

    /**
     * Handle selected files
     */
    _handleFiles(fileList) {
        const { maxFiles, labels } = this.options;

        for (const file of fileList) {
            if (this.files.length >= maxFiles) {
                this._showError(labels.maxFilesError);
                break;
            }

            const validation = this._validateFile(file);
            if (!validation.valid) {
                this._showError(`${file.name}: ${validation.error}`);
                continue;
            }

            const fileItem = {
                id: this._generateId(),
                file: file,
                name: file.name,
                size: file.size,
                type: file.type,
                status: 'pending', // pending, uploading, success, error
                progress: 0,
                error: null,
                result: null
            };

            this.files.push(fileItem);
            this._renderFileItem(fileItem);

            if (this.options.onFileAdded) {
                this.options.onFileAdded(fileItem);
            }
        }

        this._updateUI();

        if (this.options.autoUpload && this.files.some(f => f.status === 'pending')) {
            this.upload();
        }
    }

    /**
     * Validate a single file
     */
    _validateFile(file) {
        const { maxFileSize, allowedExtensions, labels } = this.options;

        // Check file size
        if (file.size > maxFileSize) {
            const maxMB = (maxFileSize / (1024 * 1024)).toFixed(1);
            return { valid: false, error: `${labels.fileSizeError} (max: ${maxMB}MB)` };
        }

        // Check file extension
        if (allowedExtensions && allowedExtensions.length > 0) {
            const ext = '.' + file.name.split('.').pop().toLowerCase();
            const allowed = allowedExtensions.map(e => e.toLowerCase());
            if (!allowed.includes(ext)) {
                return { valid: false, error: labels.fileTypeError };
            }
        }

        return { valid: true };
    }

    /**
     * Render a file item in the list
     */
    _renderFileItem(fileItem) {
        const { labels } = this.options;

        const item = document.createElement('div');
        item.className = 'batch-uploader__file-item';
        item.dataset.id = fileItem.id;
        item.style.cssText = `
            display: flex;
            align-items: center;
            padding: 12px;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border-light);
            border-radius: var(--cl-radius-md);
            margin-bottom: 8px;
            transition: all var(--cl-transition);
        `;

        // File icon
        const icon = document.createElement('div');
        icon.className = 'batch-uploader__file-icon';
        icon.innerHTML = this._getFileIcon(fileItem.type);
        icon.style.cssText = `
            width: 40px;
            height: 40px;
            display: flex;
            align-items: center;
            justify-content: center;
            margin-right: 12px;
        `;

        // File info
        const info = document.createElement('div');
        info.className = 'batch-uploader__file-info';
        info.style.cssText = `
            flex: 1;
            min-width: 0;
        `;

        const name = document.createElement('div');
        name.className = 'batch-uploader__file-name';
        name.textContent = fileItem.name;
        name.style.cssText = `
            font-size: var(--cl-font-size-lg);
            color: var(--cl-text);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        `;

        const meta = document.createElement('div');
        meta.className = 'batch-uploader__file-meta';
        meta.textContent = this._formatFileSize(fileItem.size);
        meta.style.cssText = `
            font-size: var(--cl-font-size-sm);
            color: var(--cl-text-placeholder);
            margin-top: 2px;
        `;

        info.appendChild(name);
        info.appendChild(meta);

        // Progress bar
        const progressWrapper = document.createElement('div');
        progressWrapper.className = 'batch-uploader__progress-wrapper';
        progressWrapper.style.cssText = `
            width: 100px;
            margin: 0 12px;
            display: none;
        `;

        const progressBar = document.createElement('div');
        progressBar.className = 'batch-uploader__progress-bar';
        progressBar.style.cssText = `
            height: 4px;
            background: var(--cl-border-light);
            border-radius: var(--cl-radius-sm);
            overflow: hidden;
        `;

        const progressFill = document.createElement('div');
        progressFill.className = 'batch-uploader__progress-fill';
        progressFill.style.cssText = `
            height: 100%;
            width: 0%;
            background: var(--cl-primary);
            transition: width var(--cl-transition);
        `;

        progressBar.appendChild(progressFill);
        progressWrapper.appendChild(progressBar);

        // Status
        const status = document.createElement('div');
        status.className = 'batch-uploader__file-status';
        status.textContent = labels.pending;
        status.style.cssText = `
            font-size: var(--cl-font-size-sm);
            color: var(--cl-text-placeholder);
            min-width: 70px;
            text-align: center;
        `;

        // Actions
        const actions = document.createElement('div');
        actions.className = 'batch-uploader__file-actions';
        actions.style.cssText = `
            display: flex;
            gap: 8px;
            margin-left: 12px;
        `;

        const removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'batch-uploader__remove-btn';
        removeBtn.innerHTML = `
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none">
                <line x1="18" y1="6" x2="6" y2="18" stroke="var(--cl-text-placeholder)" stroke-width="2" stroke-linecap="round"/>
                <line x1="6" y1="6" x2="18" y2="18" stroke="var(--cl-text-placeholder)" stroke-width="2" stroke-linecap="round"/>
            </svg>
        `;
        removeBtn.title = labels.remove;
        removeBtn.style.cssText = `
            border: none;
            background: transparent;
            cursor: pointer;
            padding: 4px;
            border-radius: var(--cl-radius-sm);
            display: flex;
            align-items: center;
            justify-content: center;
            transition: background var(--cl-transition);
        `;
        removeBtn.addEventListener('click', () => this.removeFile(fileItem.id));
        removeBtn.addEventListener('mouseenter', () => {
            removeBtn.style.background = 'var(--cl-bg-danger-light)';
        });
        removeBtn.addEventListener('mouseleave', () => {
            removeBtn.style.background = 'transparent';
        });

        actions.appendChild(removeBtn);

        item.appendChild(icon);
        item.appendChild(info);
        item.appendChild(progressWrapper);
        item.appendChild(status);
        item.appendChild(actions);

        this.fileList.appendChild(item);
    }

    /**
     * Update file item UI
     */
    _updateFileItem(fileItem) {
        const { labels } = this.options;
        const item = this.fileList.querySelector(`[data-id="${fileItem.id}"]`);
        if (!item) return;

        const progressWrapper = item.querySelector('.batch-uploader__progress-wrapper');
        const progressFill = item.querySelector('.batch-uploader__progress-fill');
        const status = item.querySelector('.batch-uploader__file-status');
        const removeBtn = item.querySelector('.batch-uploader__remove-btn');

        switch (fileItem.status) {
            case 'uploading':
                progressWrapper.style.display = 'block';
                progressFill.style.width = `${fileItem.progress}%`;
                status.textContent = `${fileItem.progress}%`;
                status.style.color = 'var(--cl-primary)';
                removeBtn.style.display = 'none';
                break;

            case 'success':
                progressWrapper.style.display = 'none';
                status.textContent = labels.success;
                status.style.color = 'var(--cl-success)';
                item.style.background = 'var(--cl-bg-success-light)';
                removeBtn.style.display = 'flex';
                break;

            case 'error':
                progressWrapper.style.display = 'none';
                status.textContent = labels.error;
                status.style.color = 'var(--cl-danger)';
                item.style.background = 'var(--cl-bg-danger-light)';
                removeBtn.style.display = 'flex';

                // Add retry button
                const actions = item.querySelector('.batch-uploader__file-actions');
                if (!actions.querySelector('.batch-uploader__retry-btn')) {
                    const retryBtn = document.createElement('button');
                    retryBtn.type = 'button';
                    retryBtn.className = 'batch-uploader__retry-btn';
                    retryBtn.textContent = labels.retry;
                    retryBtn.style.cssText = `
                        border: 1px solid var(--cl-danger);
                        background: transparent;
                        color: var(--cl-danger);
                        cursor: pointer;
                        padding: 2px 8px;
                        border-radius: var(--cl-radius-sm);
                        font-size: var(--cl-font-size-sm);
                    `;
                    retryBtn.addEventListener('click', () => this._retryFile(fileItem));
                    actions.insertBefore(retryBtn, actions.firstChild);
                }
                break;

            default:
                progressWrapper.style.display = 'none';
                status.textContent = labels.pending;
                status.style.color = 'var(--cl-text-placeholder)';
                item.style.background = 'var(--cl-bg)';
        }
    }

    /**
     * Upload all pending files
     */
    async upload() {
        if (this.isUploading) return;

        const pendingFiles = this.files.filter(f => f.status === 'pending' || f.status === 'error');
        if (pendingFiles.length === 0) return;

        this.isUploading = true;
        this.uploadedCount = 0;
        this.failedCount = 0;
        this._updateUI();

        if (this.options.uploadMode === 'parallel') {
            await Promise.all(pendingFiles.map(f => this._uploadFile(f)));
        } else {
            for (const fileItem of pendingFiles) {
                await this._uploadFile(fileItem);
            }
        }

        this.isUploading = false;
        this._updateUI();
        this._showSummary();

        if (this.options.onComplete) {
            this.options.onComplete({
                total: pendingFiles.length,
                success: this.uploadedCount,
                failed: this.failedCount,
                files: this.files
            });
        }
    }

    /**
     * Upload a single file
     */
    async _uploadFile(fileItem) {
        fileItem.status = 'uploading';
        fileItem.progress = 0;
        this._updateFileItem(fileItem);

        const formData = new FormData();
        formData.append('file', fileItem.file);

        if (this.options.ruleId) {
            formData.append('ruleId', this.options.ruleId);
        }
        if (this.options.tableName) {
            formData.append('tableName', this.options.tableName);
        }
        if (this.options.tablePk) {
            formData.append('tablePk', this.options.tablePk);
        }
        if (this.options.identify) {
            formData.append('identify', this.options.identify);
        }

        try {
            const result = await this._sendRequest(formData, (progress) => {
                fileItem.progress = progress;
                this._updateFileItem(fileItem);

                if (this.options.onProgress) {
                    this.options.onProgress(fileItem, progress);
                }
            });

            fileItem.status = 'success';
            fileItem.result = result;
            this.uploadedCount++;

            if (this.options.onFileComplete) {
                this.options.onFileComplete(fileItem, result);
            }
        } catch (error) {
            fileItem.status = 'error';
            fileItem.error = error.message || 'Upload failed';
            this.failedCount++;

            if (this.options.onError) {
                this.options.onError(fileItem, error);
            }
        }

        this._updateFileItem(fileItem);
    }

    /**
     * Send upload request with progress tracking
     */
    _sendRequest(formData, onProgress) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();

            xhr.upload.addEventListener('progress', (e) => {
                if (e.lengthComputable) {
                    const progress = Math.round((e.loaded / e.total) * 100);
                    onProgress(progress);
                }
            });

            xhr.addEventListener('load', () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                    try {
                        const response = JSON.parse(xhr.responseText);
                        resolve(response);
                    } catch {
                        resolve(xhr.responseText);
                    }
                } else {
                    let errorMessage = `Upload failed (${xhr.status})`;
                    try {
                        const response = JSON.parse(xhr.responseText);
                        errorMessage = response.message || response.error || errorMessage;
                    } catch {
                        // Keep default error message
                    }
                    reject(new Error(errorMessage));
                }
            });

            xhr.addEventListener('error', () => {
                reject(new Error('Network error'));
            });

            xhr.addEventListener('abort', () => {
                reject(new Error('Upload aborted'));
            });

            xhr.open('POST', this.options.apiEndpoint, true);

            // Set custom headers
            for (const [key, value] of Object.entries(this.options.headers)) {
                xhr.setRequestHeader(key, value);
            }

            xhr.send(formData);
        });
    }

    /**
     * Retry a failed file
     */
    async _retryFile(fileItem) {
        fileItem.status = 'pending';
        fileItem.error = null;
        fileItem.progress = 0;
        this._updateFileItem(fileItem);

        // Remove retry button
        const item = this.fileList.querySelector(`[data-id="${fileItem.id}"]`);
        const retryBtn = item?.querySelector('.batch-uploader__retry-btn');
        if (retryBtn) {
            retryBtn.remove();
        }

        await this._uploadFile(fileItem);
        this._showSummary();
    }

    /**
     * Remove a file from the list
     */
    removeFile(fileId) {
        const index = this.files.findIndex(f => f.id === fileId);
        if (index === -1) return;

        const fileItem = this.files[index];
        this.files.splice(index, 1);

        // Remove from DOM
        const item = this.fileList.querySelector(`[data-id="${fileId}"]`);
        if (item) {
            item.remove();
        }

        if (this.options.onFileRemoved) {
            this.options.onFileRemoved(fileItem);
        }

        this._updateUI();
    }

    /**
     * Clear all files
     */
    clear() {
        this.files = [];
        this.fileList.innerHTML = '';
        this.uploadedCount = 0;
        this.failedCount = 0;
        this._updateUI();
    }

    /**
     * Update UI state
     */
    _updateUI() {
        const hasFiles = this.files.length > 0;
        const hasPending = this.files.some(f => f.status === 'pending' || f.status === 'error');

        this.actionsBar.style.display = hasFiles ? 'flex' : 'none';
        this.uploadBtn.disabled = this.isUploading || !hasPending;
        this.uploadBtn.textContent = this.isUploading
            ? this.options.labels.uploading
            : this.options.labels.upload;
        this.uploadBtn.style.opacity = (this.isUploading || !hasPending) ? '0.6' : '1';
        this.uploadBtn.style.cursor = (this.isUploading || !hasPending) ? 'not-allowed' : 'pointer';

        this.clearBtn.disabled = this.isUploading;
        this.clearBtn.style.opacity = this.isUploading ? '0.6' : '1';
        this.clearBtn.style.cursor = this.isUploading ? 'not-allowed' : 'pointer';

        // Update dropzone
        if (this.files.length >= this.options.maxFiles) {
            this.dropzone.style.opacity = '0.5';
            this.dropzone.style.pointerEvents = 'none';
        } else {
            this.dropzone.style.opacity = '1';
            this.dropzone.style.pointerEvents = 'auto';
        }
    }

    /**
     * Show upload summary
     */
    _showSummary() {
        if (this.uploadedCount === 0 && this.failedCount === 0) {
            this.summaryBar.style.display = 'none';
            return;
        }

        let message = '';
        if (this.uploadedCount > 0 && this.failedCount === 0) {
            message = `All ${this.uploadedCount} file(s) uploaded successfully.`;
            this.summaryBar.style.background = 'var(--cl-success-light)';
            this.summaryBar.style.color = 'var(--cl-success-dark)';
        } else if (this.uploadedCount === 0 && this.failedCount > 0) {
            message = `All ${this.failedCount} file(s) failed to upload.`;
            this.summaryBar.style.background = 'var(--cl-bg-danger-light)';
            this.summaryBar.style.color = 'var(--cl-danger)';
        } else {
            message = `${this.uploadedCount} file(s) uploaded, ${this.failedCount} failed.`;
            this.summaryBar.style.background = 'var(--cl-warning-light)';
            this.summaryBar.style.color = 'var(--cl-warning-dark)';
        }

        this.summaryBar.textContent = message;
        this.summaryBar.style.display = 'block';
    }

    /**
     * Show error message
     */
    _showError(message) {
        console.warn('[BatchUploader]', message);

        if (this.options.onError) {
            this.options.onError(null, new Error(message));
        }
    }

    /**
     * Get file icon based on type
     */
    _getFileIcon(mimeType) {
        const type = mimeType?.split('/')[0];

        const icons = {
            image: `<svg width="32" height="32" viewBox="0 0 24 24" fill="none">
                <rect x="3" y="3" width="18" height="18" rx="2" stroke="var(--cl-success)" stroke-width="2"/>
                <circle cx="8.5" cy="8.5" r="1.5" fill="var(--cl-success)"/>
                <path d="M21 15l-5-5-4 4-3-3-6 6" stroke="var(--cl-success)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>`,
            video: `<svg width="32" height="32" viewBox="0 0 24 24" fill="none">
                <rect x="2" y="4" width="20" height="16" rx="2" stroke="var(--cl-purple)" stroke-width="2"/>
                <polygon points="10,8 10,16 16,12" fill="var(--cl-purple)"/>
            </svg>`,
            audio: `<svg width="32" height="32" viewBox="0 0 24 24" fill="none">
                <path d="M9 18V5l12-2v13" stroke="var(--cl-warning)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <circle cx="6" cy="18" r="3" stroke="var(--cl-warning)" stroke-width="2"/>
                <circle cx="18" cy="16" r="3" stroke="var(--cl-warning)" stroke-width="2"/>
            </svg>`,
            application: `<svg width="32" height="32" viewBox="0 0 24 24" fill="none">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" stroke="var(--cl-primary)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <polyline points="14,2 14,8 20,8" stroke="var(--cl-primary)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>`,
            default: `<svg width="32" height="32" viewBox="0 0 24 24" fill="none">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" stroke="var(--cl-grey)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <polyline points="14,2 14,8 20,8" stroke="var(--cl-grey)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>`
        };

        return icons[type] || icons.default;
    }

    /**
     * Format file size
     */
    _formatFileSize(bytes) {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    }

    /**
     * Generate unique ID
     */
    _generateId() {
        return 'file_' + Math.random().toString(36).substr(2, 9);
    }

    /**
     * Get all files
     */
    getFiles() {
        return [...this.files];
    }

    /**
     * Get pending files
     */
    getPendingFiles() {
        return this.files.filter(f => f.status === 'pending');
    }

    /**
     * Get uploaded files
     */
    getUploadedFiles() {
        return this.files.filter(f => f.status === 'success');
    }

    /**
     * Get failed files
     */
    getFailedFiles() {
        return this.files.filter(f => f.status === 'error');
    }

    /**
     * Set options dynamically
     */
    setOptions(options) {
        this.options = { ...this.options, ...options };
    }

    /**
     * Destroy the component
     */
    destroy() {
        if (this.element?.parentNode) {
            this.element.remove();
        }
        this.files = [];
    }
}

export default BatchUploader;
