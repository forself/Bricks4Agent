/**
 * PageGenerator integration test.
 *
 * The generator is package-only and may import components only from the custom
 * component library under `@component-library/ui_components`.
 */

import { PageGenerator, ComponentPaths, validateDefinition } from '../index.js';
import { FieldTypes, PageTypes } from '../PageDefinition.js';
import { DiaryEditorDefinition } from './DiaryEditorDefinition.js';
import { ContactFormDefinition } from './ContactFormDefinition.js';
import * as fs from 'fs';
import * as path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const outputDir = path.join(__dirname, 'generated');

const definitions = [
    { name: 'DiaryEditor', def: DiaryEditorDefinition },
    { name: 'ContactForm', def: ContactFormDefinition }
];

const componentLibraryDefinition = {
    name: 'PackagesTestPage',
    type: PageTypes.FORM,
    description: 'Validates custom component library imports only.',
    components: [
        'WebTextEditor',
        'DrawingBoard',
        'BasicButton',
        'ButtonGroup',
        'DateTimeInput',
        'AddressInput',
        'OrganizationInput'
    ],
    fields: [
        { name: 'content', type: FieldTypes.RICHTEXT, label: 'Content', required: true },
        { name: 'sketch', type: FieldTypes.CANVAS, label: 'Sketch', required: false },
        { name: 'eventTime', type: FieldTypes.DATETIME, label: 'Event time', required: false },
        { name: 'location', type: FieldTypes.ADDRESS, label: 'Location', required: false },
        { name: 'org', type: FieldTypes.ORGANIZATION, label: 'Organization', required: false }
    ],
    api: { baseUrl: '/api/test', endpoints: {} },
    behaviors: {}
};

const generator = new PageGenerator({
    baseImportPath: '../../core/BasePage.js'
});

if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true });
}

let allPassed = true;
const results = [];

console.log('========================================');
console.log('PageGenerator custom-library test');
console.log('========================================');

for (const { name, def } of definitions) {
    console.log(`\n--- ${name} ---`);

    const validation = validateDefinition(def);
    if (!validation.valid) {
        console.log('Validation failed:');
        validation.errors.forEach((error) => console.log(`  - ${error}`));
        allPassed = false;
        results.push({ name, success: false, error: 'validation failed' });
        continue;
    }

    const result = generator.generate(def);
    if (result.errors.length > 0) {
        console.log('Generation failed:');
        result.errors.forEach((error) => console.log(`  - ${error}`));
        allPassed = false;
        results.push({ name, success: false, error: 'generation failed' });
        continue;
    }

    if (result.code.includes('../components/')) {
        console.log('Legacy SPA import detected in generated code.');
        allPassed = false;
        results.push({ name, success: false, error: 'legacy SPA import detected' });
        continue;
    }

    const outputPath = path.join(outputDir, `${def.name}.js`);
    fs.writeFileSync(outputPath, result.code, 'utf8');

    const lines = result.code.split('\n').length;
    const sizeKb = (Buffer.byteLength(result.code, 'utf8') / 1024).toFixed(2);
    console.log(`Generated ${path.basename(outputPath)} (${lines} lines, ${sizeKb} KB)`);
    results.push({ name, success: true, lines, size: `${sizeKb} KB` });
}

console.log('\n--- Custom component library imports ---');

const libraryResult = generator.generate(componentLibraryDefinition);
if (libraryResult.errors.length > 0) {
    console.log('Generation failed:');
    libraryResult.errors.forEach((error) => console.log(`  - ${error}`));
    allPassed = false;
    results.push({ name: 'PackagesTest', success: false, error: 'generation failed' });
} else {
    const pathErrors = [];
    const importErrors = [];

    for (const componentName of componentLibraryDefinition.components) {
        const importPath = ComponentPaths[componentName]?.packages;
        if (!importPath) {
            pathErrors.push(`${componentName}: missing custom component library path`);
            continue;
        }

        const relativePath = importPath.replace('@component-library/', '');
        const fullPath = path.resolve(__dirname, '..', '..', relativePath);
        if (!fs.existsSync(fullPath)) {
            pathErrors.push(`${componentName}: missing file ${relativePath}`);
        }

        if (!libraryResult.code.includes(importPath)) {
            importErrors.push(`${componentName}: generated code is missing ${importPath}`);
        }
    }

    if (libraryResult.code.includes('../components/')) {
        importErrors.push('generated code still contains legacy SPA import paths');
    }

    if (pathErrors.length > 0 || importErrors.length > 0) {
        pathErrors.forEach((error) => console.log(`  - ${error}`));
        importErrors.forEach((error) => console.log(`  - ${error}`));
        allPassed = false;
        results.push({ name: 'PackagesTest', success: false, error: 'import validation failed' });
    } else {
        const outputPath = path.join(outputDir, 'PackagesTestPage.js');
        fs.writeFileSync(outputPath, libraryResult.code, 'utf8');

        const lines = libraryResult.code.split('\n').length;
        const sizeKb = (Buffer.byteLength(libraryResult.code, 'utf8') / 1024).toFixed(2);
        console.log(`Generated ${path.basename(outputPath)} (${lines} lines, ${sizeKb} KB)`);
        results.push({ name: 'PackagesTest', success: true, lines, size: `${sizeKb} KB` });
    }
}

console.log('\n--- Component path audit ---');

let pathAuditPassed = true;
for (const [componentName, paths] of Object.entries(ComponentPaths)) {
    const importPath = paths.packages;
    const relativePath = importPath.replace('@component-library/', '');
    const fullPath = path.resolve(__dirname, '..', '..', relativePath);

    if (!fs.existsSync(fullPath)) {
        console.log(`Missing: ${componentName} -> ${relativePath}`);
        pathAuditPassed = false;
        allPassed = false;
    } else {
        console.log(`OK: ${componentName} -> ${relativePath}`);
    }
}

console.log('\n========================================');
console.log('Summary');
console.log('========================================');

for (const result of results) {
    if (result.success) {
        console.log(`OK  ${result.name}: ${result.lines} lines, ${result.size}`);
    } else {
        console.log(`ERR ${result.name}: ${result.error}`);
    }
}

console.log(`Paths: ${pathAuditPassed ? 'OK' : 'FAILED'}`);
console.log(`Overall: ${allPassed ? 'PASS' : 'FAIL'}`);

process.exit(allPassed ? 0 : 1);
