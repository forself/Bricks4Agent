/**
 * PageGeneratorPage - 生成頁面
 *
 * @module PageGeneratorPage
 */

import { BasePage } from '../../core/BasePage.js';

export class PageGeneratorPage extends BasePage {
    async onInit() {
        this._data = {
            form: {
                pageName: '',
                isDetail: false
            },
            preview: null,
            generating: false,
            result: null,
            error: null
        };
    }

    template() {
        const { form, preview, generating, result, error } = this._data;

        if (result) {
            return this._renderResult();
        }

        return `
            <div class="page-generator-page">
                <header class="page-header">
                    <h1>生成頁面</h1>
                    <p class="page-subtitle">生成前端頁面元件，自動繼承 BasePage 並包含 XSS 防護</p>
                </header>

                ${error ? `
                    <div class="alert alert-error">
                        <p>${this.esc(error)}</p>
                    </div>
                ` : ''}

                <form id="page-form" class="form-container">
                    <div class="form-section">
                        <h2>頁面資訊</h2>
                        <div class="form-grid">
                            <div class="form-group full-width">
                                <label for="pageName">頁面名稱 *</label>
                                <input type="text" id="pageName" name="pageName"
                                       value="${this.escAttr(form.pageName)}"
                                       placeholder="ProductList 或 products/ProductDetail"
                                       required>
                                <small>支援巢狀路徑，例如：orders/OrderDetail</small>
                            </div>
                            <div class="form-group">
                                <label class="checkbox-label">
                                    <input type="checkbox" id="isDetail" name="isDetail"
                                           ${form.isDetail ? 'checked' : ''}>
                                    <span>使用詳情頁模板</span>
                                </label>
                                <small>詳情頁包含資料載入、編輯和刪除功能</small>
                            </div>
                        </div>
                    </div>

                    ${preview ? this._renderPreview() : ''}

                    <div class="form-actions">
                        <button type="button" class="btn btn-secondary" id="btn-preview"
                                ${generating ? 'disabled' : ''}>
                            預覽
                        </button>
                        <button type="submit" class="btn btn-primary btn-lg"
                                ${generating || !preview ? 'disabled' : ''}>
                            ${generating ? '生成中...' : '生成頁面'}
                        </button>
                    </div>
                </form>

                <div class="help-section">
                    <h3>使用說明</h3>
                    <div class="help-content">
                        <h4>頁面命名規則</h4>
                        <ul>
                            <li><code>ProductList</code> → 生成 <code>ProductListPage.js</code></li>
                            <li><code>products/ProductDetail</code> → 生成 <code>pages/products/ProductDetailPage.js</code></li>
                        </ul>

                        <h4>頁面模板</h4>
                        <ul>
                            <li><strong>標準模板</strong>：適用於列表頁、表單頁等</li>
                            <li><strong>詳情模板</strong>：適用於單一項目的詳情頁，包含從 URL 取得 ID 的邏輯</li>
                        </ul>

                        <h4>生成後續步驟</h4>
                        <ol>
                            <li>在 <code>routes.js</code> 加入路由設定</li>
                            <li>依需求客製化頁面內容</li>
                            <li>連接實際 API</li>
                        </ol>
                    </div>
                </div>
            </div>
        `;
    }

    _renderPreview() {
        const { preview } = this._data;

        return `
            <div class="form-section preview-section">
                <h2>預覽結果</h2>
                <div class="preview-grid">
                    <div class="preview-item">
                        <span class="preview-label">類別名稱</span>
                        <span class="preview-value">${this.esc(preview.className)}</span>
                    </div>
                    <div class="preview-item">
                        <span class="preview-label">檔案名稱</span>
                        <span class="preview-value">${this.esc(preview.fileName)}</span>
                    </div>
                    <div class="preview-item">
                        <span class="preview-label">目錄</span>
                        <span class="preview-value">${this.esc(preview.directory)}</span>
                    </div>
                    <div class="preview-item">
                        <span class="preview-label">路由路徑</span>
                        <span class="preview-value"><code>${this.esc(preview.routePath)}</code></span>
                    </div>
                    <div class="preview-item">
                        <span class="preview-label">Import 路徑</span>
                        <span class="preview-value"><code>${this.esc(preview.importPath)}</code></span>
                    </div>
                    <div class="preview-item">
                        <span class="preview-label">頁面類型</span>
                        <span class="preview-value">${preview.isDetail ? '詳情頁' : '標準頁'}</span>
                    </div>
                </div>

                <div class="route-example">
                    <h4>routes.js 設定範例</h4>
                    <pre class="code-block"><code>import { ${this.esc(preview.className)} } from '${this.esc(preview.importPath)}';

// 在 routes 陣列中加入：
{ path: '${this.esc(preview.routePath)}', component: ${this.esc(preview.className)} }</code></pre>
                </div>
            </div>
        `;
    }

    _renderResult() {
        const { result } = this._data;

        return `
            <div class="page-generator-page">
                <header class="page-header">
                    <h1>頁面生成完成!</h1>
                </header>

                <div class="result-card success">
                    <div class="result-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                            <polyline points="22 4 12 14.01 9 11.01"/>
                        </svg>
                    </div>
                    <h2>頁面已成功生成</h2>

                    <div class="result-section">
                        <h3>下一步</h3>
                        <ol>
                            <li>更新 <code>routes.js</code> 加入新路由</li>
                            <li>客製化頁面內容</li>
                            <li>連接實際 API</li>
                        </ol>
                    </div>

                    <div class="result-actions">
                        <button class="btn btn-primary" id="btn-generate-another">
                            生成另一個頁面
                        </button>
                    </div>
                </div>

                ${result.output ? `
                    <div class="output-section">
                        <h3>輸出記錄</h3>
                        <pre class="output-log">${this.esc(result.output)}</pre>
                    </div>
                ` : ''}
            </div>
        `;
    }

    events() {
        return {
            'submit #page-form': 'onSubmit',
            'input #pageName': 'onInput',
            'change #isDetail': 'onCheckboxChange',
            'click #btn-preview': 'onPreview',
            'click #btn-generate-another': 'onGenerateAnother'
        };
    }

    onInput(event) {
        const { name, value } = event.target;
        this._data.form[name] = value;
        // 清除預覽當輸入改變時
        this._data.preview = null;
    }

    onCheckboxChange(event) {
        this._data.form.isDetail = event.target.checked;
        this._data.preview = null;
        this._scheduleUpdate();
    }

    async onPreview() {
        const { form } = this._data;

        if (!form.pageName) {
            this._data.error = '請輸入頁面名稱';
            this._scheduleUpdate();
            return;
        }

        this._data.error = null;

        try {
            const response = await this.api.post('/page/preview', {
                pageName: form.pageName,
                isDetail: form.isDetail
            });

            this._data.preview = response.data;
            this._scheduleUpdate();

        } catch (error) {
            this._data.error = error.message || '預覽失敗';
            this._scheduleUpdate();
        }
    }

    onGenerateAnother() {
        this._data.result = null;
        this._data.preview = null;
        this._data.error = null;
        this._data.form = {
            pageName: '',
            isDetail: false
        };
        this._scheduleUpdate();
    }

    async onSubmit(event) {
        event.preventDefault();

        const { form, preview } = this._data;

        if (!form.pageName) {
            this._data.error = '請輸入頁面名稱';
            this._scheduleUpdate();
            return;
        }

        if (!preview) {
            this._data.error = '請先預覽確認';
            this._scheduleUpdate();
            return;
        }

        this._data.generating = true;
        this._data.error = null;
        this._scheduleUpdate();

        try {
            const response = await this.api.post('/page/generate', {
                pageName: form.pageName,
                isDetail: form.isDetail
            });

            this._data.result = {
                output: response.output
            };

            this.showMessage('頁面生成成功!', 'success');

        } catch (error) {
            this._data.error = error.message || '生成失敗';
            this.showMessage('生成失敗', 'error');
        } finally {
            this._data.generating = false;
            this._scheduleUpdate();
        }
    }
}

export default PageGeneratorPage;
