import { BasePage } from '../core/BasePage.js';

export class LoginPage extends BasePage {
    async onInit() {
        this._data = {
            email: '',
            password: '',
            error: '',
            loading: false
        };
    }

    template() {
        const { email, error, loading } = this._data;

        return `
            <div class="login-page">
                <div class="login-container">
                    <div class="login-card">
                        <div class="login-header">
                            <div class="login-logo">B</div>
                            <h1>登入</h1>
                            <p>使用會員或管理員帳號登入驗證商務流程。</p>
                        </div>

                        ${error ? `<div class="login-error">${this.esc(error)}</div>` : ''}

                        <form id="login-form" class="login-form">
                            <div class="form-group">
                                <label for="email">Email</label>
                                <input id="email" name="email" type="email" value="${this.escAttr(email)}" class="form-input" required autocomplete="email">
                            </div>
                            <div class="form-group">
                                <label for="password">密碼</label>
                                <input id="password" name="password" type="password" class="form-input" required autocomplete="current-password">
                            </div>
                            <button type="submit" class="btn btn-primary btn-block" ${loading ? 'disabled' : ''}>${loading ? '登入中...' : '登入'}</button>
                        </form>

                        <div class="login-footer">
                            <p>還沒有帳號？<a href="#/register">前往註冊</a></p>
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
            'input #password': 'onPasswordInput'
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

    async onSubmit(event) {
        event.preventDefault();

        if (!this._data.email.trim() || !this._data.password.trim()) {
            this._data.error = '請輸入 Email 與密碼。';
            this._scheduleUpdate();
            return;
        }

        this._data.loading = true;
        this._data.error = '';
        this._scheduleUpdate();

        try {
            const result = await this.api.post('/auth/login', {
                email: this._data.email.trim(),
                password: this._data.password
            });

            this.api.setToken(result.accessToken, result.refreshToken);
            this.store.setMany({
                user: result.user,
                isAuthenticated: true
            });

            this.navigate('/');
        } catch (error) {
            this._data.error = error.message || '登入失敗，請確認帳號密碼。';
            this._scheduleUpdate();
        } finally {
            this._data.loading = false;
            this._scheduleUpdate();
        }
    }
}

export default LoginPage;
