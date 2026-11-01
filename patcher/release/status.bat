@echo off
rem Show whether the skin is installed and which files it changed. ASCII only.
"%~dp0ngo_patcher\ngo_patcher.exe" status %1
echo.
pause