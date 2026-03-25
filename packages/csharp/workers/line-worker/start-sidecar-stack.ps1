#Requires -Version 5.1
param(
    [int]$BrokerPort = 5361,
    [int]$WebhookPort = 5357,
    [switch]$SkipBuild,
    [switch]$SkipWebhookUpdate
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..\..\..")
$runRoot = Join-Path $repoRoot ".run\line-sidecar"
$brokerOut = Join-Path $runRoot "broker"
$workerOut = Join-Path $runRoot "line-worker"
$logDir = Join-Path $runRoot "logs"
$brokerLog = Join-Path $logDir "broker.out.log"
$brokerErrLog = Join-Path $logDir "broker.err.log"
$workerLog = Join-Path $logDir "line-worker.out.log"
$workerErrLog = Join-Path $logDir "line-worker.err.log"
$brokerPidFile = Join-Path $runRoot "broker.pid"
$workerPidFile = Join-Path $runRoot "line-worker.pid"
$tunnelName = "line$WebhookPort"
$configPath = Join-Path $scriptDir "appsettings.json"
$lastUrlFile = Join-Path $scriptDir ".last-tunnel-url"
$openAiApiKeyFile = Join-Path $repoRoot "Api.txt"
$googleOAuthClientFile = Get-ChildItem -Path $repoRoot -Filter "client_secret_*.json" -File -ErrorAction SilentlyContinue | Select-Object -First 1
$brokerProductionOverridePath = Join-Path $brokerOut "appsettings.Production.json"

foreach ($dir in @($runRoot, $brokerOut, $workerOut, $logDir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

if (-not (Test-Path $configPath)) {
    throw "Missing config: $configPath"
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$channelAccessToken = $config.Line.ChannelAccessToken

function Stop-RecordedProcess {
    param([string]$PidFile)

    if (-not (Test-Path $PidFile)) {
        return
    }

    $procId = Get-Content $PidFile -Raw
    if ([string]::IsNullOrWhiteSpace($procId)) {
        Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
        return
    }

    try {
        Stop-Process -Id ([int]$procId) -Force -ErrorAction Stop
    } catch {
        Write-Warning ("Failed to stop recorded process {0} from {1}: {2}" -f $procId, $PidFile, $_.Exception.Message)
    }

    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

function Invoke-NgrokApi {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null
    )

    $uri = "http://127.0.0.1:4040$Path"
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri
    }

    return Invoke-RestMethod -Method $Method -Uri $uri -ContentType "application/json" -Body ($Body | ConvertTo-Json -Compress)
}

function Update-LineWebhook {
    param([string]$PublicUrl)

    if ([string]::IsNullOrWhiteSpace($channelAccessToken) -or $channelAccessToken -like "REPLACE_WITH_*") {
        Write-Warning "Skipping LINE webhook update because ChannelAccessToken is not configured."
        return
    }

    $endpoint = if ($PublicUrl -match "/webhook/line/?$") { $PublicUrl } else { "$PublicUrl/webhook/line/" }
    $headers = @{
        Authorization = "Bearer $channelAccessToken"
        "Content-Type" = "application/json"
    }

    Invoke-RestMethod -Method Put `
        -Uri "https://api.line.me/v2/bot/channel/webhook/endpoint" `
        -Headers $headers `
        -Body ([System.Text.Encoding]::UTF8.GetBytes("{`"endpoint`":`"$endpoint`"}")) | Out-Null

    $endpoint | Out-File -FilePath $lastUrlFile -Encoding utf8 -NoNewline
    Write-Host "LINE webhook endpoint updated: $endpoint" -ForegroundColor Green
}

function Remove-NgrokTunnelIfPresent {
    param([string]$Name)

    try {
        Invoke-NgrokApi -Method Delete -Path "/api/tunnels/$Name" | Out-Null
    } catch {
        Write-Host "ngrok tunnel not present: $Name" -ForegroundColor Yellow
    }
}

Stop-RecordedProcess -PidFile $brokerPidFile
Stop-RecordedProcess -PidFile $workerPidFile

if (-not $SkipBuild) {
    Write-Host "Publishing broker..." -ForegroundColor Yellow
    dotnet publish (Join-Path $repoRoot "packages\csharp\broker\Broker.csproj") `
        -c Release `
        -o $brokerOut `
        --disable-build-servers `
        --nologo -v q

    Write-Host "Publishing line-worker..." -ForegroundColor Yellow
    dotnet publish (Join-Path $scriptDir "LineWorker.csproj") `
        -c Release `
        -o $workerOut `
        --disable-build-servers `
        --nologo -v q
}

foreach ($logPath in @($brokerLog, $brokerErrLog, $workerLog, $workerErrLog)) {
    if (Test-Path $logPath) { Remove-Item $logPath -Force }
}

$env:ASPNETCORE_URLS = "http://127.0.0.1:$BrokerPort"
if (Test-Path $brokerProductionOverridePath) {
    Remove-Item $brokerProductionOverridePath -Force
}
if (Test-Path $openAiApiKeyFile) {
    $openAiApiKey = (Get-Content -Encoding utf8 $openAiApiKeyFile -Raw).Trim()
}
$productionOverrideMap = @{}
if (-not [string]::IsNullOrWhiteSpace($openAiApiKey)) {
    $productionOverrideMap["HighLevelLlm"] = @{
        ApiKey = $openAiApiKey
    }
}
if ($null -ne $googleOAuthClientFile) {
    $googleDriveConfig = @{
        OAuthClientJsonPath = $googleOAuthClientFile.FullName
        DelegatedRedirectUri = "http://localhost:$BrokerPort/api/v1/google-drive/oauth/callback"
    }
    if (-not [string]::IsNullOrWhiteSpace($env:B4A_GOOGLE_DRIVE_FOLDER_ID)) {
        $googleDriveConfig["DefaultFolderId"] = $env:B4A_GOOGLE_DRIVE_FOLDER_ID.Trim()
    }
    $productionOverrideMap["GoogleDriveDelivery"] = $googleDriveConfig
}
if ($productionOverrideMap.Count -gt 0) {
    $productionOverride = $productionOverrideMap | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($brokerProductionOverridePath, $productionOverride, [System.Text.UTF8Encoding]::new($false))
}
$brokerProc = Start-Process `
    -FilePath (Join-Path $brokerOut "Broker.exe") `
    -WorkingDirectory $brokerOut `
    -RedirectStandardOutput $brokerLog `
    -RedirectStandardError $brokerErrLog `
    -PassThru
Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
$brokerProc.Id | Out-File -FilePath $brokerPidFile -Encoding ascii -NoNewline

Start-Sleep -Seconds 3

$env:WORKER_Broker__ApiUrl = "http://localhost:$BrokerPort"
$env:WORKER_Line__WebhookPort = "$WebhookPort"
$env:WORKER_Line__WebhookHost = "*"
$workerProc = Start-Process `
    -FilePath (Join-Path $workerOut "LineWorker.exe") `
    -WorkingDirectory $workerOut `
    -RedirectStandardOutput $workerLog `
    -RedirectStandardError $workerErrLog `
    -PassThru
Remove-Item Env:WORKER_Broker__ApiUrl -ErrorAction SilentlyContinue
Remove-Item Env:WORKER_Line__WebhookPort -ErrorAction SilentlyContinue
Remove-Item Env:WORKER_Line__WebhookHost -ErrorAction SilentlyContinue
$workerProc.Id | Out-File -FilePath $workerPidFile -Encoding ascii -NoNewline

Start-Sleep -Seconds 3

Remove-NgrokTunnelIfPresent -Name $tunnelName
$tunnel = Invoke-NgrokApi -Method Post -Path "/api/tunnels" -Body @{
    name = $tunnelName
    addr = "http://localhost:$WebhookPort"
    proto = "http"
    inspect = $true
}

$publicUrl = $tunnel.public_url
if (-not $SkipWebhookUpdate) {
    Update-LineWebhook -PublicUrl $publicUrl
}

Write-Host ""
Write-Host "Broker PID:   $($brokerProc.Id)" -ForegroundColor Cyan
Write-Host "Worker PID:   $($workerProc.Id)" -ForegroundColor Cyan
Write-Host "Broker URL:   http://localhost:$BrokerPort" -ForegroundColor Cyan
Write-Host "Webhook URL:  $publicUrl/webhook/line/" -ForegroundColor Cyan
Write-Host "Broker logs:  $brokerLog | $brokerErrLog" -ForegroundColor Cyan
Write-Host "Worker logs:  $workerLog | $workerErrLog" -ForegroundColor Cyan
