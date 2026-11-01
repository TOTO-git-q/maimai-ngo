@echo off
rem Undo setup-gpu-lock.bat: reset the GPU clock and delete the two scheduled tasks (needs administrator).
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
C:\Windows\System32\nvidia-smi.exe -rgc
schtasks /Delete /F /TN "maimai GPU lock"
schtasks /Delete /F /TN "maimai GPU unlock"
echo.
echo Removed.
pause
