"""Gameplay upper panel: put the level number under the NGO knob's white disc. The digits are left-aligned in their
box (UI_Achivement/Lv, RectTransform 76420), so one digit lands 18 px and two digits 24 px left of the disc centre;
the whole Lv group moves right by <dx> px (absolute: stock x 243 + dx). Writes resources.assets.new; install with
ngo_install_new.ps1. usage: python ngo_hud_lv_move.py <dx>"""
import sys
import UnityPy

DX = float(sys.argv[1])
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
env = UnityPy.load(ASSETS); o = env.file.objects[76420]
t = o.read_typetree(); t["m_AnchoredPosition"]["x"] = 243.0 + DX; o.save_typetree(t)
open(ASSETS + ".new", "wb").write(env.file.save())
print("Lv x", t["m_AnchoredPosition"]["x"])
