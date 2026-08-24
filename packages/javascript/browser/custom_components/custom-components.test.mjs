import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import {
    analyzeCustomComponentDefinition,
    validateCustomComponentDefinition,
} from './CustomComponentDefinition.js';
import { CustomComponentRenderer } from './CustomComponentRenderer.js';
import {
    buildRegistry,
    runRegistryBuild,
    stableStringify,
} from './build-registry.mjs';

const BUILTINS = new Set(['BasicButton', 'Icon', 'TextInput']);

function componentNode(id = 'leaf', component = 'BasicButton', options = undefined) {
    const node = { type: 'component', id, component };
    if (options !== undefined) node.options = options;
    return node;
}

function groupNode(id, children) {
    return {
        type: 'group',
        id,
        children,
    };
}

function customNode(id, component) {
    return {
        type: 'custom',
        id,
        component,
    };
}

function definition(registryName, kind, root, componentId = null) {
    const snakeName = registryName
        .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
        .toLowerCase();
    return {
        schema_version: 1,
        component_id: componentId ?? `custom.${snakeName}`,
        registry_name: registryName,
        display_name: registryName,
        version: '1.0.0',
        kind,
        root,
    };
}

function nestedGroups(depth) {
    let node = componentNode('depth-leaf');
    for (let level = 1; level <= depth; level += 1) {
        node = groupNode(`depth-group-${level}`, [node]);
    }
    return node;
}

function resolverFor(...definitions) {
    const index = new Map();
    for (const entry of definitions) {
        index.set(entry.registry_name, entry);
        index.set(entry.component_id, entry);
    }
    return (reference) => index.get(reference) ?? null;
}

function assertErrorCode(result, code) {
    assert.equal(
        result.errors.some((error) => error.code === code),
        true,
        `Expected ${code}; got ${result.errors.map((error) => error.code).join(', ')}`,
    );
}

test('three group layers remain composite and four become template', () => {
    const threeLayers = definition('ThreeLayerComponent', 'composite', nestedGroups(3));
    const fourLayers = definition('FourLayerTemplate', 'template', nestedGroups(4));

    const threeResult = analyzeCustomComponentDefinition(threeLayers, { builtinNames: BUILTINS });
    const fourResult = analyzeCustomComponentDefinition(fourLayers, { builtinNames: BUILTINS });

    assert.equal(threeResult.valid, true);
    assert.equal(threeResult.derived_kind, 'composite');
    assert.equal(threeResult.max_depth, 3);
    assert.equal(fourResult.valid, true);
    assert.equal(fourResult.derived_kind, 'template');
    assert.equal(fourResult.max_depth, 4);
});

test('two custom composite references promote a group to template', () => {
    const left = definition('LeftComposite', 'composite', groupNode('left-group', [componentNode('left-leaf')]));
    const right = definition('RightComposite', 'composite', groupNode('right-group', [componentNode('right-leaf', 'Icon')]));
    const template = definition(
        'TwoCompositeTemplate',
        'template',
        groupNode('template-root', [
            customNode('left-reference', 'LeftComposite'),
            customNode('right-reference', 'custom.right_composite'),
        ]),
    );

    const result = analyzeCustomComponentDefinition(template, {
        builtinNames: BUILTINS,
        resolveCustom: resolverFor(left, right, template),
    });

    assert.equal(result.valid, true);
    assert.equal(result.derived_kind, 'template');
    assert.equal(result.custom_reference_count, 2);
    assert.equal(result.composite_reference_count, 2);
});

test('a template reference propagates template classification', () => {
    const deepTemplate = definition('DeepTemplate', 'template', nestedGroups(4));
    const wrapper = definition(
        'TemplateWrapper',
        'template',
        groupNode('wrapper-root', [customNode('deep-template', 'DeepTemplate')]),
    );

    const result = analyzeCustomComponentDefinition(wrapper, {
        builtinNames: BUILTINS,
        resolveCustom: resolverFor(deepTemplate, wrapper),
    });

    assert.equal(result.valid, true);
    assert.equal(result.derived_kind, 'template');
    assert.equal(result.custom_reference_count, 1);
});

test('a root custom node inherits the dependency tier', () => {
    const dependency = definition(
        'InheritedComposite',
        'composite',
        groupNode('inherited-group', [componentNode('inherited-leaf')]),
    );
    const alias = definition(
        'CompositeAlias',
        'composite',
        customNode('composite-reference', 'InheritedComposite'),
    );
    const result = analyzeCustomComponentDefinition(alias, {
        builtinNames: BUILTINS,
        resolveCustom: resolverFor(dependency, alias),
    });

    assert.equal(result.valid, true);
    assert.equal(result.derived_kind, 'composite');
    assert.equal(result.composite_reference_count, 1);
});

test('custom dependency cycles are rejected and reported', () => {
    const first = definition('CycleFirst', 'atomic', customNode('second-ref', 'CycleSecond'));
    const second = definition('CycleSecond', 'atomic', customNode('first-ref', 'CycleFirst'));
    const result = analyzeCustomComponentDefinition(first, {
        builtinNames: BUILTINS,
        resolveCustom: resolverFor(first, second),
    });

    assert.equal(result.valid, false);
    assertErrorCode(result, 'CUSTOM_REFERENCE_CYCLE');
    assert.deepEqual(result.cycles[0], ['CycleFirst', 'CycleSecond', 'CycleFirst']);
});

test('custom dependency cycles are rejected when the resolver returns fresh clones', () => {
    const first = definition('CloneCycleFirst', 'atomic', customNode('second-ref', 'CloneCycleSecond'));
    const second = definition('CloneCycleSecond', 'atomic', customNode('first-ref', 'custom.clone_cycle_first'));
    const sourceByReference = new Map([
        [first.registry_name, first],
        [first.component_id, first],
        [second.registry_name, second],
        [second.component_id, second],
    ]);
    let resolutionCount = 0;
    const result = analyzeCustomComponentDefinition(first, {
        builtinNames: BUILTINS,
        resolveCustom(reference) {
            resolutionCount += 1;
            const resolved = sourceByReference.get(reference);
            return resolved ? JSON.parse(JSON.stringify(resolved)) : null;
        },
    });

    assert.equal(result.valid, false);
    assertErrorCode(result, 'CUSTOM_REFERENCE_CYCLE');
    assert.deepEqual(result.cycles[0], ['CloneCycleFirst', 'CloneCycleSecond', 'CloneCycleFirst']);
    assert.equal(resolutionCount, 2);
});

test('unknown built-in and custom references are rejected', () => {
    const unknownBuiltin = definition('UnknownBuiltin', 'atomic', componentNode('missing', 'MissingBuiltin'));
    const unknownCustom = definition('UnknownCustom', 'atomic', customNode('missing-custom', 'MissingCustom'));

    const builtinResult = analyzeCustomComponentDefinition(unknownBuiltin, { builtinNames: BUILTINS });
    const customResult = analyzeCustomComponentDefinition(unknownCustom, { builtinNames: BUILTINS });

    assertErrorCode(builtinResult, 'UNRESOLVED_REFERENCE');
    assert.deepEqual(builtinResult.unresolved, ['MissingBuiltin']);
    assertErrorCode(customResult, 'UNRESOLVED_REFERENCE');
    assert.deepEqual(customResult.unresolved, ['MissingCustom']);
});

test('declared kind must equal the derived kind', () => {
    const mismatched = definition(
        'MismatchedKind',
        'atomic',
        groupNode('mismatch-root', [componentNode('mismatch-leaf')]),
    );
    const result = validateCustomComponentDefinition(mismatched, { builtinNames: BUILTINS });

    assert.equal(result.derived_kind, 'composite');
    assertErrorCode(result, 'KIND_MISMATCH');
});

test('duplicate node ids are rejected across a definition tree', () => {
    const duplicate = definition(
        'DuplicateNodeIds',
        'composite',
        groupNode('duplicate-root', [
            componentNode('same-id'),
            groupNode('nested-group', [componentNode('same-id', 'Icon')]),
        ]),
    );
    const result = validateCustomComponentDefinition(duplicate, { builtinNames: BUILTINS });

    assertErrorCode(result, 'DUPLICATE_NODE_ID');
});

test('additional, prototype-sensitive, and unsafe HTML keys are rejected', () => {
    const unsafeOptions = JSON.parse('{"innerHTML":"<img src=x>","__proto__":{"polluted":true}}');
    const unsafe = definition(
        'UnsafeOptions',
        'atomic',
        componentNode('unsafe-leaf', 'BasicButton', unsafeOptions),
    );
    unsafe.extra = true;

    const result = validateCustomComponentDefinition(unsafe, { builtinNames: BUILTINS });

    assertErrorCode(result, 'ADDITIONAL_PROPERTY');
    assertErrorCode(result, 'DANGEROUS_KEY');
    assertErrorCode(result, 'UNSAFE_HTML_KEY');
});

test('runtime component options recursively remove case-insensitive security keys', () => {
    class FakeClassList {
        constructor() { this.values = new Set(); }
        add(...names) { names.forEach((name) => this.values.add(name)); }
    }

    class FakeElement {
        constructor(ownerDocument) {
            this.ownerDocument = ownerDocument;
            this.nodeType = 1;
            this.parentNode = null;
            this.childNodes = [];
            this.classList = new FakeClassList();
            this.attributes = {};
            this.id = '';
        }
        appendChild(node) {
            node.parentNode?.removeChild?.(node);
            this.childNodes.push(node);
            node.parentNode = this;
            return node;
        }
        removeChild(node) {
            const index = this.childNodes.indexOf(node);
            if (index >= 0) this.childNodes.splice(index, 1);
            node.parentNode = null;
            return node;
        }
        contains(node) {
            for (let current = node; current; current = current.parentNode) {
                if (current === this) return true;
            }
            return false;
        }
        setAttribute(name, value) { this.attributes[name] = String(value); }
        replaceChildren() {
            this.childNodes.forEach((node) => { node.parentNode = null; });
            this.childNodes = [];
        }
        remove() { this.parentNode?.removeChild?.(this); }
        get firstChild() { return this.childNodes[0] ?? null; }
    }

    class FakeDocument {
        createElement() { return new FakeElement(this); }
    }

    const documentRef = new FakeDocument();
    let capturedOptions = null;
    class CapturingComponent {
        constructor(options) {
            capturedOptions = options;
            this.element = documentRef.createElement('div');
        }
        mount(container) { container.appendChild(this.element); return this; }
        destroy() { this.element.remove(); }
    }

    const factory = {
        registry: { CapturingComponent },
        create(name, options) { return new this.registry[name](options); },
    };
    const runtimeDefinition = definition(
        'RuntimeOptionSafety',
        'atomic',
        componentNode('safe-leaf', 'CapturingComponent', { placeholder: 'authored' }),
    );
    const runtimeOptions = JSON.parse(`{
        "placeholder":"runtime",
        "__HtMl":"unsafe",
        "HTML":"unsafe",
        "InNeRhTmL":"<img src=x>",
        "OuterHTML":"unsafe",
        "DangerouslySetInnerHTML":{"__html":"unsafe"},
        "Container":"untrusted",
        "containerID":"untrusted",
        "TARGET":"untrusted",
        "Element":"untrusted",
        "Constructor":"untrusted",
        "Prototype":"untrusted",
        "__PrOtO__":{"polluted":true},
        "safe":{
            "value":"kept",
            "RAWhtml":"unsafe",
            "nested":{
                "SrcDoc":"unsafe",
                "DocumentWrite":"unsafe",
                "InsertAdjacentHTML":"unsafe",
                "ok":true
            }
        }
    }`);
    const renderer = new CustomComponentRenderer({
        definition: runtimeDefinition,
        factory,
        nodeOptions: runtimeOptions,
    });
    const container = documentRef.createElement('main');
    renderer.mount(container);

    assert.equal(capturedOptions.placeholder, 'runtime');
    for (const key of [
        'InNeRhTmL',
        '__HtMl',
        'HTML',
        'OuterHTML',
        'DangerouslySetInnerHTML',
        'Container',
        'TARGET',
        'Element',
        'Constructor',
        'Prototype',
        '__PrOtO__',
    ]) {
        assert.equal(Object.hasOwn(capturedOptions, key), false, `${key} must be removed`);
    }
    assert.equal(Object.hasOwn(capturedOptions, 'containerID'), false);
    assert.deepEqual(capturedOptions.safe, { value: 'kept', nested: { ok: true } });
    assert.equal(capturedOptions.container.nodeType, 1);
    assert.equal(capturedOptions.containerId, capturedOptions.container.id);

    renderer.destroy();
    assert.equal(container.childNodes.length, 0);
});

test('registry build rejects duplicate component ids', async () => {
    const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'bricks-custom-duplicate-'));
    try {
        const definitionsDir = path.join(tempRoot, 'definitions');
        await fs.mkdir(definitionsDir);
        const first = definition('DuplicateOne', 'atomic', componentNode('one'), 'custom.duplicate');
        const second = definition('DuplicateTwo', 'atomic', componentNode('two'), 'custom.duplicate');
        await fs.writeFile(path.join(definitionsDir, 'one.json'), stableStringify(first));
        await fs.writeFile(path.join(definitionsDir, 'two.json'), stableStringify(second));

        await assert.rejects(
            buildRegistry({ customRoot: tempRoot, builtinNames: BUILTINS }),
            /Duplicate component_id/,
        );
    } finally {
        await fs.rm(tempRoot, { recursive: true, force: true });
    }
});

test('registry build is deterministic, supports check mode, and blocks path traversal', async () => {
    const tempParent = await fs.mkdtemp(path.join(os.tmpdir(), 'bricks-custom-build-'));
    const customRoot = path.join(tempParent, 'custom_components');
    const outsideDefinitions = path.join(tempParent, 'outside');
    try {
        const definitionsDir = path.join(customRoot, 'definitions');
        await fs.mkdir(definitionsDir, { recursive: true });
        await fs.mkdir(outsideDefinitions);

        const atomic = definition('TemporaryAtomic', 'atomic', componentNode('temporary-leaf'));
        const composite = definition(
            'TemporaryComposite',
            'composite',
            groupNode('temporary-group', [componentNode('temporary-button'), componentNode('temporary-icon', 'Icon')]),
        );
        await fs.writeFile(path.join(definitionsDir, 'z-composite.json'), stableStringify(composite));
        await fs.writeFile(path.join(definitionsDir, 'a-atomic.json'), stableStringify(atomic));

        const first = await buildRegistry({ customRoot, builtinNames: BUILTINS });
        const second = await buildRegistry({ customRoot, builtinNames: BUILTINS });
        assert.equal(stableStringify(first.registry), stableStringify(second.registry));
        assert.deepEqual(
            first.registry.components.map((entry) => entry.component_id),
            ['custom.temporary_atomic', 'custom.temporary_composite'],
        );

        await runRegistryBuild({ customRoot, builtinNames: BUILTINS });
        await runRegistryBuild({ customRoot, builtinNames: BUILTINS, checkOnly: true });

        await assert.rejects(
            buildRegistry({
                customRoot,
                definitionsDir: outsideDefinitions,
                builtinNames: BUILTINS,
            }),
            /escapes the custom component root/,
        );
    } finally {
        await fs.rm(tempParent, { recursive: true, force: true });
    }
});
