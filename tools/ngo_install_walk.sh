#!/usr/bin/env bash
# Copy Sinmai_Data\resources.assets.new over resources.assets (if present), then do one muted off-screen skin walk.
# usage: BURST=20 BURST_DT=0.5 BURST_AT=Advertise bash tools/ngo_install_walk.sh <out_dir>
cd "/e/claude workspace/maimai-partner-live2d"
if [ -f /d/maimai/Package/Sinmai_Data/resources.assets.new ]; then
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1 || exit 1
fi
rm -rf "$1"
DRIVE_FULL_ENTRY=1 python tools/ngo_skin_walk.py "$1" > "$1.out" 2>&1
tail -2 "$1.out"
echo "burst frames: $(ls "$1" | grep -c burst)"
