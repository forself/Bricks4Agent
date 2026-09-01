import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

test('TgosMap requires an injected allowlisted script URL and contains no credential', () => {
    const source = fs.readFileSync(new URL('./TgosMap.js', import.meta.url), 'utf8');
    assert.doesNotMatch(source, /https:\/\/api\.tgos\.tw\/TGOS_API\/tgos\?[^'"\s]+/);
    assert.doesNotMatch(source, /TGOS_LITE_URL/);
    assert.match(source, /scriptUrl:\s*''/);
    assert.match(source, /configuredScriptUrl\(scriptUrl\)/);
});
