import assert from 'node:assert/strict';
import test from 'node:test';

import { buildDownloadRequest } from './QueryDefinitionAdapter.js';

test('payloadDefaults survive an empty dynamic search binding', () => {
    const definition = {
        columns: [
            { key: 'id', isSelectionKey: true },
        ],
        api: {
            download: {
                id: 'exportRows',
                legacyPath: 'Log/Export',
                method: 'POST',
                selectionKey: 'id',
                payloadDefaults: { type: 'TIM' },
                payload: {
                    AuditList: '$selection.id',
                    type: '$search.Name',
                },
            },
        },
    };

    const request = buildDownloadRequest(definition, [{ id: 189 }], [0], {
        searchValues: { Name: null },
    });

    assert.deepEqual(request.payload, { type: 'TIM', AuditList: [189] });
});

test('a non-empty dynamic binding overrides payloadDefaults', () => {
    const definition = {
        columns: [{ key: 'id', isSelectionKey: true }],
        api: {
            download: {
                id: 'exportRows',
                legacyPath: 'Log/Export',
                selectionKey: 'id',
                payloadDefaults: { type: 'TIM' },
                payload: { type: '$search.Name' },
            },
        },
    };

    const request = buildDownloadRequest(definition, [{ id: 189 }], [0], {
        searchValues: { Name: 'SFT' },
    });

    assert.equal(request.payload.type, 'SFT');
});
