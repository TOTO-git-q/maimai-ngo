"""Move the board title bar's window buttons (_ [] x) inward so they stay on screen once the board is fitted to the
circle (ngo_board_fit.py makes the bar's right end run past the screen edge). Start from the board without buttons
(ngo_board_nobuttons.py) and paste the original button block SHIFT texture px to the left.
usage: python ngo_board_buttons_in.py <original board.png> <board without buttons.png> <out.png> <shift>"""
import sys
import numpy as np
from PIL import Image

orig = np.asarray(Image.open(sys.argv[1]).convert("RGBA"))
out = np.asarray(Image.open(sys.argv[2]).convert("RGBA")).copy()
shift = int(sys.argv[4])
X0, X1, ROWS = 1936, 2120, 58      # button block incl. margin and bar rows (reported by ngo_board_nobuttons.py)
out[:ROWS, X0 - shift:X1 - shift] = orig[:ROWS, X0:X1]
Image.fromarray(out).save(sys.argv[3])
print("buttons moved to x", X0 - shift, "-", X1 - shift)
