export const backendSchema = Object.freeze({
    schema_version: 1,
    tiers: Object.freeze(['N1', 'N2', 'N3']),
    required: Object.freeze(['enabled', 'tier', 'template', 'persistence', 'security', 'entities', 'modules', 'seed']),
});

export default backendSchema;
