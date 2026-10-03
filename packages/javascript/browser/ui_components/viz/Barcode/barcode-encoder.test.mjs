import test from 'node:test';
import assert from 'node:assert/strict';
import {
    encodeBarcode,
    BarcodeEncodeError,
    CODE128_PATTERNS,
    CODE39_PATTERNS,
    CODE39_CHARSET,
    code128Values,
    code39CheckChar,
    eanCheckDigit
} from './barcode-encoder.js';

/* ── Code 128 ── */

test('Code 128 符號表：107 個、互不重複、模組數與條寬奇偶性正確', () => {
    assert.equal(CODE128_PATTERNS.length, 107);
    assert.equal(new Set(CODE128_PATTERNS).size, 107);
    CODE128_PATTERNS.forEach((p, v) => {
        const w = [...p].map(Number);
        const total = w.reduce((a, b) => a + b, 0);
        assert.equal(total, v === 106 ? 13 : 11, `符號 ${v}`);
        const barSum = w.filter((_, i) => i % 2 === 0).reduce((a, b) => a + b, 0);
        assert.equal(barSum % 2, 0, `符號 ${v} 的條寬總和應為偶數`);
    });
    assert.equal(CODE128_PATTERNS[103], '211412');
    assert.equal(CODE128_PATTERNS[104], '211214');
    assert.equal(CODE128_PATTERNS[105], '211232');
});

test('Code 128 檢查碼依標準公式計算', () => {
    const values = code128Values('PJJ123C');
    assert.deepEqual(values.slice(0, -1), [104, 48, 42, 42, 17, 18, 19, 35]);
    const sum = 104 + 48 * 1 + 42 * 2 + 42 * 3 + 17 * 4 + 18 * 5 + 19 * 6 + 35 * 7;
    assert.equal(values[values.length - 1], sum % 103);
});

/** 獨立解碼：寬度 → 符號值 → 驗檢查碼 → 依字集狀態機還原文字。 */
function decodeCode128(widths) {
    const lookup = new Map(CODE128_PATTERNS.map((p, v) => [p, v]));
    const values = [];
    let i = 0;
    while (i < widths.length) {
        const key = widths.slice(i, i + 6).join('');
        if (lookup.get(key) === undefined && widths.slice(i, i + 7).join('') === '2331112') break;
        const v = lookup.get(key);
        assert.ok(v !== undefined, `無法辨識的符號 ${key}`);
        values.push(v);
        i += 6;
    }
    assert.equal(widths.slice(i).join(''), '2331112', '缺少停止碼');
    const check = values.pop();
    let sum = values[0];
    for (let k = 1; k < values.length; k++) sum += values[k] * k;
    assert.equal(sum % 103, check, '檢查碼錯誤');

    let set = { 103: 'A', 104: 'B', 105: 'C' }[values[0]];
    let out = '';
    for (const v of values.slice(1)) {
        if (set === 'C') {
            if (v < 100) out += String(v).padStart(2, '0');
            else if (v === 100) set = 'B';
            else if (v === 101) set = 'A';
            else assert.fail(`C 字集不預期的值 ${v}`);
        } else if (v === 99) {
            set = 'C';
        } else if (set === 'A' && v === 100) {
            set = 'B';
        } else if (set === 'B' && v === 101) {
            set = 'A';
        } else if (set === 'A') {
            out += String.fromCharCode(v < 64 ? v + 32 : v - 64);
        } else {
            out += String.fromCharCode(v + 32);
        }
    }
    return out;
}

test('Code 128 各種內容都能讀回', () => {
    const samples = [
        'A', 'Hello, world!', '1234', '12345', '123456789012', 'ABC1234567DEF', 'abc\tdef',
        'PJJ123C', 'S1141CS1001-01', '\x01\x02lower', '00', '0', 'x9999', 'INV-2026-000123'
    ];
    for (const text of samples) {
        const bc = encodeBarcode(text, { format: 'code128' });
        assert.equal(decodeCode128(bc.widths), text, JSON.stringify(text));
        assert.equal(bc.modules, bc.widths.reduce((a, b) => a + b, 0));
    }
});

test('Code 128 連續數字使用 C 字集縮短長度', () => {
    const digits = encodeBarcode('123456789012', { format: 'code128' });
    const letters = encodeBarcode('ABCDEFGHIJKL', { format: 'code128' });
    assert.ok(digits.modules < letters.modules);
    assert.equal(code128Values('123456789012')[0], 105);
});

test('Code 128 拒絕非 ASCII 與空字串', () => {
    assert.throws(() => encodeBarcode('學號', { format: 'code128' }), (e) => e instanceof BarcodeEncodeError && e.code === 'invalid_char');
    assert.throws(() => encodeBarcode('', { format: 'code128' }), (e) => e.code === 'empty');
});

/* ── Code 39 ── */

test('Code 39 字元表符合標準寫法', () => {
    const known = {
        '0': '000110100', '1': '100100001', '9': '001100100', A: '100001001', J: '000011100',
        K: '100000011', T: '000010110', U: '110000001', Z: '011010000', '-': '010000101',
        '.': '110000100', ' ': '011000100', '*': '010010100',
        $: '010101000', '/': '010100010', '+': '010001010', '%': '000101010'
    };
    for (const [ch, pattern] of Object.entries(known)) assert.equal(CODE39_PATTERNS[ch].join(''), pattern, ch);
    const all = [...CODE39_CHARSET, '*'];
    assert.equal(new Set(all.map((ch) => CODE39_PATTERNS[ch].join(''))).size, all.length);
    for (const ch of all) {
        const p = CODE39_PATTERNS[ch];
        assert.equal(p.length, 9);
        assert.equal(p.filter((w) => w === 1).length, 3, ch);
    }
});

test('Code 39 加起止碼、字元間隙與 mod 43 檢查字元', () => {
    const bc = encodeBarcode('AB1', { format: 'code39', checkDigit: true });
    const check = code39CheckChar('AB1');
    assert.equal(check, CODE39_CHARSET[(10 + 11 + 1) % 43]);
    assert.equal(bc.data, 'AB1' + check);
    // 起止碼 + 4 個字元，每個 9 元素，字元間 5 個間隙
    assert.equal(bc.widths.length, 6 * 9 + 5);
    // 寬窄比 3：每個字元 6 窄 + 3 寬 = 15 模組，間隙 1
    assert.equal(bc.modules, 6 * 15 + 5);
    assert.equal(encodeBarcode('AB1', { format: 'code39', wideRatio: 2.5 }).modules, 5 * 13.5 + 4);
});

test('Code 39 拒絕小寫與不合法的寬窄比', () => {
    assert.throws(() => encodeBarcode('abc', { format: 'code39' }), (e) => e.code === 'invalid_char');
    assert.throws(() => encodeBarcode('ABC', { format: 'code39', wideRatio: 4 }), (e) => e.code === 'invalid_option');
});

/* ── EAN ── */

const EAN_L = ['0001101', '0011001', '0010011', '0111101', '0100011', '0110001', '0101111', '0111011', '0110111', '0001011'];

function widthsToBits(widths) {
    return widths.map((w, i) => (i % 2 === 0 ? '1' : '0').repeat(w)).join('');
}

/** 獨立解碼 EAN-13：讀左半的 L/G 組合推回首位數字。 */
function decodeEan13(widths) {
    const bits = widthsToBits(widths);
    assert.equal(bits.length, 95);
    assert.equal(bits.slice(0, 3), '101');
    assert.equal(bits.slice(45, 50), '01010');
    assert.equal(bits.slice(92), '101');
    const invert = (s) => [...s].map((b) => (b === '1' ? '0' : '1')).join('');
    const G = EAN_L.map((p) => [...invert(p)].reverse().join(''));
    const R = EAN_L.map(invert);
    let parity = '';
    let digits = '';
    for (let i = 0; i < 6; i++) {
        const chunk = bits.slice(3 + i * 7, 10 + i * 7);
        const l = EAN_L.indexOf(chunk);
        const g = G.indexOf(chunk);
        assert.ok(l >= 0 || g >= 0, `左半第 ${i + 1} 碼無法辨識`);
        parity += l >= 0 ? 'L' : 'G';
        digits += l >= 0 ? l : g;
    }
    for (let i = 0; i < 6; i++) {
        const d = R.indexOf(bits.slice(50 + i * 7, 57 + i * 7));
        assert.ok(d >= 0, `右半第 ${i + 1} 碼無法辨識`);
        digits += d;
    }
    const first = ['LLLLLL', 'LLGLGG', 'LLGGLG', 'LLGGGL', 'LGLLGG', 'LGGLLG', 'LGGGLL', 'LGLGLG', 'LGLGGL', 'LGGLGL'].indexOf(parity);
    assert.ok(first >= 0);
    return String(first) + digits;
}

test('EAN 檢查碼符合已知例子', () => {
    assert.equal(eanCheckDigit('400638133393'), 1);
    assert.equal(eanCheckDigit('590123412345'), 7);
    assert.equal(eanCheckDigit('9638507'), 4);
});

test('EAN-13 補檢查碼並能讀回', () => {
    for (const code of ['400638133393', '5901234123457', '471986500000', '0000000000000']) {
        const bc = encodeBarcode(code, { format: 'ean13' });
        assert.equal(bc.data.length, 13);
        assert.equal(decodeEan13(bc.widths), bc.data);
    }
    assert.equal(encodeBarcode('400638133393', { format: 'ean13' }).data, '4006381333931');
});

test('EAN-8 結構正確', () => {
    const bc = encodeBarcode('9638507', { format: 'ean8' });
    assert.equal(bc.data, '96385074');
    const bits = widthsToBits(bc.widths);
    assert.equal(bits.length, 67);
    assert.equal(bits.slice(3, 10), EAN_L[9]);
    assert.equal(bits.slice(31, 36), '01010');
});

test('EAN 拒絕錯誤的檢查碼、長度與非數字', () => {
    assert.throws(() => encodeBarcode('4006381333932', { format: 'ean13' }), (e) => e.code === 'bad_check_digit' && e.detail.expected === 1);
    assert.throws(() => encodeBarcode('12345', { format: 'ean13' }), (e) => e.code === 'invalid_length');
    assert.throws(() => encodeBarcode('12A4567', { format: 'ean8' }), (e) => e.code === 'invalid_char');
    assert.throws(() => encodeBarcode('123', { format: 'upc' }), (e) => e.code === 'invalid_option');
});
