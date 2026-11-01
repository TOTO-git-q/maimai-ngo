#!/usr/bin/env bash
# Install the format-matched area select (Codex out/area_v2): textures from installed/ (replacements + Frame_01 face
# restored by ngo_frame_fill.py), mask tints from tints.json, full quads only for the Simple sprites.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
V=/e/cxref/maimai-ngo/out/area_v2
R=$V/installed
mkdir -p $R && cp $V/replacements/*.png $R/
python tools/ngo_frame_fill.py $R/UI_CMN_Frame_01.png $R/UI_CMN_Frame_01.png "#FFFFFF" 204
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-area2' -Force"
python tools/ngo_patch_assets.py $R tasks/ngo_skin_test/patch_area2.csv 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_tint_map.py $V/tints.json 2>/dev/null | tail -12
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
SLICED="UI_CMN_Frame_01 UI_CMN_InfoBase_02 UI_CHS_FrameBase_03 UI_CHS_Countryside_FrameBase"
python tools/ngo_full_quad.py $(ls $R | sed 's/\.png$//' | grep -vxF -e ${SLICED// / -e })
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
