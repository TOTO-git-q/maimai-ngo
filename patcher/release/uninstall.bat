@echo off
rem Remove the maimai NGO skin: puts your original files back from Package\NGO_skin_backup. ASCII only.
"%~dp0ngo_patcher\ngo_patcher.exe" uninstall %1
echo.
pause