/**
 * LoginPage - 登入頁面
 *
 * @module LoginPage
 */

import { BasePage } from '../core/BasePage.js';

export class LoginPage extends BasePage {
    async onInit() {
        this._data = {
            email: '',
            password: '',
            rememberMe: false,
            error: '',
            loading: false
        };
    }

    template() {
        const { email, password, rememberMe, error, loading } = this._data;

        return `
            <div class="login-page">
                <div class="login-container">
                    <div class="login-card">
                        <div class="login-header">
                            <div class="login-logo">S</div>
                            <h1>登入</h1>
                            <p>歡迎回來，請登入您的帳號</p>
                        </div>

                        ${error ? `
                            <div class="login-error">
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <circle cx="12" cy="12" r="10"/>
                                    <line x1="12" y1="8" x2="12" y2="12"/>
                                    <line x1="12" y1="16" x2="12.01" y2="16"/>
                                </svg>
                                ${this.esc(error)}
                            </div>
                        ` : ''}

                        <form id="login-form" class="login-form">
                            <div class="form-group">
                                <label for="email">Email</label>
                                <input type="email"
                                       id="email"
                                       name="email"
                                       class="form-input"
                                       value="${this.escAttr(email)}"
                                       placeholder="請輸入 Email"
                                       required
                                       autocomplete="email">
                            </div>

                            <div class="form-group">
                                <label for="password">密碼</label>
                                <input type="password"
                                       id="password"
                                       name="password"
                                       class="form-input"
                                       placeholder="請輸入密碼"
                                       required
                                       autocomplete="current-password">
                            </div>

                            <div class="form-row form-row--between">
                                <label class="checkbox-label">
                                    <input type="checkbox"
                                           id="rememberMe"
                                           ${rememberMe ? 'checked' : ''}>
                                    <span>記住我</span>
                                </label>
                                <a href="#/forgot-password" class="link">忘記密碼？</a>
                            </div>

                            <button type="submit"
                                    class="btn btn-primary btn-block"
                                    ${loading ? 'disabled' : ''}>
                                ${loading ? '登入中...' : '登入'}
                            </button>
                        </form>

                        <div class="login-footer">
                            <p>還沒有帳號？<a href="#/register" class="link">立即註冊</a></p>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    events() {
        return {
            'submit #login-form': 'onSubmit',
            'input #email': 'onEmailInput',
            'input #password': 'onPasswordInput',
            'change #rememberMe': 'onRememberChange'
        };
    }

    onEmailInput(event) {
        this._data.email = event.target.value;
        this._data.error = '';
    }

    onPasswordInput(event) {
        this._data.password = event.target.value;
        this._data.error = '';
    }

    onRememberChange(event) {
        this._data.rememberMe = event.target.checked;
    }

    async onSubmit(event) {
        event.preventDefault();

        const { email, password, rememberMe } = this._data;

        if (!email || !password) {
            this._data.error = '請輸入 Email 和密碼';
            this._scheduleUpdate();
            return;
        }

        this._data.loading = true;
        this._data.error = '';
        this._scheduleUpdate();

        try {
            // const response = await this.api.post('/auth/login', {
            //     email,
            //     password,
            //     rememberMe
            // });
            //
            // this.api.setToken(response.accessToken, response.refreshToken);
            // this.store.set('user', response.user);
            // this.store.set('isAuthenticated', true);

            // 模擬登入
            await new Promise(resolve => setTimeout(resolve, 1000));

            // 模擬成功登入
            this.store?.set('user', { id: 1, name: 'Test User', email });
            this.store?.set('isAuthenticated', true);

            // 導向首頁
            this.navigate('/');

        } catch (error) {
            this._data.error = error.message || '登入失敗，請檢查您的帳號密碼';
            this._scheduleUpdate();
        } finally {
            this._data.loading = false;
            this._scheduleUpdate();
        }
    }
}

export default LoginPage;
