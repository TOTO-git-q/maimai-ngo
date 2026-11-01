#!/usr/bin/env bash
# Install helper: restore the pre-motion assets, apply ngo_bg_motion.py, re-apply the later patches that the
# pre-motion backup does not have yet (blue sweep 2, transitions, title, entry, passport, select tabs / NEXT, area
# select, tab plate), install, walk (see ngo_skin_walk.py).
# The end-flow art, the song frames and the play-circle score gauge come after this: ngo_endflow_apply.sh (it ends with
# ngo_frames_apply.sh).
# usage: BURST=24 BURST_DT=0.25 BURST_AT=PlInformation,PhotoShoot bash tools/ngo_motion_try.sh <out_dir>
set -e
cd "/e/claude workspace/maimai-partner-live2d"
B=tasks/ngo_skin_test/bgA3
INSTALL="\$d='D:\maimai\Package\Sinmai_Data'; Copy-Item \"\$d\resources.assets.new\" \"\$d\resources.assets\" -Force; Remove-Item \"\$d\resources.assets.new\""
powershell -NoProfile -Command "Copy-Item 'D:\maimai\backup_ngo_skin\resources.assets.before-motion' 'D:\maimai\Package\Sinmai_Data\resources.assets' -Force"
python tools/ngo_bg_motion.py $B/main_motion.png $B/sub_motion.png $B/floats_atlas.png $B/floats/symbol_empty.png | grep -c . || true
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_patch_assets.py tasks/ngo_skin_test/blue2/out tasks/ngo_skin_test/patch_blue2.csv --only UI_NUM_Timer,UI_UPE_Track_01 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/transi_v2/replacements tasks/ngo_skin_test/patch_transi_v2.csv 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_transi_install.py | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_title.py tasks/ngo_skin_test/title/ngo_logo.png --mark /e/cxref/maimai-ngo/out/maimai_mark_v2/mark_B.png | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/entry_ui/replacements tasks/ngo_skin_test/patch_entry.csv 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_full_quad.py UI_CMN_Emoney_01 UI_CMN_Emoney_02 UI_CMN_Img_ButtonInfo_01 UI_CMN_Img_ButtonInfo_02
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/passport/replacements tasks/ngo_skin_test/patch_passport.csv 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_full_quad.py UI_ENT_Base_MyprofCover
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/select_art2/replacements tasks/ngo_skin_test/patch_select_b.csv --only UI_CMN_Btn_NameNext 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/tabset_v3/short tasks/ngo_skin_test/patch_tabset3.csv --only UI_CMN_Tab_Base_01,UI_CMN_Tab_Base_02 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_full_quad.py UI_CMN_Tab_Base_01 UI_CMN_Tab_Base_02 UI_CMN_Btn_NameNext
powershell -NoProfile -Command "$INSTALL"
# area select v2 (installed/ = replacements + Frame_01 face, made by ngo_area2_apply.sh); Sliced sprites keep their mesh
AREA=/e/cxref/maimai-ngo/out/area_v2/installed
python tools/ngo_patch_assets.py $AREA tasks/ngo_skin_test/patch_area2.csv 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_tint_map.py /e/cxref/maimai-ngo/out/area_v2/tints.json 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_full_quad.py $(ls $AREA | sed 's/\.png$//' | grep -vxF -e UI_CMN_Frame_01 -e UI_CMN_InfoBase_02 -e UI_CHS_FrameBase_03 -e UI_CHS_Countryside_FrameBase)
powershell -NoProfile -Command "$INSTALL"
# tab tray: part of the format-matched tab set (sort colours + selected-tab sprites: ngo_tabset3_apply.sh)
PLATE=/e/cxref/maimai-ngo/out/tabset_v3/short
python tools/ngo_patch_assets.py $PLATE tasks/ngo_skin_test/patch_tabset3.csv --only UI_CMN_Selector_Deco_02 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
python tools/ngo_full_quad.py UI_CMN_Selector_Deco_02
powershell -NoProfile -Command "$INSTALL"
# freedom mode: no "Aime をかざすとフリーダムモードを終了します" banner (UI_CMN_ExitGuide blanked)
python tools/ngo_patch_assets.py /e/cxref/maimai-ngo/out/freedom_noguide/replacements tasks/ngo_skin_test/patch_noguide.csv 2>/dev/null | tail -1
powershell -NoProfile -Command "$INSTALL"
[ -n "$1" ] || exit 0
rm -rf "$1"
DRIVE_FULL_ENTRY=1 BURST=${BURST:-10} python tools/ngo_skin_walk.py "$1" > "$1.out" 2>&1 || true
tail -3 "$1.out"; ls "$1" | wc -l
