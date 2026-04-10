import { BasePage } from '../core/BasePage.js';

export class RegisterPage extends BasePage {
    async onInit() {
        this._data = {
            name: '',
            email: '',
            password: '',
            confirmPassword: '',
            error: '',
            success: '',
            loading: false
        };
    }

    template() {
        const { name, email, error, success, loading } = this._data;

        return `
            <div class="login-page">
                <div class="login-container">
                    <div class="login-card">
                        <div class="login-header">
                            <div class="login-logo">B</div>
                            <h1>註冊</h1>
                            <p>建立會員帳號後，即可完成商品購買與訂單查詢。</p>
                        </div>

                        ${error ? `<div class="login-error">${this.esc(error)}</div>` : ''}
                        ${success ? `<div class="login-success">${this.esc(success)}</div>` : ''}

                        <form id="register-form" class="login-form">
                            <div class="form-group">
                                <label for="name">姓名</label>
                                <input id="name" name="name" type="text" value="${this.escAttr(name)}" class="form-input" required autocomplete="name">
                            </div>
                            <div class="form-group">
                                <label for="email">Email</label>
                                <input id="email" name="email" type="email" value="${this.escAttr(email)}" class="form-input" required autocomplete="email">
                            </div>
                            <div class="form-group">
                                <label for="password">密碼</label>
                                <input id="password" name="password" type="password" class="form-input" required autocomplete="new-password">
                            </div>
                            <div class="form-group">
                                <label for="confirmPassword">確認密碼</label>
                                <input id="confirmPassword" name="confirmPassword" type="password" class="form-input" required autocomplete="new-password">
                            </div>
                            <button type="submit" class="btn btn-primary btn-block" ${loading ? 'disabled' : ''}>${loading ? '建立帳號中...' : '建立帳號'}</button>
                        </form>

                        <div class="login-footer">
                            <p>已經有帳號？<a href="#/login">前往登入</a></p>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    events() {
        return {
            'submit #register-form': 'onSubmit',
            'input #name': 'onNameInput',
            'input #email': 'onEmailInput',
            'input #password': 'onPasswordInput',
            'input #confirmPassword': 'onConfirmPasswordInput'
        };
    }

    onNameInput(event) {
        this._data.name = event.target.value;
        this._clearFeedback();
    }

    onEmailInput(event) {
        this._data.email = event.target.value;
        this._clearFeedback();
    }

    onPasswordInput(event) {
        this._data.password = event.target.value;
        this._clearFeedback();
    }

    onConfirmPasswordInput(event) {
        this._data.confirmPassword = event.target.value;
        this._clearFeedback();
    }

    _clearFeedback() {
        this._data.error = '';
        this._data.success = '';
    }

    async onSubmit(event) {
        event.preventDefault();

        if (!this._data.name.trim() || !this._data.email.trim() || !this._data.password) {
            this._data.error = '請完整填寫註冊資料。';
            this._scheduleUpdate();
            return;
        }

        if (this._data.password !== this._data.confirmPassword) {
            this._data.error = '兩次輸入的密碼不一致。';
            this._scheduleUpdate();
            return;
        }

        this._data.loading = true;
        this._clearFeedback();
        this._scheduleUpdate();

        try {
            const result = await this.api.post('/auth/register', {
                name: this._data.name.trim(),
                email: this._data.email.trim(),
                password: this._data.password
            });

            this._data.success = result.message || '註冊成功';
            this._data.password = '';
            this._data.confirmPassword = '';
            this._scheduleUpdate();
        } catch (error) {
            this._data.error = error.message || '註冊失敗。';
            this._scheduleUpdate();
        } finally {
            this._data.loading = false;
            this._scheduleUpdate();
        }
    }
}

export default RegisterPage;
