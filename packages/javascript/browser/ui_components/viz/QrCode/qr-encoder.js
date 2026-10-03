/**
 * qr-encoder.js — QR Code（Model 2）編碼器。零依賴、不碰 DOM，可在 Node 直接測試。
 *
 * 依 ISO/IEC 18004：
 *   - 模式：數字、英數、位元組（UTF-8）；依內容自動選最精簡的單一模式
 *   - 版本 1～40，錯誤修正等級 L / M / Q / H
 *   - Reed–Solomon：GF(256)，原始多項式 0x11D
 *   - 八種遮罩依罰分（N1～N4）選最小，也可指定
 *
 * @example
 * const qr = encodeQr('https://example.edu.tw', { ecLevel: 'M' });
 * qr.size;            // 每邊模組數
 * qr.isDark(x, y);    // x 為欄、y 為列
 */

const EC_ORDER = ['L', 'M', 'Q', 'H'];
const FORMAT_BITS = { L: 1, M: 0, Q: 3, H: 2 };
const ALNUM_CHARSET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:';

/* 每個區塊的錯誤修正碼字數 [等級][版本]，索引 0 不用 */
const ECC_CODEWORDS_PER_BLOCK = {
    L: [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    M: [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
    Q: [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    H: [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30]
};

/* 錯誤修正區塊數 [等級][版本]，索引 0 不用 */
const NUM_ERROR_CORRECTION_BLOCKS = {
    L: [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
    M: [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
    Q: [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
    H: [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81]
};

const PENALTY_N1 = 3;
const PENALTY_N2 = 3;
const PENALTY_N3 = 40;
const PENALTY_N4 = 10;

/** 編碼失敗（內容過長、選項不合法）。code 供呼叫端分辨原因並取在地化訊息。 */
export class QrEncodeError extends Error {
    constructor(code, message, detail = {}) {
        super(message);
        this.name = 'QrEncodeError';
        this.code = code;
        this.detail = detail;
    }
}

/* ── 容量 ── */

/** 某版本扣掉功能圖形後可放資料與錯誤修正碼的模組數。 */
export function numRawDataModules(version) {
    let result = (16 * version + 128) * version + 64;
    if (version >= 2) {
        const numAlign = Math.floor(version / 7) + 2;
        result -= (25 * numAlign - 10) * numAlign - 55;
        if (version >= 7) result -= 36;
    }
    return result;
}

/** 某版本與等級可放的資料碼字數（不含錯誤修正碼）。 */
export function numDataCodewords(version, ecLevel) {
    return Math.floor(numRawDataModules(version) / 8)
        - ECC_CODEWORDS_PER_BLOCK[ecLevel][version] * NUM_ERROR_CORRECTION_BLOCKS[ecLevel][version];
}

/** 對齊圖形的中心座標（版本 1 沒有）。 */
export function alignmentPatternPositions(version) {
    if (version === 1) return [];
    const size = version * 4 + 17;
    const numAlign = Math.floor(version / 7) + 2;
    const step = version === 32 ? 26 : Math.ceil((version * 4 + 4) / (numAlign * 2 - 2)) * 2;
    const result = [6];
    for (let pos = size - 7; result.length < numAlign; pos -= step) result.splice(1, 0, pos);
    return result;
}

/* ── GF(256) 與 Reed–Solomon ── */

function gfMultiply(x, y) {
    let z = 0;
    for (let i = 7; i >= 0; i--) {
        z = (z << 1) ^ ((z >>> 7) * 0x11D);
        z ^= ((y >>> i) & 1) * x;
    }
    return z & 0xFF;
}

/** 產生多項式的係數（由高次到低次，不含首項 1）。 */
export function reedSolomonDivisor(degree) {
    const result = new Array(degree).fill(0);
    result[degree - 1] = 1;
    let root = 1;
    for (let i = 0; i < degree; i++) {
        for (let j = 0; j < result.length; j++) {
            result[j] = gfMultiply(result[j], root);
            if (j + 1 < result.length) result[j] ^= result[j + 1];
        }
        root = gfMultiply(root, 0x02);
    }
    return result;
}

/** 資料碼字除以產生多項式的餘式，即錯誤修正碼字。 */
export function reedSolomonRemainder(data, divisor) {
    const result = new Array(divisor.length).fill(0);
    for (const b of data) {
        const factor = b ^ result.shift();
        result.push(0);
        for (let i = 0; i < divisor.length; i++) result[i] ^= gfMultiply(divisor[i], factor);
    }
    return result;
}

/* ── 資料段 ── */

function charCountBits(mode, version) {
    const idx = version <= 9 ? 0 : version <= 26 ? 1 : 2;
    if (mode === 'numeric') return [10, 12, 14][idx];
    if (mode === 'alphanumeric') return [9, 11, 13][idx];
    return [8, 16, 16][idx];
}

const MODE_INDICATOR = { numeric: 0x1, alphanumeric: 0x2, byte: 0x4 };

function appendBits(bits, value, length) {
    for (let i = length - 1; i >= 0; i--) bits.push((value >>> i) & 1);
}

function utf8Bytes(text) {
    return Array.from(new TextEncoder().encode(text));
}

/** 依內容選最精簡的單一模式，並產生資料位元。 */
export function makeSegment(text) {
    const str = String(text);
    if (/^[0-9]*$/.test(str)) {
        const bits = [];
        for (let i = 0; i < str.length; i += 3) {
            const chunk = str.slice(i, i + 3);
            appendBits(bits, parseInt(chunk, 10), chunk.length * 3 + 1);
        }
        return { mode: 'numeric', numChars: str.length, bits };
    }
    if ([...str].every((c) => ALNUM_CHARSET.includes(c))) {
        const bits = [];
        let i = 0;
        for (; i + 2 <= str.length; i += 2) {
            appendBits(bits, ALNUM_CHARSET.indexOf(str[i]) * 45 + ALNUM_CHARSET.indexOf(str[i + 1]), 11);
        }
        if (i < str.length) appendBits(bits, ALNUM_CHARSET.indexOf(str[i]), 6);
        return { mode: 'alphanumeric', numChars: str.length, bits };
    }
    const bytes = utf8Bytes(str);
    const bits = [];
    for (const b of bytes) appendBits(bits, b, 8);
    return { mode: 'byte', numChars: bytes.length, bits };
}

function segmentBitLength(segment, version, eci) {
    const ccBits = charCountBits(segment.mode, version);
    if (segment.numChars >= (1 << ccBits)) return Infinity;
    return (eci ? 12 : 0) + 4 + ccBits + segment.bits.length;
}

/* ── 編碼 ── */

/**
 * 把文字編成 QR Code。
 * @param {string} text
 * @param {Object} [options]
 * @param {'L'|'M'|'Q'|'H'} [options.ecLevel='M'] - 錯誤修正等級
 * @param {number} [options.minVersion=1]
 * @param {number} [options.maxVersion=40]
 * @param {number|'auto'} [options.mask='auto'] - 0～7，或依罰分自動選
 * @param {boolean} [options.boostEcl=true] - 同一版本放得下時自動提高錯誤修正等級
 * @param {boolean} [options.eci=false] - 位元組模式前加 ECI 26（UTF-8）宣告
 * @returns {{version:number,size:number,ecLevel:string,mask:number,mode:string,modules:Uint8Array,isDark:Function}}
 */
export function encodeQr(text, options = {}) {
    const {
        ecLevel = 'M',
        minVersion = 1,
        maxVersion = 40,
        mask = 'auto',
        boostEcl = true,
        eci = false
    } = options;

    if (!EC_ORDER.includes(ecLevel)) {
        throw new QrEncodeError('invalid_option', `ecLevel 必須是 L、M、Q、H 之一：${ecLevel}`, { option: 'ecLevel' });
    }
    if (!(Number.isInteger(minVersion) && Number.isInteger(maxVersion) && minVersion >= 1 && maxVersion <= 40 && minVersion <= maxVersion)) {
        throw new QrEncodeError('invalid_option', `版本範圍不合法：${minVersion}～${maxVersion}`, { option: 'version' });
    }
    if (mask !== 'auto' && !(Number.isInteger(mask) && mask >= 0 && mask <= 7)) {
        throw new QrEncodeError('invalid_option', `mask 必須是 0～7 或 'auto'：${mask}`, { option: 'mask' });
    }

    const segment = makeSegment(text);
    const useEci = Boolean(eci) && segment.mode === 'byte';

    let version = minVersion;
    let usedBits;
    for (;; version++) {
        usedBits = segmentBitLength(segment, version, useEci);
        if (usedBits <= numDataCodewords(version, ecLevel) * 8) break;
        if (version >= maxVersion) {
            throw new QrEncodeError('too_long', `內容太長，版本 ${maxVersion}、等級 ${ecLevel} 放不下`, {
                ecLevel, maxVersion, mode: segment.mode, length: segment.numChars
            });
        }
    }

    let ecl = ecLevel;
    if (boostEcl) {
        for (const level of EC_ORDER.slice(EC_ORDER.indexOf(ecLevel) + 1)) {
            if (usedBits <= numDataCodewords(version, level) * 8) ecl = level;
        }
    }

    const bits = [];
    if (useEci) {
        appendBits(bits, 0x7, 4);
        appendBits(bits, 26, 8);
    }
    appendBits(bits, MODE_INDICATOR[segment.mode], 4);
    appendBits(bits, segment.numChars, charCountBits(segment.mode, version));
    for (const b of segment.bits) bits.push(b);

    const capacityBits = numDataCodewords(version, ecl) * 8;
    appendBits(bits, 0, Math.min(4, capacityBits - bits.length));
    appendBits(bits, 0, (8 - (bits.length % 8)) % 8);
    for (let pad = 0xEC; bits.length < capacityBits; pad ^= 0xEC ^ 0x11) appendBits(bits, pad, 8);

    const dataCodewords = [];
    for (let i = 0; i < bits.length; i += 8) {
        let byte = 0;
        for (let j = 0; j < 8; j++) byte = (byte << 1) | bits[i + j];
        dataCodewords.push(byte);
    }

    const matrix = new QrMatrix(version, ecl);
    const allCodewords = addEccAndInterleave(dataCodewords, version, ecl);
    matrix.drawCodewords(allCodewords);

    let chosen = mask;
    if (chosen === 'auto') {
        let minPenalty = Infinity;
        for (let m = 0; m < 8; m++) {
            matrix.applyMask(m);
            matrix.drawFormatBits(m);
            const penalty = matrix.penaltyScore();
            if (penalty < minPenalty) {
                chosen = m;
                minPenalty = penalty;
            }
            matrix.applyMask(m);
        }
    }
    matrix.applyMask(chosen);
    matrix.drawFormatBits(chosen);

    const size = matrix.size;
    const modules = matrix.modules;
    return {
        version,
        size,
        ecLevel: ecl,
        mask: chosen,
        mode: segment.mode,
        dataCodewords,
        modules,
        isDark: (x, y) => x >= 0 && y >= 0 && x < size && y < size && modules[y * size + x] === 1
    };
}

/** 資料碼字分區塊、加上錯誤修正碼並交錯排列。 */
export function addEccAndInterleave(data, version, ecLevel) {
    const numBlocks = NUM_ERROR_CORRECTION_BLOCKS[ecLevel][version];
    const blockEccLen = ECC_CODEWORDS_PER_BLOCK[ecLevel][version];
    const rawCodewords = Math.floor(numRawDataModules(version) / 8);
    const numShortBlocks = numBlocks - (rawCodewords % numBlocks);
    const shortBlockLen = Math.floor(rawCodewords / numBlocks);

    const divisor = reedSolomonDivisor(blockEccLen);
    const blocks = [];
    for (let i = 0, k = 0; i < numBlocks; i++) {
        const dat = data.slice(k, k + shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1));
        k += dat.length;
        const ecc = reedSolomonRemainder(dat, divisor);
        if (i < numShortBlocks) dat.push(0);
        blocks.push(dat.concat(ecc));
    }

    const result = [];
    for (let i = 0; i < blocks[0].length; i++) {
        blocks.forEach((block, j) => {
            if (i !== shortBlockLen - blockEccLen || j >= numShortBlocks) result.push(block[i]);
        });
    }
    return result;
}

/** 格式資訊的 15 位元（等級與遮罩，含 BCH 與遮罩 0x5412）。 */
export function formatBits(ecLevel, mask) {
    const data = (FORMAT_BITS[ecLevel] << 3) | mask;
    let rem = data;
    for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
    return ((data << 10) | rem) ^ 0x5412;
}

/** 版本資訊的 18 位元（版本 7 以上）。 */
export function versionBits(version) {
    let rem = version;
    for (let i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >>> 11) * 0x1F25);
    return (version << 12) | rem;
}

const MASKS = [
    (x, y) => (x + y) % 2 === 0,
    (x, y) => y % 2 === 0,
    (x) => x % 3 === 0,
    (x, y) => (x + y) % 3 === 0,
    (x, y) => (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0,
    (x, y) => ((x * y) % 2) + ((x * y) % 3) === 0,
    (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
    (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0
];

class QrMatrix {
    constructor(version, ecLevel) {
        this.version = version;
        this.ecLevel = ecLevel;
        this.size = version * 4 + 17;
        this.modules = new Uint8Array(this.size * this.size);
        this.isFunction = new Uint8Array(this.size * this.size);
        this.drawFunctionPatterns();
    }

    setFunction(x, y, dark) {
        const i = y * this.size + x;
        this.modules[i] = dark ? 1 : 0;
        this.isFunction[i] = 1;
    }

    drawFunctionPatterns() {
        const size = this.size;
        for (let i = 0; i < size; i++) {
            this.setFunction(6, i, i % 2 === 0);
            this.setFunction(i, 6, i % 2 === 0);
        }
        this.drawFinder(3, 3);
        this.drawFinder(size - 4, 3);
        this.drawFinder(3, size - 4);

        const pos = alignmentPatternPositions(this.version);
        const n = pos.length;
        for (let i = 0; i < n; i++) {
            for (let j = 0; j < n; j++) {
                const overlapsFinder = (i === 0 && j === 0) || (i === 0 && j === n - 1) || (i === n - 1 && j === 0);
                if (!overlapsFinder) this.drawAlignment(pos[i], pos[j]);
            }
        }
        this.drawFormatBits(0);
        this.drawVersion();
    }

    drawFinder(x, y) {
        for (let dy = -4; dy <= 4; dy++) {
            for (let dx = -4; dx <= 4; dx++) {
                const xx = x + dx;
                const yy = y + dy;
                if (xx < 0 || yy < 0 || xx >= this.size || yy >= this.size) continue;
                const dist = Math.max(Math.abs(dx), Math.abs(dy));
                this.setFunction(xx, yy, dist !== 2 && dist !== 4);
            }
        }
    }

    drawAlignment(x, y) {
        for (let dy = -2; dy <= 2; dy++) {
            for (let dx = -2; dx <= 2; dx++) {
                this.setFunction(x + dx, y + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
            }
        }
    }

    drawFormatBits(mask) {
        const bits = formatBits(this.ecLevel, mask);
        const bit = (i) => ((bits >>> i) & 1) === 1;
        const size = this.size;
        for (let i = 0; i <= 5; i++) this.setFunction(8, i, bit(i));
        this.setFunction(8, 7, bit(6));
        this.setFunction(8, 8, bit(7));
        this.setFunction(7, 8, bit(8));
        for (let i = 9; i < 15; i++) this.setFunction(14 - i, 8, bit(i));
        for (let i = 0; i < 8; i++) this.setFunction(size - 1 - i, 8, bit(i));
        for (let i = 8; i < 15; i++) this.setFunction(8, size - 15 + i, bit(i));
        this.setFunction(8, size - 8, true);
    }

    drawVersion() {
        if (this.version < 7) return;
        const bits = versionBits(this.version);
        for (let i = 0; i < 18; i++) {
            const dark = ((bits >>> i) & 1) === 1;
            const a = this.size - 11 + (i % 3);
            const b = Math.floor(i / 3);
            this.setFunction(a, b, dark);
            this.setFunction(b, a, dark);
        }
    }

    drawCodewords(codewords) {
        const size = this.size;
        let i = 0;
        const totalBits = codewords.length * 8;
        for (let right = size - 1; right >= 1; right -= 2) {
            if (right === 6) right = 5;
            const upward = ((right + 1) & 2) === 0;
            for (let vert = 0; vert < size; vert++) {
                for (let j = 0; j < 2; j++) {
                    const x = right - j;
                    const y = upward ? size - 1 - vert : vert;
                    const idx = y * size + x;
                    if (!this.isFunction[idx] && i < totalBits) {
                        this.modules[idx] = (codewords[i >>> 3] >>> (7 - (i & 7))) & 1;
                        i++;
                    }
                }
            }
        }
    }

    applyMask(mask) {
        const size = this.size;
        const fn = MASKS[mask];
        for (let y = 0; y < size; y++) {
            for (let x = 0; x < size; x++) {
                const idx = y * size + x;
                if (!this.isFunction[idx] && fn(x, y)) this.modules[idx] ^= 1;
            }
        }
    }

    penaltyScore() {
        const size = this.size;
        const m = this.modules;
        const at = (x, y) => m[y * size + x];
        let result = 0;

        const scanLine = (get) => {
            let score = 0;
            let runColor = -1;
            let runLen = 0;
            for (let i = 0; i < size; i++) {
                const c = get(i);
                if (c === runColor) {
                    runLen++;
                } else {
                    if (runLen >= 5) score += PENALTY_N1 + (runLen - 5);
                    runColor = c;
                    runLen = 1;
                }
            }
            if (runLen >= 5) score += PENALTY_N1 + (runLen - 5);
            // N3：1011101 前或後接四個淺色模組（界外視為淺色）
            const v = (i) => (i < 0 || i >= size ? 0 : get(i));
            for (let i = 0; i + 7 <= size; i++) {
                if (v(i) === 1 && v(i + 1) === 0 && v(i + 2) === 1 && v(i + 3) === 1 && v(i + 4) === 1 && v(i + 5) === 0 && v(i + 6) === 1) {
                    const lightBefore = !v(i - 1) && !v(i - 2) && !v(i - 3) && !v(i - 4);
                    const lightAfter = !v(i + 7) && !v(i + 8) && !v(i + 9) && !v(i + 10);
                    if (lightBefore) score += PENALTY_N3;
                    if (lightAfter) score += PENALTY_N3;
                }
            }
            return score;
        };

        for (let y = 0; y < size; y++) result += scanLine((x) => at(x, y));
        for (let x = 0; x < size; x++) result += scanLine((y) => at(x, y));

        for (let y = 0; y < size - 1; y++) {
            for (let x = 0; x < size - 1; x++) {
                const c = at(x, y);
                if (c === at(x + 1, y) && c === at(x, y + 1) && c === at(x + 1, y + 1)) result += PENALTY_N2;
            }
        }

        let dark = 0;
        for (let i = 0; i < m.length; i++) dark += m[i];
        const total = size * size;
        const k = Math.ceil(Math.abs(dark * 20 - total * 10) / total) - 1;
        result += k * PENALTY_N4;
        return result;
    }
}

export default encodeQr;
