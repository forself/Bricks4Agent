import { OfflineGisMap } from './OfflineGisMap.js';
import { GisSearch, GisScope, GisTimeline } from './controls.js';

// Local instance-scoped registry: no writes to ComponentFactory, global catalogs or routes.
export const classes = Object.freeze({ OfflineGisMap, GisSearch, GisScope, GisTimeline });
export function createGisFactory(api) {
    return {
        has: name => Object.hasOwn(classes, name),
        create(name, options) {
            if (!Object.hasOwn(classes, name)) throw new Error(`Unknown GIS component: ${name}`);
            return new classes[name]({ ...options, ...(name === 'OfflineGisMap' ? { api } : {}) });
        },
    };
}
