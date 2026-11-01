#!/usr/bin/env bash
# Music select round 3: files (colours, select_sprites, preset) + shared tab textures + the mod build.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
python tools/ngo_select_c_files.py
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-select-c' -Force"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/select_art3/replacements tasks/ngo_skin_test/patch_select_c.csv --only UI_CMN_Tab_Base_01,UI_CMN_Tab_Base_02 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_full_quad.py UI_CMN_Tab_Base_01 UI_CMN_Tab_Base_02
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install 2>&1 | tail -2
