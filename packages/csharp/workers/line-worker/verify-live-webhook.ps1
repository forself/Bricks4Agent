#Requires -Version 5.1
param(
    [string]$Message = "create a prototype document",
    [string]$MessageFile = "",
    [string]$MessageBase64Utf8 = "",
    [string]$WebhookUrl = "",
    [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $scriptDir "appsettings.json"
}

if (-not (Test-Path $ConfigPath)) {
    throw "Missing config: $ConfigPath"
}

$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$channelSecret = $config.Line.ChannelSecret
$userId = $config.Line.DefaultRecipientId
$channelAccessToken = $config.Line.ChannelAccessToken

if ($MessageFile) {
    $Message = [System.IO.File]::ReadAllText((Resolve-Path $MessageFile), [System.Text.UTF8Encoding]::new($false))
}

if ($MessageBase64Utf8) {
    $Message = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($MessageBase64Utf8))
}

Add-Type -AssemblyName System.Net.Http

if (-not $WebhookUrl) {
    if ($channelAccessToken -and $channelAccessToken -notlike "REPLACE_WITH_*") {
        $headers = @{ Authorization = "Bearer $channelAccessToken" }
        try {
            $webhookInfo = Invoke-RestMethod -Method Get -Uri "https://api.line.me/v2/bot/channel/webhook/endpoint" -Headers $headers
            $WebhookUrl = $webhookInfo.endpoint
        } catch {
            Write-Warning "Failed to read LINE webhook endpoint from LINE API: $($_.Exception.Message)"
        }
    }

    if (-not $WebhookUrl) {
        $lastUrlFile = Join-Path $scriptDir ".last-tunnel-url"
        if (Test-Path $lastUrlFile) {
            $WebhookUrl = (Get-Content $lastUrlFile -Raw).Trim()
        }
    }
}

if (-not $WebhookUrl) {
    throw "WebhookUrl is required."
}

if ($WebhookUrl -notmatch "/webhook/line/?$") {
    $WebhookUrl = "$WebhookUrl/webhook/line/"
}

$payload = @{
    destination = "test"
    events = @(
        @{
            type = "message"
            replyToken = "test-reply-token"
            source = @{
                type = "user"
                userId = $userId
            }
            timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            message = @{
                id = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds().ToString()
                type = "text"
                text = $Message
            }
        }
    )
} | ConvertTo-Json -Depth 8 -Compress

$hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($channelSecret))
try {
    $signatureBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payload))
} finally {
    $hmac.Dispose()
}

$headers = @{
    "X-Line-Signature" = [Convert]::ToBase64String($signatureBytes)
}

$http = [System.Net.Http.HttpClient]::new()
try {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $WebhookUrl)
    $request.Content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, "application/json")
    foreach ($entry in $headers.GetEnumerator()) {
        $request.Headers.TryAddWithoutValidation([string]$entry.Key, [string]$entry.Value) | Out-Null
    }

    $response = $http.SendAsync($request).GetAwaiter().GetResult()
    Write-Host "Webhook status: $([int]$response.StatusCode)" -ForegroundColor Green
} finally {
    $http.Dispose()
}

Write-Host "Webhook URL:    $WebhookUrl" -ForegroundColor Cyan
Write-Host "Message:        $Message" -ForegroundColor Cyan
