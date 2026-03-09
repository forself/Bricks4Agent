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
    if [ -f "../static-server/StaticServer.csproj" ]; then
        echo "Starting static server on http://localhost:3080"
        dotnet run --project ../static-server/StaticServer.csproj -- ./frontend 3080 &
        return 0
    fi

    if [ -f "server.js" ]; then
        echo "Falling back to node server.js"
        node server.js --port=3080 &
        return 0
    fi

    if command -v npx >/dev/null 2>&1; then
        echo "Falling back to npx serve"
        (
            cd frontend
            npx serve -l 3080
        ) &
        return 0
    fi

    if command -v python3 >/dev/null 2>&1; then
        echo "Falling back to python3 -m http.server"
        (
            cd frontend
            python3 -m http.server 3080
        ) &
        return 0
    fi

    if command -v python >/dev/null 2>&1; then
        echo "Falling back to python -m http.server"
        (
            cd frontend
            python -m http.server 3080
        ) &
        return 0
    fi

    echo "No supported frontend server was found."
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
