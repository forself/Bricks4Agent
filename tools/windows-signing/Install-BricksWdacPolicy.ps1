[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$PolicyBinary = "",
    [string]$OutputDir = ".run/wdac",
    [switch]$Deploy,
    [switch]$ListActive,
    [switch]$VerifyActive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoPath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    $scriptDir = Split-Path -Parent $MyInvocation.ScriptName
    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\.."))
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-CiToolPath {
    $command = Get-Command CiTool -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $systemPath = Join-Path $env:WINDIR "System32\CiTool.exe"
    if (Test-Path -LiteralPath $systemPath) {
        return $systemPath
    }

    throw "CiTool is not available on this host."
}

function Get-WdacPolicyIdFromPath {
    param([string]$PathValue)

    return [System.IO.Path]::GetFileNameWithoutExtension($PathValue)
}

function Get-ActiveWdacPolicyPath {
    param([string]$PolicyId)

    return Join-Path $env:WINDIR "System32\CodeIntegrity\CiPolicies\Active\$PolicyId.cip"
}

function Test-WdacPolicyActive {
    param([string]$PolicyId)

    $activePolicyPath = Get-ActiveWdacPolicyPath -PolicyId $PolicyId
    return Test-Path -LiteralPath $activePolicyPath
}

function Assert-WdacPolicyActive {
    param([string]$PolicyId)

    $activePolicyPath = Get-ActiveWdacPolicyPath -PolicyId $PolicyId
    if (-not (Test-Path -LiteralPath $activePolicyPath)) {
        throw "WDAC policy $PolicyId is not active. Expected active policy file: $activePolicyPath"
    }
}

$ciTool = Get-CiToolPath

if ($ListActive) {
    if ($PSCmdlet.ShouldProcess("active WDAC policies", "List")) {
        & $ciTool --list-policies -json
        if ($LASTEXITCODE -ne 0) {
            throw "CiTool --list-policies failed with exit code $LASTEXITCODE"
        }
    }
}

$resolvedPolicyBinary = $null
if ([string]::IsNullOrWhiteSpace($PolicyBinary)) {
    $resolvedOutputDir = Resolve-RepoPath $OutputDir
    $candidate = Get-ChildItem -LiteralPath $resolvedOutputDir -Filter *.cip -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($candidate) {
        $resolvedPolicyBinary = $candidate.FullName
    }
}
else {
    $resolvedPolicyBinary = Resolve-RepoPath $PolicyBinary
}

if (-not $resolvedPolicyBinary -or -not (Test-Path -LiteralPath $resolvedPolicyBinary)) {
    throw "Policy binary not found. Provide -PolicyBinary or run New-BricksWdacSupplementalPolicy.ps1 first."
}

$policyId = Get-WdacPolicyIdFromPath -PathValue $resolvedPolicyBinary
$activePolicyPath = Get-ActiveWdacPolicyPath -PolicyId $policyId

if (-not $Deploy) {
    [pscustomobject]@{
        Mode = "dry-run"
        PolicyBinary = $resolvedPolicyBinary
        PolicyId = $policyId
        ActivePolicyPath = $activePolicyPath
        IsActive = Test-WdacPolicyActive -PolicyId $policyId
        Command = "`"$ciTool`" --update-policy `"$resolvedPolicyBinary`" -json"
        RequiresAdministrator = $true
    } | ConvertTo-Json -Depth 3
    return
}

if (-not (Test-IsAdministrator)) {
    throw "WDAC policy deployment requires an elevated PowerShell session."
}

if ($PSCmdlet.ShouldProcess($resolvedPolicyBinary, "Deploy WDAC supplemental policy with CiTool --update-policy")) {
    & $ciTool --update-policy $resolvedPolicyBinary -json
    if ($LASTEXITCODE -ne 0) {
        throw "CiTool --update-policy failed with exit code $LASTEXITCODE"
    }
}

if ($VerifyActive) {
    Assert-WdacPolicyActive -PolicyId $policyId
}
