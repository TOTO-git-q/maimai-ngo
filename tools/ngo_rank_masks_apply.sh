#!/usr/bin/env bash
# Install the result-screen rank flash masks made by tools/ngo_rank_masks.py: the S shine mask (UI_RSL_Rank_S_White01)
# and the SSS+ plus glints (UI_RSL_Rank_Plus_White02..04, thin lines on the plus's purple outline at three inner corners).
# The other masks that script writes (S_White, A_mask, B_mask) are not installed: they never were. Runs after
# ngo_frames_apply.sh at the end of ngo_endflow_apply.sh. Safe to re-run: same pictures.
# usage: bash tools/ngo_rank_masks_apply.sh   (game must not be running; first run keeps resources.assets.before-plusticks)
set -e
cd "/e/claude workspace/maimai-partner-live2d"
O=/e/cxref/maimai-ngo/out/ngo_rank_masks_v2
[ -f /d/maimai/backup_ngo_skin/resources.assets.before-plusticks ] || powershell -NoProfile -Command \
  "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-plusticks'"
python tools/ngo_rank_masks.py $O
python tools/ngo_patch_assets.py $O tasks/ngo_skin_test/patch_rank_masks.csv \
  --only UI_RSL_Rank_S_White01,UI_RSL_Rank_Plus_White02,UI_RSL_Rank_Plus_White03,UI_RSL_Rank_Plus_White04 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
