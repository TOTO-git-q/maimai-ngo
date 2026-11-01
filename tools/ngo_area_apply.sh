#!/usr/bin/env bash
# Install the area-select clean-up (Codex out/area_v1): back up, write textures, full-quad meshes.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
R=/e/cxref/maimai-ngo/out/area_v1/replacements
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-area' -Force"
python tools/ngo_patch_assets.py $R tasks/ngo_skin_test/patch_area.csv 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_full_quad.py $(ls $R | sed 's/\.png$//')
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
