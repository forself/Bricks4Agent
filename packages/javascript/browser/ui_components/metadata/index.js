export {
    MANIFEST_SCHEMA_VERSION,
    COMPONENT_CATEGORIES,
    COMPONENT_KINDS,
    COMPONENT_MATURITY,
    GENERATOR_USAGE_MODES,
    validateManifest,
    validateManifestMap,
} from './manifest-schema.js';

export { introspectBrowserMetadata } from './introspection.js';

export {
    createManifestSkeleton,
    resolveManifestRelativePath,
    loadManifestMap,
    renderComponentCatalog,
    renderGeneratorSupportMatrix,
    buildMetadataArtifacts,
    writeJsonFile,
} from './renderer.js';
