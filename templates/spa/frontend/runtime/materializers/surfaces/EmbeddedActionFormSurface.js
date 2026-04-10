export function materializeEmbeddedActionFormSurface(surface = {}) {
    return {
        surfaceId: surface.surface_id || null,
        surfaceKind: 'embedded_action_form',
        action: surface.action || null,
        fields: Array.isArray(surface.fields) ? surface.fields : [],
    };
}

export default materializeEmbeddedActionFormSurface;
