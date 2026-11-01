#!/usr/bin/env bash
# Install the NGO passport card cover (Codex out/passport): back up, write the texture, full-quad mesh.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-passport' -Force"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/passport/replacements tasks/ngo_skin_test/patch_passport.csv 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_full_quad.py UI_ENT_Base_MyprofCover
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
