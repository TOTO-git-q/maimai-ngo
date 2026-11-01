#!/usr/bin/env bash
# Install the painted tab set (Codex out/tabset_v1): sort colours white, selected-tab sprites, tab + tray textures.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
R=/e/cxref/maimai-ngo/out/tabset_v1/replacements
python tools/ngo_sort_colors_white.py
cp $R/tabbase_flat.png $R/tabwhite_flat.png /d/maimai/Package/LocalAssets/PartnerLive2D/select_sprites/
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-tabset' -Force"
python tools/ngo_patch_assets.py $R tasks/ngo_skin_test/patch_tabset.csv --only UI_CMN_Tab_Base_01,UI_CMN_Tab_Base_02,UI_CMN_Selector_Deco_02 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_full_quad.py UI_CMN_Tab_Base_01 UI_CMN_Tab_Base_02 UI_CMN_Selector_Deco_02
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
