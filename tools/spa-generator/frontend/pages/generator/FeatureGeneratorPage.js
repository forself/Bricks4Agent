/**
 * FeatureGeneratorPage - 生成完整功能
 *
 * 同時生成前後端完整功能：
 * - 後端：Model、Service、API 端點
 * - 前端：列表頁、詳情頁
 *
 * @module FeatureGeneratorPage
 */

import { BasePage } from '../../core/BasePage.js';

export class FeatureGeneratorPage extends BasePage {
    async onInit() {
        this._data = {
            form: {
                featureName: '',
                fields: [
                    { name: 'Name', type: 'string' }
                ]
            },
            generating: false,
            progress: null,
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

    _toPascalCase(str) {
        return str
            .split(/[-_\/\s]/)
            .map(part => part.charAt(0).toUpperCase() + part.slice(1))
            .join('');
    }

    _toKebabCase(str) {
        return str
            .replace(/([a-z])([A-Z])/g, '$1-$2')
            .replace(/[\s_]+/g, '-')
            .toLowerCase();
    }

    _pluralize(word) {
        if (word.endsWith('y')) return word.slice(0, -1) + 'ies';
        if (word.endsWith('s') || word.endsWith('x') || word.endsWith('ch') || word.endsWith('sh')) return word + 'es';
        return word + 's';
    }

    template() {
        const { form, generating, progress, result, error } = this._data;

        if (result) {
            return this._renderResult();
        }

        const className = this._toPascalCase(form.featureName || 'Feature');
        const pluralName = this._pluralize(className);
        const routePath = this._toKebabCase(pluralName);

        return `
            <div class="feature-generator-page">
                <header class="page-header">
                    <h1>完整功能</h1>
                    <p class="page-subtitle">一鍵生成前後端完整功能：API + 列表頁 + 詳情頁</p>
                </header>

                ${error ? `
                    <div class="alert alert-error">
                        <p>${this.esc(error)}</p>
                    </div>
                ` : ''}

                <form id="feature-form" class="form-container">
                    <div class="form-section">
                        <h2>功能資訊</h2>
                        <div class="form-grid">
                            <div class="form-group full-width">
                                <label for="featureName">功能名稱 *</label>
                                <input type="text" id="featureName" name="featureName"
                                       value="${this.escAttr(form.featureName)}"
                                       placeholder="Product"
                                       pattern="^[A-Z][a-zA-Z0-9]*$"
                                       required>
                                <small>PascalCase 格式，例如：Product、Order、Customer</small>
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

                    ${form.featureName ? `
                    <div class="form-section preview-section">
                        <h2>將會生成</h2>
                        <div class="generation-preview">
                            <div class="preview-category">
                                <h4>後端 (C#)</h4>
                                <ul>
                                    <li><code>Models/${this.esc(className)}.cs</code> - 實體類別</li>
                                    <li><code>Services/${this.esc(className)}Service.cs</code> - 服務層</li>
                                    <li>API 端點: <code>/api/${this.esc(routePath)}</code></li>
                                </ul>
                            </div>
                            <div class="preview-category">
                                <h4>前端 (JavaScript)</h4>
                                <ul>
                                    <li><code>pages/${this.esc(routePath)}/${this.esc(className)}ListPage.js</code> - 列表頁</li>
                                    <li><code>pages/${this.esc(routePath)}/${this.esc(className)}DetailPage.js</code> - 詳情頁</li>
                                </ul>
                            </div>
                            <div class="preview-category">
                                <h4>路由設定</h4>
                                <ul>
                                    <li><code>/${this.esc(routePath)}</code> - 列表頁路由</li>
                                    <li><code>/${this.esc(routePath)}/:id</code> - 詳情頁路由</li>
                                </ul>
                            </div>
                        </div>
                    </div>
                    ` : ''}

                    ${progress ? `
                    <div class="form-section progress-section">
                        <h2>生成進度</h2>
                        <div class="progress-list">
                            ${progress.map(p => `
                                <div class="progress-item ${p.status}">
                                    <span class="progress-icon">
                                        ${p.status === 'done' ? '&#10003;' : p.status === 'pending' ? '...' : '&#10007;'}
                                    </span>
                                    <span class="progress-text">${this.esc(p.label)}</span>
                                </div>
                            `).join('')}
                        </div>
                    </div>
                    ` : ''}

                    <div class="form-actions">
                        <button type="submit" class="btn btn-primary btn-lg"
                                ${generating || !form.featureName ? 'disabled' : ''}>
                            ${generating ? '生成中...' : '生成完整功能'}
                        </button>
                    </div>
                </form>

                <div class="help-section">
                    <h3>使用說明</h3>
                    <div class="help-content">
                        <h4>此工具將自動生成</h4>
                        <ol>
                            <li><strong>後端 API</strong>：包含 Model、Service 和 CRUD 端點</li>
                            <li><strong>列表頁</strong>：顯示所有項目的列表</li>
                            <li><strong>詳情頁</strong>：顯示和編輯單一項目</li>
                        </ol>

                        <h4>生成後需要手動完成</h4>
                        <ol>
                            <li>在 <code>AppDbContext.cs</code> 加入 DbSet</li>
                            <li>在 <code>Program.cs</code> 註冊服務和 API 端點</li>
                            <li>在 <code>routes.js</code> 加入路由設定</li>
                            <li>執行 EF Core 遷移</li>
                        </ol>

                        <h4>範例：生成 Product 功能</h4>
                        <pre class="code-block"><code>功能名稱: Product
欄位: Name:string, Price:decimal, Stock:int

將生成:
- /api/products (GET, POST)
- /api/products/{id} (GET, PUT, DELETE)
- ProductListPage.js
- ProductDetailPage.js</code></pre>
                    </div>
                </div>
            </div>
        `;
    }

    _renderResult() {
        const { result } = this._data;

        return `
            <div class="feature-generator-page">
                <header class="page-header">
                    <h1>功能生成完成!</h1>
                </header>

                <div class="result-card success">
                    <div class="result-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                            <polyline points="22 4 12 14.01 9 11.01"/>
                        </svg>
                    </div>
                    <h2>前後端功能已成功生成</h2>

                    <div class="result-summary">
                        <div class="summary-item">
                            <span class="summary-icon">&#10003;</span>
                            <span>後端 API</span>
                        </div>
                        <div class="summary-item">
                            <span class="summary-icon">&#10003;</span>
                            <span>列表頁</span>
                        </div>
                        <div class="summary-item">
                            <span class="summary-icon">&#10003;</span>
                            <span>詳情頁</span>
                        </div>
                    </div>

                    <div class="result-section">
                        <h3>下一步</h3>
                        <ol>
                            <li>
                                <strong>更新 AppDbContext.cs</strong>
                                <pre class="code-inline">public DbSet&lt;Entity&gt; Entities { get; set; } = null!;</pre>
                            </li>
                            <li>
                                <strong>更新 Program.cs</strong>
                                <p>註冊服務並加入 API 端點程式碼</p>
                            </li>
                            <li>
                                <strong>更新 routes.js</strong>
                                <pre class="code-inline">import { EntityListPage } from './entities/EntityListPage.js';
import { EntityDetailPage } from './entities/EntityDetailPage.js';

// 加入路由：
{ path: '/entities', component: EntityListPage },
{ path: '/entities/:id', component: EntityDetailPage }</pre>
                            </li>
                            <li>
                                <strong>執行資料庫遷移</strong>
                                <pre class="code-inline">dotnet ef migrations add AddEntity
dotnet ef database update</pre>
                            </li>
                        </ol>
                    </div>

                    <div class="result-actions">
                        <button class="btn btn-primary" id="btn-generate-another">
                            生成另一個功能
                        </button>
                    </div>
                </div>

                ${result.results && result.results.length > 0 ? `
                    <div class="output-section">
                        <h3>生成記錄</h3>
                        ${result.results.map(r => `
                            <div class="output-item">
                                <h4>${this.esc(r.type)}</h4>
                                <pre class="output-log">${this.esc(r.output || '完成')}</pre>
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
            </div>
        `;
    }

    events() {
        return {
            'submit #feature-form': 'onSubmit',
            'input #featureName': 'onFeatureInput',
            'input .field-name': 'onFieldNameChange',
            'change .field-type': 'onFieldTypeChange',
            'click #btn-add-field': 'onAddField',
            'click .btn-remove-field': 'onRemoveField',
            'click #btn-generate-another': 'onGenerateAnother'
        };
    }

    onFeatureInput(event) {
        this._data.form.featureName = event.target.value;
        this._scheduleUpdate();
    }

    onFieldNameChange(event) {
        const index = parseInt(event.target.dataset.index);
        this._data.form.fields[index].name = event.target.value;
    }

    onFieldTypeChange(event) {
        const index = parseInt(event.target.dataset.index);
        this._data.form.fields[index].type = event.target.value;
    }

    onAddField() {
        this._data.form.fields.push({ name: '', type: 'string' });
        this._scheduleUpdate();
    }

    onRemoveField(event, target) {
        const index = parseInt(target.dataset.index);
        if (this._data.form.fields.length > 1) {
            this._data.form.fields.splice(index, 1);
            this._scheduleUpdate();
        }
    }

    _getFieldsString() {
        return this._data.form.fields
            .filter(f => f.name.trim())
            .map(f => `${f.name}:${f.type}`)
            .join(',');
    }

    onGenerateAnother() {
        this._data.result = null;
        this._data.progress = null;
        this._data.error = null;
        this._data.form = {
            featureName: '',
            fields: [{ name: 'Name', type: 'string' }]
        };
        this._scheduleUpdate();
    }

    async onSubmit(event) {
        event.preventDefault();

        const { form } = this._data;

        if (!form.featureName) {
            this._data.error = '請輸入功能名稱';
            this._scheduleUpdate();
            return;
        }

        const validFields = form.fields.filter(f => f.name.trim());
        if (validFields.length === 0) {
            this._data.error = '請至少定義一個欄位';
            this._scheduleUpdate();
            return;
        }

        this._data.generating = true;
        this._data.error = null;
        this._data.progress = [
            { label: '生成後端 API', status: 'pending' },
            { label: '生成列表頁', status: 'pending' },
            { label: '生成詳情頁', status: 'pending' }
        ];
        this._scheduleUpdate();

        try {
            const response = await this.api.post('/feature/generate', {
                featureName: form.featureName,
                fields: this._getFieldsString()
            });

            // 更新進度為完成
            this._data.progress = [
                { label: '生成後端 API', status: 'done' },
                { label: '生成列表頁', status: 'done' },
                { label: '生成詳情頁', status: 'done' }
            ];

            this._data.result = {
                results: response.results || []
            };

            this.showMessage('功能生成成功!', 'success');

        } catch (error) {
            this._data.error = error.message || '生成失敗';
            this._data.progress = null;
            this.showMessage('生成失敗', 'error');
        } finally {
            this._data.generating = false;
            this._scheduleUpdate();
        }
    }
}

export default FeatureGeneratorPage;
