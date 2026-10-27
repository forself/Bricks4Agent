import { createGisFactory } from './registry.js';
import { GisApi } from './api.js';
import { t } from './locale.js';

export async function mountGis(PageClass, container, { api = new GisApi() } = {}) {
    let page, destroyed = false, sequence = 0, searchSequence = 0;
    let selection = { bbox: [-180, -85, 180, 85] }, time = {};
    const abort = new AbortController();
    const status = text => page?.getComponent('scope')?.setValue(text);
    async function refresh() {
        if (!page || destroyed) return;
        const ticket = ++sequence;
        try {
            const result = await api.query({ ...selection, ...time }, abort.signal);
            if (destroyed || ticket !== sequence) return;
            page.getComponent('map').setValue(result.items); page.getComponent('scope').setResults(result.items);
            status(`${t('count')}: ${result.items.length}`);
        } catch (error) { if (!destroyed && ticket === sequence) status(`${t('error')}: ${error.message}`); }
    }
    const commands = new Map([
        ['gis.search', async (_context, query) => {
            const ticket = ++searchSequence;
            try { const result = await api.search(query, abort.signal); if (!destroyed && ticket === searchSequence) page.getComponent('search').setValue(result.candidates); }
            catch (error) { if (!destroyed && ticket === searchSequence) status(`${t('error')}: ${error.message}`); }
        }],
        ['gis.locate', (_context, point) => { page.getComponent('map').setView([point.lon, point.lat]); }],
        ['gis.mode', (_context, mode) => page.getComponent('map').setMode(mode)],
        ['gis.time', (_context, value) => { time = value; return refresh(); }],
        ['gis.select', (_context, value) => { selection = value; return refresh(); }],
        ['gis.marker', async (_context, point) => {
            try { await api.add({ ...point, label: page.getComponent('scope').getValue(), time: Date.now(), weight: 1 }, abort.signal); await refresh(); }
            catch (error) { if (!destroyed) status(`${t('error')}: ${error.message}`); }
        }],
    ]);
    page = new PageClass({ factory: createGisFactory(api), commandRegistry: commands });
    await page.init(); page.mount(container); await refresh();
    return { page, refresh, destroy() { destroyed = true; abort.abort(); page.destroy(); } };
}
