import assert from 'node:assert/strict';
import test from 'node:test';
import { legacyRegionMapImageUrl } from './LegacyRegionMapLayouts.js';

test('legacyRegionMapImageUrl honors an explicit published asset base', () => {
    const base = 'https://example.test/app/lib/ui_components/data/RegionMap/maps/legacy/';
    assert.equal(
        legacyRegionMapImageUrl('city', 'Taiwan.png', base),
        `${base}city/Taiwan.png`
    );
    assert.equal(legacyRegionMapImageUrl('unknown', 'Taiwan.png', base), '');
    assert.equal(legacyRegionMapImageUrl('city', '../Taiwan.png', base), '');
});
