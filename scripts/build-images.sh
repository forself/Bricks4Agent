#!/usr/bin/env bash
# ────────────────────────────────────────────────────────────────────────────
# build-images.sh — Build all container images for dashboard testing
#
# Usage (from project root):
#   bash scripts/build-images.sh
#
# Works in Git Bash, WSL2, or any POSIX shell with docker in PATH.
# ────────────────────────────────────────────────────────────────────────────
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

echo "==> Build context: $REPO_ROOT"
echo ""

build() {
  local tag="$1"
  local dockerfile="$2"
  echo "──────────────────────────────────────────"
  echo "  Building $tag"
  echo "  Dockerfile: $dockerfile"
  echo "──────────────────────────────────────────"
  docker build \
    --tag "$tag" \
    --file "$dockerfile" \
    --progress=plain \
    .
  echo ""
}

build "bricks4agent-broker:latest"      "packages/csharp/broker/Containerfile"
build "bricks4agent-file-worker:latest" "packages/csharp/workers/file-worker/Containerfile"
build "bricks4agent-line-worker:latest" "packages/csharp/workers/line-worker/Containerfile"

echo "══════════════════════════════════════════"
echo "  Build complete. Images:"
docker images --filter "reference=bricks4agent-*" \
  --format "  {{.Repository}}:{{.Tag}}  ({{.Size}})"
echo ""
echo "  Next step:"
echo "  docker compose -f tools/compose.dashboard-test.yml up -d"
echo "  Open http://localhost:5000"
echo "══════════════════════════════════════════"
