@echo off
chcp 65001 >nul
echo.
echo ==========================================
echo   SPA Generator
echo ==========================================
echo.

cd /d "%~dp0"

if exist "..\static-server\StaticServer.csproj" (
    echo Starting static server on http://localhost:3080
    dotnet run --project ..\static-server\StaticServer.csproj -- .\frontend 3080
    goto :end
)

if exist "server.js" (
    where node >nul 2>&1
    if %errorlevel% equ 0 (
        echo Falling back to node server.js
        node server.js %*
        goto :end
    )
)

where python >nul 2>&1
if %errorlevel% equ 0 (
    echo Falling back to python -m http.server
    cd frontend
    start http://localhost:3080
    python -m http.server 3080
    goto :end
)

echo No supported frontend server was found.
echo Install one of the following:
echo   - .NET 8 SDK
echo   - Node.js
echo   - Python
echo.
pause

:end
