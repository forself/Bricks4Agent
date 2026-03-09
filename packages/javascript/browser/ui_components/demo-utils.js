/**
 * Demo 頁面共用工具
 * 提供深色模式切換按鈕，供所有元件 demo.html 使用
 */

/**
 * 建立並掛載深色模式切換按鈕
 * 自動讀取 localStorage 恢復上次主題選擇
 */
export function createThemeToggle() {
    const btn = document.createElement('button');
    btn.id = 'theme-toggle';
    btn.style.cssText = `
        position: fixed; top: 12px; right: 12px; z-index: 9999;
        padding: 6px 14px; border: 1px solid var(--cl-border, #ddd);
        border-radius: var(--cl-radius-md, 6px);
        background: var(--cl-bg, #fff); color: var(--cl-text, #333);
        cursor: pointer; font-size: 13px; font-family: inherit;
        transition: background 0.2s, color 0.2s, border-color 0.2s;
        box-shadow: var(--cl-shadow-sm, 0 1px 3px rgba(0,0,0,0.1));
    `;

    const update = () => {
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
        btn.textContent = isDark ? '☀ Light' : '☾ Dark';
    };

    btn.addEventListener('click', () => {
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
        const next = isDark ? '' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('cl-demo-theme', next || 'light');
        update();
    });

    // 恢復上次主題
    const saved = localStorage.getItem('cl-demo-theme');
    if (saved === 'dark') {
        document.documentElement.setAttribute('data-theme', 'dark');
    }
    update();

    document.body.appendChild(btn);
}
