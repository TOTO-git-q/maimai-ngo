"""Put the drop shadows under the new figures' feet on the continue / game-over / data-save screens. The figures
stand higher than the stock bears did, so each root's Shadow child is raised; lifts are in screen px (1080x1920),
measured from the off-screen walk, and turned into local units with the root's scale. Absolute (stock + lift), so
re-running is safe. Writes resources.assets.new; install with ngo_install_new.ps1.
usage: python ngo_shadow_align.py <ame px> <kangel px> <pcat px>"""
import sys
import UnityPy

LIFT = dict(zip(("ame", "kangel", "pcat"), map(float, sys.argv[1:4])))
# root RectTransforms, as in ngo_puppet_scale.py (ContinueConform, ContinueSelect, GameOver, DataSave)
ROOTS = {"ame": ([86722, 86059, 81947, 78377], -275.0),
         "kangel": ([81669, 91876, 86871, 84603], -314.28570556640625),
         "pcat": ([93593, 80561, 78582, 93860], -314.28570556640625)}
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
env = UnityPy.load(ASSETS); objs = env.file.objects
for who, (ids, stock_y) in ROOTS.items():
    for pid in ids:
        root = objs[pid].read_typetree()
        k = abs(root["m_LocalScale"]["y"])
        for c in root["m_Children"]:
            o = objs[c["m_PathID"]]; t = o.read_typetree()
            if objs[t["m_GameObject"]["m_PathID"]].read_typetree()["m_Name"] != "Shadow": continue
            t["m_AnchoredPosition"]["y"] = stock_y + LIFT[who] / k
            o.save_typetree(t)
            print(who, pid, "shadow y", round(t["m_AnchoredPosition"]["y"], 1))
open(ASSETS + ".new", "wb").write(env.file.save())
