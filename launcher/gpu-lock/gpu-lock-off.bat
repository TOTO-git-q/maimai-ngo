@echo off
rem Give the NVIDIA GPU clock back to the driver.
schtasks /Run /TN "maimai GPU unlock"
echo GPU clock back to normal.
pause
