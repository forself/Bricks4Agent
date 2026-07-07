/**
 * Determinism-clean unique IDs.
 *
 * Replaces `Date.now()` + `Math.random()` instance IDs. Given a deterministic construction
 * order, component DOM IDs become reproducible (no wall-clock / RNG dependency), so the same
 * inputs yield byte-identical output — the precondition for treating a component's render as a
 * pure, cacheable function. Components may inject an explicit `id` to override.
 */
let _seq = 0;

export function nextUid(prefix = 'cl') {
    _seq += 1;
    return `${prefix}-${_seq}`;
}

/** Test seam: reset the counter so suites get reproducible IDs. */
export function resetUid() {
    _seq = 0;
}
