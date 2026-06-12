#Requires -Version 5.1
param(
    [string]$ConfigPath = "",
    [string]$LogDirectory = "",
    [string]$LastUrlPath = "",
    [int]$IntervalSeconds = 15,
    [switch]$Once
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..\..\..")
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $scriptDir "appsettings.json"
}
if (-not $LogDirectory) {
    $LogDirectory = Join-Path $repoRoot ".run\line-sidecar\logs"
}
if (-not $LastUrlPath) {
    $LastUrlPath = Join-Path $scriptDir ".last-tunnel-url"
}

function Get-LatestLocalhostRunEndpoint {
    param([string]$Directory)

    $combined = ""
    foreach ($name in @("localhostrun.out.log", "localhostrun.err.log")) {
        $path = Join-Path $Directory $name
        if (Test-Path $path) {
            $combined += "`n" + (Get-Content $path -Raw -ErrorAction SilentlyContinue)
        }
    }

    $matches = [regex]::Matches($combined, "https://[a-zA-Z0-9-]+\.lhr\.life")
    if ($matches.Count -eq 0) {
        return $null
    }

    return $matches[$matches.Count - 1].Value.TrimEnd("/") + "/webhook/line/"
}

function Sync-LineWebhookEndpoint {
    param(
        [string]$Endpoint,
        [string]$ConfigFile,
        [string]$UrlFile
    )

    if ([string]::IsNullOrWhiteSpace($Endpoint)) {
        return $false
    }

    if (-not (Test-Path $ConfigFile)) {
        throw "Missing config: $ConfigFile"
    }

    $config = Get-Content $ConfigFile -Raw | ConvertFrom-Json
    $token = $config.Line.ChannelAccessToken
    if ([string]::IsNullOrWhiteSpace($token) -or $token -like "REPLACE_WITH_*") {
        Write-Warning "Skipping LINE webhook sync because ChannelAccessToken is not configured."
        return $false
    }

    $headers = @{ Authorization = "Bearer $token" }
    $current = $null
    try {
        $currentInfo = Invoke-RestMethod -Method Get -Uri "https://api.line.me/v2/bot/channel/webhook/endpoint" -Headers $headers
        $current = [string]$currentInfo.endpoint
    } catch {
        Write-Warning "Failed to read current LINE webhook endpoint: $($_.Exception.Message)"
    }

    if ($current -ne $Endpoint) {
        $updateHeaders = @{
            Authorization = "Bearer $token"
            "Content-Type" = "application/json"
        }
        $body = @{ endpoint = $Endpoint } | ConvertTo-Json -Compress
        Invoke-RestMethod -Method Put `
            -Uri "https://api.line.me/v2/bot/channel/webhook/endpoint" `
            -Headers $updateHeaders `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) | Out-Null
        Write-Host ("LINE webhook endpoint synced: {0}" -f $Endpoint)
    }

    $Endpoint | Out-File -FilePath $UrlFile -Encoding utf8 -NoNewline
    return $true
}

do {
    try {
        $endpoint = Get-LatestLocalhostRunEndpoint -Directory $LogDirectory
        if ($endpoint) {
            Sync-LineWebhookEndpoint -Endpoint $endpoint -ConfigFile $ConfigPath -UrlFile $LastUrlPath | Out-Null
        } else {
            Write-Warning "No localhost.run endpoint found in $LogDirectory."
        }
    } catch {
        Write-Warning "LINE webhook sync failed: $($_.Exception.Message)"
    }

    if ($Once) {
        break
    }

    Start-Sleep -Seconds ([Math]::Max(5, $IntervalSeconds))
} while ($true)
