@echo off
rem Wrapper so the script also runs where the PowerShell execution policy blocks .ps1 files.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0aithread.ps1" %*
exit /b %ERRORLEVEL%
