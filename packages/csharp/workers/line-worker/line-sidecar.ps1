#Requires -Version 5.1
param(
    [Parameter(Position = 0)]
    [ValidateSet("up", "status", "verify", "verify-broker", "down", "restart")]
    [string]$Action = "status",

    [int]$BrokerPort = 5361,
    [int]$WebhookPort = 5357,
    [switch]$SkipBuild,
    [switch]$SkipWebhookUpdate,
    [string]$UserId = "utf8-test-user",
    [string]$Message = "",
    [string]$MessageFile = "",
    [string]$MessageBase64Utf8 = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Invoke-ChildScript {
    param(
        [string]$ScriptName,
        [hashtable]$Parameters = @{}
    )

    $scriptPath = Join-Path $scriptDir $ScriptName
    if (-not (Test-Path $scriptPath)) {
        throw "Missing script: $scriptPath"
    }

    & $scriptPath @Parameters
}

switch ($Action) {
    "up" {
        $params = @{
            BrokerPort = $BrokerPort
            WebhookPort = $WebhookPort
        }
        if ($SkipBuild) {
            $params.SkipBuild = $true
        }
        if ($SkipWebhookUpdate) {
            $params.SkipWebhookUpdate = $true
        }

        Invoke-ChildScript -ScriptName "start-sidecar-stack.ps1" -Parameters $params
        break
    }

    "status" {
        Invoke-ChildScript -ScriptName "status-sidecar-stack.ps1" -Parameters @{
            WebhookPort = $WebhookPort
        }
        break
    }

    "verify" {
        $params = @{}
        if (-not [string]::IsNullOrWhiteSpace($Message)) {
            $params.Message = $Message
        }
        if (-not [string]::IsNullOrWhiteSpace($MessageFile)) {
            $params.MessageFile = $MessageFile
        }
        if (-not [string]::IsNullOrWhiteSpace($MessageBase64Utf8)) {
            $params.MessageBase64Utf8 = $MessageBase64Utf8
        }

        Invoke-ChildScript -ScriptName "verify-live-webhook.ps1" -Parameters $params
        break
    }

    "verify-broker" {
        $params = @{
            BrokerUrl = "http://127.0.0.1:$BrokerPort"
            UserId = $UserId
        }
        if (-not [string]::IsNullOrWhiteSpace($Message)) {
            $params.Message = $Message
        }
        if (-not [string]::IsNullOrWhiteSpace($MessageFile)) {
            $params.MessageFile = $MessageFile
        }
        if (-not [string]::IsNullOrWhiteSpace($MessageBase64Utf8)) {
            $params.MessageBase64Utf8 = $MessageBase64Utf8
        }

        Invoke-ChildScript -ScriptName "verify-high-level-process.ps1" -Parameters $params
        break
    }

    "down" {
        Invoke-ChildScript -ScriptName "stop-sidecar-stack.ps1" -Parameters @{
            WebhookPort = $WebhookPort
        }
        break
    }

    "restart" {
        Invoke-ChildScript -ScriptName "stop-sidecar-stack.ps1" -Parameters @{
            WebhookPort = $WebhookPort
        }

        $params = @{
            BrokerPort = $BrokerPort
            WebhookPort = $WebhookPort
        }
        if ($SkipBuild) {
            $params.SkipBuild = $true
        }
        if ($SkipWebhookUpdate) {
            $params.SkipWebhookUpdate = $true
        }

        Invoke-ChildScript -ScriptName "start-sidecar-stack.ps1" -Parameters $params
        break
    }
}
