[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$CertificateThumbprint = "",
    [string]$CertificateSubject = "CN=Bricks4Agent Dev Code Signing",
    [string]$TimestampServer = "",
    [switch]$Build,
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $scriptDir = Split-Path -Parent $MyInvocation.ScriptName
    return [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\.."))
}

function Get-BricksProjectAssemblyNames {
    param([string]$RepoRoot)

    $roots = @(
        (Join-Path $RepoRoot "packages"),
        (Join-Path $RepoRoot "tools")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -Recurse -Filter *.csproj -File |
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
    }

    return $names
}

function Get-CodeSigningCertificate {
    param(
        [string]$Thumbprint,
        [string]$Subject
    )

    $certificates = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.NotAfter -gt (Get-Date) }

    if (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $normalized = $Thumbprint.Replace(" ", "").ToUpperInvariant()
        $cert = $certificates |
            Where-Object { $_.Thumbprint.ToUpperInvariant() -eq $normalized } |
            Select-Object -First 1
    }
    else {
        $cert = $certificates |
            Where-Object { $_.Subject -eq $Subject } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
    }

    if (-not $cert) {
        throw "Code-signing certificate not found. Run tools/windows-signing/New-BricksDevCodeSigningCert.ps1 first."
    }

    return $cert
}

function Get-BricksAssemblyFiles {
    param(
        [string]$RepoRoot,
        [System.Collections.Generic.HashSet[string]]$AssemblyNames
    )

    Get-ChildItem -LiteralPath $RepoRoot -Recurse -File |
        Where-Object {
            ($_.Extension -eq ".dll" -or $_.Extension -eq ".exe") -and
            $_.FullName -match "\\bin\\" -and
            $_.FullName -notmatch "\\obj\\" -and
            $_.FullName -notmatch "\\node_modules\\" -and
            $_.FullName -notmatch "\\.git\\" -and
            $_.FullName -notmatch "\\.claude\\" -and
            $_.FullName -notmatch "\\.worktrees\\" -and
            $_.FullName -notmatch "\\.run\\" -and
            $AssemblyNames.Contains($_.BaseName)
        } |
        Sort-Object FullName -Unique
}

$repoRoot = Get-RepoRoot

if ($Build) {
    $solution = Join-Path $repoRoot "packages\csharp\ControlPlane.slnx"
    if ($PSCmdlet.ShouldProcess($solution, "dotnet build before signing")) {
        dotnet build $solution
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }
    }
}

$cert = Get-CodeSigningCertificate -Thumbprint $CertificateThumbprint -Subject $CertificateSubject
$assemblyNames = Get-BricksProjectAssemblyNames -RepoRoot $repoRoot
$files = @(Get-BricksAssemblyFiles -RepoRoot $repoRoot -AssemblyNames $assemblyNames)

if ($files.Count -eq 0) {
    throw "No Bricks4Agent build assemblies were found under bin folders. Build the solution first."
}

$results = New-Object System.Collections.Generic.List[object]
foreach ($file in $files) {
    $signature = Get-AuthenticodeSignature -FilePath $file.FullName
    $signedByExpectedCertificate = $signature.SignerCertificate -and
        $signature.SignerCertificate.Thumbprint -eq $cert.Thumbprint
    $alreadySigned = [bool]$signedByExpectedCertificate

    if (-not $VerifyOnly -and -not $alreadySigned) {
        if ($PSCmdlet.ShouldProcess($file.FullName, "Authenticode sign")) {
            $signArgs = @{
                FilePath = $file.FullName
                Certificate = $cert
                HashAlgorithm = "SHA256"
            }
            if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
                $signArgs.TimestampServer = $TimestampServer
            }

            Set-AuthenticodeSignature @signArgs | Out-Null
        }

        $signature = Get-AuthenticodeSignature -FilePath $file.FullName
        $signedByExpectedCertificate = $signature.SignerCertificate -and
            $signature.SignerCertificate.Thumbprint -eq $cert.Thumbprint
    }

    $results.Add([pscustomobject]@{
        Path = $file.FullName.Substring($repoRoot.Length + 1)
        Status = $signature.Status.ToString()
        SignedByExpectedCertificate = [bool]$signedByExpectedCertificate
        AlreadySigned = $alreadySigned
    }) | Out-Null
}

$unsigned = @($results | Where-Object { -not $_.SignedByExpectedCertificate })
if ($unsigned.Count -gt 0) {
    $unsigned | Format-Table -AutoSize | Out-String | Write-Error
    throw "$($unsigned.Count) Bricks4Agent assemblies are not signed by the expected certificate."
}

$results | ConvertTo-Json -Depth 3
