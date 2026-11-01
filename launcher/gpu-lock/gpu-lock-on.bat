@echo off
rem Lock the NVIDIA GPU clock now (after setup-gpu-lock.bat was run once as administrator). Run before playing.
schtasks /Run /TN "maimai GPU lock"
echo GPU clock locked. Run gpu-lock-off.bat after playing.
pause
