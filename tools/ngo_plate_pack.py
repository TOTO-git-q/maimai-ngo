"""Pack the NGO animated nameplates (owner 2026-11-07) for the game.
Frames from E:\\claude workspace\\maimai-nameplate\\final\\<plate> (720x116 RGBA, 12 fps): loop_ame, loop_kangel,
transform_to_kangel, transform_to_ame. Writes, per plate id:
  <pkg>\\LocalAssets\\NgoPlates\\<id>\\{ame,kangel,to_kangel,to_ame}.bc7  (raw BC7 blocks, one frame after another, rows
      flipped for Unity's bottom-up LoadRawTextureData; frame size 720*116 bytes), read by plates_mod\\NgoPlates.dll
The plate data and stills come from tools\\ngo_share_pack.py (option folder A588).
usage: python tools/ngo_plate_pack.py [--pkg D:\\maimai\\Package] [--check]"""
import argparse, os, re, sys
import numpy as np, etcpak, texture2ddecoder
from PIL import Image

SRC = r"E:\claude workspace\maimai-nameplate\final"
PLATES = [(990001, "雨極", "goku"), (990002, "雨将", "shou"), (990003, "雨神", "kami"), (990004, "雨舞舞", "maimai")]
CLIPS = [("ame", "loop_ame"), ("kangel", "loop_kangel"), ("to_kangel", "transform_to_kangel"), ("to_ame", "transform_to_ame")]
W, H = 720, 116

def frames(folder):
    names = sorted((n for n in os.listdir(folder) if n.lower().endswith(".png")), key=lambda n: int(re.sub(r"\D", "", n) or 0))
    return [os.path.join(folder, n) for n in names]

def bc7(path):
    im = Image.open(path).convert("RGBA")
    if im.size != (W, H): raise SystemExit("%s is %s, not %dx%d" % (path, im.size, W, H))
    rgba = np.asarray(im.transpose(Image.FLIP_TOP_BOTTOM))
    # etcpak takes RGBA (measured: texture2ddecoder gives the blocks back as BGRA with error 0.4/255)
    data = etcpak.compress_bc7(np.ascontiguousarray(rgba).tobytes(), W, H, None)
    if len(data) != W * H: raise SystemExit("unexpected BC7 size %d" % len(data))
    return data, rgba

def check(data, rgba):
    out = np.frombuffer(texture2ddecoder.decode_bc7(data, W, H), np.uint8).reshape(H, W, 4)[:, :, [2, 1, 0, 3]]
    return float(np.abs(out.astype(int) - rgba.astype(int)).mean())

ap = argparse.ArgumentParser(); ap.add_argument("--pkg", default=r"D:\maimai\Package"); ap.add_argument("--check", action="store_true")
a = ap.parse_args()
out_root = os.path.join(a.pkg, r"LocalAssets\NgoPlates")   # read by the standalone plates_mod (NgoPlates.dll)
sa = os.path.join(a.pkg, r"Sinmai_Data\StreamingAssets")
template = open(os.path.join(sa, r"A000\plate\plate709501\Plate.xml"), encoding="utf-8").read()
for pid, name, still in PLATES:
    src = os.path.join(SRC, name); dst = os.path.join(out_root, str(pid)); os.makedirs(dst, exist_ok=True)
    counts = []
    for clip, folder in CLIPS:
        fs = frames(os.path.join(src, folder)); blob = bytearray(); err = []
        for i, f in enumerate(fs):
            data, rgba = bc7(f); blob += data
            if a.check and i % 12 == 0: err.append(check(data, rgba))
        open(os.path.join(dst, clip + ".bc7"), "wb").write(bytes(blob)); counts.append("%s %d" % (clip, len(fs)))
        if err: print("  %s %s mean abs error %.2f" % (name, clip, sum(err) / len(err)))
    # the plate data and stills live in the shared A588 package now (tools\ngo_share_pack.py), never in A500
    print(pid, name, ", ".join(counts))
