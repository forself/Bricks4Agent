# build-images.ps1 — Build container images for dashboard testing
# Usage (from project root in PowerShell):
#   .\scripts\build-images.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Set-Location $root
Write-Host "==> Build context: $root" -ForegroundColor Cyan

function Build-Image($tag, $dockerfile) {
    Write-Host ""
    Write-Host "  Building $tag ..." -ForegroundColor Yellow
    docker build --tag $tag --file $dockerfile .
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $tag" }
    Write-Host "  OK: $tag" -ForegroundColor Green
}

Build-Image "bricks4agent-broker:latest"       "packages/csharp/broker/Containerfile"
Build-Image "bricks4agent-broker-monitor:latest" "packages/csharp/broker/Containerfile.monitor"
Build-Image "bricks4agent-file-worker:latest"  "packages/csharp/workers/file-worker/Containerfile"
Build-Image "bricks4agent-quote-worker:latest" "packages/csharp/workers/quote-worker/Containerfile"

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
docker images --filter "reference=bricks4agent-*" --format "  {{.Repository}}:{{.Tag}}  ({{.Size}})"
Write-Host ""
Write-Host "Next: docker compose -f tools/compose.dashboard-test.yml up -d" -ForegroundColor Cyan
