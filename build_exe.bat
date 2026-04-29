@echo off
title Building Windrose Server GUI.exe...
echo.
echo ============================================
echo   Building WindroseServerGUI.exe
echo ============================================
echo.

echo [1/3] Installing ps2exe compiler...
powershell.exe -ExecutionPolicy Bypass -NoProfile -Command "if (-not (Get-Module -ListAvailable -Name ps2exe)) { Install-Module -Name ps2exe -Scope CurrentUser -Force -ErrorAction Stop }"
if errorlevel 1 (
    echo ERROR: Could not install ps2exe.
    pause
    exit /b 1
)

echo [2/3] Compiling WindroseServerGUI.exe...
powershell.exe -ExecutionPolicy Bypass -NoProfile -Command "Import-Module ps2exe; Invoke-ps2exe -InputFile '%~dp0windrose_backend.ps1' -OutputFile '%~dp0WindroseServerGUI.exe' -RequireAdmin -Title 'Windrose Server GUI' -Description 'Windrose Dedicated Server Manager' -Version '1.0.0'"
if errorlevel 1 (
    echo ERROR: Compilation failed.
    pause
    exit /b 1
)

echo [3/3] Done!
echo.
echo WindroseServerGUI.exe created - double-click it to run.
echo.
pause
