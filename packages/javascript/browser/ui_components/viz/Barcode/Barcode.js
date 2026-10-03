/**
 * Barcode — 一維條碼產生器（純 Canvas，零依賴）
 *
 * 支援 Code 128、Code 39、EAN-13、EAN-8，編碼器在 barcode-encoder.js。
 * 台灣超商代收與郵局劃撥的繳費單條碼是 Code 39；商品條碼是 EAN-13。
 *
 * - 預設以固定模組寬（moduleWidth，px）畫出自然寬度；給 width 時改為縮放到該寬度
 * - 條邊對齊整數像素；exportPNG(scale) 可輸出高倍率圖檔供列印
 * - EAN 依標準把護線延伸到數字列，數字分組排在條下
 * - 顏色預設取主題 token；深色主題下自動對調，條永遠比底色暗（ensureContrast）
 * - 內容不合格式時不擲錯，改在畫面上顯示訊息，getError() 取得原因
 *
 * @example
 * const bc = new Barcode({ value: '1150930ABC', format: 'code39', height: 60 });
 * bc.mount(container);
 * bc.setValue('1151031ABD');
 * bc.destroy();
 */
import { CanvasChart } from '../CanvasChart.js';
import { resolveSymbolPaint } from '../symbol-paint.js';
import Locale from '../../i18n/index.js';
import { encodeBarcode, BarcodeEncodeError, BARCODE_FORMATS } from './barcode-encoder.js';
import './locale.js';

const px = (v, d) => (typeof v === 'number' ? v + 'px' : (v || d));
const ERROR_KEYS = {
    invalid_char: 'errorInvalidChar',
    invalid_length: 'errorInvalidLength',
    bad_check_digit: 'errorBadCheckDigit',
    invalid_option: 'errorInvalidOption'
};
const FORMAT_LABELS = { code128: 'Code 128', code39: 'Code 39', ean13: 'EAN-13', ean8: 'EAN-8' };
const ERROR_WIDTH = 240;

export class Barcode extends CanvasChart {
    /**
     * @param {Object} options
     * @param {string} [options.value=''] - 要編碼的內容；空字串時不畫
     * @param {'code128'|'code39'|'ean13'|'ean8'} [options.format='code128']
     * @param {boolean} [options.checkDigit=false] - Code 39 是否附 mod 43 檢查字元
     * @param {number} [options.wideRatio=3] - Code 39 寬窄比（2～3）
     * @param {number} [options.moduleWidth=2] - 最窄條的寬度（px）
     * @param {number|string} [options.width] - 指定寬度時縮放到此寬；未指定時依內容的自然寬度
     * @param {number} [options.height=80] - 總高度（px，含數字列）
     * @param {boolean} [options.showText=true] - 是否在條下顯示可讀文字
     * @param {number} [options.fontSize=14] - 可讀文字的字級（px）
     * @param {{left:number,right:number}} [options.quietZone] - 左右留白的模組數；預設依格式
     * @param {string} [options.darkColor='--cl-text'] - 條色（token 名稱或 CSS 顏色）
     * @param {string} [options.lightColor='--cl-bg'] - 底色（token 名稱或 CSS 顏色）
     * @param {boolean} [options.ensureContrast=true] - 自動對調，確保條比底色暗
     * @param {string} [options.ariaLabel] - 無障礙名稱；預設為「條碼（格式）：內容」
     * @param {Function} [options.onError] - 編碼失敗時呼叫 (error) => void
     */
    constructor(options = {}) {
        const settings = {
            value: options.value == null ? '' : String(options.value),
            format: options.format || 'code128',
            checkDigit: Boolean(options.checkDigit),
            wideRatio: options.wideRatio ?? 3,
            moduleWidth: Number.isFinite(options.moduleWidth) && options.moduleWidth > 0 ? options.moduleWidth : 2,
            showText: options.showText !== false,
            fontSize: Number.isFinite(options.fontSize) ? options.fontSize : 14,
            quietZone: options.quietZone || null
        };
        const fitWidth = options.width !== undefined && options.width !== null && options.width !== 'auto';
        const initial = Barcode._tryEncode(settings);
        super({
            container: options.container || null,
            width: fitWidth ? px(options.width, '100%') : Barcode._naturalWidth(initial.result, settings) + 'px',
            height: px(options.height ?? 80, '80px'),
            ariaLabel: options.ariaLabel || '',
            padding: { top: 0, right: 0, bottom: 0, left: 0 },
            ...settings,
            darkColor: options.darkColor || '--cl-text',
            lightColor: options.lightColor || '--cl-bg',
            ensureContrast: options.ensureContrast !== false,
            onError: typeof options.onError === 'function' ? options.onError : null
        });
        this.element.classList.add('cl-barcode');
        this._fitWidth = fitWidth;
        this._explicitAriaLabel = options.ariaLabel || '';
        this._applyEncoding(initial);
    }

    /* ── 編碼 ── */

    static _tryEncode(o) {
        if (o.value === '') return { result: null, error: null };
        try {
            return {
                result: encodeBarcode(o.value, { format: o.format, checkDigit: o.checkDigit, wideRatio: o.wideRatio }),
                error: null
            };
        } catch (e) {
            const error = e instanceof BarcodeEncodeError ? e : new BarcodeEncodeError('unknown', String(e && e.message));
            return { result: null, error };
        }
    }

    static _quietZone(result, o) {
        return o.quietZone || (result ? result.quietZone : { left: 10, right: 10 });
    }

    static _naturalWidth(result, o) {
        if (!result) return ERROR_WIDTH;
        const qz = Barcode._quietZone(result, o);
        return Math.ceil((result.modules + qz.left + qz.right) * o.moduleWidth);
    }

    _applyEncoding({ result, error }) {
        this._bc = result;
        this._error = error;
        if (error && this.options.onError) this.options.onError(error);
        if (!this._fitWidth && this.element) {
            this.element.style.width = Barcode._naturalWidth(result, this.options) + 'px';
        }
        this._syncAriaLabel();
    }

    _encode() {
        this._applyEncoding(Barcode._tryEncode(this.options));
    }

    _syncAriaLabel() {
        if (!this.canvas) return;
        let label = this._explicitAriaLabel;
        if (!label) {
            label = this.options.value === ''
                ? Locale.t('barcode.ariaEmpty')
                : Locale.t('barcode.ariaLabel', {
                    format: FORMAT_LABELS[this.options.format] || this.options.format,
                    value: this._bc ? this._bc.data : this.options.value
                });
        }
        this.canvas.setAttribute('aria-label', label);
    }

    /* ── 繪製 ── */

    draw(ctx, w, h) {
        const { dark, light } = resolveSymbolPaint(this, ctx, this.options.darkColor, this.options.lightColor, this.options.ensureContrast);

        if (this._error) {
            this._drawError(ctx, w, h);
            return;
        }
        const bc = this._bc;
        if (!bc) return;

        const o = this.options;
        const qz = Barcode._quietZone(bc, o);
        const totalUnits = bc.modules + qz.left + qz.right;
        let unit = o.moduleWidth;
        if (this._fitWidth) {
            unit = w / totalUnits;
            if (unit >= 1) unit = Math.floor(unit);
        }
        const drawnW = unit * totalUnits;
        const ox = Math.floor((w - drawnW) / 2);
        const textH = o.showText ? Math.ceil(o.fontSize * 1.3) : 0;
        const barH = Math.max(1, h - textH);
        const guardExtra = bc.guards && o.showText ? Math.round(textH / 2) : 0;

        ctx.fillStyle = light;
        ctx.fillRect(ox, 0, drawnW, h);

        const isGuard = (moduleIndex) => Boolean(bc.guards && bc.guards.some(([from, to]) => moduleIndex >= from && moduleIndex < to));
        const startX = ox + qz.left * unit;
        ctx.beginPath();
        let pos = 0;
        bc.widths.forEach((width, i) => {
            if (i % 2 === 0) {
                const x0 = Math.round(startX + pos * unit);
                const x1 = Math.round(startX + (pos + width) * unit);
                const barBottom = barH + (isGuard(pos) ? guardExtra : 0);
                ctx.rect(x0, 0, Math.max(1, x1 - x0), barBottom);
            }
            pos += width;
        });
        ctx.fillStyle = dark;
        ctx.fill();

        if (!o.showText) return;
        ctx.font = this.font(o.fontSize);
        ctx.fillStyle = dark;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        const textY = barH + Math.max(1, Math.round((textH - o.fontSize) / 2));
        if (bc.textGroups) {
            for (const group of bc.textGroups) {
                const cx = startX + ((group.from + group.to) / 2) * unit;
                ctx.fillText(group.text, cx, textY);
            }
        } else {
            ctx.fillText(this.ellipsis(ctx, bc.text, drawnW), ox + drawnW / 2, textY);
        }
    }

    _drawError(ctx, w, h) {
        const key = ERROR_KEYS[this._error.code] || 'errorUnknown';
        const message = Locale.t('barcode.' + key, {
            char: this._error.detail && this._error.detail.char,
            expected: this._error.detail && this._error.detail.expected
        });
        const { '--cl-text-secondary': color } = this.tokens(['--cl-text-secondary']);
        ctx.font = this.font(12);
        ctx.fillStyle = color || this.tokens(['--cl-text'])['--cl-text'];
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        const lines = this.wrapText(ctx, message, Math.max(40, w - 16));
        const lineH = 16;
        const top = h / 2 - ((lines.length - 1) * lineH) / 2;
        lines.forEach((line, i) => ctx.fillText(line, w / 2, top + i * lineH));
    }

    /* ── 公開方法 ── */

    /** 目前的內容（使用者給的原文，不含自動補上的檢查碼）。 */
    getValue() {
        return this.options.value;
    }

    /** 更換內容並重繪。 */
    setValue(value) {
        this.options.value = value == null ? '' : String(value);
        this._encode();
        this.render();
        return this;
    }

    /** 清空內容（不畫任何東西）。 */
    clear() {
        return this.setValue('');
    }

    /** 最近一次編碼失敗的原因（{ code, message, detail }），成功時為 null。 */
    getError() {
        return this._error ? { code: this._error.code, message: this._error.message, detail: { ...this._error.detail } } : null;
    }

    /** 編碼結果摘要：格式、實際編入的資料（含檢查碼）、模組數；沒有內容時為 null。 */
    getInfo() {
        if (!this._bc) return null;
        const { format, data, modules } = this._bc;
        return { format, data, modules };
    }

    /** 同時更新多個選項（例如 { value, format }）並重繪。 */
    update(patch = {}) {
        if (patch.value !== undefined) patch = { ...patch, value: patch.value == null ? '' : String(patch.value) };
        if (patch.ariaLabel !== undefined) this._explicitAriaLabel = patch.ariaLabel || '';
        Object.assign(this.options, patch);
        this._encode();
        super.update({});
        return this;
    }

    /** 支援的格式。 */
    static get formats() {
        return [...BARCODE_FORMATS];
    }
}

export default Barcode;
