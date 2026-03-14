const fs = require('fs');

function validate(filePath) {
  const label = filePath.split('/').pop();
  console.log('=== Validating:', label, '===');
  const doc = JSON.parse(fs.readFileSync(filePath, 'utf8'));

  const rootRequired = ['kind', 'version', 'definitions'];
  for (const k of rootRequired) {
    if (!(k in doc)) { console.log('FAIL: missing root field', k); return false; }
  }
  if (doc.kind !== 'definition-template') { console.log('FAIL: kind != definition-template'); return false; }
  console.log('  root shape: OK');

  const defs = doc.definitions;
  if (!defs.pages && !defs.apps) { console.log('FAIL: no pages or apps'); return false; }
  console.log('  definitions.pages:', (defs.pages || []).length, 'entries');
  console.log('  definitions.apps:', (defs.apps || []).length, 'entries');

  const pageIds = new Set();
  for (const p of (defs.pages || [])) {
    if (!p.id || !p.definition) { console.log('FAIL: page missing id/definition'); return false; }
    if (!/^[a-z0-9][a-z0-9-]*$/.test(p.id)) { console.log('FAIL: page id pattern:', p.id); return false; }
    if (pageIds.has(p.id)) { console.log('FAIL: duplicate page id:', p.id); return false; }
    pageIds.add(p.id);
    if (!p.definition.name || !p.definition.type) { console.log('FAIL: page def missing name/type for', p.id); return false; }
    if (!['form','list','detail','dashboard'].includes(p.definition.type)) { console.log('FAIL: invalid page type:', p.definition.type, 'for', p.id); return false; }
  }
  console.log('  page ids: unique, valid pattern');

  const appIds = new Set();
  for (const a of (defs.apps || [])) {
    if (!a.id || !a.app) { console.log('FAIL: app missing id/app'); return false; }
    if (appIds.has(a.id)) { console.log('FAIL: duplicate app id:', a.id); return false; }
    appIds.add(a.id);

    const be = a.app.backend;
    if (!be) { console.log('FAIL: no backend for app', a.id); return false; }
    for (const k of ['features','services','middleware','routeGroups','hosting']) {
      if (!(k in be)) { console.log('FAIL: backend missing', k, 'for app', a.id); return false; }
    }

    const pageRefs = (a.app.frontend || {}).pageRefs || [];
    for (const ref of pageRefs) {
      if (!pageIds.has(ref)) { console.log('FAIL: pageRef', ref, 'not in pages'); return false; }
    }

    const modIds = new Set((be.endpointModules || []).map(m => m.id));
    for (const rg of be.routeGroups) {
      for (const mr of rg.moduleRefs) {
        if (!modIds.has(mr)) { console.log('FAIL: moduleRef', mr, 'not in endpointModules for group', rg.id); return false; }
      }
      const polIds = new Set((be.policies || []).map(p => p.id));
      for (const pr of (rg.policies || [])) {
        if (!polIds.has(pr)) { console.log('FAIL: policy ref', pr, 'not in policies for group', rg.id); return false; }
      }
    }

    console.log('  app', a.id, ': backend shape OK, referential integrity OK');
    console.log('    services:', be.services.length);
    console.log('    middleware:', be.middleware.length);
    console.log('    endpointModules:', (be.endpointModules || []).length);
    console.log('    routeGroups:', be.routeGroups.length);
    console.log('    pageRefs:', pageRefs.length);
  }

  console.log('  PASS\n');
  return true;
}

const ok1 = validate('tools/spa-generator/schemas/golden/spa-generator.definition-template.json');
const ok2 = validate('tools/spa-generator/schemas/golden/shop-bricks.definition-template.json');
console.log('Result:', ok1 && ok2 ? 'ALL PASS' : 'SOME FAILED');
process.exit(ok1 && ok2 ? 0 : 1);
