import test from 'node:test';
import assert from 'node:assert/strict';
import {
    encodeQr,
    QrEncodeError,
    formatBits,
    versionBits,
    reedSolomonDivisor,
    reedSolomonRemainder,
    alignmentPatternPositions,
    numRawDataModules,
    numDataCodewords
} from './qr-encoder.js';

/* ── 測試用的獨立解碼器：以對數表實作 GF(256)，逐步讀回矩陣，與編碼器的寫法不同 ── */

const EXP = new Array(512);
const LOG = new Array(256);
{
    let x = 1;
    for (let i = 0; i < 255; i++) {
        EXP[i] = x;
        LOG[x] = i;
        x <<= 1;
        if (x & 0x100) x ^= 0x11D;
    }
    for (let i = 255; i < 512; i++) EXP[i] = EXP[i - 255];
}
const gfMul = (a, b) => (a === 0 || b === 0 ? 0 : EXP[LOG[a] + LOG[b]]);

/** 碼字多項式在 α^0..α^(n-1) 的值（全為 0 表示錯誤修正碼正確）。 */
function syndromes(codeword, eccLen) {
    const out = [];
    for (let i = 0; i < eccLen; i++) {
        let s = 0;
        for (const c of codeword) s = gfMul(s, EXP[i]) ^ c;
        out.push(s);
    }
    return out;
}

const ECC_PER_BLOCK = {
    L: [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    M: [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
    Q: [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    H: [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30]
};

function functionMask(version) {
    const size = version * 4 + 17;
    const fn = new Uint8Array(size * size);
    const mark = (x, y) => {
        if (x >= 0 && y >= 0 && x < size && y < size) fn[y * size + x] = 1;
    };
    for (let y = 0; y < 9; y++) for (let x = 0; x < 9; x++) mark(x, y);
    for (let y = 0; y < 9; y++) for (let x = size - 8; x < size; x++) mark(x, y);
    for (let y = size - 8; y < size; y++) for (let x = 0; x < 9; x++) mark(x, y);
    for (let i = 0; i < size; i++) { mark(6, i); mark(i, 6); }
    const pos = alignmentPatternPositions(version);
    for (const cy of pos) {
        for (const cx of pos) {
            if ((cx < 9 && cy < 9) || (cx > size - 9 && cy < 9) || (cx < 9 && cy > size - 9)) continue;
            for (let dy = -2; dy <= 2; dy++) for (let dx = -2; dx <= 2; dx++) mark(cx + dx, cy + dy);
        }
    }
    if (version >= 7) {
        for (let a = 0; a < 6; a++) {
            for (let b = size - 11; b < size - 8; b++) { mark(a, b); mark(b, a); }
        }
    }
    return fn;
}

const MASK_FN = [
    (r, c) => (r + c) % 2 === 0,
    (r) => r % 2 === 0,
    (r, c) => c % 3 === 0,
    (r, c) => (r + c) % 3 === 0,
    (r, c) => (Math.floor(r / 2) + Math.floor(c / 3)) % 2 === 0,
    (r, c) => ((r * c) % 2) + ((r * c) % 3) === 0,
    (r, c) => (((r * c) % 2) + ((r * c) % 3)) % 2 === 0,
    (r, c) => (((r + c) % 2) + ((r * c) % 3)) % 2 === 0
];

function decode(qr) {
    const { size } = qr;
    const version = (size - 17) / 4;
    const dark = (x, y) => qr.isDark(x, y) ? 1 : 0;

    // 格式資訊：左上那一份，位元 14..0
    const coords = [];
    for (let i = 0; i <= 5; i++) coords.push([8, i]);
    coords.push([8, 7], [8, 8], [7, 8]);
    for (let i = 9; i < 15; i++) coords.push([14 - i, 8]);
    let fmt = 0;
    coords.forEach(([x, y], i) => { fmt |= dark(x, y) << i; });
    let ecLevel = null;
    let mask = null;
    for (const l of ['L', 'M', 'Q', 'H']) {
        for (let m = 0; m < 8; m++) if (formatBits(l, m) === fmt) { ecLevel = l; mask = m; }
    }
    assert.ok(ecLevel, '格式資訊無法辨識');

    // 第二份格式資訊必須相同
    let fmt2 = 0;
    for (let i = 0; i < 8; i++) fmt2 |= dark(size - 1 - i, 8) << i;
    for (let i = 8; i < 15; i++) fmt2 |= dark(8, size - 15 + i) << i;
    assert.equal(fmt2, fmt, '兩份格式資訊不一致');
    assert.equal(dark(8, size - 8), 1, '缺少暗模組');

    if (version >= 7) {
        let v = 0;
        for (let i = 0; i < 18; i++) v |= dark(size - 11 + (i % 3), Math.floor(i / 3)) << i;
        assert.equal(v, versionBits(version), '版本資訊不符');
    }

    // 依之字形讀出資料位元並解除遮罩
    const fn = functionMask(version);
    const bits = [];
    for (let col = size - 1; col > 0; col -= 2) {
        if (col === 6) col--;
        const goingUp = ((size - 1 - col) >> 1) % 2 === 0;
        for (let k = 0; k < size; k++) {
            const row = goingUp ? size - 1 - k : k;
            for (const c of [col, col - 1]) {
                if (fn[row * size + c]) continue;
                bits.push(dark(c, row) ^ (MASK_FN[mask](row, c) ? 1 : 0));
            }
        }
    }
    const rawCodewords = Math.floor(numRawDataModules(version) / 8);
    const codewords = [];
    for (let i = 0; i < rawCodewords; i++) {
        let b = 0;
        for (let j = 0; j < 8; j++) b = (b << 1) | bits[i * 8 + j];
        codewords.push(b);
    }

    // 解交錯並驗證每個區塊的錯誤修正碼
    const eccLen = ECC_PER_BLOCK[ecLevel][version];
    const totalData = numDataCodewords(version, ecLevel);
    const numBlocks = (rawCodewords - totalData) / eccLen;
    const shortData = Math.floor(totalData / numBlocks);
    const numLong = totalData % numBlocks;
    const dataLens = Array.from({ length: numBlocks }, (_, i) => shortData + (i >= numBlocks - numLong ? 1 : 0));
    const blocks = dataLens.map(() => []);
    let k = 0;
    for (let i = 0; i < shortData + 1; i++) {
        for (let b = 0; b < numBlocks; b++) if (i < dataLens[b]) blocks[b].push(codewords[k++]);
    }
    for (let i = 0; i < eccLen; i++) for (let b = 0; b < numBlocks; b++) blocks[b].push(codewords[k++]);
    assert.equal(k, rawCodewords);
    blocks.forEach((block, b) => {
        assert.ok(syndromes(block, eccLen).every((s) => s === 0), `區塊 ${b} 的錯誤修正碼不正確`);
    });
    const data = blocks.flatMap((block, b) => block.slice(0, dataLens[b]));

    // 讀出資料段
    const dbits = [];
    for (const b of data) for (let j = 7; j >= 0; j--) dbits.push((b >> j) & 1);
    let p = 0;
    const read = (n) => { let v = 0; for (let i = 0; i < n; i++) v = (v << 1) | dbits[p++]; return v; };
    let modeInd = read(4);
    if (modeInd === 0x7) { assert.equal(read(8), 26); modeInd = read(4); }
    const idx = version <= 9 ? 0 : version <= 26 ? 1 : 2;
    const ALNUM = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:';
    if (modeInd === 0x1) {
        let n = read([10, 12, 14][idx]);
        let out = '';
        while (n >= 3) { out += String(read(10)).padStart(3, '0'); n -= 3; }
        if (n === 2) out += String(read(7)).padStart(2, '0');
        if (n === 1) out += String(read(4));
        return { text: out, ecLevel, mask, version };
    }
    if (modeInd === 0x2) {
        let n = read([9, 11, 13][idx]);
        let out = '';
        while (n >= 2) { const v = read(11); out += ALNUM[Math.floor(v / 45)] + ALNUM[v % 45]; n -= 2; }
        if (n === 1) out += ALNUM[read(6)];
        return { text: out, ecLevel, mask, version };
    }
    assert.equal(modeInd, 0x4, `未知的模式 ${modeInd}`);
    const n = read([8, 16, 16][idx]);
    const bytes = new Uint8Array(n);
    for (let i = 0; i < n; i++) bytes[i] = read(8);
    return { text: new TextDecoder().decode(bytes), ecLevel, mask, version };
}

/* ── 已知數值 ── */

test('格式資訊與版本資訊符合標準表', () => {
    assert.equal(formatBits('L', 0), 0b111011111000100);
    assert.equal(formatBits('M', 0), 0b101010000010010);
    assert.equal(formatBits('Q', 0), 0b011010101011111);
    assert.equal(formatBits('H', 0), 0b001011010001001);
    assert.equal(versionBits(7), 0b000111110010010100);
});

test('產生多項式（7 個錯誤修正碼字）符合標準', () => {
    const div = reedSolomonDivisor(7);
    assert.deepEqual(div.map((c) => LOG[c]), [87, 229, 146, 149, 238, 102, 21]);
});

test('HELLO WORLD（版本 1、等級 M）的資料與錯誤修正碼字', () => {
    const qr = encodeQr('HELLO WORLD', { ecLevel: 'M', boostEcl: false });
    assert.equal(qr.version, 1);
    assert.equal(qr.mode, 'alphanumeric');
    assert.deepEqual(qr.dataCodewords, [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17]);
    assert.deepEqual(reedSolomonRemainder(qr.dataCodewords, reedSolomonDivisor(10)), [196, 35, 39, 119, 235, 215, 231, 226, 93, 23]);
});

test('對齊圖形位置與容量', () => {
    assert.deepEqual(alignmentPatternPositions(1), []);
    assert.deepEqual(alignmentPatternPositions(2), [6, 18]);
    assert.deepEqual(alignmentPatternPositions(7), [6, 22, 38]);
    assert.deepEqual(alignmentPatternPositions(32), [6, 34, 60, 86, 112, 138]);
    assert.deepEqual(alignmentPatternPositions(40), [6, 30, 58, 86, 114, 142, 170]);
    assert.equal(numRawDataModules(1) / 8 | 0, 26);
    assert.equal(numRawDataModules(2) / 8 | 0, 44);
    assert.equal(numRawDataModules(7) / 8 | 0, 196);
    assert.equal(numRawDataModules(40) / 8 | 0, 3706);
    assert.deepEqual(['L', 'M', 'Q', 'H'].map((l) => numDataCodewords(1, l)), [19, 16, 13, 9]);
    assert.deepEqual(['L', 'M', 'Q', 'H'].map((l) => numDataCodewords(40, l)), [2956, 2334, 1666, 1276]);
});

test('每個版本與等級的區塊結構都整除', () => {
    for (let v = 1; v <= 40; v++) {
        for (const l of ['L', 'M', 'Q', 'H']) {
            const raw = Math.floor(numRawDataModules(v) / 8);
            const data = numDataCodewords(v, l);
            assert.ok(data > 0 && data < raw, `v${v}-${l}`);
            assert.equal((raw - data) % ECC_PER_BLOCK[l][v], 0, `v${v}-${l} 錯誤修正碼字數`);
        }
    }
});

/* ── 往返解碼 ── */

const SAMPLES = [
    '',
    '0',
    '0123456789012345',
    'HELLO WORLD',
    'https://course.example.edu.tw/syllabus/1141/CS1001',
    '學分費繳費單 115 學年度第 1 學期',
    '外校選修學生成績證明 查驗碼：7F3A-9C21-44B0',
    'a'.repeat(300),
    '1'.repeat(1000)
];

test('各種內容與等級都能讀回原文，錯誤修正碼正確', () => {
    for (const text of SAMPLES) {
        for (const ecLevel of ['L', 'M', 'Q', 'H']) {
            const qr = encodeQr(text, { ecLevel, boostEcl: false });
            const out = decode(qr);
            assert.equal(out.text, text, `${ecLevel}：${text.slice(0, 20)}`);
            assert.equal(out.ecLevel, ecLevel);
            assert.equal(out.mask, qr.mask);
        }
    }
});

test('八種遮罩與 ECI 都能讀回', () => {
    for (let mask = 0; mask < 8; mask++) {
        const qr = encodeQr('遮罩測試 mask ' + mask, { mask, eci: true });
        const out = decode(qr);
        assert.equal(out.mask, mask);
        assert.equal(out.text, '遮罩測試 mask ' + mask);
    }
});

test('高版本（含版本資訊）能讀回', () => {
    for (const v of [7, 10, 27, 40]) {
        const qr = encodeQr('V' + v, { minVersion: v, maxVersion: v, ecLevel: 'H' });
        assert.equal(qr.version, v);
        assert.equal(decode(qr).text, 'V' + v);
    }
});

test('boostEcl 在同一版本放得下時提高等級', () => {
    const qr = encodeQr('HELLO', { ecLevel: 'L' });
    assert.equal(qr.version, 1);
    assert.equal(qr.ecLevel, 'H');
    assert.equal(encodeQr('HELLO', { ecLevel: 'L', boostEcl: false }).ecLevel, 'L');
});

test('容量上限與錯誤選項', () => {
    assert.equal(encodeQr('a'.repeat(2953), { ecLevel: 'L' }).version, 40);
    assert.throws(() => encodeQr('a'.repeat(2954), { ecLevel: 'L' }), (e) => e instanceof QrEncodeError && e.code === 'too_long');
    assert.throws(() => encodeQr('x', { ecLevel: 'X' }), (e) => e.code === 'invalid_option');
    assert.throws(() => encodeQr('x', { mask: 9 }), (e) => e.code === 'invalid_option');
    assert.throws(() => encodeQr('x', { minVersion: 5, maxVersion: 2 }), (e) => e.code === 'invalid_option');
});
