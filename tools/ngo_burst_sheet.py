"""Contact sheet + animated GIF of the burst_*.png frames a BURST walk grabbed (ngo_skin_walk.py), for checking motion.
usage: python ngo_burst_sheet.py <walk_dir>   -> <walk_dir>/_burst_sheet.png, _burst.gif"""
import glob, os, sys
from PIL import Image

D = sys.argv[1]
frames = [Image.open(p).convert("RGB") for p in sorted(glob.glob(os.path.join(D, "burst_*.png")))]
small = [f.resize((f.width // 3, f.height // 3)) for f in frames]
cols = min(5, len(small))
rows = (len(small) + cols - 1) // cols
sheet = Image.new("RGB", (cols * small[0].width, rows * small[0].height), (0, 0, 0))
for i, f in enumerate(small):
    sheet.paste(f, ((i % cols) * f.width, (i // cols) * f.height))
sheet.save(os.path.join(D, "_burst_sheet.png"))
half = [f.resize((f.width // 2, f.height // 2)) for f in frames]
half[0].save(os.path.join(D, "_burst.gif"), save_all=True, append_images=half[1:],
             duration=int(float(os.environ.get("BURST_DT", "0.7")) * 1000), loop=0)
print(len(frames), "frames")
