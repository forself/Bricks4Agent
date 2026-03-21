param(
    [string]$Model = "qwen3-coder:30b",
    [int]$PollInterval = 2,
    [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $ConfigPath) {
    $ConfigPath = Join-Path $scriptDir "appsettings.json"
}

$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$token = $config.Line.ChannelAccessToken
$defaultRecipient = $config.Line.DefaultRecipientId

$pendingDir = Join-Path $scriptDir "bin\Debug\net8.0\pending_messages"

if (-not (Test-Path $pendingDir)) {
    Write-Host "Waiting for pending_messages directory: $pendingDir" -ForegroundColor Yellow
    while (-not (Test-Path $pendingDir)) {
        Start-Sleep -Seconds 2
    }
}

$ollamaBase = "http://127.0.0.1:11434"

$systemPrompt = "You are a helpful assistant communicating via LINE messaging. Keep responses concise (under 500 characters) since this is a chat interface. Answer in the same language as the user message. For math questions, show your work briefly then give the answer. For general knowledge questions, give a clear direct answer."

Write-Host "=== LINE Agent Bridge ===" -ForegroundColor Cyan
Write-Host "Model:     $Model" -ForegroundColor DarkGray
Write-Host "Pending:   $pendingDir" -ForegroundColor DarkGray
Write-Host "Recipient: $defaultRecipient" -ForegroundColor DarkGray
Write-Host "Poll:      ${PollInterval}s" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Waiting for LINE messages..." -ForegroundColor Green
Write-Host "(Ctrl+C to stop)" -ForegroundColor DarkGray
Write-Host ""

function Send-LineMessage {
    param([string]$to, [string]$text)
    $body = @{
        to = $to
        messages = @(@{ type = "text"; text = $text })
    } | ConvertTo-Json -Depth 3

    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type"  = "application/json; charset=utf-8"
    }

    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)

    try {
        Invoke-RestMethod -Uri "https://api.line.me/v2/bot/message/push" -Method POST -Body $bodyBytes -Headers $headers | Out-Null
        return $true
    } catch {
        $errMsg = $_.Exception.Message
        Write-Host ("  Send error: " + $errMsg) -ForegroundColor Red
        return $false
    }
}

function Get-LlmResponse {
    param([string]$userMessage)
    $reqBody = @{
        model = $Model
        messages = @(
            @{ role = "system"; content = $systemPrompt }
            @{ role = "user"; content = $userMessage }
        )
        stream = $false
        options = @{
            temperature = 0.3
            num_predict = 512
        }
    } | ConvertTo-Json -Depth 4

    try {
        $resp = Invoke-RestMethod -Uri "$ollamaBase/api/chat" -Method POST -Body ([System.Text.Encoding]::UTF8.GetBytes($reqBody)) -ContentType "application/json" -TimeoutSec 120

        $content = $resp.message.content

        if ($content -match '(?s)<think>.*?</think>\s*(.*)') {
            $content = $Matches[1].Trim()
        }

        return $content
    } catch {
        return ("Error generating response: " + $_.Exception.Message)
    }
}

while ($true) {
    try {
        $files = Get-ChildItem -Path $pendingDir -Filter "*.json" -ErrorAction SilentlyContinue | Sort-Object CreationTimeUtc

        foreach ($file in $files) {
            try {
                $msg = Get-Content $file.FullName -Raw | ConvertFrom-Json

                $text = $msg.text
                if (-not $text) {
                    Write-Host ("Skipping message without text") -ForegroundColor DarkGray
                    Remove-Item $file.FullName -Force
                    continue
                }
                $userId = $msg.userId
                $replyTo = if ($userId) { $userId } else { $defaultRecipient }

                $ts = (Get-Date).ToString("HH:mm:ss")
                Write-Host ("[$ts] Received: " + $text) -ForegroundColor Yellow

                Write-Host "  Thinking..." -ForegroundColor DarkGray
                $response = Get-LlmResponse $text

                if ($response.Length -gt 4900) {
                    $response = $response.Substring(0, 4900)
                }

                $previewLen = [Math]::Min(80, $response.Length)
                $preview = $response.Substring(0, $previewLen)
                Write-Host ("  Response: " + $preview) -ForegroundColor Green

                $sent = Send-LineMessage -to $replyTo -text $response
                if ($sent) {
                    $short = $replyTo.Substring(0, [Math]::Min(8, $replyTo.Length))
                    Write-Host ("  Sent to " + $short + "...") -ForegroundColor Green
                }

                Remove-Item $file.FullName -Force

            } catch {
                $errMsg = $_.Exception.Message
                Write-Host ("  Error: " + $errMsg) -ForegroundColor Red
                Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue
            }
        }
    } catch {
        $errMsg = $_.Exception.Message
        Write-Host ("Poll error: " + $errMsg) -ForegroundColor Red
    }

    Start-Sleep -Seconds $PollInterval
}