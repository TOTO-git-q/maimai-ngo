#!/usr/bin/env bash
# Install the unified song frames and the score gauge at the top of the play circle, all made by tools/ngo_diff_frames.py
# into out/ngo_unified_frames (that folder also holds UI_UPE_Box_01 from ngo_upe_hud.py and UI_NUM_MLevel_* from
# ngo_mlevel_font.py). Runs last in ngo_endflow_apply.sh: it supersedes the older per-screen plates that EXTRA installs
# there (ngo_end_result UPE plates + GAM_Achieve_Base, ngo_card_frames MSS/TST cards, ngo_pixelated MLevel).
# Safe to re-run: same pictures, and each tint is mapped from its stock value only.
# usage: bash tools/ngo_frames_apply.sh   (game must not be running; first run keeps resources.assets.before-frames)
set -e
cd "/e/claude workspace/maimai-partner-live2d"
U=/e/cxref/maimai-ngo/out/ngo_unified_frames
INSTALL="powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1"
[ -f /d/maimai/backup_ngo_skin/resources.assets.before-frames ] || powershell -NoProfile -Command \
  "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-frames'"
python tools/ngo_diff_frames.py $U /e/cxref/maimai-ngo/out/ngo_unified_frames_preview.png
python tools/ngo_patch_assets.py $U tasks/ngo_skin_test/patch_frames.csv 2>/dev/null | tail -1
$INSTALL
# the gauge's empty part (stock black LeftColorBlack / LeftColorGaugeMask) becomes the score boxes' deep-purple screen
# (SCREEN_DK). The band keeps its stock role: a mask the game itself tints #EF8CB8 at run time (prefab colour is white).
echo '{"UI_GAM_Gauge_Color": {"#000000": "#261068"}}' > /e/cxref/maimai-ngo/out/gauge_tints.json
python tools/ngo_tint_map.py /e/cxref/maimai-ngo/out/gauge_tints.json 2>/dev/null | tail -1
$INSTALL
# the pixel-snapped art reaches 1-2 px past the stock traced meshes; UI_GAM_Gauge_Color keeps its traced mesh (it is
# also the gauge's mask shape, and the new fill mask lies inside the stock one)
python tools/ngo_full_quad.py UI_GAM_Gauge_Base UI_GAM_Gauge_Line UI_GAM_Achieve_Base 2>/dev/null | tail -1
$INSTALL
