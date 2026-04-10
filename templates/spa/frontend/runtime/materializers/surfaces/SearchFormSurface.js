export function materializeSearchFormSurface(surface = {}) {
    return {
        surfaceId: surface.surface_id || null,
        surfaceKind: 'search_form',
        fields: Array.isArray(surface.fields) ? surface.fields : [],
    };
}

export default materializeSearchFormSurface;
