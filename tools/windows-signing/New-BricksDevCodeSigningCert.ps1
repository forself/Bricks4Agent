[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Subject = "CN=Bricks4Agent Dev Code Signing",
    [string]$FriendlyName = "Bricks4Agent Dev Code Signing",
    [string]$OutputDir = ".run/code-signing",
    [int]$ValidYears = 3,
    [switch]$ForceNew,
    [switch]$TrustForCurrentUser,
    [switch]$ExportPfx,
    [securestring]$PfxPassword
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

if (-not (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue)) {
    throw "New-SelfSignedCertificate is not available on this Windows host."
}

$resolvedOutputDir = Resolve-RepoPath $OutputDir
New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($ForceNew -or -not $cert) {
    if ($PSCmdlet.ShouldProcess($Subject, "Create CurrentUser code-signing certificate")) {
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $Subject `
            -FriendlyName $FriendlyName `
            -CertStoreLocation Cert:\CurrentUser\My `
            -KeyAlgorithm RSA `
            -KeyLength 3072 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy Exportable `
            -NotAfter (Get-Date).AddYears($ValidYears)
    }
}

if (-not $cert) {
    throw "No code-signing certificate is available. Re-run without -WhatIf or provide an existing certificate."
}

$certificatePath = Join-Path $resolvedOutputDir "Bricks4AgentDevCodeSigning.cer"
if ($PSCmdlet.ShouldProcess($certificatePath, "Export public code-signing certificate")) {
    Export-Certificate -Cert $cert -FilePath $certificatePath -Force | Out-Null
}

$pfxPath = $null
if ($ExportPfx) {
    if (-not $PfxPassword) {
        throw "-ExportPfx requires -PfxPassword. Store the PFX only outside git-tracked paths."
    }

    $pfxPath = Join-Path $resolvedOutputDir "Bricks4AgentDevCodeSigning.pfx"
    if ($PSCmdlet.ShouldProcess($pfxPath, "Export private code-signing certificate PFX")) {
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $PfxPassword -Force | Out-Null
    }
}

if ($TrustForCurrentUser) {
    foreach ($store in @("Cert:\CurrentUser\TrustedPublisher", "Cert:\CurrentUser\Root")) {
        if ($PSCmdlet.ShouldProcess($store, "Import Bricks4Agent dev certificate")) {
            Import-Certificate -FilePath $certificatePath -CertStoreLocation $store | Out-Null
        }
    }
}

[pscustomobject]@{
    Subject = $cert.Subject
    Thumbprint = $cert.Thumbprint
    StorePath = "Cert:\CurrentUser\My\$($cert.Thumbprint)"
    CertificatePath = $certificatePath
    PfxPath = $pfxPath
    TrustedForCurrentUser = [bool]$TrustForCurrentUser
} | ConvertTo-Json -Depth 3
