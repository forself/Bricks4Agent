#Requires -Version 5.1
param(
    [string]$BrokerUrl = "http://127.0.0.1:5361",
    [string]$UserId = "utf8-test-user",
    [string]$Message = "",
    [string]$MessageFile = "",
    [string]$MessageBase64Utf8 = ""
)

$ErrorActionPreference = "Stop"
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

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

$resp = Invoke-RestMethod -Method Post `
    -Uri ($BrokerUrl.TrimEnd('/') + "/api/v1/high-level/line/process") `
    -ContentType "application/json; charset=utf-8" `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($body))

$resp | ConvertTo-Json -Depth 8
