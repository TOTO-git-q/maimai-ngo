#!/usr/bin/env bash
# Install the NGO screen transition (Codex transi_v2): back up, write the 9 textures, then tints + full-quad meshes.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-transi' -Force"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/transi_v2/replacements tasks/ngo_skin_test/patch_transi_v2.csv 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_transi_install.py
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
