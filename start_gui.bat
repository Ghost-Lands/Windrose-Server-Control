@echo off
title Windrose Server GUI
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0windrose_backend.ps1"
pause
