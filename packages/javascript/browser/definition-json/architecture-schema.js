export const architectureSchema = Object.freeze({
    schema_version: 1,
    required: Object.freeze(['project', 'frontend', 'backend', 'docs']),
    project: Object.freeze({
        required: Object.freeze(['id', 'name', 'mode']),
    }),
    frontend: Object.freeze({
        required: Object.freeze(['enabled', 'definition_file']),
    }),
    backend: Object.freeze({
        required: Object.freeze(['enabled', 'definition_file']),
    }),
});

export default architectureSchema;
