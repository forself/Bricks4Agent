/**
 * Security Utilities - 安全性工具函數
 *
 * 提供 XSS 防護、輸入驗證等安全功能
 *
 * @module Security
 * @version 1.0.0
 */

/**
 * HTML 轉義 - 防止 XSS 攻擊
 * 將特殊字元轉換為 HTML 實體
 *
 * @param {string} str - 要轉義的字串
 * @returns {string} 轉義後的安全字串
 *
 * @example
 * escapeHtml('<script>alert("xss")</script>')
 * // 返回: &lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;
 */
export function escapeHtml(str) {
    if (str === null || str === undefined) return '';
    if (typeof str !== 'string') str = String(str);

    const escapeMap = {
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#x27;',
        '/': '&#x2F;',
        '`': '&#x60;',
        '=': '&#x3D;'
    };

    return str.replace(/[&<>"'`=\/]/g, char => escapeMap[char]);
}

/**
 * HTML 屬性值轉義
 * 用於安全地將值放入 HTML 屬性中
 *
 * @param {string} str - 要轉義的字串
 * @returns {string} 轉義後的安全字串
 */
export function escapeAttr(str) {
    if (str === null || str === undefined) return '';
    if (typeof str !== 'string') str = String(str);

    return str
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#x27;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

/**
 * URL 參數轉義
 *
 * @param {string} str - 要轉義的字串
 * @returns {string} URL 編碼後的字串
 */
export function escapeUrl(str) {
    if (str === null || str === undefined) return '';
    return encodeURIComponent(String(str));
}

/**
 * 安全的 JSON 解析
 * 防止 JSON 注入攻擊
 *
 * @param {string} json - JSON 字串
 * @param {any} defaultValue - 解析失敗時的預設值
 * @returns {any} 解析後的物件
 */
export function safeJsonParse(json, defaultValue = null) {
    try {
        return JSON.parse(json);
    } catch {
        return defaultValue;
    }
}

/**
 * 驗證 Email 格式
 *
 * @param {string} email - Email 地址
 * @returns {boolean} 是否為有效的 Email
 */
export function isValidEmail(email) {
    if (!email || typeof email !== 'string') return false;
    // RFC 5322 簡化版本
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return emailRegex.test(email) && email.length <= 254;
}

/**
 * 驗證密碼強度
 *
 * @param {string} password - 密碼
 * @returns {Object} 驗證結果 { valid: boolean, errors: string[] }
 */
export function validatePassword(password) {
    const errors = [];

    if (!password || typeof password !== 'string') {
        return { valid: false, errors: ['密碼不能為空'] };
    }

    if (password.length < 8) {
        errors.push('密碼長度至少 8 個字元');
    }

    if (password.length > 128) {
        errors.push('密碼長度不能超過 128 個字元');
    }

    if (!/[a-z]/.test(password)) {
        errors.push('密碼需包含小寫字母');
    }

    if (!/[A-Z]/.test(password)) {
        errors.push('密碼需包含大寫字母');
    }

    if (!/[0-9]/.test(password)) {
        errors.push('密碼需包含數字');
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

/**
 * 清理使用者輸入
 * 移除控制字元和過長的輸入
 *
 * @param {string} input - 使用者輸入
 * @param {number} maxLength - 最大長度
 * @returns {string} 清理後的字串
 */
export function sanitizeInput(input, maxLength = 1000) {
    if (input === null || input === undefined) return '';
    if (typeof input !== 'string') input = String(input);

    // 移除控制字元 (除了換行和 tab)
    let sanitized = input.replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g, '');

    // 限制長度
    if (sanitized.length > maxLength) {
        sanitized = sanitized.substring(0, maxLength);
    }

    return sanitized.trim();
}

/**
 * 產生安全的隨機字串 (用於 CSRF token 等)
 *
 * @param {number} length - 字串長度
 * @returns {string} 隨機字串
 */
export function generateSecureToken(length = 32) {
    const array = new Uint8Array(length);
    crypto.getRandomValues(array);
    return Array.from(array, byte => byte.toString(16).padStart(2, '0')).join('');
}

/**
 * 建立安全的模板標籤函數
 * 自動轉義所有插值
 *
 * @example
 * const userName = '<script>alert("xss")</script>';
 * const html = safeHtml`<div>${userName}</div>`;
 * // 返回: <div>&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;</div>
 */
export function safeHtml(strings, ...values) {
    return strings.reduce((result, str, i) => {
        const value = i < values.length ? escapeHtml(values[i]) : '';
        return result + str + value;
    }, '');
}

export default {
    escapeHtml,
    escapeAttr,
    escapeUrl,
    safeJsonParse,
    isValidEmail,
    validatePassword,
    sanitizeInput,
    generateSecureToken,
    safeHtml
};
