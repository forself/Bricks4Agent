@echo off
chcp 65001 >nul
echo.
echo ==========================================
echo   SPA Generator
echo ==========================================
echo.

cd /d "%~dp0"

REM 優先使用 Node server.js（支援 API 路由 + /packages/ 路徑映射）
if exist "server.js" (
    where node >nul 2>&1
    if %errorlevel% equ 0 (
        echo Starting Node server on http://localhost:3080
        echo   - API endpoints: /api/*
        echo   - Library paths: /packages/*, /templates/*
        echo.
        node server.js %*
        goto :end
    )
)

REM Fallback: C# StaticServer（僅靜態檔案，不支援 API 和 /packages/）
if exist "..\static-server\StaticServer.csproj" (
    echo WARNING: Using static server - API endpoints and /packages/ paths not available
    echo Starting static server on http://localhost:3080
    dotnet run --project ..\static-server\StaticServer.csproj -- .\frontend 3080
    goto :end
)

REM Fallback: Python（僅靜態檔案）
where python >nul 2>&1
if %errorlevel% equ 0 (
    echo WARNING: Using Python http.server - API endpoints and /packages/ paths not available
    cd frontend
    start http://localhost:3080
    python -m http.server 3080
    goto :end
)

echo No supported frontend server was found.
echo Install one of the following:
echo   - Node.js (recommended - full API support)
echo   - .NET 8 SDK (static files only)
echo   - Python (static files only)
echo.
pause

:end
