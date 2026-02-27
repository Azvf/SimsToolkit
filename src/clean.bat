@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "CLEAN_SCRIPT=%SCRIPT_DIR%clean-workspace.ps1"

if not exist "%CLEAN_SCRIPT%" (
    echo [clean.bat] ERROR: Missing script "%CLEAN_SCRIPT%"
    echo.
    pause
    exit /b 1
)

echo [clean.bat] Running workspace cleanup...
echo [clean.bat] Script: "%CLEAN_SCRIPT%"
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%CLEAN_SCRIPT%" -Force -StopProcesses %*
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if "%EXIT_CODE%"=="0" (
    echo [clean.bat] Cleanup completed successfully.
) else (
    echo [clean.bat] Cleanup finished with issues. Exit code: %EXIT_CODE%
)

echo.
pause
exit /b %EXIT_CODE%
