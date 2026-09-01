import { describe, it, expect, beforeEach } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { nextUid, resetUid } from '../../ui_components/utils/uid.js';
import { Notification } from '../../ui_components/common/Notification/Notification.js';

beforeEach(() => { resetUid(); document.body.innerHTML = ''; });

const testDir = path.dirname(fileURLToPath(import.meta.url));
const uiComponentsRoot = path.resolve(testDir, '../../ui_components');

function findJavaScriptFiles(root) {
    const files = [];
    const queue = [root];
    while (queue.length > 0) {
        const current = queue.pop();
        for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
            const fullPath = path.join(current, entry.name);
            if (entry.isDirectory()) {
                if (entry.name === 'vendor') continue;
                queue.push(fullPath);
            } else if (entry.isFile() && entry.name.endsWith('.js')) {
                files.push(fullPath);
            }
        }
    }
    return files.sort();
}

describe('determinism-clean IDs (Stage 3)', () => {
    it('nextUid returns deterministic IDs and can be reset', () => {
        resetUid();
        expect(nextUid('x')).toBe('x-1');
        expect(nextUid('x')).toBe('x-2');
        expect(nextUid('y')).toBe('y-3');
        resetUid();
        expect(nextUid('x')).toBe('x-1');
    });

    it('Notification IDs are reproducible and avoid epoch/random sources', () => {
        resetUid();
        const a1 = new Notification({ message: 'a' }).id;
        const a2 = new Notification({ message: 'b' }).id;
        resetUid();
        const b1 = new Notification({ message: 'a' }).id;
        const b2 = new Notification({ message: 'b' }).id;

        expect([a1, a2]).toEqual([b1, b2]);
        expect(a1).toBe('notification-1');
        expect(a1).not.toMatch(/\d{13}/);
    });

    it('Notification ID accepts explicit injection', () => {
        expect(new Notification({ message: 'a', id: 'fixed-id' }).id).toBe('fixed-id');
    });

    it('ui_components production code does not use wall-clock or RNG generators', () => {
        const offenders = [];
        const ignored = new Set([
            path.normalize(path.join(uiComponentsRoot, 'utils', 'uid.js')),
        ]);

        for (const file of findJavaScriptFiles(uiComponentsRoot)) {
            if (ignored.has(path.normalize(file))) continue;
            const rel = path.relative(uiComponentsRoot, file).replaceAll(path.sep, '/');
            const source = fs.readFileSync(file, 'utf8')
                .replace(/\/\*[\s\S]*?\*\//g, '')
                .replace(/(^|\s)\/\/.*$/gm, '$1');
            for (const pattern of [/Date\.now\(/g, /Math\.random\(/g]) {
                let match;
                while ((match = pattern.exec(source)) !== null) {
                    const line = source.slice(0, match.index).split('\n').length;
                    offenders.push(`${rel}:${line}:${match[0]}`);
                }
            }
        }

        expect(offenders).toEqual([]);
    });
});
