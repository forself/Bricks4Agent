export function materializeMessagePanelSurface(surface = {}) {
    return {
        surfaceId: surface.surface_id || null,
        surfaceKind: 'message_panel',
        title: surface.title || '',
        message: surface.message || '',
    };
}

export default materializeMessagePanelSurface;
