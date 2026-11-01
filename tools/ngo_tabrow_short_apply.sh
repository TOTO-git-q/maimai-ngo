#!/usr/bin/env bash
# Shorter side tabs + one baseline for tabs, dome and board bar (ngo_tab_shorten.py, ngo_tabrow_geometry.py).
set -e
cd "/e/claude workspace/maimai-partner-live2d"
V=/e/cxref/maimai-ngo/out/tabset_v3
python tools/ngo_tab_shorten.py $V/installed $V/short 26
cp $V/installed/UI_CMN_Selector_Deco_02.png $V/short/
cp /d/maimai/Package/LocalAssets/PartnerLive2D/select_layout.ini /d/maimai/backup_ngo_skin/select_layout.ini.before-tabrow
python tools/ngo_tabrow_geometry.py
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-tabshort' -Force"
python tools/ngo_patch_assets.py $V/short tasks/ngo_skin_test/patch_tabshort.csv --only UI_CMN_Tab_Base_01,UI_CMN_Tab_Base_02 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
