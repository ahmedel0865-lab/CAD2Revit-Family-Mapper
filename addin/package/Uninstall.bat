@echo off
rem Removes DMC Remapper (and the add-in's former names) from all Revit versions (current user).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall
pause
