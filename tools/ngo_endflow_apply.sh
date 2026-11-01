#!/usr/bin/env bash
# Install the end-flow art (Codex out/ngo_end_*): puppet (bear -> Ame, lime/lemon -> KAngel), track start, end screens,
# plus digits / result when listed in EXTRA. Sky stripes: the TST sky texture is 76x8, so it carries the
# stock's vertical-stripe idea in NGO pinks; the ground mask is tinted violet instead of the stock navy.
set -e
cd "/e/claude workspace/maimai-partner-live2d"
X=/e/cxref/maimai-ngo/out
I=$X/endflow_install
rm -rf $I && mkdir -p $I
cp $X/${PUPPET:-ngo_end_puppet/candidates}/*.png $I/
cp $X/ngo_end_trackstart/replacements/*.png $I/
cp $X/ngo_end_screens/replacements/*.png $I/
for d in ${EXTRA:-}; do cp $X/$d/replacements/*.png $I/; done   # EXTRA="ngo_end_digits ngo_end_result" once reviewed
python -c "from PIL import Image; im=Image.new('RGBA',(76,8)); [im.putpixel((x,y),(0xF4,0xD4,0xF4,255) if x<38 else (0xFB,0xE6,0xF5,255)) for x in range(76) for y in range(8)]; im.save(r'E:\cxref\maimai-ngo\out\endflow_install\UI_TST_BG_Pat_01.png')"
echo '{"UI_TST_TrackStart_Grass": {"#4242C6": "#815CDD"}}' > $I/../endflow_tints.json
ls $I | wc -l
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item 'D:\maimai\Package\Sinmai_Data\resources.assets' 'D:\maimai\backup_ngo_skin\resources.assets.before-endflow' -Force"
python tools/ngo_patch_assets.py $I tasks/ngo_skin_test/patch_endflow.csv 2>/dev/null | tail -1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
python tools/ngo_tint_map.py $X/endflow_tints.json 2>/dev/null | tail -3
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
for tj in ${TINTS:-}; do   # TINTS="ngo_end_collection" once reviewed: shared selector masks keep their art, only tints change
  python tools/ngo_tint_map.py $X/$tj/tints.json 2>/dev/null | tail -1
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
done
SLICED=$(grep -l "Sliced" /e/cxref/maimai-ngo/tasks/refs_endflow/*/FORMAT_*.md >/dev/null; python - <<'EOF'
import glob, re
names = set()
for f in glob.glob(r"E:/cxref/maimai-ngo/tasks/refs_endflow/*/FORMAT_*.md"):
    for line in open(f, encoding="utf-8"):
        m = re.match(r"\| (\S+) \|", line)
        if m and " Sliced " in line: names.add(m.group(1))
print(" ".join(sorted(names)))
EOF
)
# the result / music-select navi "でらっくま" is a second copy of the puppet, in the navi bundle
AB=/d/maimai/Package/Sinmai_Data/StreamingAssets/A000/AssetBundleImages/navichara/prefab/ui_navichara_00.ab
[ -f /d/maimai/backup_ngo_skin/ui_navichara_00.ab.before-ame ] || cp $AB /d/maimai/backup_ngo_skin/ui_navichara_00.ab.before-ame
cp /d/maimai/backup_ngo_skin/ui_navichara_00.ab.before-ame $AB
NGO_TARGET="D:\\maimai\\Package\\Sinmai_Data\\StreamingAssets\\A000\\AssetBundleImages\\navichara\\prefab\\ui_navichara_00.ab" \
  python tools/ngo_patch_assets.py $X/${PUPPET:-ngo_end_puppet/candidates} tasks/ngo_skin_test/patch_navi00.csv 2>/dev/null | tail -1
mv -f $AB.new $AB
if [ -n "${TIMER_MOVE:-}" ]; then   # e.g. "-70 0.85": countdown off the music-select card info
  python tools/ngo_timer_move.py $TIMER_MOVE 2>/dev/null | tail -1
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
fi
if [ -n "${PUPPET_SCALE:-}" ]; then   # e.g. "0.32 0.55": Ame, KAngel and the P cat the same size
  python tools/ngo_puppet_scale.py $PUPPET_SCALE 2>/dev/null | tail -1
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
fi
python tools/ngo_full_quad.py $(ls $I | sed 's/\.png$//' | grep -vi '^UI_NUM_' | grep -vxF $(for s in $SLICED; do printf -- "-e %s " $s; done) -e __none__)
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ngo_install_new.ps1
# song frames (select / track start / gameplay top) and the play-circle score gauge in one look: ngo_diff_frames.py.
# Last, because it replaces the older plates EXTRA put in above (ngo_end_result, ngo_card_frames, ngo_pixelated).
bash tools/ngo_frames_apply.sh
