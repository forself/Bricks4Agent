# Isolated launcher checks: dotnet is mocked; no worker or real credential store is used.
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$launcher = Join-Path $repoRoot 'packages/csharp/workers/run-worker.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('b4a-launcher-' + [guid]::NewGuid().ToString('N'))
$oldSecrets = $env:BRICKS4AGENT_SECRETS_DIR
$envNames = @('WORKER_Worker__Auth__WorkerType', 'WORKER_Worker__Auth__KeyId', 'WORKER_Worker__Auth__SharedSecret', 'WORKER_Worker__Auth__AllowUnauthenticated')
$oldAuth = @{}
foreach ($name in $envNames) { $oldAuth[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$launchState = @{}
function dotnet {
    $launchState.launched = $true
    $launchState.arguments = @($args)
    $launchState.key = $env:WORKER_Worker__Auth__KeyId
    $launchState.secret = $env:WORKER_Worker__Auth__SharedSecret
    $launchState.type = $env:WORKER_Worker__Auth__WorkerType
    $launchState.allowUnauthenticated = $env:WORKER_Worker__Auth__AllowUnauthenticated
    $global:LASTEXITCODE = 0
}
try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $env:BRICKS4AGENT_SECRETS_DIR = $testRoot
    $launchState.launched = $false
    $rejected = $false
    try { & $launcher -Worker file } catch { $rejected = $true }
    Assert $rejected 'Missing credentials must reject by default.'
    Assert (-not $launchState.launched) 'Worker must not start without credentials.'

    $env:WORKER_Worker__Auth__SharedSecret = 'caller-value'
    & $launcher -Worker file -AllowUnauthenticated
    Assert $launchState.launched 'Explicit development mode should start.'
    Assert ([string]::IsNullOrEmpty($launchState.secret)) 'Development mode must clear inherited credentials.'
    Assert ($launchState.allowUnauthenticated -eq 'true') 'Development mode opt-in flag must be passed to workers.'
    Assert ($env:WORKER_Worker__Auth__SharedSecret -eq 'caller-value') 'Caller environment must be restored.'

    $launchState.launched = $false
    & $launcher -Worker site-crawler -AllowUnauthenticated
    Assert $launchState.launched 'Site crawler should start with explicit development mode.'
    Assert ($launchState.type -eq 'site-crawler-worker') 'Site crawler worker type environment mapping incorrect.'
    Assert ([string]::IsNullOrEmpty($launchState.key) -and [string]::IsNullOrEmpty($launchState.secret)) 'Site crawler development launch must clear credentials.'
    Assert ($launchState.allowUnauthenticated -eq 'true') 'Site crawler development opt-in flag must be passed.'

    $store = Join-Path $testRoot 'worker-auth.json'
    @{ WorkerType = 'file-worker'; KeyId = ''; SharedSecret = 'test-only-secret' } |
        ConvertTo-Json | Set-Content -LiteralPath $store
    $rejected = $false
    try { & $launcher -Worker file } catch { $rejected = $true }
    Assert $rejected 'Blank key id must reject.'

    @{ WorkerType = 'file-worker'; KeyId = 'test-key'; SharedSecret = 'test-only-secret' } |
        ConvertTo-Json | Set-Content -LiteralPath $store
    & $launcher -Worker file
    Assert ($launchState.type -eq 'file-worker') 'Worker type environment mapping incorrect.'
    Assert ($launchState.key -eq 'test-key' -and $launchState.secret -eq 'test-only-secret') 'Credentials not passed through environment.'
    Assert ($launchState.allowUnauthenticated -eq 'false') 'Authenticated launch must not enable unauthenticated mode.'
    Assert (-not (($launchState.arguments -join ' ') -match 'test-only-secret|Worker:Auth:')) 'Credentials leaked into command arguments.'
    Assert ($env:WORKER_Worker__Auth__SharedSecret -eq 'caller-value') 'Authenticated launch must restore caller environment.'
    Write-Host 'Worker launcher: 5 scenarios passed.'
} finally {
    $env:BRICKS4AGENT_SECRETS_DIR = $oldSecrets
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $oldAuth[$name], 'Process') }
    # Exact known files only; no recursive removal or computed directory traversal.
    $store = Join-Path $testRoot 'worker-auth.json'
    if (Test-Path -LiteralPath $store) { Remove-Item -LiteralPath $store }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot }
}

