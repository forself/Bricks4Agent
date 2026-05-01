#Requires -Version 5.1
param(
    [string]$BrokerUrl = "http://127.0.0.1:5361",
    [string]$UserId = "utf8-test-user",
    [string]$Message = "",
    [string]$MessageFile = "",
    [string]$MessageBase64Utf8 = "",
    [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

if (-not $ConfigPath) {
    $ConfigPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "appsettings.json"
}

if ($MessageFile) {
    $Message = [System.IO.File]::ReadAllText((Resolve-Path $MessageFile), [System.Text.UTF8Encoding]::new($false))
}

if ($MessageBase64Utf8) {
    $Message = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($MessageBase64Utf8))
}

if (-not $Message) {
    throw "Message, MessageFile, or MessageBase64Utf8 is required."
}

$body = @{
    user_id = $UserId
    message = $Message
} | ConvertTo-Json -Compress

$headers = @{}
if (Test-Path $ConfigPath) {
    $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
    $workerType = $config.Worker.Auth.WorkerType
    if (-not $workerType) { $workerType = $config.Broker.WorkerAuth.WorkerType }
    $keyId = $config.Worker.Auth.KeyId
    if (-not $keyId) { $keyId = $config.Broker.WorkerAuth.KeyId }
    $sharedSecret = $config.Worker.Auth.SharedSecret
    if (-not $sharedSecret) { $sharedSecret = $config.Broker.WorkerAuth.SharedSecret }

    if ($workerType -and $keyId -and $sharedSecret) {
        $timestamp = [DateTimeOffset]::UtcNow.ToString("O")
        $nonce = [Guid]::NewGuid().ToString("N")
        $path = "/api/v1/high-level/line/process"
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        $bodyHashBytes = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($body))
        $bodyHash = ([System.BitConverter]::ToString($bodyHashBytes) -replace '-', '').ToLowerInvariant()
        $baseString = @(
            "POST"
            $path
            $bodyHash
            $workerType
            $keyId
            $timestamp
            $nonce
        ) -join "`n"
        $hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($sharedSecret))
        $signature = [Convert]::ToBase64String($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($baseString)))

        $headers["X-B4A-Worker-Type"] = $workerType
        $headers["X-B4A-Key-Id"] = $keyId
        $headers["X-B4A-Timestamp"] = $timestamp
        $headers["X-B4A-Nonce"] = $nonce
        $headers["X-B4A-Signature"] = $signature
    }
}

$resp = Invoke-RestMethod -Method Post `
    -Uri ($BrokerUrl.TrimEnd('/') + "/api/v1/high-level/line/process") `
    -Headers $headers `
    -ContentType "application/json; charset=utf-8" `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($body))

$resp | ConvertTo-Json -Depth 8
