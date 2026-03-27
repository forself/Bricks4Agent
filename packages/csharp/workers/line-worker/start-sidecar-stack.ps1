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
$ngrokPidFile = Join-Path $runRoot "ngrok.pid"
$tunnelName = "line$WebhookPort"
$configPath = Join-Path $scriptDir "appsettings.json"
$lastUrlFile = Join-Path $scriptDir ".last-tunnel-url"
$openAiApiKeyFile = Join-Path $repoRoot "Api.txt"
$googleOAuthClientFile = Get-ChildItem -Path $repoRoot -Filter "client_secret_*.json" -File -ErrorAction SilentlyContinue | Select-Object -First 1
$brokerProductionOverridePath = Join-Path $brokerOut "appsettings.Production.json"
$ngrokConfigPath = Join-Path $env:LOCALAPPDATA "ngrok\ngrok.yml"
$ngrokOutLog = Join-Path $logDir "ngrok.out.log"
$ngrokErrLog = Join-Path $logDir "ngrok.err.log"

foreach ($dir in @($runRoot, $brokerOut, $workerOut, $logDir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

function Clear-PublishOutputDirectory {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
        return
    }

    Get-ChildItem -Force -LiteralPath $Path | ForEach-Object {
        try {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
        } catch {
            throw "Failed to clear $Label output path '$Path' because '$($_.FullName)' is still locked or inaccessible. $_"
        }
    }
}

if (-not (Test-Path $configPath)) {
    throw "Missing config: $configPath"
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$channelAccessToken = $config.Line.ChannelAccessToken
$googleDriveSettings = $null
if ($config.PSObject.Properties.Name -contains "GoogleDriveDelivery") {
    $googleDriveSettings = $config.GoogleDriveDelivery
}

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

function Stop-ProcessByExecutablePath {
    param(
        [string]$ExecutablePath,
        [string]$Label
    )

    $normalized = [System.IO.Path]::GetFullPath($ExecutablePath)
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq $normalized)
        } catch {
            $false
        }
    } | ForEach-Object {
        try {
            Stop-Process -Id $_.Id -Force -ErrorAction Stop
            $deadline = (Get-Date).AddSeconds(20)
            do {
                Start-Sleep -Milliseconds 250
                $stillRunning = Get-Process -Id $_.Id -ErrorAction SilentlyContinue
            } while ($stillRunning -and (Get-Date) -lt $deadline)
            Write-Host "$Label stopped by executable path: PID $($_.Id)" -ForegroundColor Green
        } catch {
            Write-Warning ("Failed to stop {0} PID {1} by executable path: {2}" -f $Label, $_.Id, $_.Exception.Message)
        }
    }
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

function Test-NgrokAdminApi {
    try {
        Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:4040/api/tunnels" | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Wait-HttpStatusCode {
    param(
        [string]$Uri,
        [int[]]$AcceptStatusCodes,
        [int]$TimeoutSeconds,
        [string]$Label,
        [string]$Method = "GET",
        [string]$Body = ""
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            if ($Method -eq "POST") {
                $response = Invoke-WebRequest -Uri $Uri -Method Post -Body $Body -ContentType "application/json" -UseBasicParsing -TimeoutSec 5
            } else {
                $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            }
            if ($AcceptStatusCodes -contains [int]$response.StatusCode) {
                return
            }
        } catch {
            if ($_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode.value__
                if ($AcceptStatusCodes -contains $statusCode) {
                    return
                }
            }
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "$Label did not become ready within $TimeoutSeconds seconds at $Uri."
}

function Wait-NgrokTunnelReady {
    param(
        [string]$Name,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $tunnel = Invoke-NgrokApi -Method Get -Path "/api/tunnels/$Name"
            if ($tunnel -and $tunnel.public_url) {
                return $tunnel
            }
        } catch {
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "ngrok tunnel '$Name' did not become available within $TimeoutSeconds seconds."
}

function Start-NgrokAgentIfNeeded {
    if (Test-NgrokAdminApi) {
        return
    }

    $ngrokCommand = Get-Command ngrok -ErrorAction SilentlyContinue
    if (-not $ngrokCommand) {
        throw "ngrok is not installed or not available on PATH. The sidecar requires ngrok because it creates tunnels through the local ngrok admin API on 127.0.0.1:4040."
    }

    if (-not (Test-Path $ngrokConfigPath)) {
        throw "Missing ngrok config: $ngrokConfigPath. Run 'ngrok config add-authtoken <token>' or create a working ngrok config before starting the sidecar."
    }

    foreach ($logPath in @($ngrokOutLog, $ngrokErrLog)) {
        if (Test-Path $logPath) { Remove-Item $logPath -Force }
    }

    $ngrokProc = Start-Process `
        -FilePath $ngrokCommand.Source `
        -ArgumentList @("start", "--none", "--config", $ngrokConfigPath) `
        -RedirectStandardOutput $ngrokOutLog `
        -RedirectStandardError $ngrokErrLog `
        -PassThru
    $ngrokProc.Id | Out-File -FilePath $ngrokPidFile -Encoding ascii -NoNewline

    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        if (Test-NgrokAdminApi) {
            return
        }
    } while ((Get-Date) -lt $deadline)

    throw "ngrok started but the admin API on 127.0.0.1:4040 did not become available within 15 seconds. Check $ngrokOutLog and $ngrokErrLog."
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
Stop-ProcessByExecutablePath -ExecutablePath (Join-Path $brokerOut "Broker.exe") -Label "broker"
Stop-ProcessByExecutablePath -ExecutablePath (Join-Path $workerOut "LineWorker.exe") -Label "line-worker"

if (-not $SkipBuild) {
    Clear-PublishOutputDirectory -Path $brokerOut -Label "broker"
    Clear-PublishOutputDirectory -Path $workerOut -Label "line-worker"

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

Start-NgrokAgentIfNeeded

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
        DelegatedRedirectUri = "http://127.0.0.1:$BrokerPort/api/v1/google-drive/oauth/callback"
    }
    $sharedDriveOwnerUserId = if (-not [string]::IsNullOrWhiteSpace($env:B4A_GOOGLE_DRIVE_SHARED_USER_ID)) {
        $env:B4A_GOOGLE_DRIVE_SHARED_USER_ID.Trim()
    } elseif (-not [string]::IsNullOrWhiteSpace($config.Line.DefaultRecipientId)) {
        $config.Line.DefaultRecipientId.Trim()
    } else {
        ""
    }
    $defaultIdentityMode = if (-not [string]::IsNullOrWhiteSpace($env:B4A_GOOGLE_DRIVE_IDENTITY_MODE)) {
        $env:B4A_GOOGLE_DRIVE_IDENTITY_MODE.Trim()
    } elseif (-not [string]::IsNullOrWhiteSpace($sharedDriveOwnerUserId)) {
        "shared_delegated"
    } else {
        "user_delegated"
    }
    $googleDriveConfig["DefaultIdentityMode"] = $defaultIdentityMode
    $googleDriveConfig["SharedDelegatedChannel"] = if (-not [string]::IsNullOrWhiteSpace($env:B4A_GOOGLE_DRIVE_SHARED_CHANNEL)) {
        $env:B4A_GOOGLE_DRIVE_SHARED_CHANNEL.Trim()
    } else {
        "line"
    }
    if (-not [string]::IsNullOrWhiteSpace($sharedDriveOwnerUserId)) {
        $googleDriveConfig["SharedDelegatedUserId"] = $sharedDriveOwnerUserId
    }
    $defaultFolderId = if (-not [string]::IsNullOrWhiteSpace($env:B4A_GOOGLE_DRIVE_FOLDER_ID)) {
        $env:B4A_GOOGLE_DRIVE_FOLDER_ID.Trim()
    } elseif ($googleDriveSettings -and -not [string]::IsNullOrWhiteSpace($googleDriveSettings.DefaultFolderId)) {
        $googleDriveSettings.DefaultFolderId.Trim()
    } else {
        $null
    }
    if (-not [string]::IsNullOrWhiteSpace($defaultFolderId)) {
        $googleDriveConfig["DefaultFolderId"] = $defaultFolderId
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

Wait-HttpStatusCode -Uri "http://127.0.0.1:$BrokerPort/api/v1/local-admin/status" -AcceptStatusCodes @(200) -TimeoutSeconds 60 -Label "Broker"

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

Wait-HttpStatusCode -Uri "http://127.0.0.1:$WebhookPort/webhook/line/" -AcceptStatusCodes @(400, 401, 403, 405) -TimeoutSeconds 60 -Label "LINE worker webhook" -Method "POST" -Body "{}"

Remove-NgrokTunnelIfPresent -Name $tunnelName
$tunnel = Invoke-NgrokApi -Method Post -Path "/api/tunnels" -Body @{
    name = $tunnelName
    addr = "http://localhost:$WebhookPort"
    proto = "http"
    inspect = $true
}
$tunnel = Wait-NgrokTunnelReady -Name $tunnelName -TimeoutSeconds 10

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
