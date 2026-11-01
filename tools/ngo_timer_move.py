"""Move the common countdown timer so it no longer covers the music-select card info. The digits come from the
CommonTimer prefab (root Transform at local y -380), instantiated at runtime under GenericProcess Main/UI_Timer
(whose own position is driven at runtime). So UI_Timer is left at its stock values and the prefab root is moved and
scaled instead. Writes resources.assets.new; install with ngo_install_new.ps1.
usage: python ngo_timer_move.py <prefab local y> <scale>   (stock: -380 1)"""
import sys
import UnityPy

Y, K = float(sys.argv[1]), float(sys.argv[2])
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
env = UnityPy.load(ASSETS); objs = env.file.objects
ui = objs[74599].read_typetree()   # UI_Timer RectTransform: back to stock
ui["m_LocalPosition"]["y"] = 0.0; ui["m_LocalScale"]["x"] = ui["m_LocalScale"]["y"] = 1.0; objs[74599].save_typetree(ui)
t = objs[43655].read_typetree()    # CommonTimer prefab root Transform
t["m_LocalPosition"]["y"] = Y; t["m_LocalScale"]["x"] = t["m_LocalScale"]["y"] = K
objs[43655].save_typetree(t)
open(ASSETS + ".new", "wb").write(env.file.save()); print("CommonTimer y", Y, "scale", K)
