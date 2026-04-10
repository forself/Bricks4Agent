export const frontendSchema = Object.freeze({
    schema_version: 1,
    required: Object.freeze(['app', 'auth', 'navigation', 'shared_resources', 'routes']),
    routeKinds: Object.freeze([
        'content_page',
        'auth_form_page',
        'resource_list_page',
        'resource_form_page',
        'detail_page',
    ]),
});

export default frontendSchema;
