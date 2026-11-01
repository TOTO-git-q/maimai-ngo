#!/usr/bin/env bash
# Install the NGO attract entry banner + 糖糖 button icon (Codex out/entry_ui): back up, write textures, full-quad meshes.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-entry' -Force"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/entry_ui/replacements tasks/ngo_skin_test/patch_entry.csv 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_full_quad.py UI_CMN_Emoney_01 UI_CMN_Emoney_02 UI_CMN_Img_ButtonInfo_01 UI_CMN_Img_ButtonInfo_02
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
