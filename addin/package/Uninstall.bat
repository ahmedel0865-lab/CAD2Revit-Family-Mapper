@echo off
rem Removes CAD2Revit from all Revit versions (current user).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall
pause
