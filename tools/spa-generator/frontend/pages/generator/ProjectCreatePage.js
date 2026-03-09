/**
 * ProjectCreatePage - 建立新專案頁面
 *
 * @module ProjectCreatePage
 */

import { BasePage } from '../../core/BasePage.js';

export class ProjectCreatePage extends BasePage {
    async onInit() {
        this._data = {
            form: {
                projectName: '',
                displayName: '',
                description: '',
                outputDir: '',
                dbName: '',
                apiPort: '5001',
                jwtKey: '',
                jwtIssuer: '',
                corsOrigins: 'http://localhost:3000',
                adminEmail: 'admin@example.com',
                adminPassword: 'Admin@123',
                adminName: 'Admin'
            },
            submitting: false,
            result: null,
            error: null
        };
    }

    _generateJwtKey() {
        const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*';
        let result = '';
        for (let i = 0; i < 64; i++) {
            result += chars.charAt(Math.floor(Math.random() * chars.length));
        }
        return result;
    }

    template() {
        const { form, submitting, result, error } = this._data;

        if (result) {
            return this._renderResult();
        }

        return `
            <div class="project-create-page">
                <header class="page-header">
                    <h1>建立新專案</h1>
                    <p class="page-subtitle">建立一個新的 SPA 專案，包含前後端完整結構</p>
                </header>

                ${error ? `
                    <div class="alert alert-error">
                        <p>${this.esc(error)}</p>
                    </div>
                ` : ''}

                <form id="project-form" class="form-container">
                    <!-- 專案資訊 -->
                    <div class="form-section">
                        <h2>專案資訊</h2>
                        <div class="form-grid">
                            <div class="form-group">
                                <label for="projectName">專案名稱 *</label>
                                <input type="text" id="projectName" name="projectName"
                                       value="${this.escAttr(form.projectName)}"
                                       placeholder="my-spa-app" required
                                       pattern="^[a-z][a-z0-9-]*$">
                                <small>小寫字母開頭，只能包含小寫字母、數字和連字號</small>
                            </div>
                            <div class="form-group">
                                <label for="displayName">顯示名稱</label>
                                <input type="text" id="displayName" name="displayName"
                                       value="${this.escAttr(form.displayName)}"
                                       placeholder="我的 SPA 應用程式">
                            </div>
                            <div class="form-group full-width">
                                <label for="description">專案描述</label>
                                <input type="text" id="description" name="description"
                                       value="${this.escAttr(form.description)}"
                                       placeholder="基於 SPA 範本建立的應用程式">
                            </div>
                            <div class="form-group full-width">
                                <label for="outputDir">輸出目錄 *</label>
                                <input type="text" id="outputDir" name="outputDir"
                                       value="${this.escAttr(form.outputDir)}"
                                       placeholder="D:/projects" required>
                            </div>
                        </div>
                    </div>

                    <!-- 後端配置 -->
                    <div class="form-section">
                        <h2>後端配置</h2>
                        <div class="form-grid">
                            <div class="form-group">
                                <label for="dbName">資料庫檔名</label>
                                <input type="text" id="dbName" name="dbName"
                                       value="${this.escAttr(form.dbName)}"
                                       placeholder="app.db">
                            </div>
                            <div class="form-group">
                                <label for="apiPort">API 埠號</label>
                                <input type="number" id="apiPort" name="apiPort"
                                       value="${this.escAttr(form.apiPort)}"
                                       placeholder="5001" min="1024" max="65535">
                            </div>
                        </div>
                    </div>

                    <!-- 安全性配置 -->
                    <div class="form-section">
                        <h2>安全性配置</h2>
                        <div class="form-grid">
                            <div class="form-group">
                                <label for="jwtKey">
                                    JWT 金鑰
                                    <button type="button" class="btn-link" id="btn-generate-jwt">
                                        自動產生
                                    </button>
                                </label>
                                <input type="text" id="jwtKey" name="jwtKey"
                                       value="${this.escAttr(form.jwtKey)}"
                                       placeholder="留空則自動產生 (至少 32 字元)">
                            </div>
                            <div class="form-group">
                                <label for="jwtIssuer">JWT Issuer</label>
                                <input type="text" id="jwtIssuer" name="jwtIssuer"
                                       value="${this.escAttr(form.jwtIssuer)}"
                                       placeholder="與專案名稱相同">
                            </div>
                            <div class="form-group full-width">
                                <label for="corsOrigins">CORS 允許來源</label>
                                <input type="text" id="corsOrigins" name="corsOrigins"
                                       value="${this.escAttr(form.corsOrigins)}"
                                       placeholder="http://localhost:3000">
                                <small>多個來源以逗號分隔</small>
                            </div>
                        </div>
                    </div>

                    <!-- 管理員帳號 -->
                    <div class="form-section">
                        <h2>初始管理員</h2>
                        <div class="form-grid">
                            <div class="form-group">
                                <label for="adminEmail">Email</label>
                                <input type="email" id="adminEmail" name="adminEmail"
                                       value="${this.escAttr(form.adminEmail)}"
                                       placeholder="admin@example.com">
                            </div>
                            <div class="form-group">
                                <label for="adminPassword">密碼</label>
                                <input type="text" id="adminPassword" name="adminPassword"
                                       value="${this.escAttr(form.adminPassword)}"
                                       placeholder="Admin@123">
                            </div>
                            <div class="form-group">
                                <label for="adminName">姓名</label>
                                <input type="text" id="adminName" name="adminName"
                                       value="${this.escAttr(form.adminName)}"
                                       placeholder="Admin">
                            </div>
                        </div>
                    </div>

                    <div class="form-actions">
                        <button type="submit" class="btn btn-primary btn-lg" ${submitting ? 'disabled' : ''}>
                            ${submitting ? '建立中...' : '建立專案'}
                        </button>
                    </div>
                </form>
            </div>
        `;
    }

    _renderResult() {
        const { result } = this._data;

        return `
            <div class="project-create-page">
                <header class="page-header">
                    <h1>專案建立完成!</h1>
                </header>

                <div class="result-card success">
                    <div class="result-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                            <polyline points="22 4 12 14.01 9 11.01"/>
                        </svg>
                    </div>
                    <h2>專案已成功建立</h2>
                    <p class="result-path">${this.esc(result.projectPath)}</p>

                    <div class="result-section">
                        <h3>下一步</h3>
                        <div class="code-block">
                            <code>cd ${this.esc(result.projectPath)}</code>
                            <code># 啟動後端</code>
                            <code>cd backend && dotnet restore && dotnet run</code>
                        </div>
                        <h4>啟動前端 (任選一種)</h4>
                        <div class="code-block">
                            <code># C# 靜態伺服器</code>
                            <code>dotnet run --project tools/static-server -- frontend 3000</code>
                            <code># 或 Node.js</code>
                            <code>cd frontend && npx serve -l 3000</code>
                            <code># 或 Python</code>
                            <code>cd frontend && python -m http.server 3000</code>
                        </div>
                        <p>然後在瀏覽器開啟 <strong>http://localhost:3000</strong></p>
                    </div>

                    <div class="result-section">
                        <h3>管理員帳號</h3>
                        <p>Email: ${this.esc(result.adminEmail)}</p>
                    </div>

                    <div class="result-actions">
                        <button class="btn btn-primary" id="btn-create-another">
                            建立另一個專案
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
            'submit #project-form': 'onSubmit',
            'input .form-group input': 'onInput',
            'click #btn-generate-jwt': 'onGenerateJwt',
            'click #btn-create-another': 'onCreateAnother'
        };
    }

    onInput(event) {
        const { name, value } = event.target;
        this._data.form[name] = value;
    }

    onGenerateJwt() {
        this._data.form.jwtKey = this._generateJwtKey();
        this._scheduleUpdate();
    }

    onCreateAnother() {
        this._data.result = null;
        this._data.error = null;
        this._data.form = {
            projectName: '',
            displayName: '',
            description: '',
            outputDir: this._data.form.outputDir,
            dbName: '',
            apiPort: '5001',
            jwtKey: '',
            jwtIssuer: '',
            corsOrigins: 'http://localhost:3000',
            adminEmail: 'admin@example.com',
            adminPassword: 'Admin@123',
            adminName: 'Admin'
        };
        this._scheduleUpdate();
    }

    async onSubmit(event) {
        event.preventDefault();

        const { form } = this._data;

        // 驗證
        if (!form.projectName) {
            this._data.error = '請輸入專案名稱';
            this._scheduleUpdate();
            return;
        }

        if (!form.outputDir) {
            this._data.error = '請輸入輸出目錄';
            this._scheduleUpdate();
            return;
        }

        this._data.submitting = true;
        this._data.error = null;
        this._scheduleUpdate();

        try {
            const config = {
                project: {
                    name: form.projectName,
                    displayName: form.displayName || form.projectName,
                    description: form.description,
                    outputDir: form.outputDir
                },
                backend: {
                    dbName: form.dbName || `${form.projectName}.db`,
                    apiPort: form.apiPort || '5001'
                },
                security: {
                    jwtKey: form.jwtKey || this._generateJwtKey(),
                    jwtIssuer: form.jwtIssuer || form.projectName,
                    corsOrigins: form.corsOrigins.split(',').map(s => s.trim()).filter(s => s)
                },
                admin: {
                    email: form.adminEmail,
                    password: form.adminPassword,
                    name: form.adminName
                }
            };

            const response = await this.api.post('/generator/project', config);

            this._data.result = {
                projectPath: response.data?.projectPath || `${form.outputDir}/${form.projectName}`,
                adminEmail: form.adminEmail,
                output: response.output
            };

            this.showMessage('專案建立成功!', 'success');

        } catch (error) {
            this._data.error = error.message || '建立失敗';
            this.showMessage('建立失敗', 'error');
        } finally {
            this._data.submitting = false;
            this._scheduleUpdate();
        }
    }
}

export default ProjectCreatePage;
