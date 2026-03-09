/**
 * Locale — 元件庫集中式國際化模組
 *
 * 提供全域語言切換、字串取值、插值、元件級覆寫等功能。
 * 預設語言為 zh-TW，內建 zh-TW 與 en 兩套語系。
 *
 * 使用方式：
 *   import Locale from '../i18n/index.js';
 *   Locale.setLang('en');
 *   const label = Locale.t('dropdown.placeholder');
 *   const strings = Locale.getComponentStrings('dropdown', options.locale);
 */

import zhTW from './locales/zh-TW.js';
import en from './locales/en.js';

const Locale = {
    /** 目前語言 */
    _lang: 'zh-TW',

    /** 語系資料 { 'zh-TW': { namespace: { key: value } }, 'en': { ... } } */
    _strings: {
        'zh-TW': zhTW,
        'en': en
    },

    /**
     * 設定全域語言
     * @param {string} lang - 語言代碼，如 'zh-TW', 'en'
     */
    setLang(lang) {
        if (this._strings[lang] || lang === this._lang) {
            this._lang = lang;
            // 觸發自訂事件通知元件更新
            if (typeof window !== 'undefined') {
                window.dispatchEvent(new CustomEvent('locale-changed', { detail: { lang } }));
            }
        } else {
            console.warn(`[Locale] 語系 "${lang}" 尚未註冊`);
        }
    },

    /**
     * 取得目前語言
     * @returns {string}
     */
    getLang() {
        return this._lang;
    },

    /**
     * 取得所有已註冊的語言代碼
     * @returns {string[]}
     */
    getAvailableLangs() {
        return Object.keys(this._strings);
    },

    /**
     * 註冊語系包（可局部或整包）
     * @param {string} lang - 語言代碼
     * @param {string|object} nsOrStrings - 命名空間字串，或整個語系物件
     * @param {object} [strings] - 該命名空間下的字串物件（當第二參數為命名空間時）
     */
    register(lang, nsOrStrings, strings) {
        if (!this._strings[lang]) {
            this._strings[lang] = {};
        }
        if (typeof nsOrStrings === 'string' && strings) {
            // register('ja', 'dropdown', { placeholder: '...' })
            this._strings[lang][nsOrStrings] = this._deepMerge(
                this._strings[lang][nsOrStrings] || {},
                strings
            );
        } else if (typeof nsOrStrings === 'object') {
            // register('ja', { dropdown: { ... }, datePicker: { ... } })
            this._strings[lang] = this._deepMerge(this._strings[lang], nsOrStrings);
        }
    },

    /**
     * 取得翻譯字串
     * @param {string} path - 點分隔路徑，如 'dropdown.placeholder'
     * @param {object} [params] - 插值參數，如 { count: 3 }
     * @param {string} [lang] - 指定語言（預設使用全域語言）
     * @returns {string}
     */
    t(path, params, lang) {
        const targetLang = lang || this._lang;
        const keys = path.split('.');
        let value = this._strings[targetLang];

        for (const key of keys) {
            if (value == null || typeof value !== 'object') {
                // fallback 到 zh-TW
                value = null;
                break;
            }
            value = value[key];
        }

        // fallback: 若目標語系找不到，嘗試 zh-TW
        if (value == null && targetLang !== 'zh-TW') {
            value = this._strings['zh-TW'];
            for (const key of keys) {
                if (value == null || typeof value !== 'object') {
                    value = null;
                    break;
                }
                value = value[key];
            }
        }

        // 最終 fallback: 回傳路徑本身
        if (value == null) return path;
        if (typeof value !== 'string') return value;

        // 插值處理: {key} → params[key]
        if (params) {
            return value.replace(/\{(\w+)\}/g, (_, k) =>
                params[k] !== undefined ? params[k] : `{${k}}`
            );
        }
        return value;
    },

    /**
     * 取得元件的完整字串物件（合併預設 + 使用者覆寫）
     * @param {string} namespace - 元件命名空間，如 'dropdown'
     * @param {object} [overrides] - 使用者覆寫的字串物件
     * @param {string} [lang] - 指定語言
     * @returns {object} 合併後的字串物件
     */
    getComponentStrings(namespace, overrides, lang) {
        const targetLang = lang || this._lang;
        const base = (this._strings[targetLang] && this._strings[targetLang][namespace]) || {};
        const fallback = (this._strings['zh-TW'] && this._strings['zh-TW'][namespace]) || {};
        // 三層合併: zh-TW fallback → 目標語言 → 使用者覆寫
        const merged = this._deepMerge(this._deepMerge({}, fallback), base);
        if (overrides && typeof overrides === 'object') {
            return this._deepMerge(merged, overrides);
        }
        return merged;
    },

    /**
     * 深層合併物件
     * @private
     */
    _deepMerge(target, source) {
        const result = Object.assign({}, target);
        for (const key of Object.keys(source)) {
            if (
                source[key] &&
                typeof source[key] === 'object' &&
                !Array.isArray(source[key]) &&
                target[key] &&
                typeof target[key] === 'object' &&
                !Array.isArray(target[key])
            ) {
                result[key] = this._deepMerge(target[key], source[key]);
            } else {
                result[key] = source[key];
            }
        }
        return result;
    }
};

export default Locale;
