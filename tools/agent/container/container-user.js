'use strict';

// One "runs as root" rule for the host-side checks: the compose check
// (tools/agent/tests/test-agent-container-config.js), the Containerfile check
// (tools/scripts/verify-container-images.mjs) and the docker/podman inspect check
// (tools/agent/tests/lib/container-stack.js). It matches ContainerManager.ValidateUser in the broker:
// a user spec "user[:group]" is root when any part is the name root (any case) or a number whose value
// is 0 (0, 00, +0, -0), because the runtime parses numeric ids.

/** The parts of a "user[:group]" spec, trimmed. */
function userSpecParts(spec) {
    return String(spec ?? '').split(':').map((part) => part.trim());
}

/** True when one part of a user spec names root: the name root (any case) or a numeric id of 0. */
function isRootPart(part) {
    const value = String(part ?? '').trim();
    if (/^[+-]?\d+$/.test(value)) {
        return Number(value) === 0;
    }
    return value.toLowerCase() === 'root';
}

/** True when any part of a "user[:group]" spec is root (see isRootPart). */
function isRootUserSpec(spec) {
    return userSpecParts(spec).some(isRootPart);
}

module.exports = { isRootPart, isRootUserSpec, userSpecParts };
