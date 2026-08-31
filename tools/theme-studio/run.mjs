/**
 * Backward-compatible Theme Studio browser smoke entry point.
 *
 * The authoritative browser harness uses Playwright's supported Edge launcher.
 * This replaces the former hand-written DevTools pipe, which could terminate
 * current Windows Edge builds before the first assertion was executed.
 */
await import('../scripts/studio-integration-smoke.mjs');
