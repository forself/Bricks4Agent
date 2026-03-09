/**
 * SettingsPage - 設定頁面
 *
 * @module SettingsPage
 */

import { BasePage } from '../core/BasePage.js';

export class SettingsPage extends BasePage {
    async onInit() {
        this._data = {
            theme: this.store?.get('theme') || 'light',
            language: 'zh-TW',
            notifications: {
                email: true,
                push: false,
                sms: false
            },
            saved: false
        };
    }

    template() {
        const { theme, language, notifications, saved } = this._data;

        return `
            <div class="settings-page">
                <header class="page-header">
                    <h1>設定</h1>
                    <p class="page-subtitle">自訂您的應用程式偏好</p>
                </header>

                <div class="settings-container">
                    <!-- 外觀設定 -->
                    <section class="settings-section">
                        <h2>外觀</h2>

                        <div class="setting-item">
                            <div class="setting-info">
                                <label>主題</label>
                                <p class="setting-desc">選擇應用程式的顯示主題</p>
                            </div>
                            <div class="setting-control">
                                <select id="theme" class="form-select">
                                    <option value="light" ${theme === 'light' ? 'selected' : ''}>淺色</option>
                                    <option value="dark" ${theme === 'dark' ? 'selected' : ''}>深色</option>
                                    <option value="system" ${theme === 'system' ? 'selected' : ''}>跟隨系統</option>
                                </select>
                            </div>
                        </div>

                        <div class="setting-item">
                            <div class="setting-info">
                                <label>語言</label>
                                <p class="setting-desc">選擇介面顯示語言</p>
                            </div>
                            <div class="setting-control">
                                <select id="language" class="form-select">
                                    <option value="zh-TW" ${language === 'zh-TW' ? 'selected' : ''}>繁體中文</option>
                                    <option value="zh-CN" ${language === 'zh-CN' ? 'selected' : ''}>简体中文</option>
                                    <option value="en" ${language === 'en' ? 'selected' : ''}>English</option>
                                </select>
                            </div>
                        </div>
                    </section>

                    <!-- 通知設定 -->
                    <section class="settings-section">
                        <h2>通知</h2>

                        <div class="setting-item">
                            <div class="setting-info">
                                <label>Email 通知</label>
                                <p class="setting-desc">接收 Email 通知</p>
                            </div>
                            <div class="setting-control">
                                <label class="toggle">
                                    <input type="checkbox"
                                           id="notify-email"
                                           ${notifications.email ? 'checked' : ''}>
                                    <span class="toggle-slider"></span>
                                </label>
                            </div>
                        </div>

                        <div class="setting-item">
                            <div class="setting-info">
                                <label>推播通知</label>
                                <p class="setting-desc">接收瀏覽器推播通知</p>
                            </div>
                            <div class="setting-control">
                                <label class="toggle">
                                    <input type="checkbox"
                                           id="notify-push"
                                           ${notifications.push ? 'checked' : ''}>
                                    <span class="toggle-slider"></span>
                                </label>
                            </div>
                        </div>

                        <div class="setting-item">
                            <div class="setting-info">
                                <label>簡訊通知</label>
                                <p class="setting-desc">接收重要事項的簡訊通知</p>
                            </div>
                            <div class="setting-control">
                                <label class="toggle">
                                    <input type="checkbox"
                                           id="notify-sms"
                                           ${notifications.sms ? 'checked' : ''}>
                                    <span class="toggle-slider"></span>
                                </label>
                            </div>
                        </div>
                    </section>

                    <!-- 儲存按鈕 -->
                    <div class="settings-actions">
                        ${saved ? '<span class="save-indicator">設定已儲存</span>' : ''}
                        <button class="btn btn-primary" id="btn-save">儲存設定</button>
                    </div>
                </div>
            </div>
        `;
    }

    events() {
        return {
            'change #theme': 'onThemeChange',
            'change #language': 'onLanguageChange',
            'change [id^="notify-"]': 'onNotifyChange',
            'click #btn-save': 'onSave'
        };
    }

    onThemeChange(event) {
        const theme = event.target.value;
        this._data.theme = theme;
        this.store?.set('theme', theme);
    }

    onLanguageChange(event) {
        this._data.language = event.target.value;
    }

    onNotifyChange(event) {
        const key = event.target.id.replace('notify-', '');
        this._data.notifications[key] = event.target.checked;
    }

    async onSave() {
        try {
            // await this.api.put('/settings', {
            //     theme: this._data.theme,
            //     language: this._data.language,
            //     notifications: this._data.notifications
            // });

            this._data.saved = true;
            this._scheduleUpdate();

            setTimeout(() => {
                this._data.saved = false;
                this._scheduleUpdate();
            }, 3000);

            this.showMessage('設定已儲存', 'success');

        } catch (error) {
            this.showMessage('儲存失敗', 'error');
        }
    }
}

export default SettingsPage;
