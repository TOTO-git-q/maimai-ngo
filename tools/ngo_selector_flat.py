"""Collection selector: the half-circle bases behind the centre card (UI_CMN_Selector_Base_06) carried an iridescent
glass gradient that read as a glass orb. Their face becomes flat NGO paper #F7E7FD, shape and alpha kept.
Source: the installed texture dumped by ngo_dump_tex.py. usage: python ngo_selector_flat.py <out dir>"""
import os, sys
import numpy as np
from PIL import Image

SRC = r"E:\cxref\maimai-ngo\tasks\refs_audit3\now_UI_CMN_Selector_Base_06.png"
a = np.array(Image.open(SRC).convert("RGBA"))
a[..., :3] = (247, 231, 253)
os.makedirs(sys.argv[1], exist_ok=True)
Image.fromarray(a).save(os.path.join(sys.argv[1], "UI_CMN_Selector_Base_06.png"))
