@echo off
rem Installs DMC Remapper for every Revit 2022-2026 found on this PC (current user, no admin needed).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
pause
