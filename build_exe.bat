@echo off
title Building Windrose Server Control.exe
echo.
echo ============================================
echo   Building Windrose Server Control.exe
echo ============================================
echo.

taskkill /F /IM "Windrose Server Control.exe" >nul 2>&1
timeout /t 1 /nobreak >nul

echo [1/4] Finding C# compiler...
set CSC=
if exist "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if "%CSC%"=="" if exist "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if "%CSC%"=="" (
    echo ERROR: .NET Framework 4.0 compiler not found.
    echo.
    echo .NET Framework 4.0 is required. It should be built into Windows 10/11.
    echo If missing, download it from:
    echo https://dotnet.microsoft.com/en-us/download/dotnet-framework/net40
    echo.
    pause & exit /b 1
)
echo Found: %CSC%
echo.

echo [2/4] Extracting Windrose icon...
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0extract_icon.ps1"
set ICON_ARG=
if exist "%~dp0windrose.ico" (
    echo Icon ready.
    set ICON_ARG=/win32icon:"%~dp0windrose.ico"
) else (
    echo Using default icon.
)
echo.

echo [3/4] Updating source title...
powershell.exe -ExecutionPolicy Bypass -NoProfile -Command "(Get-Content '%~dp0WindroseServerGUI.cs') -replace 'Windrose Server GUI','Windrose Server Control' | Set-Content '%~dp0WindroseServerGUI.cs'"
echo.

echo [4/4] Compiling...
%CSC% /target:winexe /out:"%~dp0Windrose Server Control.exe" %ICON_ARG% /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll /optimize+ /nologo "%~dp0WindroseServerGUI.cs"
if errorlevel 1 (
    echo ERROR: Compilation failed.
    pause & exit /b 1
)

if exist "%~dp0windrose.ico" del "%~dp0windrose.ico"

echo.
echo ============================================
echo   Done! Windrose Server Control.exe ready.
echo ============================================
echo.
pause
