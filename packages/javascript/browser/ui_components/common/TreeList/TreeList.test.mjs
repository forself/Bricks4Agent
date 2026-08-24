import assert from 'node:assert/strict';
import test from 'node:test';
import { installTestDom } from './test-dom.mjs';

const documentRef = installTestDom();
const { TreeList } = await import('./TreeList.js');

function findByNodeId(root, className, nodeId) {
    return root.querySelectorAll(`.${className}`)
        .find((element) => element.dataset.nodeId === nodeId) ?? null;
}

test('parent row selects while its toggle independently expands and collapses', () => {
    const selections = [];
    const tree = new TreeList({
        data: [{
            id: 'parent',
            label: 'Parent',
            children: [{ id: 'child', label: 'Child' }],
        }],
        onSelect: (node) => selections.push(node.id),
    }).mount(documentRef.body);

    findByNodeId(tree.element, 'tree-node-row', 'parent').click();
    assert.equal(tree.activeId, 'parent');
    assert.deepEqual(selections, ['parent']);
    assert.equal(tree.expandedIds.has('parent'), false);

    const toggle = findByNodeId(tree.element, 'tree-node-toggle', 'parent');
    const event = { type: 'click' };
    toggle.dispatchEvent(event);
    assert.equal(event.propagationStopped, true);
    assert.equal(tree.expandedIds.has('parent'), true);
    assert.equal(tree.activeId, 'parent');
    assert.deepEqual(selections, ['parent']);
    assert.ok(findByNodeId(tree.element, 'tree-node-row', 'child'));

    findByNodeId(tree.element, 'tree-node-toggle', 'parent').click();
    assert.equal(tree.expandedIds.has('parent'), false);
    assert.equal(tree.activeId, 'parent');
    tree.destroy();
});

test('setActive expands ancestors and setData remains usable after parent selection', () => {
    const tree = new TreeList({
        data: [{
            id: 'parent',
            label: 'Parent',
            children: [{ id: 'child', label: 'Child' }],
        }],
    });

    tree.setActive('child');
    assert.equal(tree.activeId, 'child');
    assert.equal(tree.expandedIds.has('parent'), true);
    assert.ok(findByNodeId(tree.element, 'tree-node-row', 'child'));

    tree.setData([{ id: 'replacement', label: 'Replacement', children: [{ id: 'nested', label: 'Nested' }] }]);
    findByNodeId(tree.element, 'tree-node-row', 'replacement').click();
    assert.equal(tree.activeId, 'replacement');
    tree.destroy();
    assert.equal(tree._icons.length, 0);
});
