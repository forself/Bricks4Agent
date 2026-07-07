[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$BasePolicyId,

    [string]$ScanPath = ".",
    [string]$CertificatePath = ".run/code-signing/Bricks4AgentDevCodeSigning.cer",
    [string]$OutputDir = ".run/wdac",
    [string]$PolicyName = "Bricks4Agent Dev Code Signing Supplemental",
    [string]$Version = "1.0.0.0",
    [ValidateSet("Publisher", "FilePublisher", "Hash")]
    [string]$PolicyLevel = "Publisher",
    [string[]]$FallbackLevels = @("Hash"),
    [switch]$AuditMode,
    [switch]$SkipConvert
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

foreach ($commandName in @("New-CIPolicy", "Set-CIPolicyIdInfo", "Set-CIPolicyVersion")) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "$commandName is not available. Install/enable the Windows App Control PowerShell cmdlets on this host."
    }
}

if (-not $SkipConvert -and -not (Get-Command ConvertFrom-CIPolicy -ErrorAction SilentlyContinue)) {
    throw "ConvertFrom-CIPolicy is not available. Use -SkipConvert to produce only XML."
}

function Remove-AuditModeRule {
    param([string]$PolicyXml)

    if (Get-Command Set-RuleOption -ErrorAction SilentlyContinue) {
        Set-RuleOption -FilePath $PolicyXml -Option 3 -Delete
    }
    else {
        [xml]$policyDoc = Get-Content -LiteralPath $PolicyXml
        $namespaceManager = [System.Xml.XmlNamespaceManager]::new($policyDoc.NameTable)
        $namespaceManager.AddNamespace("si", $policyDoc.DocumentElement.NamespaceURI)
        $auditRules = @(
            $policyDoc.SelectNodes(
                "//si:Rules/si:Rule[si:Option='Enabled:Audit Mode']",
                $namespaceManager
            )
        )

        foreach ($rule in $auditRules) {
            [void]$rule.ParentNode.RemoveChild($rule)
        }

        $policyDoc.Save($PolicyXml)
    }

    if (Select-String -LiteralPath $PolicyXml -Pattern "Enabled:Audit Mode" -Quiet) {
        throw "Failed to remove Enabled:Audit Mode from WDAC policy XML: $PolicyXml"
    }
}

$resolvedScanPath = Resolve-RepoPath $ScanPath
$resolvedCertificatePath = Resolve-RepoPath $CertificatePath
$resolvedOutputDir = Resolve-RepoPath $OutputDir

if (-not (Test-Path -LiteralPath $resolvedScanPath)) {
    throw "ScanPath does not exist: $resolvedScanPath"
}

if (-not (Test-Path -LiteralPath $resolvedCertificatePath)) {
    throw "CertificatePath does not exist: $resolvedCertificatePath"
}

New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null

$policyXml = Join-Path $resolvedOutputDir "Bricks4Agent.DevCodeSigning.Supplemental.xml"
$policyLog = Join-Path $resolvedOutputDir "Bricks4Agent.DevCodeSigning.Supplemental.scan.log"

if ($PSCmdlet.ShouldProcess($policyXml, "Create WDAC supplemental policy XML")) {
    $newPolicyArgs = @{
        FilePath = $policyXml
        Level = $PolicyLevel
        ScanPath = $resolvedScanPath
        UserPEs = $true
        MultiplePolicyFormat = $true
    }
    if ($PolicyLevel -ne "Hash" -and $FallbackLevels.Count -gt 0) {
        $newPolicyArgs.Fallback = $FallbackLevels
    }

    New-CIPolicy @newPolicyArgs 3> $policyLog
}

if (Get-Command Add-SignerRule -ErrorAction SilentlyContinue) {
    if ($PSCmdlet.ShouldProcess($policyXml, "Add signer rule for Bricks4Agent dev certificate")) {
        Add-SignerRule -FilePath $policyXml -CertificatePath $resolvedCertificatePath -User
    }
}
else {
    Write-Warning "Add-SignerRule is not available; the policy only contains rules discovered by New-CIPolicy scan."
}

if (-not $AuditMode) {
    if ($PSCmdlet.ShouldProcess($policyXml, "Remove WDAC audit mode rule")) {
        Remove-AuditModeRule -PolicyXml $policyXml
    }
}

if ($PSCmdlet.ShouldProcess($policyXml, "Mark policy as supplemental")) {
    Set-CIPolicyIdInfo `
        -FilePath $policyXml `
        -PolicyName $PolicyName `
        -SupplementsBasePolicyID $BasePolicyId

    Set-CIPolicyVersion -FilePath $policyXml -Version $Version
}

$policyBinary = $null
if (-not $SkipConvert) {
    [xml]$policyDoc = Get-Content -LiteralPath $policyXml
    $policyId = [string]$policyDoc.SiPolicy.PolicyID
    if ([string]::IsNullOrWhiteSpace($policyId)) {
        throw "Policy XML does not contain PolicyID; cannot name .cip output."
    }

    $policyBinary = Join-Path $resolvedOutputDir "$policyId.cip"
    if ($PSCmdlet.ShouldProcess($policyBinary, "Convert WDAC supplemental policy XML to binary")) {
        ConvertFrom-CIPolicy -XmlFilePath $policyXml -BinaryFilePath $policyBinary
    }
}

[pscustomobject]@{
    PolicyName = $PolicyName
    BasePolicyId = $BasePolicyId
    ScanPath = $resolvedScanPath
    CertificatePath = $resolvedCertificatePath
    PolicyXml = $policyXml
    PolicyBinary = $policyBinary
    ScanLog = $policyLog
    PolicyLevel = $PolicyLevel
    FallbackLevels = $FallbackLevels
    AuditMode = [bool]$AuditMode
} | ConvertTo-Json -Depth 3
