[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$BasePolicyId = "{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}",
    [string]$RuntimeRoot = ".run\line-sidecar",
    [string]$OutputDir = ".run\wdac\line-sidecar-runtime",
    [string]$CertificateSubject = "CN=Bricks4Agent Dev Code Signing",
    [ValidateSet("Publisher", "FilePublisher", "Hash")]
    [string]$PolicyLevel = "Publisher",
    [switch]$Deploy,
    [switch]$SkipRuntimeSigning
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

function Get-CodeSigningCertificate {
    param([string]$Subject)

    Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Subject -eq $Subject -and
            $_.NotAfter -gt (Get-Date)
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

function Ensure-CodeSigningCertificate {
    param([string]$Subject)

    $cert = Get-CodeSigningCertificate -Subject $Subject
    if ($cert) {
        return $cert
    }

    $certScript = Join-Path $script:RepoRoot "tools\windows-signing\New-BricksDevCodeSigningCert.ps1"
    if (-not (Test-Path -LiteralPath $certScript)) {
        throw "Missing certificate script: $certScript"
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File $certScript -TrustForCurrentUser | Out-Null
    $cert = Get-CodeSigningCertificate -Subject $Subject
    if (-not $cert) {
        throw "Code-signing certificate was not created for subject $Subject."
    }

    return $cert
}

function Get-BricksProjectAssemblyNames {
    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot "packages") -Recurse -Filter *.csproj -File |
        Where-Object {
            $_.FullName -notmatch "\\bin\\" -and
            $_.FullName -notmatch "\\obj\\" -and
            $_.FullName -notmatch "\\node_modules\\"
        } |
        ForEach-Object {
            $projectFile = $_
            $assemblyName = $projectFile.BaseName
            try {
                [xml]$projectXml = Get-Content -LiteralPath $projectFile.FullName
                $declaredName = $projectXml.Project.PropertyGroup.AssemblyName |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                    Select-Object -First 1
                if ($declaredName) {
                    $assemblyName = [string]$declaredName
                }
            }
            catch {
                $assemblyName = $projectFile.BaseName
            }

            [void]$names.Add($assemblyName)
        }

    return $names
}

function Sign-RuntimeAssemblies {
    param(
        [string]$ResolvedRuntimeRoot,
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    $assemblyNames = Get-BricksProjectAssemblyNames
    $files = @(
        Get-ChildItem -LiteralPath $ResolvedRuntimeRoot -Recurse -File |
            Where-Object {
                ($_.Extension -eq ".dll" -or $_.Extension -eq ".exe") -and
                $assemblyNames.Contains($_.BaseName)
            } |
            Sort-Object FullName -Unique
    )

    foreach ($file in $files) {
        $signature = Get-AuthenticodeSignature -FilePath $file.FullName
        $signedByExpectedCertificate = $signature.SignerCertificate -and
            $signature.SignerCertificate.Thumbprint -eq $Certificate.Thumbprint

        if (-not $signedByExpectedCertificate) {
            if ($PSCmdlet.ShouldProcess($file.FullName, "Authenticode sign runtime assembly")) {
                Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $Certificate -HashAlgorithm SHA256 | Out-Null
            }
        }
    }

    return $files.Count
}

function Get-CodeIntegrityBlockedFiles {
    Get-WinEvent -LogName "Microsoft-Windows-CodeIntegrity/Operational" -MaxEvents 120 -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match "Bricks4Agent|line-sidecar|0x800711C7|Enterprise signing level" } |
        ForEach-Object {
            $blockedPath = ""
            if ($_.Message -match "attempted to load (?<path>.+?) that did not meet") {
                $blockedPath = $Matches.path
            }

            [pscustomobject]@{
                TimeCreated = $_.TimeCreated
                EventId = $_.Id
                BlockedPath = $blockedPath
                Message = $_.Message
            }
        } |
        Select-Object -First 20
}

$script:RepoRoot = Get-RepoRoot
$resolvedRuntimeRoot = Resolve-RepoPath $RuntimeRoot
$resolvedOutputDir = Resolve-RepoPath $OutputDir
$requiresAdministrator = [bool]$Deploy

if ($Deploy -and -not (Test-IsAdministrator)) {
    throw "Repair with -Deploy RequiresAdministrator. Re-run this script from an elevated PowerShell session."
}

if (-not (Test-Path -LiteralPath $resolvedRuntimeRoot)) {
    throw "RuntimeRoot does not exist: $resolvedRuntimeRoot. Start or publish the sidecar runtime before repairing WDAC trust."
}

$signedRuntimeAssemblyCount = 0
if (-not $SkipRuntimeSigning) {
    $cert = Ensure-CodeSigningCertificate -Subject $CertificateSubject
    $signedRuntimeAssemblyCount = Sign-RuntimeAssemblies -ResolvedRuntimeRoot $resolvedRuntimeRoot -Certificate $cert
}

$policyScript = Join-Path $script:RepoRoot "tools\windows-signing\New-BricksWdacSupplementalPolicy.ps1"
$installScript = Join-Path $script:RepoRoot "tools\windows-signing\Install-BricksWdacPolicy.ps1"

& powershell -NoProfile -ExecutionPolicy Bypass -File $policyScript `
    -BasePolicyId $BasePolicyId `
    -ScanPath $resolvedRuntimeRoot `
    -OutputDir $resolvedOutputDir `
    -PolicyLevel $PolicyLevel | Out-Host

if ($LASTEXITCODE -ne 0) {
    throw "New-BricksWdacSupplementalPolicy.ps1 failed with exit code $LASTEXITCODE"
}

$policyBinary = Get-ChildItem -LiteralPath $resolvedOutputDir -Filter *.cip -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $policyBinary) {
    throw "No WDAC policy binary was generated under $resolvedOutputDir."
}

$installArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $installScript,
    "-PolicyBinary", $policyBinary.FullName
)
if ($Deploy) {
    $installArgs += @("-Deploy", "-VerifyActive")
}

& powershell @installArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Install-BricksWdacPolicy.ps1 failed with exit code $LASTEXITCODE"
}

[pscustomobject]@{
    RuntimeRoot = $resolvedRuntimeRoot
    SignedRuntimeAssemblyCount = $signedRuntimeAssemblyCount
    PolicyBinary = $policyBinary.FullName
    PolicyLevel = $PolicyLevel
    Deploy = [bool]$Deploy
    RequiresAdministrator = $requiresAdministrator
    RecentBlockedFiles = @(Get-CodeIntegrityBlockedFiles)
} | ConvertTo-Json -Depth 5
