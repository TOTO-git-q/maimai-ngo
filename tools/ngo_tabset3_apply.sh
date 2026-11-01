#!/usr/bin/env bash
# Install the tab row v3 (Codex out/tabset_v3): stock geometry and roles, so the stock title sprites and labels sit
# where the game puts them; mod label offsets back to 0; deep NGO tints under the stock white label text.
# installed/ = side tabs re-split into the stock roles (ngo_tab_side_roles.py) + the v3 tray.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
R=/e/cxref/maimai-ngo/out/tabset_v3/replacements
I=/e/cxref/maimai-ngo/out/tabset_v3/installed
python tools/ngo_tab_side_roles.py $R $I
cp $R/UI_CMN_Selector_Deco_02.png $I/
python tools/ngo_sort_colors_white.py ngo
cp $R/tabbase_flat.png $R/tabwhite_flat.png /d/maimai/Package/LocalAssets/PartnerLive2D/select_sprites/
[ -f /d/maimai/backup_ngo_skin/select_layout.ini.before-tabset3 ] || cp /d/maimai/Package/LocalAssets/PartnerLive2D/select_layout.ini /d/maimai/backup_ngo_skin/select_layout.ini.before-tabset3
python tools/ngo_layout_set.py tabLabelOffsetY=0 tabTitleOffsetY=0
[ -f /d/maimai/backup_ngo_skin/resources.assets.before-tabset3 ] || powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-tabset3' -Force"
python tools/ngo_patch_assets.py $I tasks/ngo_skin_test/patch_tabset3.csv --only UI_CMN_Tab_Base_01,UI_CMN_Tab_Base_02,UI_CMN_Selector_Deco_02 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
