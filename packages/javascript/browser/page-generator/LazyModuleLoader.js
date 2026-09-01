/**
 * Create a memoized asynchronous module/export loader.
 *
 * The importer is intentionally supplied by the consuming application so its
 * bundler can keep the dynamic import as a real chunk boundary.  B4A owns the
 * concurrency, validation and retry semantics; generated projects only map a
 * route or renderer to its module.
 *
 * A failed load is not cached.  A later navigation may retry after a transient
 * network or deployment error without reloading the entire application.
 */
export function createLazyModuleLoader(importer, options = {}) {
    if (typeof importer !== 'function') {
        throw new TypeError('createLazyModuleLoader requires an importer function.');
    }

    const exportName = options.exportName === undefined ? 'default' : options.exportName;
    const label = String(options.label || exportName || 'module export');
    let pending = null;
    let resolved = false;
    let resolvedValue;

    const load = async () => {
        if (resolved) return resolvedValue;
        if (pending) return pending;

        pending = Promise.resolve()
            .then(() => importer())
            .then(moduleNamespace => {
                const value = exportName === null
                    ? moduleNamespace
                    : moduleNamespace?.[exportName];
                if (value === undefined || value === null) {
                    throw new TypeError(`Lazy module did not provide ${label}.`);
                }
                resolvedValue = value;
                resolved = true;
                pending = null;
                return value;
            })
            .catch(error => {
                pending = null;
                throw error;
            });

        return pending;
    };

    Object.defineProperty(load, 'isB4ALazyModuleLoader', {
        value: true,
        enumerable: false,
    });
    load.preload = load;
    load.peek = () => resolved ? resolvedValue : undefined;
    return load;
}

/** Resolve a B4A lazy loader while leaving ordinary class/function values intact. */
export function resolveLazyModuleValue(value) {
    return typeof value === 'function' && value.isB4ALazyModuleLoader === true
        ? value()
        : Promise.resolve(value);
}

export default createLazyModuleLoader;
