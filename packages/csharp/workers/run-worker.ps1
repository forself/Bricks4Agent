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

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("file", "browser", "transport-tdx", "site-crawler", "generation")]
    [string]$Worker,
    [string]$BrokerHost = "localhost",
    [int]$BrokerPort = 7000,
    [string]$GenerationOutputRoot = ""
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

$authArgs = @()
if (Test-Path $workerAuthStorePath) {
    $credentials = @((Get-Content $workerAuthStorePath -Raw | ConvertFrom-Json) | ForEach-Object { $_ })
    $credential = $credentials | Where-Object { $_.WorkerType -eq $selected.Type } | Select-Object -First 1
    if ($null -ne $credential -and -not [string]::IsNullOrWhiteSpace($credential.SharedSecret)) {
        $authArgs = @(
            "--Worker:Auth:WorkerType=$($selected.Type)",
            "--Worker:Auth:KeyId=$($credential.KeyId)",
            "--Worker:Auth:SharedSecret=$($credential.SharedSecret)"
        )
    } else {
        Write-Warning "No credential for '$($selected.Type)' in $workerAuthStorePath; starting without worker auth. Run the sidecar stack once to provision it."
    }
} else {
    Write-Warning "Worker auth store not found at $workerAuthStorePath; starting without worker auth. Run the sidecar stack once to provision it."
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
) + $generationArgs + $authArgs

Write-Host "Starting $($selected.Type) -> broker ${BrokerHost}:${BrokerPort} (auth: $(if ($authArgs.Count -gt 0) { 'enabled' } else { 'disabled' }))"
& dotnet @runArgs
exit $LASTEXITCODE
