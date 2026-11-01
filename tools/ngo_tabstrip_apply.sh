#!/usr/bin/env bash
# Install the tab-row fixes (ngo_tabstrip.py): selected-tab face into the base sprite, tab trough texture.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
O=tasks/ngo_skin_test/tabstrip
python tools/ngo_tabstrip.py "$O/UI_CMN_Selector_Deco_02.png" "$O"
cp "$O/select_sprites/tabbase_flat.png" "$O/select_sprites/tabwhite_flat.png" /d/maimai/Package/LocalAssets/PartnerLive2D/select_sprites/
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-tabstrip' -Force"
python tools/ngo_patch_assets.py "$O/replacements" tasks/ngo_skin_test/patch_tabstrip.csv 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_full_quad.py UI_CMN_Selector_Deco_02
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
