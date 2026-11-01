"""Make Ame, KAngel and the P cat the same size on the continue / game-over / data-save screens: the stock bear
(UI_CMN_Derakkuma) root is 0.4 and the lime/lemon bears 0.35, and the bear's art is about twice as tall, so Ame is
scaled down and the side pair up. Edits the root RectTransforms' m_LocalScale (sign kept: Lemon is mirrored).
Writes resources.assets.new; install with ngo_install_new.ps1.
usage: python ngo_puppet_scale.py <ame scale> <side scale>"""
import sys
import UnityPy

AME, SIDE = float(sys.argv[1]), float(sys.argv[2])
# RectTransform path ids found with ngo_find_nodes.py (ContinueConform, ContinueSelect, GameOver, DataSave)
DERA = [86722, 86059, 81947, 78377]
LEMON = [81669, 91876, 86871, 84603]
LIME = [93593, 80561, 78582, 93860]
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
env = UnityPy.load(ASSETS); objs = env.file.objects
for ids, k in ((DERA, AME), (LEMON, SIDE), (LIME, SIDE)):
    for pid in ids:
        o = objs[pid]; t = o.read_typetree(); sc = t["m_LocalScale"]
        sc["x"] = k if sc["x"] >= 0 else -k; sc["y"] = k
        o.save_typetree(t)
open(ASSETS + ".new", "wb").write(env.file.save())
print("scaled: ame", AME, "side", SIDE)
