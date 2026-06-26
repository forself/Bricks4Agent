[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$BasePolicyId = "{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}",
    [ValidateSet("Publisher", "FilePublisher", "Hash")]
    [string]$PolicyLevel = "Hash",
    [switch]$Deploy,
    [switch]$SkipBuild,
    [switch]$SkipSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $scriptDir = Split-Path -Parent $MyInvocation.ScriptName
    return [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\.."))
}

function Resolve-RepoPath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot $PathValue))
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Checked {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$FailureMessage
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE"
    }
}

function Build-Project {
    param([string]$ProjectPath)

    $resolvedProject = Resolve-RepoPath $ProjectPath
    if (-not (Test-Path -LiteralPath $resolvedProject)) {
        throw "Project does not exist: $resolvedProject"
    }

    if ($PSCmdlet.ShouldProcess($resolvedProject, "dotnet build")) {
        dotnet build $resolvedProject --nologo -v q -m:1 /nodeReuse:false
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for $resolvedProject. Exit code: $LASTEXITCODE"
        }
    }
}

function Get-LatestPolicy {
    param([string]$OutputDir)

    $resolvedOutputDir = Resolve-RepoPath $OutputDir
    Get-ChildItem -LiteralPath $resolvedOutputDir -Filter *.cip -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Test-PolicyActive {
    param([string]$PolicyBinary)

    $policyId = [System.IO.Path]::GetFileNameWithoutExtension($PolicyBinary)
    $activePath = Join-Path $env:WINDIR "System32\CodeIntegrity\CiPolicies\Active\$policyId.cip"
    return Test-Path -LiteralPath $activePath
}

$script:RepoRoot = Get-RepoRoot

if ($Deploy -and -not (Test-IsAdministrator)) {
    throw "RequiresAdministrator: Re-run this script from an elevated PowerShell session when using -Deploy."
}

$signScript = Resolve-RepoPath "tools\windows-signing\Sign-BricksAssemblies.ps1"
$repairScript = Resolve-RepoPath "tools\windows-signing\Repair-BricksWdacRuntimeTrust.ps1"
foreach ($scriptPath in @($signScript, $repairScript)) {
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        throw "Required script does not exist: $scriptPath"
    }
}

if (-not $SkipBuild) {
    Build-Project "packages\csharp\broker\Broker.csproj"
    Build-Project "packages\csharp\tests\broker-tests\Broker.Tests.csproj"
}

if (-not $SkipSigning) {
    Invoke-Checked `
        -FilePath "powershell" `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $signScript) `
        -FailureMessage "Sign-BricksAssemblies.ps1 failed."
}

$targets = @(
    [pscustomobject]@{
        Name = "main-broker-debug"
        RuntimeRoot = "packages\csharp\broker\bin\Debug\net8.0"
        OutputDir = ".run\wdac\main-broker-debug"
    },
    [pscustomobject]@{
        Name = "main-broker-tests"
        RuntimeRoot = "packages\csharp\tests\broker-tests\bin\Debug\net8.0"
        OutputDir = ".run\wdac\main-broker-tests"
    }
)

$results = New-Object System.Collections.Generic.List[object]
foreach ($target in $targets) {
    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $repairScript,
        "-BasePolicyId", $BasePolicyId,
        "-RuntimeRoot", (Resolve-RepoPath $target.RuntimeRoot),
        "-OutputDir", $target.OutputDir,
        "-PolicyLevel", $PolicyLevel,
        "-SkipRuntimeSigning"
    )
    if ($Deploy) {
        $args += "-Deploy"
    }

    Invoke-Checked `
        -FilePath "powershell" `
        -ArgumentList $args `
        -FailureMessage "Repair-BricksWdacRuntimeTrust.ps1 failed for $($target.Name)."

    $policy = Get-LatestPolicy -OutputDir $target.OutputDir
    if (-not $policy) {
        throw "No policy was generated for $($target.Name)."
    }

    $results.Add([pscustomobject]@{
        Name = $target.Name
        RuntimeRoot = Resolve-RepoPath $target.RuntimeRoot
        PolicyBinary = $policy.FullName
        PolicyLevel = $PolicyLevel
        IsActive = Test-PolicyActive -PolicyBinary $policy.FullName
        RequiresAdministrator = [bool](-not $Deploy)
    }) | Out-Null
}

[pscustomobject]@{
    Deploy = [bool]$Deploy
    PolicyLevel = $PolicyLevel
    Results = $results
} | ConvertTo-Json -Depth 4
