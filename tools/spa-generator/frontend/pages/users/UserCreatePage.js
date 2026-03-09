/**
 * UserCreatePage - 新增使用者頁面
 *
 * @module UserCreatePage
 */

import { BasePage } from '../../core/BasePage.js';

export class UserCreatePage extends BasePage {
    async onInit() {
        this._data = {
            form: {
                name: '',
                email: '',
                password: '',
                confirmPassword: '',
                role: 'user',
                department: '',
                phone: ''
            },
            errors: {},
            submitting: false,
            roles: [
                { value: 'user', label: '使用者' },
                { value: 'editor', label: '編輯' },
                { value: 'admin', label: '管理員' }
            ],
            departments: [
                { value: '', label: '請選擇部門' },
                { value: 'it', label: '資訊部' },
                { value: 'hr', label: '人資部' },
                { value: 'sales', label: '業務部' },
                { value: 'marketing', label: '行銷部' }
            ]
        };
    }

    template() {
        const { form, errors, submitting, roles, departments } = this._data;

        return `
            <div class="user-create-page">
                <!-- 返回按鈕 -->
                <div class="page-back">
                    <a href="#/users" class="back-link">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="19" y1="12" x2="5" y2="12"/>
                            <polyline points="12 19 5 12 12 5"/>
                        </svg>
                        返回列表
                    </a>
                </div>

                <div class="form-card">
                    <h2>新增使用者</h2>

                    <form id="create-form" class="form">
                        <!-- 基本資訊 -->
                        <div class="form-section">
                            <h3>基本資訊</h3>

                            <div class="form-row">
                                <div class="form-group">
                                    <label for="name">姓名 <span class="required">*</span></label>
                                    <input type="text"
                                           id="name"
                                           name="name"
                                           class="form-input ${errors.name ? 'is-invalid' : ''}"
                                           value="${this.escAttr(form.name)}"
                                           placeholder="請輸入姓名"
                                           required>
                                    ${errors.name ? `<div class="form-error">${this.esc(errors.name)}</div>` : ''}
                                </div>

                                <div class="form-group">
                                    <label for="email">Email <span class="required">*</span></label>
                                    <input type="email"
                                           id="email"
                                           name="email"
                                           class="form-input ${errors.email ? 'is-invalid' : ''}"
                                           value="${this.escAttr(form.email)}"
                                           placeholder="請輸入 Email"
                                           required>
                                    ${errors.email ? `<div class="form-error">${this.esc(errors.email)}</div>` : ''}
                                </div>
                            </div>

                            <div class="form-row">
                                <div class="form-group">
                                    <label for="password">密碼 <span class="required">*</span></label>
                                    <input type="password"
                                           id="password"
                                           name="password"
                                           class="form-input ${errors.password ? 'is-invalid' : ''}"
                                           placeholder="請輸入密碼"
                                           required>
                                    ${errors.password ? `<div class="form-error">${this.esc(errors.password)}</div>` : ''}
                                </div>

                                <div class="form-group">
                                    <label for="confirmPassword">確認密碼 <span class="required">*</span></label>
                                    <input type="password"
                                           id="confirmPassword"
                                           name="confirmPassword"
                                           class="form-input ${errors.confirmPassword ? 'is-invalid' : ''}"
                                           placeholder="請再次輸入密碼"
                                           required>
                                    ${errors.confirmPassword ? `<div class="form-error">${this.esc(errors.confirmPassword)}</div>` : ''}
                                </div>
                            </div>
                        </div>

                        <!-- 帳號設定 -->
                        <div class="form-section">
                            <h3>帳號設定</h3>

                            <div class="form-row">
                                <div class="form-group">
                                    <label for="role">角色 <span class="required">*</span></label>
                                    <select id="role" name="role" class="form-select">
                                        ${roles.map(role => `
                                            <option value="${this.escAttr(role.value)}" ${form.role === role.value ? 'selected' : ''}>
                                                ${this.esc(role.label)}
                                            </option>
                                        `).join('')}
                                    </select>
                                </div>

                                <div class="form-group">
                                    <label for="department">部門</label>
                                    <select id="department" name="department" class="form-select">
                                        ${departments.map(dept => `
                                            <option value="${this.escAttr(dept.value)}" ${form.department === dept.value ? 'selected' : ''}>
                                                ${this.esc(dept.label)}
                                            </option>
                                        `).join('')}
                                    </select>
                                </div>
                            </div>

                            <div class="form-group">
                                <label for="phone">電話</label>
                                <input type="tel"
                                       id="phone"
                                       name="phone"
                                       class="form-input"
                                       value="${this.escAttr(form.phone)}"
                                       placeholder="請輸入電話">
                            </div>
                        </div>

                        <!-- 按鈕 -->
                        <div class="form-actions">
                            <a href="#/users" class="btn btn-secondary">取消</a>
                            <button type="submit" class="btn btn-primary" ${submitting ? 'disabled' : ''}>
                                ${submitting ? '建立中...' : '建立使用者'}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        `;
    }

    events() {
        return {
            'submit #create-form': 'onSubmit',
            'input .form-input': 'onInput',
            'change .form-select': 'onInput'
        };
    }

    onInput(event) {
        const { name, value } = event.target;
        this._data.form[name] = value;

        // 清除該欄位的錯誤
        if (this._data.errors[name]) {
            delete this._data.errors[name];
        }
    }

    async onSubmit(event) {
        event.preventDefault();

        // 驗證表單
        if (!this._validate()) {
            return;
        }

        this._data.submitting = true;
        this._scheduleUpdate();

        try {
            const { form } = this._data;

            // await this.api.post('/users', {
            //     name: form.name,
            //     email: form.email,
            //     password: form.password,
            //     role: form.role,
            //     department: form.department,
            //     phone: form.phone
            // });

            // 模擬 API 延遲
            await new Promise(resolve => setTimeout(resolve, 500));

            this.showMessage('使用者建立成功', 'success');
            this.navigate('/users');

        } catch (error) {
            this.showMessage(error.message || '建立失敗', 'error');
        } finally {
            this._data.submitting = false;
            this._scheduleUpdate();
        }
    }

    _validate() {
        const { form } = this._data;
        const errors = {};

        // 姓名驗證
        if (!form.name.trim()) {
            errors.name = '請輸入姓名';
        }

        // Email 驗證
        if (!form.email.trim()) {
            errors.email = '請輸入 Email';
        } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email)) {
            errors.email = 'Email 格式不正確';
        }

        // 密碼驗證
        if (!form.password) {
            errors.password = '請輸入密碼';
        } else if (form.password.length < 8) {
            errors.password = '密碼至少需要 8 個字元';
        }

        // 確認密碼驗證
        if (!form.confirmPassword) {
            errors.confirmPassword = '請再次輸入密碼';
        } else if (form.password !== form.confirmPassword) {
            errors.confirmPassword = '兩次密碼輸入不一致';
        }

        this._data.errors = errors;

        if (Object.keys(errors).length > 0) {
            this._scheduleUpdate();
            return false;
        }

        return true;
    }
}

export default UserCreatePage;
