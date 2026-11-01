"""Top screen (CommonProcess UI_UserInformation/UI_CharacterSlot/UI_Parent): show the four partner cards larger.
CharactorSlot02-05 scale by <k>; the first card's left edge stays where it is (the leader in CharactorSlot01 is left
alone, so its portrait keeps its place by the screen edge), the cards keep their 139-unit pitch times k, and the row's
bottom (the stars, at the top screen's lower edge) stays put while the cards grow upward. Absolute from the stock
values, so re-running with another k is safe. Writes resources.assets.new; install with ngo_install_new.ps1.
usage: python ngo_partner_row_scale.py <k>"""
import sys
import UnityPy

K = float(sys.argv[1])
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
SLOTS = [71530, 75353, 71970, 71587]   # CharactorSlot02-05, stock x -259 + 139 i, y 2, scale 1
X0, PITCH, Y0 = -259.0, 139.0, 2.0
HALF_W, HALF_H = 74.0, 136.0           # card frame half width; frame half height plus the star's overhang

env = UnityPy.load(ASSETS); objs = env.file.objects
left = X0 - HALF_W
for i, pid in enumerate(SLOTS):
    o = objs[pid]; t = o.read_typetree()
    t["m_AnchoredPosition"]["x"] = left + HALF_W * K + PITCH * K * i
    t["m_AnchoredPosition"]["y"] = Y0 - HALF_H + HALF_H * K
    t["m_LocalScale"]["x"] = t["m_LocalScale"]["y"] = K
    o.save_typetree(t)
    print(objs[t["m_GameObject"]["m_PathID"]].peek_name(), t["m_AnchoredPosition"], K)
open(ASSETS + ".new", "wb").write(env.file.save())
