/**
 * ApiGeneratorPage - 生成 API 端點
 *
 * @module ApiGeneratorPage
 */

import { BasePage } from '../../core/BasePage.js';

export class ApiGeneratorPage extends BasePage {
    async onInit() {
        this._data = {
            form: {
                entityName: '',
                fields: [
                    { name: 'Name', type: 'string' }
                ]
            },
            preview: null,
            generating: false,
            result: null,
            error: null
        };

        this._fieldTypes = [
            { value: 'string', label: '字串 (string)' },
            { value: 'int', label: '整數 (int)' },
            { value: 'long', label: '長整數 (long)' },
            { value: 'decimal', label: '小數 (decimal)' },
            { value: 'double', label: '浮點數 (double)' },
            { value: 'bool', label: '布林 (bool)' },
            { value: 'datetime', label: '日期時間 (DateTime)' },
            { value: 'guid', label: 'GUID' }
        ];
    }

    template() {
        const { form, preview, generating, result, error } = this._data;

        if (result) {
            return this._renderResult();
        }

        return `
            <div class="api-generator-page">
                <header class="page-header">
                    <h1>生成 API</h1>
                    <p class="page-subtitle">生成後端 API 端點、Model 和 Service</p>
                </header>

                ${error ? `
                    <div class="alert alert-error">
                        <p>${this.esc(error)}</p>
                    </div>
                ` : ''}

                <form id="api-form" class="form-container">
                    <div class="form-section">
                        <h2>實體資訊</h2>
                        <div class="form-grid">
                            <div class="form-group full-width">
                                <label for="entityName">實體名稱 *</label>
                                <input type="text" id="entityName" name="entityName"
                                       value="${this.escAttr(form.entityName)}"
                                       placeholder="Product"
                                       pattern="^[A-Z][a-zA-Z0-9]*$"
                                       required>
                                <small>PascalCase 格式，例如：Product、OrderItem</small>
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h2>欄位定義</h2>
                        <div class="fields-container">
                            ${form.fields.map((field, index) => `
                                <div class="field-row" data-index="${index}">
                                    <div class="field-inputs">
                                        <input type="text" class="field-name"
                                               value="${this.escAttr(field.name)}"
                                               placeholder="欄位名稱"
                                               data-index="${index}">
                                        <select class="field-type" data-index="${index}">
                                            ${this._fieldTypes.map(t => `
                                                <option value="${this.escAttr(t.value)}"
                                                        ${field.type === t.value ? 'selected' : ''}>
                                                    ${this.esc(t.label)}
                                                </option>
                                            `).join('')}
                                        </select>
                                    </div>
                                    <button type="button" class="btn btn-icon btn-remove-field"
                                            data-index="${index}"
                                            ${form.fields.length === 1 ? 'disabled' : ''}>
                                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <line x1="18" y1="6" x2="6" y2="18"/>
                                            <line x1="6" y1="6" x2="18" y2="18"/>
                                        </svg>
                                    </button>
                                </div>
                            `).join('')}
                        </div>
                        <button type="button" class="btn btn-secondary btn-sm" id="btn-add-field">
                            + 新增欄位
                        </button>
                    </div>

                    ${preview ? this._renderPreview() : ''}

                    <div class="form-actions">
                        <button type="button" class="btn btn-secondary" id="btn-preview"
                                ${generating ? 'disabled' : ''}>
                            預覽
                        </button>
                        <button type="submit" class="btn btn-primary btn-lg"
                                ${generating || !preview ? 'disabled' : ''}>
                            ${generating ? '生成中...' : '生成 API'}
                        </button>
                    </div>
                </form>

                <div class="help-section">
                    <h3>使用說明</h3>
                    <div class="help-content">
                        <h4>欄位類型說明</h4>
                        <table class="help-table">
                            <tr><td><code>string</code></td><td>文字字串</td></tr>
                            <tr><td><code>int</code></td><td>整數 (-2,147,483,648 到 2,147,483,647)</td></tr>
                            <tr><td><code>long</code></td><td>長整數 (更大範圍)</td></tr>
                            <tr><td><code>decimal</code></td><td>精確小數 (適合金額)</td></tr>
                            <tr><td><code>double</code></td><td>浮點數 (科學計算)</td></tr>
                            <tr><td><code>bool</code></td><td>布林值 (true/false)</td></tr>
                            <tr><td><code>datetime</code></td><td>日期時間</td></tr>
                            <tr><td><code>guid</code></td><td>全域唯一識別碼</td></tr>
                        </table>

                        <h4>生成內容</h4>
                        <ul>
                            <li><strong>Model</strong>：實體類別、CreateRequest、UpdateRequest、Response</li>
                            <li><strong>Service</strong>：服務介面和實作</li>
                            <li><strong>API 端點</strong>：CRUD 操作程式碼</li>
                        </ul>

                        <h4>生成後續步驟</h4>
                        <ol>
                            <li>在 <code>AppDbContext.cs</code> 的 <code>EnsureCreated()</code> 中加入建表 SQL</li>
                            <li>在 <code>Program.cs</code> 註冊服務並加入 API 端點</li>
                            <li>重新啟動應用程式，讓 <code>EnsureCreated()</code> 自動建表</li>
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
                        <span class="preview-label">複數名稱</span>
                        <span class="preview-value">${this.esc(preview.pluralName)}</span>
                    </div>
                    <div class="preview-item">
                        <span class="preview-label">API 路徑</span>
                        <span class="preview-value"><code>${this.esc(preview.routePath)}</code></span>
                    </div>
                    <div class="preview-item">
                        <span class="preview-label">Model 檔案</span>
                        <span class="preview-value">${this.esc(preview.modelFile)}</span>
                    </div>
                    <div class="preview-item">
                        <span class="preview-label">Service 檔案</span>
                        <span class="preview-value">${this.esc(preview.serviceFile)}</span>
                    </div>
                </div>

                <div class="preview-fields">
                    <h4>欄位</h4>
                    <table class="preview-table">
                        <thead>
                            <tr>
                                <th>名稱</th>
                                <th>類型</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${preview.fields.map(f => `
                                <tr>
                                    <td>${this.esc(f.name)}</td>
                                    <td><code>${this.esc(f.type)}</code></td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>

                <div class="preview-endpoints">
                    <h4>API 端點</h4>
                    <table class="preview-table">
                        <thead>
                            <tr>
                                <th>方法</th>
                                <th>路徑</th>
                                <th>說明</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${preview.endpoints.map(e => `
                                <tr>
                                    <td><span class="method-badge method-${e.method.toLowerCase()}">${this.esc(e.method)}</span></td>
                                    <td><code>${this.esc(e.path)}</code></td>
                                    <td>${this.esc(e.description)}</td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            </div>
        `;
    }

    _renderResult() {
        const { result } = this._data;

        return `
            <div class="api-generator-page">
                <header class="page-header">
                    <h1>API 生成完成!</h1>
                </header>

                <div class="result-card success">
                    <div class="result-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                            <polyline points="22 4 12 14.01 9 11.01"/>
                        </svg>
                    </div>
                    <h2>API 已成功生成</h2>

                    <div class="result-section">
                        <h3>下一步</h3>
                        <ol>
                            <li>在 <code>AppDbContext.cs</code> 加入建表 SQL：
                                <pre class="code-inline">// EnsureCreated()
Execute(@"CREATE TABLE IF NOT EXISTS Entities (...)");</pre>
                            </li>
                            <li>在 <code>Program.cs</code> 註冊服務</li>
                            <li>複製 API 端點程式碼到 <code>Program.cs</code></li>
                            <li>重新啟動應用程式</li>
                            <li>確認資料表已由 <code>EnsureCreated()</code> 自動建立</li>
                        </ol>
                    </div>

                    <div class="result-actions">
                        <button class="btn btn-primary" id="btn-generate-another">
                            生成另一個 API
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
            'submit #api-form': 'onSubmit',
            'input #entityName': 'onEntityInput',
            'input .field-name': 'onFieldNameChange',
            'change .field-type': 'onFieldTypeChange',
            'click #btn-add-field': 'onAddField',
            'click .btn-remove-field': 'onRemoveField',
            'click #btn-preview': 'onPreview',
            'click #btn-generate-another': 'onGenerateAnother'
        };
    }

    onEntityInput(event) {
        this._data.form.entityName = event.target.value;
        this._data.preview = null;
    }

    onFieldNameChange(event) {
        const index = parseInt(event.target.dataset.index);
        this._data.form.fields[index].name = event.target.value;
        this._data.preview = null;
    }

    onFieldTypeChange(event) {
        const index = parseInt(event.target.dataset.index);
        this._data.form.fields[index].type = event.target.value;
        this._data.preview = null;
    }

    onAddField() {
        this._data.form.fields.push({ name: '', type: 'string' });
        this._data.preview = null;
        this._scheduleUpdate();
    }

    onRemoveField(event, target) {
        const index = parseInt(target.dataset.index);
        if (this._data.form.fields.length > 1) {
            this._data.form.fields.splice(index, 1);
            this._data.preview = null;
            this._scheduleUpdate();
        }
    }

    _getFieldsString() {
        return this._data.form.fields
            .filter(f => f.name.trim())
            .map(f => `${f.name}:${f.type}`)
            .join(',');
    }

    async onPreview() {
        const { form } = this._data;

        if (!form.entityName) {
            this._data.error = '請輸入實體名稱';
            this._scheduleUpdate();
            return;
        }

        const validFields = form.fields.filter(f => f.name.trim());
        if (validFields.length === 0) {
            this._data.error = '請至少定義一個欄位';
            this._scheduleUpdate();
            return;
        }

        this._data.error = null;

        try {
            const response = await this.api.post('/endpoint/preview', {
                entityName: form.entityName,
                fields: this._getFieldsString()
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
            entityName: '',
            fields: [{ name: 'Name', type: 'string' }]
        };
        this._scheduleUpdate();
    }

    async onSubmit(event) {
        event.preventDefault();

        const { form, preview } = this._data;

        if (!form.entityName) {
            this._data.error = '請輸入實體名稱';
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
            const response = await this.api.post('/endpoint/generate', {
                entityName: form.entityName,
                fields: this._getFieldsString()
            });

            this._data.result = {
                output: response.output
            };

            this.showMessage('API 生成成功!', 'success');

        } catch (error) {
            this._data.error = error.message || '生成失敗';
            this.showMessage('生成失敗', 'error');
        } finally {
            this._data.generating = false;
            this._scheduleUpdate();
        }
    }
}

export default ApiGeneratorPage;
