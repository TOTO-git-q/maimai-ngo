"""Crops of a BURST walk for judging background motion: the sub strip and the visible margins of the main circle,
as an animated GIF plus a 4-frame still (frames 0, 1/3, 2/3, last).
usage: python ngo_burst_crops.py <walk_dir>   -> <walk_dir>/_motion.gif, _motion_frames.png"""
import glob, os, sys
from PIL import Image

D = sys.argv[1]
DT = float(os.environ.get("BURST_DT", "0.7"))
frames = [Image.open(p).convert("RGB") for p in sorted(glob.glob(os.path.join(D, "burst_*.png")))]
W, H = frames[0].size                      # screenshots are half size: 540 x 960


def compose(f):
    top = f.crop((0, 0, W, 225))
    circ = f.crop((0, 420, W, H))
    c = Image.new("RGB", (W, 225 + 8 + circ.height), (0, 0, 0))
    c.paste(top, (0, 0)); c.paste(circ, (0, 233))
    return c


comp = [compose(f) for f in frames]
comp[0].save(os.path.join(D, "_motion.gif"), save_all=True, append_images=comp[1:], duration=int(DT * 1000), loop=0)
pick = [comp[0], comp[len(comp) // 3], comp[2 * len(comp) // 3], comp[-1]]
still = Image.new("RGB", (W * 4 + 24, pick[0].height), (0, 0, 0))
for i, c in enumerate(pick):
    still.paste(c, (i * (W + 8), 0))
still.save(os.path.join(D, "_motion_frames.png"))
print(len(frames), "frames")
