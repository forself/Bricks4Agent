import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import * as vm from 'node:vm';

function stringToken(source, start, quote) {
    let index = start + 1;
    let raw = '';
    while (index < source.length) {
        const char = source[index++];
        if (char === quote) {
            try {
                const value = quote === '"' ? JSON.parse(`"${raw}"`)
                    : JSON.parse(`"${raw.replace(/\\'/g, "'").replace(/"/g, '\\"')}"`);
                return { token: { type: 'string', value }, end: index };
            } catch { return { token: { type: 'invalid-string' }, end: index }; }
        }
        if (char === '\\' && index < source.length) raw += char + source[index++];
        else raw += char;
    }
    return { token: { type: 'invalid-string' }, end: index };
}

function tokens(source) {
    const result = [];
    let index = 0;
    while (index < source.length) {
        const char = source[index];
        if (/\s/.test(char)) { index++; continue; }
        if (char === '/' && source[index + 1] === '/') {
            index += 2; while (index < source.length && source[index] !== '\n') index++; continue;
        }
        if (char === '/' && source[index + 1] === '*') {
            index += 2;
            const end = source.indexOf('*/', index);
            if (end < 0) throw new SyntaxError('Unclosed JavaScript comment');
            index = end + 2; continue;
        }
        if (char === '"' || char === "'") {
            const parsed = stringToken(source, index, char);
            result.push(parsed.token); index = parsed.end; continue;
        }
        if (char === '`') {
            const start = index++;
            while (index < source.length) {
                if (source[index] === '\\') { index += 2; continue; }
                if (source[index++] === '`') break;
            }
            const template = source.slice(start, index);
            result.push({ type: 'template', hasInterpolation: template.includes('${') });
            continue;
        }
        if (/[A-Za-z_$]/.test(char)) {
            const start = index++;
            while (index < source.length && /[\w$]/.test(source[index])) index++;
            result.push({ type: 'identifier', value: source.slice(start, index) });
            continue;
        }
        result.push({ type: 'punct', value: char }); index++;
    }
    return result;
}

export function analyzeJavaScriptModule(source, identifier = 'publish-policy-input.mjs') {
    if (typeof vm.SourceTextModule !== 'function') throw new Error('Run with --experimental-vm-modules.');
    const module = new vm.SourceTextModule(source, { identifier });
    const moduleTokens = tokens(source);
    const dynamicImports = [];
    const unresolvedDynamic = [];
    for (let index = 0; index < moduleTokens.length; index++) {
        const current = moduleTokens[index];
        if (current.type === 'invalid-string') unresolvedDynamic.push('ambiguous JavaScript lexical token');
        if (current.type === 'template' && current.hasInterpolation) unresolvedDynamic.push('template interpolation');
        if (current.type === 'punct' && current.value === '/') unresolvedDynamic.push('ambiguous slash token');
        if (current.type !== 'identifier' || current.value !== 'import' || moduleTokens[index - 1]?.value === '.') continue;
        if (moduleTokens[index + 1]?.value === '.' && moduleTokens[index + 2]?.value === 'meta' &&
            moduleTokens[index + 3]?.value === '.' && moduleTokens[index + 4]?.value === 'glob' &&
            moduleTokens[index + 5]?.value === '(') {
            unresolvedDynamic.push('import.meta.glob');
            continue;
        }
        if (moduleTokens[index + 1]?.value !== '(') continue;
        const argument = moduleTokens[index + 2];
        const afterArgument = moduleTokens[index + 3];
        if (argument?.type === 'string' && (afterArgument?.value === ')' || afterArgument?.value === ','))
            dynamicImports.push(argument.value);
        else unresolvedDynamic.push('non-literal import()');
    }
    return {
        imports: module.dependencySpecifiers,
        dynamicImports: [...new Set(dynamicImports)],
        unresolvedDynamic: [...new Set(unresolvedDynamic)]
    };
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
    try {
        const file = process.argv[2];
        if (!file) throw new Error('Module-analysis input file is required.');
        process.stdout.write(JSON.stringify(analyzeJavaScriptModule(readFileSync(file, 'utf8'), file)));
    } catch (error) {
        process.stderr.write(`${error.message}\n`);
        process.exitCode = 1;
    }
}
