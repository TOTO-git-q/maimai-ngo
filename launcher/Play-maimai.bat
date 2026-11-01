@echo off
rem One-click start. Details and log: launcher.ps1 / launcher.log
start "" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0launcher.ps1" %*
