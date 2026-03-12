/**
 * SimpleDialog - 輕量級通用對話框組件
 * 提供非阻塞的 Alert, Confirm, Prompt 功能，可指定掛載容器。
 */
import Locale from '../../i18n/index.js';

export class SimpleDialog {

    /**
     * 顯示提示訊息 (取代 window.alert)
     * @param {string} message 訊息內容
     * @param {HTMLElement} container 掛載容器 (預設為 document.body)
     */
    static alert(message, container = document.body) {
        return new Promise((resolve) => {
            this._create({
                title: '提示',
                content: message,
                onConfirm: () => resolve(true),
                container
            });
        });
    }

    /**
     * 顯示確認對話框 (取代 window.confirm)
     * @param {string} message 詢問內容
     * @param {HTMLElement} container 掛載容器
     */
    static confirm(message, container = document.body) {
        return new Promise((resolve) => {
            this._create({
                title: '確認',
                content: message,
                onConfirm: () => resolve(true),
                onCancel: () => resolve(false),
                container
            });
        });
    }

    /**
     * 顯示輸入對話框 (取代 window.prompt)
     * @param {string} message 提示文字
     * @param {string} defaultValue 預設值
     * @param {HTMLElement} container 掛載容器
     */
    static prompt(message, defaultValue = '', container = document.body) {
        return new Promise((resolve) => {
            this._create({
                title: message,
                inputValue: defaultValue,
                onConfirm: (val) => resolve(val),
                onCancel: () => resolve(null),
                container
            });
        });
    }

    /**
     * 內部實作：建立 DOM
     */
    static _create({ title, content, onConfirm, onCancel, inputValue, container }) {
        // 遮罩
        const overlay = document.createElement('div');
        overlay.style.cssText = `
            position: fixed;
            top: 0; left: 0; width: 100%; height: 100%;
            background: var(--cl-bg-overlay);
            display: flex;
            justify-content: center;
            align-items: center;
            z-index: 9999;
            opacity: 0;
            transition: opacity var(--cl-transition);
        `;

        // 對話框
        const dialog = document.createElement('div');
        dialog.style.cssText = `
            background: var(--cl-bg);
            padding: 20px;
            border-radius: var(--cl-radius-lg);
            box-shadow: var(--cl-shadow-lg);
            width: 320px;
            display: flex;
            flex-direction: column;
            gap: 15px;
            transform: translateY(-20px);
            transition: transform var(--cl-transition);
            text-align: left;
            font-family: var(--cl-font-family);
        `;

        // 標題
        const titleEl = document.createElement('h3');
        titleEl.textContent = title;
        titleEl.style.margin = '0';
        titleEl.style.fontSize = 'var(--cl-font-size-2xl)';
        titleEl.style.color = 'var(--cl-text)';
        dialog.appendChild(titleEl);

        // 訊息內容
        if (content) {
            const msgEl = document.createElement('div');
            msgEl.textContent = content;
            msgEl.style.cssText = 'font-size: var(--cl-font-size-lg); color: var(--cl-text-secondary); line-height: 1.5; white-space: pre-wrap;';
            dialog.appendChild(msgEl);
        }

        // 輸入框
        let input = null;
        if (inputValue !== undefined) {
             input = document.createElement('input');
             input.type = 'text';
             input.value = inputValue || '';
             input.style.cssText = `
                padding: 10px;
                border: 1px solid var(--cl-border);
                border-radius: var(--cl-radius-sm);
                width: 100%;
                font-size: var(--cl-font-size-lg);
                box-sizing: border-box;
                font-family: inherit;
             `;
             dialog.appendChild(input);

             input.onkeydown = (e) => {
                 if (e.key === 'Enter') confirmBtn.click();
                 if (e.key === 'Escape') cancelBtn ? cancelBtn.click() : null;
             };
        }

        // 按鈕區
        const btnContainer = document.createElement('div');
        btnContainer.style.cssText = `
            display: flex;
            justify-content: flex-end;
            gap: 10px;
            margin-top: 5px;
        `;

        const close = () => {
            overlay.style.opacity = '0';
            setTimeout(() => {
                if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
            }, 200);
        };

        // 取消按鈕
        let cancelBtn = null;
        if (onCancel) {
            cancelBtn = document.createElement('button');
            cancelBtn.textContent = Locale.t('dialog.cancelBtn');
            cancelBtn.style.cssText = `
                padding: 8px 16px;
                border: 1px solid var(--cl-border);
                background: var(--cl-bg);
                color: var(--cl-text-secondary);
                font-family: inherit;
                border-radius: var(--cl-radius-sm);
                cursor: pointer;
                font-size: var(--cl-font-size-lg);
                transition: background var(--cl-transition);
            `;
            cancelBtn.onmouseover = () => cancelBtn.style.background = 'var(--cl-bg-secondary)';
            cancelBtn.onmouseout = () => cancelBtn.style.background = 'var(--cl-bg)';
            cancelBtn.onclick = () => {
                close();
                onCancel();
            };
            btnContainer.appendChild(cancelBtn);
        }

        // 確認按鈕
        const confirmBtn = document.createElement('button');
        confirmBtn.textContent = Locale.t('dialog.confirmBtn');
        confirmBtn.style.cssText = `
            padding: 8px 16px;
            border: none;
            background: var(--cl-primary);
            color: var(--cl-text-inverse);
            font-family: inherit;
            border-radius: var(--cl-radius-sm);
            cursor: pointer;
            font-size: var(--cl-font-size-lg);
            font-weight: 500;
            transition: background var(--cl-transition);
        `;
        confirmBtn.onmouseover = () => confirmBtn.style.background = 'var(--cl-primary-dark)';
        confirmBtn.onmouseout = () => confirmBtn.style.background = 'var(--cl-primary)';
        confirmBtn.onclick = () => {
            close();
            onConfirm(input ? input.value : true);
        };
        btnContainer.appendChild(confirmBtn);

        dialog.appendChild(btnContainer);
        overlay.appendChild(dialog);

        // 掛載到 body（使用 fixed 定位，不需要 container 有 position）
        document.body.appendChild(overlay);

        // 動畫進場
        requestAnimationFrame(() => {
            overlay.style.opacity = '1';
            dialog.style.transform = 'translateY(0)';
            if (input) {
                input.focus();
                input.select();
            }
        });
    }
}
