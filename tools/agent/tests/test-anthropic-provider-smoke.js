#!/usr/bin/env node
'use strict';

const assert = require('assert');

const ANTHROPIC_MODELS_URL = 'https://api.anthropic.com/v1/models';
const ANTHROPIC_VERSION = '2023-06-01';
const EXPECTED_MODEL = 'claude-sonnet-4-6';

function getVisibleModelIds(payload) {
    const candidates = Array.isArray(payload?.data)
        ? payload.data
        : Array.isArray(payload?.models)
            ? payload.models
            : [];

    return candidates
        .map((model) => model?.id ?? model?.name)
        .filter((id) => typeof id === 'string' && id.length > 0);
}

async function main() {
    const apiKey = process.env.ANTHROPIC_API_KEY?.trim();
    if (!apiKey) {
        console.log('SKIP Anthropic provider smoke: ANTHROPIC_API_KEY is not set.');
        return;
    }

    const response = await fetch(ANTHROPIC_MODELS_URL, {
        method: 'GET',
        headers: {
            'x-api-key': apiKey,
            'anthropic-version': ANTHROPIC_VERSION,
        },
    });

    assert(
        response.ok,
        `Anthropic models request failed with HTTP ${response.status}.`
    );

    const payload = await response.json();
    const visibleModelIds = getVisibleModelIds(payload);

    assert(
        visibleModelIds.includes(EXPECTED_MODEL),
        `Expected Anthropic model ${EXPECTED_MODEL} to be visible. Visible models: ${visibleModelIds.join(', ') || '(none)'}`
    );

    console.log(`Anthropic provider smoke passed: ${EXPECTED_MODEL} is visible via /v1/models.`);
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
