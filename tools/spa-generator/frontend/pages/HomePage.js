/**
 * HomePage - SPA Generator 首頁
 *
 * 提供快速入口和系統資訊
 *
 * @module HomePage
 */

import { BasePage } from '../core/BasePage.js';

export class HomePage extends BasePage {
    async onInit() {
        this._data = {
            systemInfo: null,
            loading: true
        };

        await this._loadSystemInfo();
    }

    async _loadSystemInfo() {
        try {
            const response = await this.api.get('/info');
            this._data.systemInfo = response.data;
        } catch (error) {
            console.error('[HomePage] 載入系統資訊失敗:', error);
            this._data.systemInfo = null;
        } finally {
            this._data.loading = false;
        }
    }

    template() {
        const { systemInfo, loading } = this._data;

        return `
            <div class="home-page">
                <header class="page-header">
                    <h1>SPA Generator</h1>
                    <p class="page-subtitle">快速建立 SPA 專案和功能元件</p>
                </header>

                <!-- 功能卡片 -->
                <section class="feature-cards">
                    <a href="#/project" class="feature-card">
                        <div class="feature-icon feature-icon--project">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>
                            </svg>
                        </div>
                        <h3>建立專案</h3>
                        <p>建立完整的 SPA 專案，包含前後端架構、認證系統和資料庫設定</p>
                        <span class="feature-arrow">&rarr;</span>
                    </a>

                    <a href="#/page-editor" class="feature-card feature-card--new">
                        <div class="feature-icon feature-icon--editor">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                                <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                            </svg>
                        </div>
                        <h3>頁面定義編輯器</h3>
                        <p>視覺化定義頁面結構、欄位和元件，透過問卷互動生成程式碼</p>
                        <span class="feature-badge">NEW</span>
                        <span class="feature-arrow">&rarr;</span>
                    </a>

                    <a href="#/page" class="feature-card">
                        <div class="feature-icon feature-icon--page">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                                <polyline points="14 2 14 8 20 8"/>
                            </svg>
                        </div>
                        <h3>生成頁面 (簡易)</h3>
                        <p>快速生成基礎頁面元件，適合簡單的列表和詳情頁</p>
                        <span class="feature-arrow">&rarr;</span>
                    </a>

                    <a href="#/api" class="feature-card">
                        <div class="feature-icon feature-icon--api">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <rect x="2" y="3" width="20" height="14" rx="2" ry="2"/>
                                <line x1="8" y1="21" x2="16" y2="21"/>
                                <line x1="12" y1="17" x2="12" y2="21"/>
                            </svg>
                        </div>
                        <h3>生成 API</h3>
                        <p>生成後端 API 端點、Model 和 Service，支援 CRUD 操作</p>
                        <span class="feature-arrow">&rarr;</span>
                    </a>

                    <a href="#/feature" class="feature-card feature-card--primary">
                        <div class="feature-icon feature-icon--feature">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <polygon points="12 2 2 7 12 12 22 7 12 2"/>
                                <polyline points="2 17 12 22 22 17"/>
                                <polyline points="2 12 12 17 22 12"/>
                            </svg>
                        </div>
                        <h3>完整功能</h3>
                        <p>一鍵生成前後端完整功能：API + 列表頁 + 詳情頁</p>
                        <span class="feature-arrow">&rarr;</span>
                    </a>
                </section>

                <!-- 快速開始 -->
                <section class="quick-start">
                    <h2>快速開始</h2>
                    <div class="steps">
                        <div class="step">
                            <div class="step-number">1</div>
                            <div class="step-content">
                                <h4>建立專案</h4>
                                <p>使用「建立專案」功能建立一個新的 SPA 專案基礎架構</p>
                            </div>
                        </div>
                        <div class="step">
                            <div class="step-number">2</div>
                            <div class="step-content">
                                <h4>生成功能</h4>
                                <p>使用「完整功能」生成器快速建立 CRUD 功能</p>
                            </div>
                        </div>
                        <div class="step">
                            <div class="step-number">3</div>
                            <div class="step-content">
                                <h4>整合與客製</h4>
                                <p>依照指引更新路由和程式碼，客製化功能細節</p>
                            </div>
                        </div>
                        <div class="step">
                            <div class="step-number">4</div>
                            <div class="step-content">
                                <h4>啟動專案</h4>
                                <p>執行啟動腳本，開始開發您的應用程式</p>
                            </div>
                        </div>
                    </div>
                </section>

                <!-- 系統資訊 -->
                <section class="system-info">
                    <h2>系統資訊</h2>
                    ${loading ? `
                        <div class="loading-inline">
                            <div class="loading-spinner-sm"></div>
                            <span>載入中...</span>
                        </div>
                    ` : systemInfo ? `
                        <div class="info-grid">
                            <div class="info-item">
                                <span class="info-label">Node.js 版本</span>
                                <span class="info-value">${this.esc(systemInfo.nodeVersion)}</span>
                            </div>
                            <div class="info-item">
                                <span class="info-label">.NET SDK 版本</span>
                                <span class="info-value ${!systemInfo.dotnetVersion ? 'info-value--warning' : ''}">${systemInfo.dotnetVersion ? this.esc(systemInfo.dotnetVersion) : '未安裝'}</span>
                            </div>
                            <div class="info-item">
                                <span class="info-label">平台</span>
                                <span class="info-value">${this.esc(systemInfo.platform)}</span>
                            </div>
                        </div>
                        ${!systemInfo.dotnetVersion ? `
                            <div class="alert alert-warning">
                                <strong>注意：</strong> 未偵測到 .NET SDK。若要建立專案，請先安裝 .NET 8 SDK。
                            </div>
                        ` : ''}
                    ` : `
                        <div class="alert alert-error">
                            無法取得系統資訊
                        </div>
                    `}
                </section>

                <!-- 技術規格 -->
                <section class="tech-specs">
                    <h2>技術規格</h2>
                    <div class="specs-grid">
                        <div class="spec-category">
                            <h4>前端</h4>
                            <ul>
                                <li>Vanilla JavaScript (ES6+)</li>
                                <li>SPA 路由系統</li>
                                <li>響應式佈局</li>
                                <li>XSS 防護</li>
                                <li>狀態管理</li>
                            </ul>
                        </div>
                        <div class="spec-category">
                            <h4>後端</h4>
                            <ul>
                                <li>.NET 8 Minimal API</li>
                                <li>SQLite 資料庫</li>
                                <li>Entity Framework Core</li>
                                <li>JWT 認證</li>
                                <li>Repository Pattern</li>
                            </ul>
                        </div>
                        <div class="spec-category">
                            <h4>安全性</h4>
                            <ul>
                                <li>PBKDF2 密碼雜湊</li>
                                <li>CORS 配置</li>
                                <li>安全性標頭</li>
                                <li>速率限制</li>
                                <li>輸入驗證</li>
                            </ul>
                        </div>
                    </div>
                </section>
            </div>
        `;
    }

    events() {
        return {};
    }
}

export default HomePage;
