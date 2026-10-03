/**
 * RFC 4180 CSV 解析（純函式、零相依），供 ImportWizard 使用，也可單獨匯入。
 *
 * - 欄位以單一字元分隔（, 或 Tab 或 ; …），記錄以 CRLF、LF 或單獨的 CR 分隔。
 * - 雙引號包住的欄位可含分隔字元與換行，"" 代表一個雙引號。
 * - 檔尾的換行不會產生空記錄；開頭的 BOM 會被移除。
 * - 所有值一律是字串，不做任何型別轉換或運算。
 *
 * 容錯：引號未關閉（unterminatedQuote）或結束引號後緊接其他字元（malformedQuote）時，
 * 仍盡量取出內容，並在 errors 回報該記錄；欄位數不一致（ragged rows）由呼叫端比對。
 */

export const DELIMITER_CANDIDATES = Object.freeze([',', '\t', ';']);
const QUOTE = '"';

/** 移除字串開頭的 BOM（U+FEFF）。 */
export function stripBom(text) {
    const value = text === null || text === undefined ? '' : String(text);
    return value.charCodeAt(0) === 0xfeff ? value.slice(1) : value;
}

/**
 * @param {string} text - 原始文字
 * @param {object} [options]
 * @param {string} [options.delimiter=','] - 單一字元，不可為雙引號或換行
 * @param {boolean|'greedy'} [options.skipEmptyLines=true] - true：略過空白行；'greedy'：連只含空白或分隔字元的行也略過
 * @param {number} [options.maxRecords=Infinity] - 最多解析幾筆記錄（含標題列），達上限即停止
 * @returns {{ rows: string[][], lines: number[], errors: Array<{ code: string, line: number, record: number }>, truncated: boolean }}
 *   rows：每筆記錄的欄位；lines：每筆記錄起始的行號（1 起算）；errors.record 指向 rows 的索引；
 *   truncated：因 maxRecords 提早停止、仍有未解析的內容。
 */
export function parseCsv(text, { delimiter = ',', skipEmptyLines = true, maxRecords = Infinity } = {}) {
    if (typeof delimiter !== 'string' || delimiter.length !== 1 || delimiter === QUOTE || delimiter === '\n' || delimiter === '\r') {
        throw new TypeError('parseCsv: delimiter must be one character other than a double quote or a line break.');
    }
    const input = stripBom(text);
    const length = input.length;
    const rows = [];
    const lines = [];
    const errors = [];
    const isBreak = (char) => char === '\n' || char === '\r';
    let index = 0;
    let line = 1;
    let truncated = false;

    while (index < length) {
        if (rows.length >= maxRecords) {
            truncated = true;
            break;
        }
        const startLine = line;
        const record = [];
        let quoted = false;
        let recordError = null;

        for (;;) {
            let value = '';
            if (input[index] === QUOTE) {
                quoted = true;
                const quoteLine = line;
                let chunkStart = index + 1;
                let closed = false;
                index += 1;
                while (index < length) {
                    const char = input[index];
                    if (char === QUOTE) {
                        value += input.slice(chunkStart, index);
                        if (input[index + 1] === QUOTE) {
                            value += QUOTE;
                            index += 2;
                            chunkStart = index;
                            continue;
                        }
                        index += 1;
                        closed = true;
                        break;
                    }
                    if (char === '\n' || (char === '\r' && input[index + 1] !== '\n')) line += 1;
                    index += 1;
                }
                if (!closed) {
                    value += input.slice(chunkStart, index);
                    recordError = recordError || { code: 'unterminatedQuote', line: quoteLine };
                } else if (index < length && input[index] !== delimiter && !isBreak(input[index])) {
                    recordError = recordError || { code: 'malformedQuote', line };
                    const restStart = index;
                    while (index < length && input[index] !== delimiter && !isBreak(input[index])) index += 1;
                    value += input.slice(restStart, index);
                }
            } else {
                const start = index;
                while (index < length && input[index] !== delimiter && !isBreak(input[index])) index += 1;
                value = input.slice(start, index);
            }
            record.push(value);
            if (index < length && input[index] === delimiter) {
                index += 1;
                continue;
            }
            break;
        }

        if (index < length) {
            if (input[index] === '\r') {
                index += 1;
                if (input[index] === '\n') index += 1;
            } else {
                index += 1;
            }
            line += 1;
        }

        const blank = !quoted && record.length === 1 && record[0] === '';
        const whitespaceOnly = !quoted && record.every((field) => field.trim() === '');
        if ((skipEmptyLines && blank) || (skipEmptyLines === 'greedy' && whitespaceOnly)) continue;

        rows.push(record);
        lines.push(startLine);
        if (recordError) errors.push({ ...recordError, record: rows.length - 1 });
    }

    return { rows, lines, errors, truncated };
}

/**
 * 從候選分隔字元中挑出「各記錄欄位數最一致、且至少兩欄」的一個；都不像時回傳第一個候選。
 * 只取前 64 KB、最多 50 筆記錄判斷，並正確處理引號內的分隔字元。
 *
 * @param {string} text
 * @param {string[]} [candidates=[',', '\t', ';']] - 同分時以陣列順序為準
 * @returns {string}
 */
export function detectDelimiter(text, candidates = DELIMITER_CANDIDATES) {
    const input = stripBom(text);
    const sample = input.length > 65536 ? input.slice(0, 65536) : input;
    const partial = sample.length < input.length;
    let best = candidates[0] ?? ',';
    let bestScore = 0;

    for (const delimiter of candidates) {
        let parsed;
        try {
            parsed = parseCsv(sample, { delimiter, skipEmptyLines: 'greedy', maxRecords: 50 });
        } catch {
            continue;
        }
        let { rows } = parsed;
        // 樣本被截斷時，最後一筆可能只有半筆
        if (partial && !parsed.truncated && rows.length > 1) rows = rows.slice(0, -1);
        if (!rows.length) continue;

        const widths = new Map();
        rows.forEach((row) => widths.set(row.length, (widths.get(row.length) || 0) + 1));
        let mode = 0;
        let modeCount = 0;
        for (const [width, count] of widths) {
            if (count > modeCount || (count === modeCount && width > mode)) {
                mode = width;
                modeCount = count;
            }
        }
        if (mode < 2) continue;
        const score = (modeCount / rows.length) * 1000 + Math.min(mode, 999);
        if (score > bestScore) {
            best = delimiter;
            bestScore = score;
        }
    }
    return best;
}
