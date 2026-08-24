import fs from 'node:fs';
import path from 'node:path';

const repoRoot = process.cwd();
const failures = [];

function assert(condition, message) {
  if (!condition) failures.push(message);
}

function exists(relativePath) {
  return fs.existsSync(path.join(repoRoot, relativePath));
}

function read(relativePath) {
  const fullPath = path.join(repoRoot, relativePath);
  if (!fs.existsSync(fullPath)) {
    failures.push(`missing file: ${relativePath}`);
    return '';
  }
  return fs.readFileSync(fullPath, 'utf8');
}

function assertContains(label, text, needle) {
  assert(text.includes(needle), `${label} should contain ${needle}`);
}

const certScriptPath = 'tools/windows-signing/New-BricksDevCodeSigningCert.ps1';
const signScriptPath = 'tools/windows-signing/Sign-BricksAssemblies.ps1';
const policyScriptPath = 'tools/windows-signing/New-BricksWdacSupplementalPolicy.ps1';
const installScriptPath = 'tools/windows-signing/Install-BricksWdacPolicy.ps1';
const repairScriptPath = 'tools/windows-signing/Repair-BricksWdacRuntimeTrust.ps1';
const repairTestsScriptPath = 'tools/windows-signing/Repair-BricksWdacTestTrust.ps1';
const sidecarStartScriptPath = 'packages/csharp/workers/line-worker/start-sidecar-stack.ps1';
const manualPath = 'docs/manuals/dev-code-signing-wdac.zh-TW.md';

for (const required of [
  certScriptPath,
  signScriptPath,
  policyScriptPath,
  installScriptPath,
  repairScriptPath,
  repairTestsScriptPath,
  sidecarStartScriptPath,
  manualPath
]) {
  assert(exists(required), `expected ${required} to exist`);
}

const certScript = read(certScriptPath);
assertContains('cert script', certScript, 'New-SelfSignedCertificate');
assertContains('cert script', certScript, 'Export-Certificate');
assertContains('cert script', certScript, 'Bricks4Agent Dev Code Signing');
assertContains('cert script', certScript, '.run/code-signing');
assertContains('cert script', certScript, 'SupportsShouldProcess');
assertContains('cert script', certScript, 'TrustForCurrentUser');

const signScript = read(signScriptPath);
assertContains('sign script', signScript, 'Set-AuthenticodeSignature');
assertContains('sign script', signScript, 'Get-AuthenticodeSignature');
assertContains('sign script', signScript, 'Get-BricksProjectAssemblyNames');
assertContains('sign script', signScript, 'bin');
assertContains('sign script', signScript, 'TimestampServer');
assertContains('sign script', signScript, 'SupportsShouldProcess');
assertContains('sign script', signScript, 'node_modules');
assertContains('sign script', signScript, '.claude');
assertContains('sign script', signScript, '.worktrees');
assertContains('sign script', signScript, 'signedByExpectedCertificate');
assertContains('sign script', signScript, 'AlreadySigned');

const sidecarStartScript = read(sidecarStartScriptPath);
assertContains('sidecar start script', sidecarStartScript, 'Sign-SidecarRuntimeAssemblies');
assertContains('sidecar start script', sidecarStartScript, 'Get-BricksProjectAssemblyNames');
assertContains('sidecar start script', sidecarStartScript, 'Set-AuthenticodeSignature');
assertContains('sidecar start script', sidecarStartScript, '$_.Extension -eq ".dll"');
assertContains('sidecar start script', sidecarStartScript, '$_.Extension -eq ".exe"');
assertContains('sidecar start script', sidecarStartScript, 'Sign-SidecarRuntimeAssemblies -RuntimeRoot $runRoot');

const policyScript = read(policyScriptPath);
assertContains('WDAC policy script', policyScript, 'New-CIPolicy');
assertContains('WDAC policy script', policyScript, 'ConvertFrom-CIPolicy');
assertContains('WDAC policy script', policyScript, 'Set-CIPolicyIdInfo');
assertContains('WDAC policy script', policyScript, 'SupplementsBasePolicyID');
assertContains('WDAC policy script', policyScript, '.run/wdac');
assertContains('WDAC policy script', policyScript, 'BasePolicyId');
assertContains('WDAC policy script', policyScript, 'PolicyLevel');
assertContains('WDAC policy script', policyScript, 'ValidateSet("Publisher", "FilePublisher", "Hash")');
assertContains('WDAC policy script', policyScript, 'Level = $PolicyLevel');
assertContains('WDAC policy script', policyScript, 'FallbackLevels');
assertContains('WDAC policy script', policyScript, 'Fallback =');
assertContains('WDAC policy script', policyScript, 'AuditMode');
assertContains('WDAC policy script', policyScript, 'Remove-AuditModeRule');
assertContains('WDAC policy script', policyScript, 'Enabled:Audit Mode');

const installScript = read(installScriptPath);
assertContains('WDAC install script', installScript, 'CiTool');
assertContains('WDAC install script', installScript, 'Deploy');
assertContains('WDAC install script', installScript, 'Test-IsAdministrator');
assertContains('WDAC install script', installScript, '--update-policy');
assertContains('WDAC install script', installScript, 'dry-run');
assertContains('WDAC install script', installScript, 'Assert-WdacPolicyActive');
assertContains('WDAC install script', installScript, 'CiPolicies\\Active');
assertContains('WDAC install script', installScript, 'VerifyActive');

const repairScript = read(repairScriptPath);
assertContains('WDAC repair script', repairScript, 'New-BricksWdacSupplementalPolicy.ps1');
assertContains('WDAC repair script', repairScript, 'Install-BricksWdacPolicy.ps1');
assertContains('WDAC repair script', repairScript, 'Sign-RuntimeAssemblies');
assertContains('WDAC repair script', repairScript, 'Get-CodeIntegrityBlockedFiles');
assertContains('WDAC repair script', repairScript, 'Deploy');
assertContains('WDAC repair script', repairScript, 'RequiresAdministrator');
assertContains('WDAC repair script', repairScript, '.run\\line-sidecar');
assertContains('WDAC repair script', repairScript, 'PolicyLevel');

const repairTestsScript = read(repairTestsScriptPath);
assertContains('WDAC test repair script', repairTestsScript, 'Sign-BricksAssemblies.ps1');
assertContains('WDAC test repair script', repairTestsScript, 'Repair-BricksWdacRuntimeTrust.ps1');
assertContains('WDAC test repair script', repairTestsScript, 'packages\\csharp\\broker\\bin\\Debug\\net10.0');
assertContains('WDAC test repair script', repairTestsScript, 'packages\\csharp\\tests\\broker-tests\\bin\\Debug\\net10.0');
assertContains('WDAC test repair script', repairTestsScript, 'packages\\csharp\\tests\\unit\\bin\\Debug\\net10.0');
assertContains('WDAC test repair script', repairTestsScript, 'packages\\csharp\\tests\\integration\\bin\\Debug\\net10.0');
assertContains('WDAC test repair script', repairTestsScript, 'main-broker-debug');
assertContains('WDAC test repair script', repairTestsScript, 'main-broker-tests');
assertContains('WDAC test repair script', repairTestsScript, 'main-unit-tests');
assertContains('WDAC test repair script', repairTestsScript, 'main-integration-tests');
assertContains('WDAC test repair script', repairTestsScript, '[string]$PolicyLevel = "Publisher"');
assertContains('WDAC test repair script', repairTestsScript, 'Deploy');
assertContains('WDAC test repair script', repairTestsScript, 'RequiresAdministrator');

const manual = read(manualPath);
assertContains('manual', manual, 'Smart App Control');
assertContains('manual', manual, '沒有單一 app allow-list');
assertContains('manual', manual, 'BasePolicyId');
assertContains('manual', manual, 'CiTool');
assertContains('manual', manual, '不要提交');

const packageJson = JSON.parse(read('package.json') || '{}');
const scripts = packageJson.scripts || {};
for (const scriptName of [
  'signing:cert',
  'signing:assemblies',
  'signing:wdac-policy',
  'signing:wdac-install',
  'signing:wdac-repair',
  'signing:wdac-repair-tests',
  'validate:baseorm:signed',
  'validate:broker-scope:signed',
  'test:broker:signed',
  'test:unit:signed',
  'test:integration:signed',
  'test:dotnet:signed',
  'validate:db:signed',
  'validate:code-signing-flow'
]) {
  assert(Object.hasOwn(scripts, scriptName), `package.json should define ${scriptName}`);
}

for (const scriptName of ['validate:baseorm:signed', 'validate:broker-scope:signed', 'test:broker:signed', 'test:unit:signed', 'test:integration:signed']) {
  assertContains(`package.json ${scriptName}`, scripts[scriptName] || '', 'dotnet build');
  assertContains(`package.json ${scriptName}`, scripts[scriptName] || '', 'npm run signing:assemblies');
  assert(
    (scripts[scriptName] || '').includes('dotnet run --no-build') ||
    (scripts[scriptName] || '').includes('dotnet test') && (scripts[scriptName] || '').includes('--no-build'),
    `package.json ${scriptName} should run without rebuilding after signing`
  );
}

const gitignore = read('.gitignore');
assertContains('.gitignore', gitignore, '*.pfx');
assertContains('.gitignore', gitignore, '*.p12');

if (failures.length > 0) {
  console.error('Code signing flow validation failed:');
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log('Code signing flow validation passed.');
