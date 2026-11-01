@echo off
rem One-time setup: lets the maimai launcher lock the NVIDIA GPU clock while the game runs (needs administrator once).
rem Creates two on-demand scheduled tasks with highest privileges; they do nothing until something starts them:
rem   "maimai GPU lock"   = nvidia-smi -lgc 1800,3090  (the GPU never drops below 1800 MHz)
rem   "maimai GPU unlock" = nvidia-smi -rgc            (back to normal)
rem Registered with Register-ScheduledTask and no trigger, so no date on the command line: schtasks /SD wants the
rem date in the Windows date format (yyyy/mm/dd on a Chinese system) and refused the old fixed 01/01/2000.
rem Remove them again with remove-gpu-lock.bat.
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$smi = 'C:\Windows\System32\nvidia-smi.exe'; if (-not (Test-Path $smi)) { Write-Host 'nvidia-smi.exe not found (no NVIDIA driver?)'; exit 1 }; $p = New-ScheduledTaskPrincipal -UserId ($env:USERDOMAIN + '\' + $env:USERNAME) -LogonType Interactive -RunLevel Highest; $s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 1); Register-ScheduledTask -TaskName 'maimai GPU lock' -Action (New-ScheduledTaskAction -Execute $smi -Argument '-lgc 1800,3090') -Principal $p -Settings $s -Force | Out-Null; Register-ScheduledTask -TaskName 'maimai GPU unlock' -Action (New-ScheduledTaskAction -Execute $smi -Argument '-rgc') -Principal $p -Settings $s -Force | Out-Null; Get-ScheduledTask -TaskName 'maimai GPU*' | Format-Table TaskName, State -AutoSize"
schtasks /Run /TN "maimai GPU unlock" >nul 2>&1
echo.
echo If "maimai GPU lock" and "maimai GPU unlock" are listed above, it is done:
echo the launcher now locks the GPU clock while maimai runs.
pause
