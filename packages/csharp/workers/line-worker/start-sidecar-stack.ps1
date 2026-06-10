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
$dataRoot = Join-Path $runRoot "data"
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
$cloudflaredPidFile = Join-Path $runRoot "cloudflared.pid"
$localhostRunPidFile = Join-Path $runRoot "localhostrun.pid"
$webhookSyncPidFile = Join-Path $runRoot "webhook-sync.pid"
$tunnelName = "line$WebhookPort"
$configPath = Join-Path $scriptDir "appsettings.json"
$brokerSourceConfigPath = Join-Path $repoRoot "packages\csharp\broker\appsettings.json"
$lastUrlFile = Join-Path $scriptDir ".last-tunnel-url"
$secureSecretsRoot = if ($env:BRICKS4AGENT_SECRETS_DIR) { $env:BRICKS4AGENT_SECRETS_DIR } else { "C:\secure\Bricks4Agent" }
$openAiApiKeyFile = Join-Path $secureSecretsRoot "Api.txt"
if (-not (Test-Path $openAiApiKeyFile)) {
    $openAiApiKeyFile = Join-Path $repoRoot "Api.txt"
}
$googleOAuthClientFile = Get-ChildItem -Path @($secureSecretsRoot, $repoRoot) -Filter "client_secret_*.json" -File -ErrorAction SilentlyContinue | Select-Object -First 1
$brokerProductionOverridePath = Join-Path $brokerOut "appsettings.Production.json"
$workerRuntimeConfigPath = Join-Path $workerOut "appsettings.json"
$brokerRuntimeDbPath = Join-Path $dataRoot "broker.db"
$ngrokConfigPath = Join-Path $env:LOCALAPPDATA "ngrok\ngrok.yml"
$ngrokOutLog = Join-Path $logDir "ngrok.out.log"
$ngrokErrLog = Join-Path $logDir "ngrok.err.log"
$cloudflaredOutLog = Join-Path $logDir "cloudflared.out.log"
$cloudflaredErrLog = Join-Path $logDir "cloudflared.err.log"
$localhostRunOutLog = Join-Path $logDir "localhostrun.out.log"
$localhostRunErrLog = Join-Path $logDir "localhostrun.err.log"
$webhookSyncOutLog = Join-Path $logDir "webhook-sync.out.log"
$webhookSyncErrLog = Join-Path $logDir "webhook-sync.err.log"
$projectInterviewTemplateCatalogPath = Join-Path $repoRoot "packages\javascript\browser\templates\catalog.json"
$managedWorkspaceRoot = Join-Path $env:LOCALAPPDATA "Bricks4Agent\managed-workspaces"

foreach ($dir in @($runRoot, $dataRoot, $brokerOut, $workerOut, $logDir, $managedWorkspaceRoot)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCommand) {
    $userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $userDotnet) {
        $dotnetCommand = Get-Item $userDotnet
    }
}
if (-not $dotnetCommand) {
    throw "dotnet is not available on PATH or at $env:USERPROFILE\.dotnet\dotnet.exe."
}
$dotnetPath = if ($dotnetCommand.Source) { $dotnetCommand.Source } else { $dotnetCommand.FullName }
$dotnetRoot = Split-Path -Parent $dotnetPath
$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"

function Initialize-BrokerRuntimeDatabase {
    param(
        [string]$LegacyBrokerOutputPath,
        [string]$RuntimeDbPath
    )

    $runtimeDir = Split-Path -Parent $RuntimeDbPath
    New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

    if (Test-Path $RuntimeDbPath) {
        return
    }

    $legacyDbPath = Join-Path $LegacyBrokerOutputPath "broker.db"
    if (-not (Test-Path $legacyDbPath)) {
        return
    }

    Copy-Item -LiteralPath $legacyDbPath -Destination $RuntimeDbPath -Force
    foreach ($suffix in @("-wal", "-shm")) {
        $legacySidecarPath = "$legacyDbPath$suffix"
        if (Test-Path $legacySidecarPath) {
            Copy-Item -LiteralPath $legacySidecarPath -Destination "$RuntimeDbPath$suffix" -Force
        }
    }
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
$brokerSourceConfigRaw = ""
if (Test-Path $brokerSourceConfigPath) {
    $brokerSourceConfigRaw = Get-Content $brokerSourceConfigPath -Raw
}
$channelAccessToken = $config.Line.ChannelAccessToken
$googleDriveSettings = $null
if ($config.PSObject.Properties.Name -contains "GoogleDriveDelivery") {
    $googleDriveSettings = $config.GoogleDriveDelivery
}
$lineWorkerKeyId = if (-not [string]::IsNullOrWhiteSpace($env:B4A_LINE_WORKER_KEY_ID)) {
    $env:B4A_LINE_WORKER_KEY_ID.Trim()
} else {
    "sidecar-line-worker"
}
$lineWorkerSharedSecret = if (-not [string]::IsNullOrWhiteSpace($env:B4A_LINE_WORKER_SHARED_SECRET)) {
    $env:B4A_LINE_WORKER_SHARED_SECRET.Trim()
} else {
    $secretBytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($secretBytes)
    } finally {
        $rng.Dispose()
    }
    [Convert]::ToBase64String($secretBytes)
}
$artifactDownloadSigningSecret = if (-not [string]::IsNullOrWhiteSpace($env:B4A_ARTIFACT_DOWNLOAD_SIGNING_SECRET)) {
    $env:B4A_ARTIFACT_DOWNLOAD_SIGNING_SECRET.Trim()
} else {
    $secretBytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($secretBytes)
    } finally {
        $rng.Dispose()
    }
    [Convert]::ToBase64String($secretBytes)
}

function Get-BrokerJsonSectionValue {
    param(
        [string]$RawJson,
        [string]$Section,
        [string]$Name,
        [object]$DefaultValue
    )

    if ([string]::IsNullOrWhiteSpace($RawJson)) {
        return $DefaultValue
    }

    $sectionPattern = '"' + [regex]::Escape($Section) + '"\s*:\s*\{(?<body>[\s\S]*?)\n\s*\}'
    $sectionMatch = [regex]::Match($RawJson, $sectionPattern)
    if (-not $sectionMatch.Success) {
        return $DefaultValue
    }

    $body = $sectionMatch.Groups["body"].Value
    $namePattern = [regex]::Escape($Name)
    $stringMatch = [regex]::Match($body, '"' + $namePattern + '"\s*:\s*"(?<value>[^"]*)"')
    if ($stringMatch.Success) {
        return $stringMatch.Groups["value"].Value
    }

    $boolMatch = [regex]::Match($body, '"' + $namePattern + '"\s*:\s*(?<value>true|false)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($boolMatch.Success) {
        return [bool]::Parse($boolMatch.Groups["value"].Value)
    }

    $intMatch = [regex]::Match($body, '"' + $namePattern + '"\s*:\s*(?<value>\d+)')
    if ($intMatch.Success) {
        return [int]::Parse($intMatch.Groups["value"].Value)
    }

    return $DefaultValue
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

function Start-CloudflaredTunnel {
    $cloudflaredCommand = Get-Command cloudflared -ErrorAction SilentlyContinue
    if (-not $cloudflaredCommand) {
        throw "cloudflared is not installed or not available on PATH. Install Cloudflare.cloudflared or configure ngrok."
    }

    foreach ($logPath in @($cloudflaredOutLog, $cloudflaredErrLog)) {
        if (Test-Path $logPath) { Remove-Item $logPath -Force }
    }

    $cloudflaredProc = Start-Process `
        -FilePath $cloudflaredCommand.Source `
        -ArgumentList @("tunnel", "--url", "http://localhost:$WebhookPort", "--no-autoupdate") `
        -RedirectStandardOutput $cloudflaredOutLog `
        -RedirectStandardError $cloudflaredErrLog `
        -WindowStyle Hidden `
        -PassThru
    $cloudflaredProc.Id | Out-File -FilePath $cloudflaredPidFile -Encoding ascii -NoNewline

    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $combined = ""
        foreach ($logPath in @($cloudflaredOutLog, $cloudflaredErrLog)) {
            if (Test-Path $logPath) {
                $combined += "`n" + (Get-Content $logPath -Raw -ErrorAction SilentlyContinue)
            }
        }

        $match = [regex]::Match($combined, "https://[a-zA-Z0-9-]+\.trycloudflare\.com")
        if ($match.Success) {
            return $match.Value.TrimEnd("/")
        }
    } while ((Get-Date) -lt $deadline)

    throw "cloudflared did not produce a public URL within 45 seconds. Check $cloudflaredOutLog and $cloudflaredErrLog."
}

function Start-LocalhostRunTunnel {
    $sshCommand = Get-Command ssh -ErrorAction SilentlyContinue
    if (-not $sshCommand) {
        throw "ssh is not available on PATH. Install OpenSSH Client, configure ngrok, or install cloudflared."
    }

    foreach ($logPath in @($localhostRunOutLog, $localhostRunErrLog)) {
        if (Test-Path $logPath) { Remove-Item $logPath -Force }
    }

    $sshProc = Start-Process `
        -FilePath $sshCommand.Source `
        -ArgumentList @("-o", "StrictHostKeyChecking=no", "-o", "ServerAliveInterval=30", "-R", "80:localhost:$WebhookPort", "nokey@localhost.run") `
        -RedirectStandardOutput $localhostRunOutLog `
        -RedirectStandardError $localhostRunErrLog `
        -WindowStyle Hidden `
        -PassThru
    $sshProc.Id | Out-File -FilePath $localhostRunPidFile -Encoding ascii -NoNewline

    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $combined = ""
        foreach ($logPath in @($localhostRunOutLog, $localhostRunErrLog)) {
            if (Test-Path $logPath) {
                $combined += "`n" + (Get-Content $logPath -Raw -ErrorAction SilentlyContinue)
            }
        }

        $match = [regex]::Match($combined, "https://[a-zA-Z0-9-]+\.lhr\.life")
        if ($match.Success) {
            return $match.Value.TrimEnd("/")
        }
    } while ((Get-Date) -lt $deadline)

    throw "localhost.run did not produce a public URL within 45 seconds. Check $localhostRunOutLog and $localhostRunErrLog."
}

function Start-LocalhostRunWebhookSync {
    param(
        [string]$RuntimeConfigPath,
        [string]$LogsPath,
        [string]$UrlPath
    )

    $syncScript = Join-Path $scriptDir "sync-localhostrun-webhook.ps1"
    if (-not (Test-Path $syncScript)) {
        Write-Warning "Skipping LINE webhook sync watchdog because script is missing: $syncScript"
        return
    }

    foreach ($logPath in @($webhookSyncOutLog, $webhookSyncErrLog)) {
        if (Test-Path $logPath) { Remove-Item $logPath -Force }
    }

    $syncProc = Start-Process `
        -FilePath "powershell" `
        -ArgumentList @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", $syncScript,
            "-ConfigPath", $RuntimeConfigPath,
            "-LogDirectory", $LogsPath,
            "-LastUrlPath", $UrlPath,
            "-IntervalSeconds", "15"
        ) `
        -RedirectStandardOutput $webhookSyncOutLog `
        -RedirectStandardError $webhookSyncErrLog `
        -WindowStyle Hidden `
        -PassThru

    $syncProc.Id | Out-File -FilePath $webhookSyncPidFile -Encoding ascii -NoNewline
    Write-Host "LINE webhook sync watchdog started: PID $($syncProc.Id)" -ForegroundColor Green
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
Stop-RecordedProcess -PidFile $cloudflaredPidFile
Stop-RecordedProcess -PidFile $localhostRunPidFile
Stop-RecordedProcess -PidFile $webhookSyncPidFile
Initialize-BrokerRuntimeDatabase -LegacyBrokerOutputPath $brokerOut -RuntimeDbPath $brokerRuntimeDbPath

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

$ngrokAvailable = $true
try {
    Start-NgrokAgentIfNeeded
} catch {
    $ngrokAvailable = $false
    Write-Warning ("ngrok is not available/configured; falling back to cloudflared quick tunnel. {0}" -f $_.Exception.Message)
}

$env:ASPNETCORE_URLS = "http://127.0.0.1:$BrokerPort"
if (Test-Path $brokerProductionOverridePath) {
    Remove-Item $brokerProductionOverridePath -Force
}
if (Test-Path $openAiApiKeyFile) {
    $openAiApiKey = (Get-Content -Encoding utf8 $openAiApiKeyFile -Raw).Trim()
}
$productionOverrideMap = @{
    Database = @{
        Path = $brokerRuntimeDbPath
    }
}
if (-not [string]::IsNullOrWhiteSpace($openAiApiKey)) {
    $productionOverrideMap["HighLevelLlm"] = @{
        ApiKey = $openAiApiKey
    }
    $productionOverrideMap["LlmProxy"] = @{
        Enabled = $true
        Provider = Get-BrokerJsonSectionValue -RawJson $brokerSourceConfigRaw -Section "HighLevelLlm" -Name "Provider" -DefaultValue "openai-compatible"
        BaseUrl = Get-BrokerJsonSectionValue -RawJson $brokerSourceConfigRaw -Section "HighLevelLlm" -Name "BaseUrl" -DefaultValue "https://api.openai.com"
        ApiKey = $openAiApiKey
        ApiFormat = Get-BrokerJsonSectionValue -RawJson $brokerSourceConfigRaw -Section "HighLevelLlm" -Name "ApiFormat" -DefaultValue "responses"
        DefaultModel = Get-BrokerJsonSectionValue -RawJson $brokerSourceConfigRaw -Section "HighLevelLlm" -Name "DefaultModel" -DefaultValue "gpt-5.4-mini"
        AllowModelOverride = Get-BrokerJsonSectionValue -RawJson $brokerSourceConfigRaw -Section "HighLevelLlm" -Name "AllowModelOverride" -DefaultValue $false
        SupportsToolCalling = $true
        StreamingEnabled = Get-BrokerJsonSectionValue -RawJson $brokerSourceConfigRaw -Section "HighLevelLlm" -Name "StreamingEnabled" -DefaultValue $false
        TimeoutSeconds = Get-BrokerJsonSectionValue -RawJson $brokerSourceConfigRaw -Section "HighLevelLlm" -Name "TimeoutSeconds" -DefaultValue 120
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
$productionOverrideMap["ProjectInterview"] = @{
    TemplateCatalogPath = $projectInterviewTemplateCatalogPath
}
$productionOverrideMap["ArtifactDownload"] = @{
    SigningSecret = $artifactDownloadSigningSecret
    LinkTtlMinutes = 60
    AllowRepeatedDownloads = $true
    SidecarLastTunnelUrlPath = $lastUrlFile
}
$containerRuntime = if (-not [string]::IsNullOrWhiteSpace($env:B4A_CONTAINER_RUNTIME)) {
    $env:B4A_CONTAINER_RUNTIME.Trim()
} elseif (Get-Command podman -ErrorAction SilentlyContinue) {
    "podman"
} else {
    "docker"
}
$containerManagerEnabled = if (-not [string]::IsNullOrWhiteSpace($env:B4A_CONTAINER_MANAGER_ENABLED)) {
    [System.Convert]::ToBoolean($env:B4A_CONTAINER_MANAGER_ENABLED)
} else {
    $null -ne (Get-Command $containerRuntime -ErrorAction SilentlyContinue)
}
$agentBrokerUrl = if (-not [string]::IsNullOrWhiteSpace($env:B4A_AGENT_BROKER_URL)) {
    $env:B4A_AGENT_BROKER_URL.Trim()
} else {
    "http://host.containers.internal:$BrokerPort"
}
$productionOverrideMap["FunctionPool"] = @{
    Enabled = $true
    StrictMode = $false
    ListenPort = 7000
    BindAddress = "127.0.0.1"
    DispatchTimeoutSeconds = 30
    MaxRetries = 2
    HeartbeatTimeoutSeconds = 30
    HealthCheckIntervalSeconds = 10
    MaxWorkers = 32
    ContainerManager = @{
        Enabled = $containerManagerEnabled
        Runtime = $containerRuntime
        NetworkName = ""
        AgentBrokerUrl = $agentBrokerUrl
        MaxContainersPerType = 3
        WorkerImages = @{
            agent = @{
                Image = "bricks4agent-agent:latest"
                MemoryLimit = "512m"
                Volumes = @("$managedWorkspaceRoot`:/workspace")
            }
        }
    }
}
$productionOverrideMap["WorkerAuth"] = @{
    Enforce = $true
    ClockSkewSeconds = 300
    Credentials = @(
        @{
            WorkerType = "line-worker"
            KeyId = $lineWorkerKeyId
            SharedSecret = $lineWorkerSharedSecret
            Status = "active"
        }
    )
    HttpRoutes = @(
        @{
            WorkerType = "line-worker"
            Paths = @(
                "/api/v1/high-level/line/process",
                "/api/v1/high-level/line/notifications/pending",
                "/api/v1/high-level/line/notifications/complete"
            )
        }
    )
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

$workerRuntimeConfig = @{
    Worker = @{
        BrokerHost = "localhost"
        BrokerPort = 7000
        MaxConcurrent = 4
        HeartbeatIntervalSeconds = 5
        Auth = @{
            WorkerType = "line-worker"
            KeyId = $lineWorkerKeyId
            SharedSecret = $lineWorkerSharedSecret
        }
    }
    Broker = @{
        ApiUrl = "http://localhost:$BrokerPort"
        WorkerAuth = @{
            WorkerType = "line-worker"
            KeyId = $lineWorkerKeyId
            SharedSecret = $lineWorkerSharedSecret
        }
    }
    Line = @{
        ChannelAccessToken = $config.Line.ChannelAccessToken
        ChannelSecret = $config.Line.ChannelSecret
        DefaultRecipientId = $config.Line.DefaultRecipientId
        AllowedUserIds = $config.Line.AllowedUserIds
        WebhookHost = "*"
        WebhookPort = $WebhookPort
        TtsProvider = if ($config.Line.PSObject.Properties.Name -contains "TtsProvider") { $config.Line.TtsProvider } else { "none" }
        SttProvider = if ($config.Line.PSObject.Properties.Name -contains "SttProvider") { $config.Line.SttProvider } else { "none" }
        AudioTempPath = if ($config.Line.PSObject.Properties.Name -contains "AudioTempPath") { $config.Line.AudioTempPath } else { "./audio_temp" }
    }
}
if ($googleDriveSettings) {
    $workerRuntimeConfig["GoogleDriveDelivery"] = @{
        DefaultFolderId = $googleDriveSettings.DefaultFolderId
    }
}
$workerRuntimeJson = $workerRuntimeConfig | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($workerRuntimeConfigPath, $workerRuntimeJson, [System.Text.UTF8Encoding]::new($false))

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

$tunnelProvider = "ngrok"
if ($ngrokAvailable) {
    Remove-NgrokTunnelIfPresent -Name $tunnelName
    $tunnel = Invoke-NgrokApi -Method Post -Path "/api/tunnels" -Body @{
        name = $tunnelName
        addr = "http://localhost:$WebhookPort"
        proto = "http"
        inspect = $true
    }
    $tunnel = Wait-NgrokTunnelReady -Name $tunnelName -TimeoutSeconds 10
    $publicUrl = $tunnel.public_url
} else {
    $tunnelProvider = "localhost.run"
    $publicUrl = Start-LocalhostRunTunnel
}

$webhookUpdated = $false
if (-not $SkipWebhookUpdate -and -not $ngrokAvailable) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Update-LineWebhook -PublicUrl $publicUrl
            $webhookUpdated = $true
            break
        } catch {
            if ($attempt -ge 5) {
                throw
            }

            Write-Warning ("Cloudflared URL was rejected by LINE or not ready yet; retrying with a new quick tunnel. Attempt {0}/5. {1}" -f $attempt, $_.Exception.Message)
            Stop-RecordedProcess -PidFile $cloudflaredPidFile
            Stop-RecordedProcess -PidFile $localhostRunPidFile
            if ($tunnelProvider -eq "localhost.run") {
                $publicUrl = Start-LocalhostRunTunnel
            } else {
                $publicUrl = Start-CloudflaredTunnel
            }
        }
    }
}

if (-not $SkipWebhookUpdate -and -not $webhookUpdated) {
    Update-LineWebhook -PublicUrl $publicUrl
} else {
    "$publicUrl/webhook/line/" | Out-File -FilePath $lastUrlFile -Encoding utf8 -NoNewline
}

if (-not $SkipWebhookUpdate -and $tunnelProvider -eq "localhost.run") {
    Start-LocalhostRunWebhookSync -RuntimeConfigPath $workerRuntimeConfigPath -LogsPath $logDir -UrlPath $lastUrlFile
}

Write-Host ""
Write-Host "Broker PID:   $($brokerProc.Id)" -ForegroundColor Cyan
Write-Host "Worker PID:   $($workerProc.Id)" -ForegroundColor Cyan
Write-Host "Broker URL:   http://localhost:$BrokerPort" -ForegroundColor Cyan
Write-Host "Tunnel:       $tunnelProvider" -ForegroundColor Cyan
Write-Host "Webhook URL:  $publicUrl/webhook/line/" -ForegroundColor Cyan
Write-Host "Broker logs:  $brokerLog | $brokerErrLog" -ForegroundColor Cyan
Write-Host "Worker logs:  $workerLog | $workerErrLog" -ForegroundColor Cyan
