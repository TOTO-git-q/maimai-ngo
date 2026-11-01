"""Put the pixel font (PixelMplus12) into the game's only Font object, LiberationSans, in resources.assets, so the mod can
build a dynamic TMP font asset from it (Unity 2018 cannot load a TTF from disk). Writes resources.assets.new; install
with ngo_install_new.ps1. Backup first. usage: python ngo_font_data.py [ttf]"""
import sys
import UnityPy

TTF = sys.argv[1] if len(sys.argv) > 1 else r"E:\cxref\amuproj\app\assets\dialog\PixelMplus12-Regular.ttf"
ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
env = UnityPy.load(ASSETS)
for o in env.objects:
    if o.type.name == "Font" and o.peek_name() == "LiberationSans":
        t = o.read_typetree()
        data = open(TTF, "rb").read()
        t["m_FontData"] = list(data) if isinstance(t["m_FontData"], list) else data
        t["m_FontNames"] = ["PixelMplus12"]
        o.save_typetree(t)
        print("LiberationSans <- %s (%d bytes)" % (TTF, len(data)))
open(ASSETS + ".new", "wb").write(env.file.save())
