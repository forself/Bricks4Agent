/**
 * barcode-encoder.js — 一維條碼編碼器。零依賴、不碰 DOM，可在 Node 直接測試。
 *
 * 支援格式：
 *   - code128：ASCII 0～127，自動在 A / B / C 字集間切換（連續數字用 C 縮短長度）
 *   - code39 ：0-9、A-Z、- . 空白 $ / + %，可選 mod 43 檢查碼；台灣超商代收、郵局劃撥的繳費單條碼即此格式
 *   - ean13  ：12 碼自動補檢查碼，或 13 碼驗證檢查碼
 *   - ean8   ：7 碼自動補檢查碼，或 8 碼驗證檢查碼
 *
 * 回傳的 widths 是「條、空、條、空…」交錯的寬度（單位為模組，第一個一定是條）。
 *
 * @example
 * const bc = encodeBarcode('ABC-123', { format: 'code39' });
 * bc.widths;   // [1, 3, 1, 1, 3, ...]
 */

export const BARCODE_FORMATS = ['code128', 'code39', 'ean13', 'ean8'];

/** 編碼失敗（字元不支援、長度不符、檢查碼錯誤）。code 供呼叫端分辨原因並取在地化訊息。 */
export class BarcodeEncodeError extends Error {
    constructor(code, message, detail = {}) {
        super(message);
        this.name = 'BarcodeEncodeError';
        this.code = code;
        this.detail = detail;
    }
}

/* ── Code 128 ── */

/* 符號值 0～106 的條空寬度；0～105 每個 11 模組，106 是 13 模組的停止碼 */
export const CODE128_PATTERNS = [
    '212222', '222122', '222221', '121223', '121322', '131222', '122213', '122312', '132212', '221213',
    '221312', '231212', '112232', '122132', '122231', '113222', '123122', '123221', '223211', '221132',
    '221231', '213212', '223112', '312131', '311222', '321122', '321221', '312212', '322112', '322211',
    '212123', '212321', '232121', '111323', '131123', '131321', '112313', '132113', '132311', '211313',
    '231113', '231311', '112133', '112331', '132131', '113123', '113321', '133121', '313121', '211331',
    '231131', '213113', '213311', '213131', '311123', '311321', '331121', '312113', '312311', '332111',
    '314111', '221411', '431111', '111224', '111422', '121124', '121421', '141122', '141221', '112214',
    '112412', '122114', '122411', '142112', '142211', '241211', '221114', '413111', '241112', '134111',
    '111242', '121142', '121241', '114212', '124112', '124211', '411212', '421112', '421211', '212141',
    '214121', '412121', '111143', '111341', '131141', '114113', '114311', '411113', '411311', '113141',
    '114131', '311141', '411131', '211412', '211214', '211232', '2331112'
];

const C128 = { CODE_C: 99, CODE_B: 100, CODE_A: 101, START_A: 103, START_B: 104, START_C: 105, STOP: 106 };

const isDigit = (c) => c >= 48 && c <= 57;

function digitRun(codes, i) {
    let n = 0;
    while (i + n < codes.length && isDigit(codes[i + n])) n++;
    return n;
}

/** 字元在 A 或 B 字集的符號值；不在該字集回傳 -1。 */
function code128Value(set, c) {
    if (set === 'A') {
        if (c >= 32 && c <= 95) return c - 32;
        if (c >= 0 && c <= 31) return c + 64;
        return -1;
    }
    if (c >= 32 && c <= 127) return c - 32;
    return -1;
}

/** 從位置 i 起，下一個只屬於 A 或 B 的字元決定要用哪個字集。 */
function preferredSet(codes, i) {
    for (let k = i; k < codes.length; k++) {
        if (codes[k] < 32) return 'A';
        if (codes[k] >= 96) return 'B';
    }
    return 'B';
}

/** 把文字編成 Code 128 的符號值（含起始碼與檢查碼，不含停止碼）。 */
export function code128Values(text) {
    const codes = [];
    for (const ch of String(text)) {
        const c = ch.codePointAt(0);
        if (c > 127) throw new BarcodeEncodeError('invalid_char', `Code 128 只支援 ASCII 字元：${ch}`, { char: ch });
        codes.push(c);
    }
    if (codes.length === 0) throw new BarcodeEncodeError('empty', '條碼內容不得為空');

    const values = [];
    let set;
    const leadDigits = digitRun(codes, 0);
    if (leadDigits >= 4 || (leadDigits === codes.length && leadDigits % 2 === 0)) {
        set = 'C';
        values.push(C128.START_C);
    } else {
        set = preferredSet(codes, 0);
        values.push(set === 'A' ? C128.START_A : C128.START_B);
    }

    let i = 0;
    while (i < codes.length) {
        if (set === 'C') {
            if (digitRun(codes, i) >= 2) {
                values.push((codes[i] - 48) * 10 + (codes[i + 1] - 48));
                i += 2;
                continue;
            }
            set = preferredSet(codes, i);
            values.push(set === 'A' ? C128.CODE_A : C128.CODE_B);
            continue;
        }
        const run = digitRun(codes, i);
        if (run >= 4 && (run >= 6 || i + run === codes.length)) {
            if (run % 2 === 1) {
                values.push(code128Value(set, codes[i]));
                i++;
            }
            set = 'C';
            values.push(C128.CODE_C);
            continue;
        }
        let v = code128Value(set, codes[i]);
        if (v < 0) {
            set = set === 'A' ? 'B' : 'A';
            values.push(set === 'A' ? C128.CODE_A : C128.CODE_B);
            v = code128Value(set, codes[i]);
        }
        values.push(v);
        i++;
    }

    let sum = values[0];
    for (let k = 1; k < values.length; k++) sum += values[k] * k;
    values.push(sum % 103);
    return values;
}

function encodeCode128(text) {
    const values = code128Values(text);
    const widths = [];
    for (const v of [...values, C128.STOP]) {
        for (const d of CODE128_PATTERNS[v]) widths.push(Number(d));
    }
    return {
        format: 'code128',
        text: String(text),
        data: String(text),
        widths,
        quietZone: { left: 10, right: 10 }
    };
}

/* ── Code 39 ── */

export const CODE39_CHARSET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%';

/*
 * Code 39 每個字元 5 條 4 空，其中 3 個寬元素。前 40 個字元（含起止碼 *）有 2 條寬、1 空寬，
 * 依「寬空位置」分四組、每組 10 個字元共用同一組寬條排列；$ / + % 是 3 個寬空。
 */
const CODE39_BAR_PATTERNS = ['10001', '01001', '11000', '00101', '10100', '01100', '00011', '10010', '01010', '00110'];
const CODE39_GROUPS = [
    { chars: '1234567890', wideSpace: 1 },
    { chars: 'ABCDEFGHIJ', wideSpace: 2 },
    { chars: 'KLMNOPQRST', wideSpace: 3 },
    { chars: 'UVWXYZ-. *', wideSpace: 0 }
];
const CODE39_SPECIAL = { '$': [0, 1, 2], '/': [0, 1, 3], '+': [0, 2, 3], '%': [1, 2, 3] };

/** 每個字元的寬窄序列（條空交錯 9 個元素，1 為寬）。 */
export const CODE39_PATTERNS = (() => {
    const table = {};
    for (const group of CODE39_GROUPS) {
        [...group.chars].forEach((ch, i) => {
            const bars = CODE39_BAR_PATTERNS[i];
            const seq = [];
            for (let k = 0; k < 5; k++) {
                seq.push(Number(bars[k]));
                if (k < 4) seq.push(k === group.wideSpace ? 1 : 0);
            }
            table[ch] = seq;
        });
    }
    for (const [ch, wideSpaces] of Object.entries(CODE39_SPECIAL)) {
        const seq = [];
        for (let k = 0; k < 5; k++) {
            seq.push(0);
            if (k < 4) seq.push(wideSpaces.includes(k) ? 1 : 0);
        }
        table[ch] = seq;
    }
    return table;
})();

/** Code 39 的 mod 43 檢查字元。 */
export function code39CheckChar(text) {
    let sum = 0;
    for (const ch of text) sum += CODE39_CHARSET.indexOf(ch);
    return CODE39_CHARSET[sum % 43];
}

function encodeCode39(text, { checkDigit = false, wideRatio = 3 } = {}) {
    const str = String(text);
    if (str.length === 0) throw new BarcodeEncodeError('empty', '條碼內容不得為空');
    for (const ch of str) {
        if (!CODE39_CHARSET.includes(ch)) {
            throw new BarcodeEncodeError('invalid_char', `Code 39 不支援的字元：${ch}（只支援 0-9、大寫 A-Z 與 - . 空白 $ / + %）`, { char: ch });
        }
    }
    if (!(wideRatio >= 2 && wideRatio <= 3)) {
        throw new BarcodeEncodeError('invalid_option', `寬窄比必須介於 2 與 3 之間：${wideRatio}`, { option: 'wideRatio' });
    }
    const data = checkDigit ? str + code39CheckChar(str) : str;
    const widths = [];
    const symbols = ['*', ...data, '*'];
    symbols.forEach((ch, idx) => {
        for (const w of CODE39_PATTERNS[ch]) widths.push(w ? wideRatio : 1);
        if (idx < symbols.length - 1) widths.push(1);
    });
    return {
        format: 'code39',
        text: str,
        data,
        widths,
        quietZone: { left: 10, right: 10 }
    };
}

/* ── EAN-13 / EAN-8 ── */

const EAN_L = ['0001101', '0011001', '0010011', '0111101', '0100011', '0110001', '0101111', '0111011', '0110111', '0001011'];
const EAN_R = EAN_L.map((p) => [...p].map((b) => (b === '1' ? '0' : '1')).join(''));
const EAN_G = EAN_R.map((p) => [...p].reverse().join(''));
const EAN13_PARITY = ['LLLLLL', 'LLGLGG', 'LLGGLG', 'LLGGGL', 'LGLLGG', 'LGGLLG', 'LGGGLL', 'LGLGLG', 'LGLGGL', 'LGGLGL'];

/** EAN 檢查碼：由右往左（不含檢查碼）權重 3、1 交替。 */
export function eanCheckDigit(digits) {
    let sum = 0;
    for (let i = 0; i < digits.length; i++) {
        const fromRight = digits.length - i;
        sum += Number(digits[i]) * (fromRight % 2 === 1 ? 3 : 1);
    }
    return (10 - (sum % 10)) % 10;
}

function bitsToWidths(bits) {
    const widths = [];
    let run = 1;
    for (let i = 1; i <= bits.length; i++) {
        if (i < bits.length && bits[i] === bits[i - 1]) {
            run++;
        } else {
            widths.push(run);
            run = 1;
        }
    }
    return widths;
}

function normalizeEanDigits(text, length, format) {
    const str = String(text);
    if (!/^[0-9]+$/.test(str)) {
        throw new BarcodeEncodeError('invalid_char', `${format.toUpperCase()} 只能是數字：${str}`, {});
    }
    if (str.length === length - 1) return str + eanCheckDigit(str);
    if (str.length === length) {
        const expected = eanCheckDigit(str.slice(0, -1));
        if (Number(str[length - 1]) !== expected) {
            throw new BarcodeEncodeError('bad_check_digit', `${format.toUpperCase()} 檢查碼應為 ${expected}：${str}`, { expected });
        }
        return str;
    }
    throw new BarcodeEncodeError('invalid_length', `${format.toUpperCase()} 必須是 ${length - 1} 或 ${length} 碼：${str}`, { length });
}

function encodeEan13(text) {
    const data = normalizeEanDigits(text, 13, 'ean13');
    const parity = EAN13_PARITY[Number(data[0])];
    let bits = '101';
    for (let i = 1; i <= 6; i++) bits += (parity[i - 1] === 'L' ? EAN_L : EAN_G)[Number(data[i])];
    bits += '01010';
    for (let i = 7; i <= 12; i++) bits += EAN_R[Number(data[i])];
    bits += '101';
    return {
        format: 'ean13',
        text: data,
        data,
        widths: bitsToWidths(bits),
        modules: bits.length,
        quietZone: { left: 11, right: 7 },
        guards: [[0, 3], [45, 50], [92, 95]],
        textGroups: [
            { text: data[0], from: -8, to: -1 },
            { text: data.slice(1, 7), from: 3, to: 45 },
            { text: data.slice(7), from: 50, to: 92 }
        ]
    };
}

function encodeEan8(text) {
    const data = normalizeEanDigits(text, 8, 'ean8');
    let bits = '101';
    for (let i = 0; i < 4; i++) bits += EAN_L[Number(data[i])];
    bits += '01010';
    for (let i = 4; i < 8; i++) bits += EAN_R[Number(data[i])];
    bits += '101';
    return {
        format: 'ean8',
        text: data,
        data,
        widths: bitsToWidths(bits),
        modules: bits.length,
        quietZone: { left: 7, right: 7 },
        guards: [[0, 3], [31, 36], [64, 67]],
        textGroups: [
            { text: data.slice(0, 4), from: 3, to: 31 },
            { text: data.slice(4), from: 36, to: 64 }
        ]
    };
}

/* ── 入口 ── */

/**
 * 把文字編成一維條碼。
 * @param {string} text
 * @param {Object} [options]
 * @param {'code128'|'code39'|'ean13'|'ean8'} [options.format='code128']
 * @param {boolean} [options.checkDigit=false] - Code 39 是否附 mod 43 檢查字元
 * @param {number} [options.wideRatio=3] - Code 39 寬窄比（2～3）
 * @returns {{format:string,text:string,data:string,widths:number[],modules:number,quietZone:{left:number,right:number}}}
 */
export function encodeBarcode(text, options = {}) {
    const format = options.format || 'code128';
    let result;
    if (format === 'code128') result = encodeCode128(text);
    else if (format === 'code39') result = encodeCode39(text, options);
    else if (format === 'ean13') result = encodeEan13(text);
    else if (format === 'ean8') result = encodeEan8(text);
    else throw new BarcodeEncodeError('invalid_option', `不支援的條碼格式：${format}`, { option: 'format' });
    if (result.modules === undefined) result.modules = result.widths.reduce((a, b) => a + b, 0);
    return result;
}

export default encodeBarcode;
