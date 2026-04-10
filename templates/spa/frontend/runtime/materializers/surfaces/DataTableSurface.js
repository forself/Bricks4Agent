export function materializeDataTableSurface(surface = {}) {
    return {
        surfaceId: surface.surface_id || null,
        surfaceKind: 'data_table',
        source: surface.source || null,
        columns: Array.isArray(surface.columns) ? surface.columns : [],
        rowActions: Array.isArray(surface.row_actions) ? surface.row_actions : [],
    };
}

export default materializeDataTableSurface;
