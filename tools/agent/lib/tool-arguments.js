'use strict';

/**
 * 工具參數在送出之前的整理（受治理代理，見 governed-executor）。
 *
 * 有些模型把宣告為 object 或 array 的參數寫成 JSON 字串：例如模型輸出的 JSON 少了一個 } 時，模型伺服器把那段原文
 * 當成字串放進 tool_calls。broker 的 schema 驗證只會回「型別不符」，模型看不到真正的語法錯誤，只能原樣重送。
 * 這裡依工具的 parameters 定義：宣告為 object 或 array、收到字串時先 JSON.parse，得到相符的型別就改用解析結果。
 * 生成工具（validate_definition、generate_scaffold）在 parse 失敗時再做保守的閉合修補：只看字串外的括號，以堆疊
 * 在不相符處或結尾補上缺少的 } 或 ]。仍不成功時回結構化錯誤（參數、錯誤位置與附近的文字），由呼叫端在本地回覆，
 * 不送 broker。這裡不放寬任何檢查：broker 的 schema 驗證、驗證器與 generate 的完整驗證仍是最後的關卡。
 */

// 與 broker 的 System.Text.Json 預設深度上限一致；更深的值不轉換、不當成工具呼叫，也不再序列化。
const MAX_ARGUMENT_DEPTH = 64;
// parse 失敗時做閉合修補的工具（只有生成工具的定義參數會長到模型容易漏掉括號）。
const REPAIRABLE_TOOLS = new Set(['validate_definition', 'generate_scaffold']);
const ERROR_CONTEXT_CHARS = 60;

function isPlainObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/**
 * 值的巢狀深度是否超過 limit（物件與陣列各算一層，純量為 0）。以迭代走訪，不受呼叫堆疊限制；超過即停止。
 */
function exceedsJsonDepth(value, limit = MAX_ARGUMENT_DEPTH) {
    if (value === null || typeof value !== 'object') {
        return false;
    }
    const stack = [[value, 1]];
    while (stack.length > 0) {
        const [current, depth] = stack.pop();
        if (depth > limit) {
            return true;
        }
        const children = Array.isArray(current) ? current : Object.values(current);
        for (const child of children) {
            if (child !== null && typeof child === 'object') {
                stack.push([child, depth + 1]);
            }
        }
    }
    return false;
}

function matchesType(value, expected) {
    return expected === 'array' ? Array.isArray(value) : isPlainObject(value);
}

function describeValue(value) {
    if (value === null) return 'null';
    if (Array.isArray(value)) return 'an array';
    if (typeof value === 'object') return 'an object';
    return `a ${typeof value}`;
}

/** 這段開頭是否可能是某個 JSON 的開頭：可以 parse，或錯誤發生在結尾（輸入不完整）。 */
function isValidJsonPrefix(prefix) {
    try {
        JSON.parse(prefix);
        return true;
    } catch (error) {
        const message = String(error?.message || '');
        if (/end of JSON input/i.test(message)) {
            return true;
        }
        const match = /position (\d+)/.exec(message);
        return match !== null && Number(match[1]) >= prefix.length;
    }
}

/**
 * 錯誤的位置：訊息帶 position 時用它；輸入不完整時在結尾；V8 的「Unexpected token」不帶位置，
 * 這時以二分搜尋找出最長的有效開頭，錯誤就在它的下一個字元。
 */
function locateJsonError(text, message) {
    const match = /position (\d+)/.exec(message);
    if (match) {
        return Math.min(Number(match[1]), text.length);
    }
    if (/end of JSON input/i.test(message)) {
        return text.length;
    }
    let low = 0;
    let high = text.length;
    while (high - low > 1) {
        const middle = Math.floor((low + high) / 2);
        if (isValidJsonPrefix(text.slice(0, middle))) {
            low = middle;
        } else {
            high = middle;
        }
    }
    return low;
}

/** JSON.parse 的錯誤訊息、位置與附近約 ERROR_CONTEXT_CHARS 個字元；可以 parse 時回 null。 */
function describeJsonError(text) {
    try {
        JSON.parse(text);
        return null;
    } catch (error) {
        const message = String(error?.message || 'invalid JSON');
        const position = locateJsonError(text, message);
        const half = ERROR_CONTEXT_CHARS / 2;
        const start = Math.max(0, position - half);
        return {
            message,
            position,
            near: text.slice(start, Math.min(text.length, start + ERROR_CONTEXT_CHARS)),
        };
    }
}

/** 位置 index 之後第一個不是空白的字元。 */
function nextSignificant(text, index) {
    for (let cursor = index; cursor < text.length; cursor += 1) {
        if (!/\s/.test(text[cursor])) return text[cursor];
    }
    return '';
}

/**
 * 保守的閉合修補：只看字串外的 { [ } ]，只補閉合符號、不刪也不改其他字元。不相符處有兩種：
 * 閉合符號與堆疊頂端不相符時，先補上缺少的閉合符號直到相符；物件中逗號後面直接接著 { 或 [
 * （物件成員一定以字串鍵開頭，這在 JSON 中不可能成立，常見於陣列元素之間漏了物件的 }）時，在逗號前補上 }。
 * 結尾補上剩下的。閉合符號沒有對應的開頭、字串沒有結束或深度超過上限時放棄。回傳 null 表示沒有可補的或無法修補。
 */
function repairClosers(text) {
    const stack = [];
    let output = '';
    let added = 0;
    let inString = false;
    let escaped = false;

    for (let index = 0; index < text.length; index += 1) {
        const char = text[index];
        if (inString) {
            output += char;
            if (escaped) {
                escaped = false;
            } else if (char === '\\') {
                escaped = true;
            } else if (char === '"') {
                inString = false;
            }
            continue;
        }
        if (char === '"') {
            inString = true;
        } else if (char === '{' || char === '[') {
            stack.push(char === '{' ? '}' : ']');
            if (stack.length > MAX_ARGUMENT_DEPTH) {
                return null;
            }
        } else if (char === '}' || char === ']') {
            while (stack.length > 0 && stack[stack.length - 1] !== char) {
                output += stack.pop();
                added += 1;
            }
            if (stack.length === 0) {
                return null;
            }
            stack.pop();
        } else if (char === ',' && stack[stack.length - 1] === '}' && '{['.includes(nextSignificant(text, index + 1) || ' ')) {
            while (stack.length > 0 && stack[stack.length - 1] === '}') {
                output += stack.pop();
                added += 1;
            }
            if (stack.length === 0) {
                return null;
            }
        }
        output += char;
    }

    if (inString) {
        return null;
    }
    while (stack.length > 0) {
        output += stack.pop();
        added += 1;
    }
    return added > 0 ? { text: output, added } : null;
}

function invalidArgument(parameter, expected, detail) {
    const kind = expected === 'array' ? 'array' : 'object';
    return {
        code: 'ARGUMENT_JSON_INVALID',
        parameter,
        message: `The ${parameter} argument is a string that is not valid JSON: ${detail.message}`,
        position: detail.position,
        near: detail.near,
        hint: `Pass ${parameter} as a JSON ${kind} value, not as a string. The string is not a valid JSON ${kind}, `
            + 'even after adding missing closing brackets: check the brackets, quotes and commas near the position. '
            + 'The request was not sent to the broker and used no quota.',
    };
}

/**
 * 依工具的 parameters 定義整理參數。
 * @param {string} toolName
 * @param {object} args - 工具參數（模型送出的）
 * @param {object} parameters - 工具定義的 parameters（JSON schema 的 properties 與 type）
 * @returns {{ args: object, notes: string[], error: object|null }}
 *   error 不為 null 時（只有生成工具），呼叫端在本地回覆錯誤，不送 broker。
 */
function coerceToolArguments(toolName, args, parameters) {
    const properties = isPlainObject(parameters?.properties) ? parameters.properties : {};
    if (!isPlainObject(args)) {
        return { args, notes: [], error: null };
    }

    const repairable = REPAIRABLE_TOOLS.has(toolName);
    let coerced = args;
    const notes = [];
    for (const [parameter, schema] of Object.entries(properties)) {
        const expected = schema?.type;
        if ((expected !== 'object' && expected !== 'array') || typeof args[parameter] !== 'string') {
            continue;
        }

        const text = args[parameter].trim();
        let value;
        let added = 0;
        const parseError = describeJsonError(text);
        if (parseError === null) {
            value = JSON.parse(text);
        } else if (repairable) {
            const repaired = repairClosers(text);
            if (!repaired || describeJsonError(repaired.text) !== null) {
                return { args, notes, error: invalidArgument(parameter, expected, parseError) };
            }
            value = JSON.parse(repaired.text);
            added = repaired.added;
        } else {
            // 其他工具照原樣送出：broker 的 schema 驗證回報型別不符。
            continue;
        }

        if (!matchesType(value, expected) || exceedsJsonDepth(value)) {
            if (!repairable) {
                continue;
            }
            const problem = exceedsJsonDepth(value)
                ? `The ${parameter} value is nested deeper than ${MAX_ARGUMENT_DEPTH} levels.`
                : `The ${parameter} string holds ${describeValue(value)}, not ${expected === 'array' ? 'an array' : 'an object'}.`;
            return {
                args,
                notes,
                error: {
                    code: 'ARGUMENT_TYPE_INVALID',
                    parameter,
                    message: problem,
                    hint: `Pass ${parameter} as a JSON ${expected === 'array' ? 'array' : 'object'} value. `
                        + 'The request was not sent to the broker and used no quota.',
                },
            };
        }

        if (coerced === args) {
            coerced = { ...args };
        }
        coerced[parameter] = value;
        if (added > 0) {
            notes.push(`The ${parameter} argument arrived as a JSON string that was missing ${added} closing `
                + `bracket${added === 1 ? '' : 's'}; ${added === 1 ? 'it was' : 'they were'} added automatically before `
                + 'sending, so check that the structure is what you meant. '
                + `Pass ${parameter} as a JSON ${expected === 'array' ? 'array' : 'object'} value, not as a string.`);
        }
    }
    return { args: coerced, notes, error: null };
}

/** 本地的參數錯誤，格式與驗證結果相同（ok 與 errors），讓模型照同一種方式修正。 */
function formatArgumentError(error) {
    return JSON.stringify({ ok: false, source: 'agent', errors: [error] });
}

/** 把參數整理的附註加到工具結果：結果是 JSON 物件時放進 agent_note，否則接在後面一行。 */
function attachArgumentNotes(result, notes) {
    if (!Array.isArray(notes) || notes.length === 0) {
        return result;
    }
    const note = notes.join(' ');
    if (typeof result === 'string') {
        try {
            const parsed = JSON.parse(result);
            if (isPlainObject(parsed)) {
                parsed.agent_note = note;
                return JSON.stringify(parsed);
            }
        } catch (_) {
            // 不是 JSON：接在後面
        }
    }
    return `${result}\n[Governed] note: ${note}`;
}

module.exports = {
    MAX_ARGUMENT_DEPTH,
    attachArgumentNotes,
    coerceToolArguments,
    exceedsJsonDepth,
    formatArgumentError,
    repairClosers,
};
