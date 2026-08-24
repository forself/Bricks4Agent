import {
    BasicButton,
    CommandComposer,
    TextInput
} from '../../ui_components/index.js';
import { PortalApiClient, PortalApiError } from './PortalApiClient.js';

class PortalApp {
    constructor(root, api = new PortalApiClient()) {
        this.root = root;
        this.api = api;
        this.components = [];
        this.state = {
            authMode: 'login',
            authenticated: false,
            status: null,
            me: null,
            lineVerificationIssue: null,
            results: [],
            artifacts: [],
            busy: false,
            error: ''
        };
    }

    async init() {
        this.renderLoading('正在讀取入口狀態');
        try {
            const status = await this.api.status();
            this.state.status = status;
            this.state.authenticated = !!status.authenticated;
            if (this.state.authenticated) {
                await this.loadDashboard();
                return;
            }
        } catch (error) {
            this.state.error = describeError(error);
        }
        this.renderAuth();
    }

    renderLoading(text) {
        this.cleanup();
        this.root.innerHTML = '';
        const el = document.createElement('div');
        el.className = 'portal-loading';
        el.textContent = text;
        this.root.appendChild(el);
    }

    renderAuth() {
        this.cleanup();
        this.root.innerHTML = '';

        const screen = document.createElement('main');
        screen.className = 'portal-auth';
        screen.dataset.testid = 'auth-screen';

        const panel = document.createElement('section');
        panel.className = 'portal-auth__panel';
        panel.setAttribute('aria-labelledby', 'portal-auth-title');

        const brand = document.createElement('div');
        brand.className = 'portal-auth__brand';
        const title = document.createElement('h1');
        title.id = 'portal-auth-title';
        title.className = 'portal-auth__title';
        title.textContent = 'Bricks4Agent';
        const badge = document.createElement('span');
        badge.className = 'portal-badge';
        badge.textContent = 'Portal';
        brand.append(title, badge);

        const mode = document.createElement('div');
        mode.className = 'portal-auth__mode';
        mode.append(
            this.createModeButton('login', '登入'),
            this.createModeButton('register', '註冊')
        );

        const form = document.createElement('form');
        form.className = 'portal-auth__fields';
        form.dataset.testid = 'auth-form';

        const userInput = this.addComponent(new TextInput({
            label: '使用者 ID',
            placeholder: 'user@example.com',
            required: true,
            maxLength: 80,
            autocomplete: 'username',
            enableSecurity: false
        }));
        userInput.mount(form);

        const passwordInput = this.addComponent(new TextInput({
            type: 'password',
            label: '密碼',
            placeholder: '至少 8 個字元',
            required: true,
            autocomplete: this.state.authMode === 'login' ? 'current-password' : 'new-password',
            enableSecurity: false
        }));
        passwordInput.mount(form);

        let displayNameInput = null;
        if (this.state.authMode === 'register') {
            displayNameInput = this.addComponent(new TextInput({
                label: '顯示名稱',
                placeholder: '選填',
                maxLength: 80,
                autocomplete: 'name',
                enableSecurity: false
            }));
            displayNameInput.mount(form);
        }

        const actions = document.createElement('div');
        actions.className = 'portal-auth__actions';
        const submit = this.addComponent(new BasicButton({
            type: 'confirm',
            customLabel: this.state.authMode === 'login' ? '登入' : '建立帳號',
            showIcon: false,
            onClick: () => form.requestSubmit()
        }));
        submit.mount(actions);

        const error = document.createElement('div');
        error.className = 'portal-alert';
        error.hidden = !this.state.error;
        error.textContent = this.state.error;
        error.dataset.testid = 'auth-error';

        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            submit.setLoading(true);
            this.state.error = '';
            try {
                const payload = {
                    user_id: userInput.getValue(),
                    password: passwordInput.getValue()
                };
                if (displayNameInput) {
                    payload.display_name = displayNameInput.getValue();
                }

                const authResult = this.state.authMode === 'login'
                    ? await this.api.login(payload)
                    : await this.api.register(payload);
                this.state.lineVerificationIssue = authResult?.line_verification ?? null;
                await this.loadDashboard();
            } catch (err) {
                this.state.error = describeError(err);
                this.renderAuth();
            } finally {
                submit.setLoading(false);
            }
        });

        panel.append(brand, mode, form, actions, error);
        screen.appendChild(panel);
        this.root.appendChild(screen);
    }

    createModeButton(mode, label) {
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = label;
        button.setAttribute('aria-pressed', String(this.state.authMode === mode));
        button.addEventListener('click', () => {
            this.state.authMode = mode;
            this.state.error = '';
            this.renderAuth();
        });
        return button;
    }

    async loadDashboard() {
        this.renderLoading('正在讀取工作區');
        const [me, results, artifacts, status] = await Promise.all([
            this.api.me(),
            this.api.results(30),
            this.api.artifacts(50),
            this.api.status()
        ]);

        this.state.authenticated = true;
        this.state.status = status;
        this.state.me = me;
        this.state.results = Array.isArray(results.items) ? results.items : [];
        this.state.artifacts = Array.isArray(artifacts.items) ? artifacts.items : [];
        this.state.error = '';
        this.renderDashboard();
    }

    renderDashboard() {
        this.cleanup();
        this.root.innerHTML = '';

        const shell = document.createElement('main');
        shell.className = 'portal-shell';
        shell.dataset.testid = 'portal-shell';
        shell.append(this.renderTopbar(), this.renderLayout());
        this.root.appendChild(shell);
    }

    renderTopbar() {
        const profile = this.state.me?.profile ?? {};
        const topbar = document.createElement('header');
        topbar.className = 'portal-topbar';

        const identity = document.createElement('div');
        identity.className = 'portal-topbar__identity';
        const name = document.createElement('div');
        name.className = 'portal-topbar__name';
        name.textContent = profile.display_name || profile.user_id || '使用者';
        const meta = document.createElement('div');
        meta.className = 'portal-topbar__meta';
        meta.textContent = `${profile.user_id ?? ''} · ${profile.access_tier ?? 'basic'} · ${profile.registration_status ?? ''}`;
        identity.append(name, meta);

        const actions = document.createElement('div');
        actions.className = 'portal-topbar__actions';
        const refresh = this.addComponent(new BasicButton({
            type: 'refresh',
            variant: 'secondary',
            customLabel: '重新整理',
            onClick: async () => {
                try {
                    await this.loadDashboard();
                } catch (error) {
                    this.state.error = describeError(error);
                    this.renderDashboard();
                }
            }
        }));
        const logout = this.addComponent(new BasicButton({
            type: 'close',
            variant: 'secondary',
            customLabel: '登出',
            onClick: async () => {
                await this.api.logout();
                this.state.authenticated = false;
                this.state.me = null;
                this.state.status = null;
                this.state.lineVerificationIssue = null;
                this.renderAuth();
            }
        }));
        refresh.mount(actions);
        logout.mount(actions);

        topbar.append(identity, actions);
        return topbar;
    }

    renderLayout() {
        const layout = document.createElement('div');
        layout.className = 'portal-layout';
        layout.append(
            this.renderProfilePanel(),
            this.renderWorkspace(),
            this.renderArtifactsPanel()
        );
        return layout;
    }

    renderProfilePanel() {
        const profile = this.state.me?.profile ?? {};
        const currentLineStatus = this.state.me?.line_verification ?? this.state.status?.line_verification ?? null;
        const lineVerification = currentLineStatus?.verified
            ? currentLineStatus
            : (this.state.lineVerificationIssue ?? currentLineStatus);
        const section = document.createElement('aside');
        section.className = 'portal-section';
        section.setAttribute('aria-labelledby', 'portal-profile-title');
        section.innerHTML = '<h2 id="portal-profile-title" class="portal-section__title">帳戶</h2>';

        const stats = document.createElement('div');
        stats.className = 'portal-stat-list';
        [
            ['狀態', profile.registration_status ?? ''],
            ['權限等級', profile.access_tier ?? ''],
            ['使用者代碼', profile.user_code || '未設定'],
            ['最後互動', formatDate(profile.last_interaction_at)]
        ].forEach(([label, value]) => stats.appendChild(createStat(label, value)));

        section.append(stats, this.renderLineVerificationPanel(lineVerification));
        return section;
    }

    renderLineVerificationPanel(lineVerification) {
        const panel = document.createElement('div');
        panel.className = 'portal-line-verification';
        panel.dataset.testid = 'line-verification-panel';

        const title = document.createElement('h3');
        title.className = 'portal-line-verification__title';
        title.textContent = 'LINE 帳號綁定';

        const status = document.createElement('div');
        status.className = 'portal-line-verification__status';
        status.textContent = lineVerification?.verified ? '已完成' : '尚未完成';

        const body = document.createElement('p');
        body.className = 'portal-line-verification__body';
        body.textContent = lineVerification?.verified
            ? '這個 Portal 帳號已可透過 LINE 使用。'
            : '請在 LINE 傳送網站產生的驗證指令；帳號或驗證碼不符合時，LINE 入口會拒絕操作。';

        panel.append(title, status, body);

        if (!lineVerification?.verified && lineVerification?.command) {
            const command = document.createElement('code');
            command.className = 'portal-line-verification__command';
            command.dataset.testid = 'line-verification-command';
            command.textContent = lineVerification.command;
            panel.appendChild(command);
        }

        if (!lineVerification?.verified && lineVerification?.expires_at) {
            const expires = document.createElement('div');
            expires.className = 'portal-line-verification__expires';
            expires.textContent = `有效期限 ${formatDate(lineVerification.expires_at)}`;
            panel.appendChild(expires);
        }

        if (!lineVerification?.verified) {
            const actions = document.createElement('div');
            actions.className = 'portal-line-verification__actions';
            const issue = this.addComponent(new BasicButton({
                type: 'refresh',
                variant: 'secondary',
                size: 'small',
                customLabel: lineVerification?.command ? '重新產生驗證碼' : '產生 LINE 驗證碼',
                showIcon: false,
                onClick: async () => {
                    issue.setLoading(true);
                    try {
                        this.state.lineVerificationIssue = await this.api.issueLineVerification();
                        this.state.error = '';
                        this.renderDashboard();
                    } catch (error) {
                        this.state.error = describeError(error);
                        this.updateStatus(this.state.error);
                    } finally {
                        issue.setLoading(false);
                    }
                }
            }));
            issue.mount(actions);
            panel.appendChild(actions);
        }

        return panel;
    }

    renderWorkspace() {
        const workspace = document.createElement('section');
        workspace.className = 'portal-workspace';
        workspace.setAttribute('aria-labelledby', 'portal-workspace-title');

        const header = document.createElement('div');
        header.className = 'portal-workspace__header';
        const title = document.createElement('h2');
        title.id = 'portal-workspace-title';
        title.className = 'portal-workspace__title';
        title.textContent = '指令與回應';
        const status = document.createElement('div');
        status.className = 'portal-status';
        status.dataset.testid = 'portal-status';
        status.textContent = this.state.error;
        header.append(title, status);

        const feed = document.createElement('div');
        feed.className = 'portal-workspace__feed';
        feed.dataset.testid = 'result-feed';
        if (this.state.results.length === 0) {
            feed.appendChild(createEmpty('尚無結果'));
        } else {
            this.state.results.forEach((item) => {
                feed.appendChild(renderInteraction(item));
            });
        }

        const composerHost = document.createElement('div');
        composerHost.className = 'portal-workspace__composer';
        const composer = this.addComponent(new CommandComposer({
            placeholder: '輸入需求或指令',
            submitLabel: '送出',
            ariaLabel: '送出需求',
            onSubmit: (value) => {
                this.submitCommand(value);
            }
        }));
        composer.mount(composerHost);

        workspace.append(header, feed, composerHost);
        requestAnimationFrame(() => {
            feed.scrollTop = feed.scrollHeight;
            composer.focus();
        });
        return workspace;
    }

    renderArtifactsPanel() {
        const section = document.createElement('aside');
        section.className = 'portal-section';
        section.setAttribute('aria-labelledby', 'portal-artifacts-title');
        section.innerHTML = '<h2 id="portal-artifacts-title" class="portal-section__title">結果檔案</h2>';

        const list = document.createElement('div');
        list.className = 'portal-artifact-list';
        list.dataset.testid = 'artifact-list';
        if (this.state.artifacts.length === 0) {
            list.appendChild(createEmpty('尚無檔案'));
        } else {
            this.state.artifacts.forEach((item) => list.appendChild(renderArtifact(item)));
        }

        section.appendChild(list);
        return section;
    }

    async submitCommand(value) {
        this.state.busy = true;
        this.state.error = '';
        const composer = this.components.find((component) => component instanceof CommandComposer);
        composer?.setLoading(true);
        this.updateStatus('處理中');

        try {
            await this.api.sendCommand(value);
            const [results, artifacts, me] = await Promise.all([
                this.api.results(30),
                this.api.artifacts(50),
                this.api.me()
            ]);
            this.state.results = Array.isArray(results.items) ? results.items : [];
            this.state.artifacts = Array.isArray(artifacts.items) ? artifacts.items : [];
            this.state.me = me;
            this.state.error = '';
            this.renderDashboard();
        } catch (error) {
            this.state.error = describeError(error);
            this.updateStatus(this.state.error);
        } finally {
            composer?.setLoading(false);
            this.state.busy = false;
        }
    }

    updateStatus(text) {
        const status = this.root.querySelector('[data-testid="portal-status"]');
        if (status) status.textContent = text;
    }

    addComponent(component) {
        this.components.push(component);
        return component;
    }

    cleanup() {
        this.components.forEach((component) => component.destroy?.());
        this.components = [];
    }
}

function renderInteraction(item) {
    const group = document.createElement('article');
    group.className = 'portal-result-list';
    group.dataset.testid = 'result-item';

    const user = document.createElement('div');
    user.className = 'portal-card portal-card__user';
    const userTitle = document.createElement('h3');
    userTitle.className = 'portal-card__title';
    userTitle.textContent = '使用者';
    const userMeta = document.createElement('div');
    userMeta.className = 'portal-card__meta';
    userMeta.textContent = formatDate(item.occurred_at);
    const userBody = document.createElement('p');
    userBody.className = 'portal-card__body';
    userBody.textContent = item.user_message ?? '';
    user.append(userTitle, userMeta, userBody);

    const assistant = document.createElement('div');
    assistant.className = 'portal-card portal-card__assistant';
    const assistantTitle = document.createElement('h3');
    assistantTitle.className = 'portal-card__title';
    assistantTitle.textContent = 'AI 代理';
    const assistantMeta = document.createElement('div');
    assistantMeta.className = 'portal-card__meta';
    assistantMeta.textContent = [item.route_mode, item.workflow_action, item.decision_reason]
        .filter(Boolean)
        .join(' · ');
    const assistantBody = document.createElement('p');
    assistantBody.className = 'portal-card__body';
    assistantBody.textContent = item.reply ?? item.error ?? '';
    assistant.append(assistantTitle, assistantMeta, assistantBody);

    group.append(user, assistant);
    return group;
}

function renderArtifact(item) {
    const card = document.createElement('article');
    card.className = 'portal-card portal-card__artifact';

    const title = document.createElement('h3');
    title.className = 'portal-card__title';
    title.textContent = item.file_name || item.document_id || item.artifact_id;

    const meta = document.createElement('div');
    meta.className = 'portal-card__meta';
    meta.textContent = [item.format, item.overall_status, formatDate(item.created_at)].filter(Boolean).join(' · ');

    card.append(title, meta);
    if (item.download_url) {
        const link = document.createElement('a');
        link.href = item.download_url;
        link.textContent = '下載';
        if (/^https?:\/\//i.test(item.download_url)) {
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
        }
        card.appendChild(link);
    }

    return card;
}

function createStat(label, value) {
    const stat = document.createElement('div');
    stat.className = 'portal-stat';
    const labelEl = document.createElement('div');
    labelEl.className = 'portal-stat__label';
    labelEl.textContent = label;
    const valueEl = document.createElement('div');
    valueEl.className = 'portal-stat__value';
    valueEl.textContent = value || '無';
    stat.append(labelEl, valueEl);
    return stat;
}

function createEmpty(text) {
    const empty = document.createElement('div');
    empty.className = 'portal-empty';
    empty.textContent = text;
    return empty;
}

function formatDate(value) {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat('zh-TW', {
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
    }).format(date);
}

function describeError(error) {
    if (error instanceof PortalApiError) {
        return error.message || `HTTP ${error.status}`;
    }
    return error?.message ?? '發生未知錯誤';
}

const root = document.getElementById('app');
if (root) {
    const app = new PortalApp(root);
    app.init();
}

export { PortalApp };
