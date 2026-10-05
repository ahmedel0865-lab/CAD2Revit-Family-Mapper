@echo off
rem Removes SmartHost MEP (and the add-in's former name) from all Revit versions (current user).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall
pause
