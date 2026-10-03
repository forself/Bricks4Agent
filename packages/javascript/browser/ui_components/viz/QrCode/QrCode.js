/**
 * QrCode — QR Code 產生器（純 Canvas，零依賴）
 *
 * 用途：繳費資訊、證明文件的查驗連結、課程大綱或教室課表的公開網址、活動報到等。
 * 編碼器在 qr-encoder.js（ISO/IEC 18004，版本 1～40、錯誤修正 L/M/Q/H、UTF-8）。
 *
 * - 模組以整數像素對齊，邊緣銳利；exportPNG(scale) 可輸出高倍率圖檔供列印
 * - 顏色預設取主題 token；深色主題下自動對調，模組永遠比底色暗（ensureContrast）
 * - 內容過長或設定錯誤時不擲錯，改在畫面上顯示訊息，getError() 取得原因
 *
 * @example
 * const qr = new QrCode({ value: 'https://example.edu.tw/verify/7F3A9C21', size: 180, ecLevel: 'Q' });
 * qr.mount(container);
 * qr.setValue('https://example.edu.tw/verify/44B0D7E1');
 * const png = qr.exportPNG(4);
 * qr.destroy();
 */
import { CanvasChart } from '../CanvasChart.js';
import { resolveSymbolPaint } from '../symbol-paint.js';
import Locale from '../../i18n/index.js';
import { encodeQr, QrEncodeError } from './qr-encoder.js';
import './locale.js';

const px = (v, d) => (typeof v === 'number' ? v + 'px' : (v || d));
const ERROR_KEYS = { too_long: 'errorTooLong', invalid_option: 'errorInvalidOption' };
const ARIA_VALUE_MAX = 200;

export class QrCode extends CanvasChart {
    /**
     * @param {Object} options
     * @param {string} [options.value=''] - 要編碼的文字或網址；空字串時不畫
     * @param {'L'|'M'|'Q'|'H'} [options.ecLevel='M'] - 錯誤修正等級（L 7%、M 15%、Q 25%、H 30%）
     * @param {number|string} [options.size=160] - 邊長（數字為 px）
     * @param {number} [options.margin=4] - 四周留白的模組數（標準為 4）
     * @param {number} [options.minVersion=1] - 最小版本
     * @param {number} [options.maxVersion=40] - 最大版本
     * @param {number|'auto'} [options.mask='auto'] - 遮罩 0～7，預設依罰分自動選
     * @param {boolean} [options.boostEcl=true] - 同一版本放得下時自動提高錯誤修正等級
     * @param {boolean} [options.eci=false] - 加上 UTF-8 的 ECI 宣告（少數舊掃描器才需要）
     * @param {string} [options.darkColor='--cl-text'] - 模組色（token 名稱或 CSS 顏色）
     * @param {string} [options.lightColor='--cl-bg'] - 底色（token 名稱或 CSS 顏色）
     * @param {boolean} [options.ensureContrast=true] - 自動對調，確保模組比底色暗
     * @param {string} [options.ariaLabel] - 無障礙名稱；預設為「QR Code：內容」
     * @param {Function} [options.onError] - 編碼失敗時呼叫 (error) => void
     */
    constructor(options = {}) {
        const size = options.size ?? 160;
        super({
            container: options.container || null,
            width: px(size, '160px'),
            height: px(size, '160px'),
            ariaLabel: options.ariaLabel || '',
            padding: { top: 0, right: 0, bottom: 0, left: 0 },
            value: options.value == null ? '' : String(options.value),
            ecLevel: options.ecLevel || 'M',
            margin: Number.isFinite(options.margin) ? Math.max(0, Math.floor(options.margin)) : 4,
            minVersion: options.minVersion ?? 1,
            maxVersion: options.maxVersion ?? 40,
            mask: options.mask ?? 'auto',
            boostEcl: options.boostEcl !== false,
            eci: Boolean(options.eci),
            darkColor: options.darkColor || '--cl-text',
            lightColor: options.lightColor || '--cl-bg',
            ensureContrast: options.ensureContrast !== false,
            onError: typeof options.onError === 'function' ? options.onError : null
        });
        this.element.classList.add('cl-qr-code');
        this._explicitAriaLabel = options.ariaLabel || '';
        this._encode();
    }

    /* ── 編碼 ── */

    _encode() {
        const o = this.options;
        this._qr = null;
        this._error = null;
        if (o.value !== '') {
            try {
                this._qr = encodeQr(o.value, {
                    ecLevel: o.ecLevel,
                    minVersion: o.minVersion,
                    maxVersion: o.maxVersion,
                    mask: o.mask,
                    boostEcl: o.boostEcl,
                    eci: o.eci
                });
            } catch (e) {
                this._error = e instanceof QrEncodeError ? e : new QrEncodeError('unknown', String(e && e.message));
                if (o.onError) o.onError(this._error);
            }
        }
        this._syncAriaLabel();
    }

    _syncAriaLabel() {
        if (!this.canvas) return;
        let label = this._explicitAriaLabel;
        if (!label) {
            const value = this.options.value;
            label = value === ''
                ? Locale.t('qrCode.ariaEmpty')
                : Locale.t('qrCode.ariaLabel', { value: value.length > ARIA_VALUE_MAX ? value.slice(0, ARIA_VALUE_MAX) + '…' : value });
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
        const qr = this._qr;
        if (!qr) return;

        const margin = this.options.margin;
        const total = qr.size + margin * 2;
        let unit = Math.min(w, h) / total;
        if (unit >= 1) unit = Math.floor(unit);
        const drawn = unit * total;
        const ox = Math.floor((w - drawn) / 2);
        const oy = Math.floor((h - drawn) / 2);

        ctx.fillStyle = light;
        ctx.fillRect(ox, oy, drawn, drawn);

        // 同一列相鄰的深色模組合併成一個矩形，整張一次填色，避免模組間出現細縫
        ctx.beginPath();
        for (let y = 0; y < qr.size; y++) {
            let runStart = -1;
            for (let x = 0; x <= qr.size; x++) {
                const darkHere = x < qr.size && qr.isDark(x, y);
                if (darkHere && runStart < 0) runStart = x;
                if (!darkHere && runStart >= 0) {
                    ctx.rect(ox + (margin + runStart) * unit, oy + (margin + y) * unit, (x - runStart) * unit, unit);
                    runStart = -1;
                }
            }
        }
        ctx.fillStyle = dark;
        ctx.fill();
    }

    _drawError(ctx, w, h) {
        const key = ERROR_KEYS[this._error.code] || 'errorUnknown';
        const { '--cl-text-secondary': color } = this.tokens(['--cl-text-secondary']);
        ctx.font = this.font(12);
        ctx.fillStyle = color || this.tokens(['--cl-text'])['--cl-text'];
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        const lines = this.wrapText(ctx, Locale.t('qrCode.' + key), Math.max(40, w - 16));
        const lineH = 16;
        const top = h / 2 - ((lines.length - 1) * lineH) / 2;
        lines.forEach((line, i) => ctx.fillText(line, w / 2, top + i * lineH));
    }

    /* ── 公開方法 ── */

    /** 目前的內容。 */
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

    /** 最近一次編碼失敗的原因（{ code, message }），成功時為 null。 */
    getError() {
        return this._error ? { code: this._error.code, message: this._error.message } : null;
    }

    /** 編碼結果摘要：版本、邊長模組數、實際的錯誤修正等級、遮罩與模式；沒有內容時為 null。 */
    getInfo() {
        if (!this._qr) return null;
        const { version, size, ecLevel, mask, mode } = this._qr;
        return { version, size, ecLevel, mask, mode };
    }

    /** 同時更新多個選項（例如 { value, ecLevel }）並重繪。 */
    update(patch = {}) {
        if (patch.value !== undefined) patch = { ...patch, value: patch.value == null ? '' : String(patch.value) };
        if (patch.ariaLabel !== undefined) this._explicitAriaLabel = patch.ariaLabel || '';
        Object.assign(this.options, patch);
        this._encode();
        super.update({});
        return this;
    }
}

export default QrCode;
