/**
 * Utility services and security helpers public entrypoint.
 */
export { GeolocationService, GeolocationError } from './GeolocationService.js';
export { WeatherService, WeatherError } from './WeatherService.js';
export { default as SimpleZip } from './SimpleZip.js';
export { nextUid, resetUid } from './uid.js';
export {
    escapeHtml,
    escapeAttr,
    raw,
    isRawHtml,
    safeHtml,
    hasSqlInjectionRisk,
    hasPathTraversalRisk,
    sanitizeUrl,
    sanitizeHTML
} from './security.js';
export { onThemeChange, notifyThemeChange, resolveTokens } from './theme-bus.js';
export { sequentialScale, divergingScale, categoricalColor, hierarchicalColor, CATEGORICAL_HUES, mixHex, sampleRamp } from './color-scale.js';
export { aggregate, groupBy, summarize, pivot, binNumeric, bucketTime, topN, AGGS } from './aggregation-engine.js';
export { buildQuadtree, bhAccumulate, nearestBody } from './quadtree.js';
export { createSimulation, createRng } from './force-engine.js';
