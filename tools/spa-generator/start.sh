#!/bin/bash

echo "Starting SPA Generator..."
echo

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

if [ -d "$SCRIPT_DIR/backend" ]; then
    (
        cd "$SCRIPT_DIR/backend"
        dotnet run
    ) &
    BACKEND_PID=$!
fi

cd "$SCRIPT_DIR"

start_frontend() {
    # 優先使用 Node server.js（支援 API 路由 + /packages/ 路徑映射）
    if [ -f "server.js" ] && command -v node >/dev/null 2>&1; then
        echo "Starting Node server on http://localhost:3080"
        echo "  - API endpoints: /api/*"
        echo "  - Library paths: /packages/*, /templates/*"
        node server.js --port=3080 &
        return 0
    fi

    # Fallback: C# StaticServer（僅靜態檔案）
    if [ -f "../static-server/StaticServer.csproj" ]; then
        echo "WARNING: Using static server - API endpoints and /packages/ paths not available"
        echo "Starting static server on http://localhost:3080"
        dotnet run --project ../static-server/StaticServer.csproj -- ./frontend 3080 &
        return 0
    fi

    # Fallback: npx serve（僅靜態檔案）
    if command -v npx >/dev/null 2>&1; then
        echo "WARNING: Using npx serve - API endpoints and /packages/ paths not available"
        (
            cd frontend
            npx serve -l 3080
        ) &
        return 0
    fi

    # Fallback: Python（僅靜態檔案）
    if command -v python3 >/dev/null 2>&1; then
        echo "WARNING: Using python3 - API endpoints and /packages/ paths not available"
        (
            cd frontend
            python3 -m http.server 3080
        ) &
        return 0
    fi

    if command -v python >/dev/null 2>&1; then
        echo "WARNING: Using python - API endpoints and /packages/ paths not available"
        (
            cd frontend
            python -m http.server 3080
        ) &
        return 0
    fi

    echo "No supported frontend server was found."
    echo "Install Node.js (recommended) for full API support."
    return 1
}

start_frontend

echo
echo "API Server: https://localhost:5002"
echo "Frontend:   http://localhost:3080"
echo

sleep 3
open http://localhost:3080 2>/dev/null || xdg-open http://localhost:3080 2>/dev/null || true

wait ${BACKEND_PID:-0}
