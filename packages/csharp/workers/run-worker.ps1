# Runs a capability worker with its identity credentials from the shared
# worker-auth store, so it can register with a broker that has
# WorkerAuth.Enforce = true (the canonical sidecar stack does).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker site-crawler
#   powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker transport-tdx -BrokerPort 7000
#   powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker generation
#
# The generation worker runs the repository's generator (tools/generation/cli.mjs) with node
# (Generation:NodePath, else B4A_NODE_PATH, else node on PATH) and writes packages under
# -GenerationOutputRoot (default: the sidecar data directory, outside every user workspace).
# The broker must use the same Generation:OutputRoot.
#
# Credentials are provisioned by start-sidecar-stack.ps1 into
# $env:BRICKS4AGENT_SECRETS_DIR\worker-auth.json (default C:\secure\Bricks4Agent).
# Missing credentials fail closed. -AllowUnauthenticated is an explicit local
# development opt-in: the worker registers without a key id or signature, which only a
# broker with WorkerAuth.Enforce = false accepts (an enforcing broker rejects the registration).
# Secrets are passed through the child environment, never argv.

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("file", "browser", "transport-tdx", "site-crawler", "generation")]
    [string]$Worker,
    [string]$BrokerHost = "localhost",
    [int]$BrokerPort = 7000,
    [string]$GenerationOutputRoot = "",
    [switch]$AllowUnauthenticated
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$workerMap = @{
    "file"          = @{ Project = "file-worker\FileWorker.csproj";                Type = "file-worker" }
    "browser"       = @{ Project = "browser-worker\BrowserWorker.csproj";          Type = "browser-worker" }
    "transport-tdx" = @{ Project = "transport-tdx-worker\TransportTdxWorker.csproj"; Type = "transport-tdx" }
    "site-crawler"  = @{ Project = "site-crawler-worker\SiteCrawlerWorker.csproj"; Type = "site-crawler-worker" }
    "generation"    = @{ Project = "generation-worker\GenerationWorker.csproj";   Type = "generation-worker" }
}

$selected = $workerMap[$Worker]
$projectPath = Join-Path $scriptDir $selected.Project
if (-not (Test-Path $projectPath)) {
    throw "Worker project not found: $projectPath"
}

$secureSecretsRoot = if ($env:BRICKS4AGENT_SECRETS_DIR) { $env:BRICKS4AGENT_SECRETS_DIR } else { "C:\secure\Bricks4Agent" }
$workerAuthStorePath = Join-Path $secureSecretsRoot "worker-auth.json"

$credential = $null
if (Test-Path $workerAuthStorePath) {
    try {
        $credentials = @((Get-Content -LiteralPath $workerAuthStorePath -Raw | ConvertFrom-Json) | ForEach-Object { $_ })
    } catch {
        throw 'Cannot read a valid worker credential store.'
    }
    $matches = @($credentials | Where-Object { $_.WorkerType -eq $selected.Type })
    if ($matches.Count -gt 1) { throw 'Worker credential store contains duplicate worker identities.' }
    if ($matches.Count -eq 1) { $credential = $matches[0] }
}
$hasAuth = $null -ne $credential -and
    $credential.KeyId -is [string] -and $credential.SharedSecret -is [string] -and
    -not [string]::IsNullOrWhiteSpace($credential.KeyId) -and
    -not [string]::IsNullOrWhiteSpace($credential.SharedSecret)
if (-not $hasAuth -and -not $AllowUnauthenticated) {
    throw "No valid worker credential for '$($selected.Type)'. Provision worker-auth.json with the sidecar stack first. For isolated development only, use -AllowUnauthenticated explicitly."
}

$generationArgs = @()
if ($Worker -eq "generation") {
    $repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..\..")).Path
    if ([string]::IsNullOrWhiteSpace($GenerationOutputRoot)) {
        $GenerationOutputRoot = Join-Path $repoRoot ".run\line-sidecar\data\generation-out"
    }
    New-Item -ItemType Directory -Force -Path $GenerationOutputRoot | Out-Null
    $generationArgs = @(
        "--Generation:ToolsRoot=$repoRoot",
        "--Generation:OutputRoot=$GenerationOutputRoot"
    )
}

$runArgs = @(
    "run", "--project", $projectPath, "--",
    "--Worker:BrokerHost=$BrokerHost",
    "--Worker:BrokerPort=$BrokerPort"
) + $generationArgs

# Workers strip the WORKER_ prefix and translate __ to configuration colons.
# Keep credentials out of the command line, and restore the caller's environment.
$authEnvironment = @{
    'WORKER_Worker__Auth__WorkerType' = $selected.Type
    'WORKER_Worker__Auth__KeyId' = $(if ($hasAuth) { [string]$credential.KeyId } else { '' })
    'WORKER_Worker__Auth__SharedSecret' = $(if ($hasAuth) { [string]$credential.SharedSecret } else { '' })
    'WORKER_Worker__Auth__AllowUnauthenticated' = $(if ($AllowUnauthenticated -and -not $hasAuth) { 'true' } else { 'false' })
}
$previousEnvironment = @{}
try {
    foreach ($name in $authEnvironment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $authEnvironment[$name], 'Process')
    }
    Write-Host "Starting $($selected.Type) -> broker ${BrokerHost}:${BrokerPort} (auth: $(if ($hasAuth) { 'enabled' } else { 'disabled (development)' }))"
    & dotnet @runArgs
    $workerExitCode = $LASTEXITCODE
} finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
exit $workerExitCode
