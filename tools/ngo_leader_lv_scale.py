"""Music-select leader slot: shrink the LV plate under the leader portrait. The plate (IMG_Frame), the "LV" tag (Lv),
the level digits (IMG_Level/IMG_LevelNum) and the reborn star scale by <k> about the plate bar's centre; the star badge
(Skill_Type01) stays put under it. Absolute from the stock values, so re-running with another k is safe.
Writes resources.assets.new; install with ngo_install_new.ps1. usage: python ngo_leader_lv_scale.py <k>"""
import sys
import UnityPy

K = float(sys.argv[1])
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
PIVOT = (-2.0, -81.0)      # centre of the plate's bar (UI_CMN_Chara_LFrame_01 rows 8-40), Null_object space
# RectTransform path id: (stock x, y in Null_object space, stock scale, anchor offset x)
STOCK = {93974: (-2.0, -89.0, 1.0, 0.0),      # IMG_Frame
         86721: (-41.3, -79.0, 0.8, 0.0),     # Lv
         77098: (26.5, -79.0, 0.75, 20.5),    # IMG_LevelNum: anchored to the right edge of IMG_Level (41 wide, at 0,0)
         79994: (-81.0, -75.0, 0.9, 0.0)}     # IMG_RebornStar

env = UnityPy.load(ASSETS); objs = env.file.objects
for pid, (x, y, s, ax) in STOCK.items():
    o = objs[pid]; t = o.read_typetree()
    t["m_AnchoredPosition"]["x"] = PIVOT[0] + (x - PIVOT[0]) * K - ax
    t["m_AnchoredPosition"]["y"] = PIVOT[1] + (y - PIVOT[1]) * K
    t["m_LocalScale"]["x"] = t["m_LocalScale"]["y"] = s * K
    o.save_typetree(t)
    print(objs[t["m_GameObject"]["m_PathID"]].peek_name(), t["m_AnchoredPosition"], t["m_LocalScale"]["x"])
open(ASSETS + ".new", "wb").write(env.file.save())
