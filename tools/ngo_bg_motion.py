"""Give the installed NGO menu background its motion, still without a runtime mod, by re-tuning maimai's own particle
systems inside Sinmai_Data\\resources.assets (run after ngo_bg_assets.py):
- main screen: the bokeh holder "Particle System" (BokehSmall / BokehMedium / BokehLarge) is moved out of the switched-off
  hill onto Adjust_WorldPos, centred on the circle and facing the camera; its material M_v170BG_BokehHill01 goes to
  Sprites/Default with our float atlas (in the texture slot of the switched-off lake, T_v170BG_LakeRocks, point-filtered).
  Each system draws one atlas row: Small = twinkles that fade in and out in place, Medium = mid floats drifting up,
  Large = front floats drifting up a little faster. BG_Sky shows the painting without those floats (main_motion.png).
- both background paintings are drawn in the opaque pass (material render queue), so everything on the BackGround
  sorting layer lands on top of them.
- sub screen: with the strip behind, the 9 note slots and the symbol show; the
  cyan glow ripple is switched off; ToneParticle/Particle (same material) drifts mid floats across the strip.
Writes resources.assets.new; the caller copies it over.
- sub screen, too: ToneParticle bobs both strip windows (atlas bottom-right block); the treble-clef slot over
  window_01 is emptied (symbol_empty.png).
usage: python ngo_bg_motion.py <main_motion.png> <sub_motion.png> <floats_atlas.png> <floats/symbol_empty.png>"""
import os, struct, sys
import UnityPy
from UnityPy.enums import TextureFormat
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from PIL import Image

MAIN_PNG, SUB_PNG, ATLAS_PNG, SYMBOL_PNG = sys.argv[1:5]
DATA = r"D:\maimai\Package\Sinmai_Data"
ASSETS = os.path.join(DATA, "resources.assets")
TILE, COLS, ROWS = 256, 8, 6               # float grid over the 2048 x 1536 atlas (rows 0-2 used)
BOB = dict(tiles=(2, 2), frame=2, size=(1024, 768), offset=(0.0, 540.0 - 340.0), period=4.0, amp=12.0)
# the windows block (bottom-left 1024 x 768 of the atlas, centred on main-image (540, 340)) bobs up and down
SUB_BOB, SUB_BOB_CENTRE = dict(BOB, frame=3, period=4.6, amp=10.0), (440, 225)   # bottom-right block, sub-strip px
CIRCLE_CENTRE = (1.15, -413.15)           # BG_Sky's centre in Adjust_WorldPos space = the main circle's centre
HOLDER_Z = 600.0                          # in front of BG_Sky (z 780), camera looks along +z
BACKGROUND_LAYER = (-168763845, -3)       # sorting layer "BackGround" (id, value), drawn before Default

env = UnityPy.load(ASSETS)
gen = TypeTreeGenerator("2018.4.7f1")
gen.load_local_dll_folder(os.path.join(DATA, "Managed"))
env.typetree_generator = gen
log = []


def comps(go_obj):
    for comp in go_obj.read().m_Components:
        yield (comp.component if hasattr(comp, "component") else comp).deref()


def comp_of(go_obj, type_name):
    return next((c for c in comps(go_obj) if c.type.name == type_name), None)


def children(go_obj):
    tr = comp_of(go_obj, "Transform") or comp_of(go_obj, "RectTransform")
    for ch in tr.read().m_Children:
        yield ch.read().m_GameObject.deref()


def find(root, path):
    cur = root
    for name in path:
        cur = next((c for c in children(cur) if c.peek_name() == name), None)
        if cur is None: return None
    return cur


def set_active(go_obj, active):
    t = go_obj.read_typetree()
    if bool(t["m_IsActive"]) != active:
        t["m_IsActive"] = active
        go_obj.save_typetree(t)
        log.append("%s active -> %s" % (t["m_Name"], active))


def set_local(go_obj, pos, identity_rot=True, unit_scale=True):
    tr = comp_of(go_obj, "Transform")
    t = tr.read_typetree()
    t["m_LocalPosition"] = {"x": pos[0], "y": pos[1], "z": pos[2]}
    if identity_rot: t["m_LocalRotation"] = {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0}
    if unit_scale: t["m_LocalScale"] = {"x": 1.0, "y": 1.0, "z": 1.0}
    tr.save_typetree(t)


def reparent(go_obj, new_parent_go, pos):
    """move under new_parent_go at local pos (identity rotation, unit scale). Every object is read and saved once:
    this UnityPy re-reads the original bytes, so a second read-modify-save of the same object drops the first edit"""
    tr, new_tr = comp_of(go_obj, "Transform"), comp_of(new_parent_go, "Transform")
    t = tr.read_typetree()
    old_id = t["m_Father"]["m_PathID"]
    assert old_id != new_tr.path_id, "already reparented: run on the pre-motion assets"
    old = env.file.objects[old_id]
    ot = old.read_typetree()
    ot["m_Children"] = [c for c in ot["m_Children"] if c["m_PathID"] != tr.path_id]
    old.save_typetree(ot)
    nt = new_tr.read_typetree()
    nt["m_Children"].append({"m_FileID": 0, "m_PathID": tr.path_id})
    new_tr.save_typetree(nt)
    t["m_Father"] = {"m_FileID": 0, "m_PathID": new_tr.path_id}
    t["m_LocalPosition"] = {"x": pos[0], "y": pos[1], "z": pos[2]}
    t["m_LocalRotation"] = {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0}
    t["m_LocalScale"] = {"x": 1.0, "y": 1.0, "z": 1.0}
    tr.save_typetree(t)


# ---- particle presets -------------------------------------------------------------------------------------------
def const(v):
    return {"minMaxState": 0, "scalar": float(v), "minScalar": float(v)}


def between(a, b):
    return {"minMaxState": 3, "scalar": float(b), "minScalar": float(a)}


def put_curve(dst, spec):
    dst.update(spec)


def fade_gradient(t_in, t_out):
    """white, alpha 0 -> 1 at t_in -> 1 at t_out -> 0 (times as fractions of life)"""
    g = {"m_Mode": 0, "m_NumColorKeys": 2, "m_NumAlphaKeys": 4}
    alphas = [(0.0, 0.0), (t_in, 1.0), (t_out, 1.0), (1.0, 0.0)]
    for i in range(8):
        g["key%d" % i] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": alphas[i][1] if i < 4 else 0.0}
        g["ctime%d" % i] = [0, 65535][i] if i < 2 else 0
        g["atime%d" % i] = int(round(alphas[i][0] * 65535)) if i < 4 else 0
    return g


KEEP_MODULES = {"InitialModule", "ShapeModule", "EmissionModule", "ColorModule", "UVModule", "VelocityModule", "NoiseModule"}


def tune(ps_obj, row, rate, max_n, life, vy, vx, noise, box, fade, bob=None):
    t = ps_obj.read_typetree()
    t["lengthInSec"] = 10.0; t["looping"] = True; t["prewarm"] = True; t["playOnAwake"] = True
    put_curve(t["startDelay"], const(0))
    t["moveWithTransform"] = 0            # simulate in local space: particles follow the background if it moves
    t["scalingMode"] = 1                  # local: parent scale does not resize the 1:1 pixel tiles
    for k, v in t.items():
        if k.endswith("Module") and isinstance(v, dict) and "enabled" in v and k not in KEEP_MODULES:
            v["enabled"] = False
    im = t["InitialModule"]
    put_curve(im["startLifetime"], between(*life))
    put_curve(im["startSpeed"], const(0))
    put_curve(im["startSize"], const(TILE))
    im["size3D"] = False; im["rotation3D"] = False
    put_curve(im["startRotation"], const(0))
    im["randomizeRotationDirection"] = 0.0
    put_curve(im["gravityModifier"], const(0))
    im["startColor"]["minMaxState"] = 0
    im["startColor"]["maxColor"] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
    im["maxNumParticles"] = max_n
    sh = t["ShapeModule"]
    sh["enabled"] = True; sh["type"] = 5    # box volume
    sh["m_Position"] = {"x": 0.0, "y": 0.0, "z": 0.0}
    sh["m_Rotation"] = {"x": 0.0, "y": 0.0, "z": 0.0}
    sh["m_Scale"] = {"x": float(box[0]), "y": float(box[1]), "z": 0.0}
    sh["randomDirectionAmount"] = 0.0; sh["sphericalDirectionAmount"] = 0.0; sh["randomPositionAmount"] = 0.0
    em = t["EmissionModule"]
    em["enabled"] = True
    put_curve(em["rateOverTime"], const(rate))
    put_curve(em["rateOverDistance"], const(0))
    em["m_BurstCount"] = 0; em["m_Bursts"] = []
    co = t["ColorModule"]
    co["enabled"] = True
    co["gradient"]["minMaxState"] = 1
    co["gradient"]["maxGradient"] = fade_gradient(*fade)
    uv = t["UVModule"]
    uv["enabled"] = True; uv["mode"] = 0; uv["timeMode"] = 0
    uv["tilesX"] = COLS; uv["tilesY"] = ROWS
    uv["animationType"] = 1; uv["randomRow"] = False; uv["rowIndex"] = row; uv["cycles"] = 1.0
    put_curve(uv["frameOverTime"], between(0.0, 0.9999))   # random between two constants = one fixed random tile each
    put_curve(uv["startFrame"], const(0))
    uv["flipU"] = 0.0; uv["flipV"] = 0.0
    ve = t["VelocityModule"]
    ve["enabled"] = bool(vy or vx)
    put_curve(ve["x"], between(-vx, vx) if vx else const(0))
    put_curve(ve["y"], between(*vy) if vy else const(0))
    put_curve(ve["z"], const(0))
    for k in ("orbitalX", "orbitalY", "orbitalZ", "orbitalOffsetX", "orbitalOffsetY", "orbitalOffsetZ", "radial"):
        if k in ve: put_curve(ve[k], const(0))
    if "speedModifier" in ve: put_curve(ve["speedModifier"], const(1))
    if "inWorldSpace" in ve: ve["inWorldSpace"] = False
    no = t["NoiseModule"]
    no["enabled"] = bool(noise)
    if noise:
        put_curve(no["strength"], const(noise)); no["separateAxes"] = False
        # smooth sway: high-quality 3D noise, low frequency, slowly scrolling (quality 1 = 1D noise moved every axis
        # together and read as jitter)
        no["frequency"] = 0.12; no["damping"] = False; no["octaves"] = 1; no["quality"] = 2
        put_curve(no["scrollSpeed"], const(0.04))
        if "positionAmount" in no: put_curve(no["positionAmount"], const(1))
        for k in ("rotationAmount", "sizeAmount"):
            if k in no: put_curve(no[k], const(0))
        if "remapEnabled" in no: no["remapEnabled"] = False
    if bob: make_bob(t, **bob)
    ps_obj.save_typetree(t)


def cos_curve(scale):
    """one cosine period over the particle's life: integrates to 0, so the particle ends where it started"""
    keys = [(0.0, 1.0, 0.0), (0.25, 0.0, -6.2832), (0.5, -1.0, 0.0), (0.75, 0.0, 6.2832), (1.0, 1.0, 0.0)]
    return {"minMaxState": 1, "scalar": float(scale), "minScalar": float(scale),
            "maxCurve": {"m_Curve": [{"time": tm, "value": v, "inSlope": s, "outSlope": s, "weightedMode": 0,
                                      "inWeight": 1 / 3, "outWeight": 1 / 3} for tm, v, s in keys],
                         "m_PreInfinity": 2, "m_PostInfinity": 2, "m_RotationOrder": 4}}


def flat_curve():
    return {"minMaxState": 1, "scalar": 0.0, "minScalar": 0.0,
            "maxCurve": {"m_Curve": [{"time": tm, "value": 0.0, "inSlope": 0.0, "outSlope": 0.0, "weightedMode": 0,
                                      "inWeight": 1 / 3, "outWeight": 1 / 3} for tm in (0.0, 1.0)],
                         "m_PreInfinity": 2, "m_PostInfinity": 2, "m_RotationOrder": 4}}


def make_bob(t, tiles, frame, size, offset, period, amp):
    """one particle at a fixed spot showing one big atlas frame, moving y = amp * sin(2 pi t / period). A burst every
    period re-spawns it; each lives a hair longer than the period, so the hand-over never shows a gap"""
    t["lengthInSec"] = period
    im = t["InitialModule"]
    put_curve(im["startLifetime"], const(period + 0.04))
    im["size3D"] = True
    put_curve(im["startSize"], const(size[0])); put_curve(im["startSizeY"], const(size[1])); put_curve(im["startSizeZ"], const(1))
    im["maxNumParticles"] = 2
    sh = t["ShapeModule"]
    sh["m_Position"] = {"x": offset[0], "y": offset[1], "z": 0.0}
    sh["m_Scale"] = {"x": 0.0, "y": 0.0, "z": 0.0}
    em = t["EmissionModule"]
    put_curve(em["rateOverTime"], const(0))
    burst = {"time": 0.0, "countCurve": const(1), "cycleCount": 1, "repeatInterval": period, "probability": 1.0}
    for k in ("countCurve",):
        burst[k]["maxCurve"] = {"m_Curve": [], "m_PreInfinity": 2, "m_PostInfinity": 2, "m_RotationOrder": 4}
        burst[k]["minCurve"] = {"m_Curve": [], "m_PreInfinity": 2, "m_PostInfinity": 2, "m_RotationOrder": 4}
    em["m_Bursts"] = [burst]; em["m_BurstCount"] = 1
    t["ColorModule"]["enabled"] = False
    uv = t["UVModule"]
    uv["tilesX"], uv["tilesY"] = tiles
    uv["animationType"] = 0
    put_curve(uv["frameOverTime"], const((frame + 0.5) / (tiles[0] * tiles[1])))
    ve = t["VelocityModule"]
    ve["enabled"] = True
    ve["x"].update(flat_curve()); ve["z"].update(flat_curve())
    ve["y"].update(cos_curve(6.2832 * amp / period))
    t["NoiseModule"]["enabled"] = False


def tune_renderer(r_obj, order, material=None):
    t = r_obj.read_typetree()
    if material is not None: t["m_Materials"] = [{"m_FileID": 0, "m_PathID": material}]
    t["m_Enabled"] = True
    t["m_RenderMode"] = 0
    t["m_SortingLayerID"], t["m_SortingLayer"] = BACKGROUND_LAYER
    t["m_SortingOrder"] = order
    t["m_MaxParticleSize"] = 2.0
    t["m_MinParticleSize"] = 0.0
    r_obj.save_typetree(t)


# name -> (atlas row, rate/s, max, lifetime, vy range, |vx|, noise, sort order, z offset, fade in/out)
MAIN = {
    "BokehSmall":  dict(row=0, rate=2.4, max_n=8, life=(1.6, 2.8), vy=None, vx=0, noise=0, fade=(0.35, 0.65), order=1, z=60.0),
    "BokehMedium": dict(row=1, rate=0.6, max_n=7, life=(10.0, 14.0), vy=(14.0, 22.0), vx=4.0, noise=4.0, fade=(0.15, 0.8), order=2, z=30.0),
    "BokehLarge":  dict(row=2, rate=0.35, max_n=4, life=(9.0, 12.0), vy=(22.0, 32.0), vx=5.0, noise=5.0, fade=(0.15, 0.8), order=4, z=0.0),
}
HOLDER_ORDER = 3             # the bobbing windows: above the mid floats, below the front ones (as in the layout)

roots_main = [o for o in env.objects if o.type.name == "GameObject" and o.peek_name() == "UI_CMN_BG"]
roots_sub = [o for o in env.objects if o.type.name == "GameObject" and o.peek_name() == "UI_CMN_SubBG"]
bokeh_mat_ids, behind_mat_ids = set(), set()
BEHIND_QUEUE = 2450          # opaque pass (<= 2500): drawn before every transparent sprite / particle whatever its layer
for r in roots_main:
    adj = find(r, ["Adjust_WorldPos"])
    if adj is None: continue
    holder = find(adj, ["SM_v170_Hill_Setted", "Particle System"]) or find(adj, ["Particle System"])
    if holder is None: log.append("no bokeh holder under a UI_CMN_BG"); continue
    reparent(holder, adj, (CIRCLE_CENTRE[0], CIRCLE_CENTRE[1], HOLDER_Z))
    log.append("Particle System -> Adjust_WorldPos")
    set_active(holder, True)
    tune(comp_of(holder, "ParticleSystem"), 0, 0, 2, (1, 1), None, 0, 0, (0, 0), (0.1, 0.9), bob=BOB)
    tune_renderer(comp_of(holder, "ParticleSystemRenderer"), HOLDER_ORDER)
    log.append("tuned holder (bobbing windows)")
    for ch in children(holder):
        p = MAIN.get(ch.peek_name())
        if p is None: continue
        set_local(ch, (0.0, 0.0, p["z"]))
        set_active(ch, True)
        tune(comp_of(ch, "ParticleSystem"), p["row"], p["rate"], p["max_n"], p["life"], p["vy"], p["vx"], p["noise"],
             (1080, 1080), p["fade"])
        rend = comp_of(ch, "ParticleSystemRenderer")
        tune_renderer(rend, p["order"])
        bokeh_mat_ids.add(rend.read_typetree()["m_Materials"][0]["m_PathID"])
        log.append("tuned %s" % ch.peek_name())

for r in roots_sub:
    adj = find(r, ["BG", "Null_all", "Adjust"])
    if adj is None: continue
    # the strip sprite sits on sorting layer Default, drawn after the BackGround layer the notes and particles use, so
    # it covered them; changing its sorting layer crashes the game, so it is drawn in the opaque pass instead, through
    # the switched-off procedural sky's material turned into Sprites/Default at queue BEHIND_QUEUE
    sky_mr = comp_of(find(adj, ["Sky"]), "MeshRenderer")
    behind_mat_ids.add(sky_mr.read_typetree()["m_Materials"][0]["m_PathID"])
    sr = comp_of(find(adj, ["BG"]), "SpriteRenderer")
    t = sr.read_typetree()
    t["m_Materials"] = [{"m_FileID": 0, "m_PathID": sky_mr.read_typetree()["m_Materials"][0]["m_PathID"]}]
    sr.save_typetree(t)
    log.append("sub BG sprite -> sky material (opaque pass)")
    tp = find(adj, ["ToneParticle"])
    if tp is None: continue
    rip = find(tp, ["Ripple"])
    if rip is not None: set_active(rip, False)
    # ToneParticle (stock white dots) becomes the sub windows bobber; its 0.98 scale goes to 1 so the pixel art stays 1:1
    tpt = comp_of(tp, "Transform").read_typetree()["m_LocalPosition"]
    set_local(tp, (tpt["x"], tpt["y"], tpt["z"]), identity_rot=False, unit_scale=True)
    bgp = comp_of(find(adj, ["BG"]), "Transform").read_typetree()["m_LocalPosition"]   # strip centre = (539, 225)
    sub_bob = dict(SUB_BOB, offset=(bgp["x"] + SUB_BOB_CENTRE[0] - 539 - tpt["x"], bgp["y"] - (SUB_BOB_CENTRE[1] - 225) - tpt["y"]))
    tune(comp_of(tp, "ParticleSystem"), 0, 0, 2, (1, 1), None, 0, 0, (0, 0), (0.1, 0.9), bob=sub_bob)
    part = find(tp, ["Particle"])
    if part is not None:
        set_local(part, (-tpt["x"], -tpt["y"], 10.0))    # centre the box on the strip
        tune(comp_of(part, "ParticleSystem"), 1, 0.35, 3, (10.0, 14.0), (10.0, 16.0), 4.0, 3.0, (1078, 450), (0.15, 0.8))
        rend = comp_of(part, "ParticleSystemRenderer")
        tune_renderer(rend, 1)
        bokeh_mat = rend.read_typetree()["m_Materials"][0]["m_PathID"]
        bokeh_mat_ids.add(bokeh_mat)
        tune_renderer(comp_of(tp, "ParticleSystemRenderer"), 0, material=bokeh_mat)
        log.append("tuned sub Particle + ToneParticle (bobbing windows)")

# the main sky plane (Sprites/Default since ngo_bg_assets.py, sorting layer Default) likewise goes to the opaque pass
for r in roots_main:
    sky = find(r, ["Adjust_WorldPos", "BG_Sky"])
    if sky is not None:
        behind_mat_ids.add(comp_of(sky, "MeshRenderer").read_typetree()["m_Materials"][0]["m_PathID"])

# atlas into the lake's rock texture (the lake is switched off everywhere; that material is its only user)
lake = next(o for o in env.objects if o.type.name == "Texture2D" and o.peek_name() == "T_v170BG_LakeRocks")
tx = lake.read()
atlas = Image.open(ATLAS_PNG).convert("RGBA")
assert atlas.size == (TILE * COLS, TILE * ROWS), atlas.size
tx.m_Width, tx.m_Height = atlas.size
tx.set_image(atlas, target_format=TextureFormat.RGBA32)
tx.m_MipCount = 1
tx.m_TextureSettings.m_FilterMode = 1          # bilinear: point filtering made slow drift snap pixel by pixel
tx.m_TextureSettings.m_WrapU = tx.m_TextureSettings.m_WrapV = 1
if hasattr(tx.m_TextureSettings, "m_WrapW"): tx.m_TextureSettings.m_WrapW = 1
tx.save()
log.append("texture T_v170BG_LakeRocks <- float atlas %dx%d" % atlas.size)

ext = [e.path.lower() for e in env.file.externals]
sprites_default_shader = {"m_FileID": ext.index("resources/unity_builtin_extra") + 1, "m_PathID": 10753}
for mid in bokeh_mat_ids:
    m = env.file.objects[mid]
    t = m.read_typetree()
    t["m_Shader"] = sprites_default_shader
    sp = t["m_SavedProperties"]
    sp["m_TexEnvs"] = [e for e in sp["m_TexEnvs"] if e[0] != "_MainTex"] + [
        ("_MainTex", {"m_Texture": {"m_FileID": 0, "m_PathID": lake.path_id}, "m_Scale": {"x": 1.0, "y": 1.0}, "m_Offset": {"x": 0.0, "y": 0.0}})]
    cols = dict(sp["m_Colors"])
    cols["_Color"] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
    cols["_RendererColor"] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
    sp["m_Colors"] = list(cols.items())
    t["m_ShaderKeywords"] = ""; t["m_CustomRenderQueue"] = -1
    m.save_typetree(t)
    log.append("material %s -> Sprites/Default + float atlas" % t["m_Name"])
for mid in behind_mat_ids:
    m = env.file.objects[mid]
    t = m.read_typetree()
    t["m_Shader"] = sprites_default_shader
    sp = t["m_SavedProperties"]
    cols = dict(sp["m_Colors"])
    cols["_Color"] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
    cols["_RendererColor"] = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
    sp["m_Colors"] = list(cols.items())
    t["m_ShaderKeywords"] = ""; t["m_CustomRenderQueue"] = BEHIND_QUEUE
    m.save_typetree(t)
    log.append("material %s -> Sprites/Default queue %d" % (t["m_Name"], BEHIND_QUEUE))

# the sub note / symbol sprites carry tight meshes traced around the stock notes and clef, which clip our icons and
# window: give each a plain full-rect quad (layout copied from T_v170SubBG: positions stream, then uv stream)
for o in env.objects:
    if o.type.name != "Sprite": continue
    n = o.peek_name()
    if not (n.startswith("T_v170SubBG_Onpu") or n == "T_v170SubBG_ToneSymbol"): continue
    t = o.read_typetree()
    rd, rect, ppu = t["m_RD"], t["m_Rect"], t["m_PixelsToUnits"]
    w, h = rect["width"] / ppu / 2, rect["height"] / ppu / 2
    pos = struct.pack("<12f", -w, h, 0, w, -h, 0, w, h, 0, -w, -h, 0)
    uv = struct.pack("<8f", 0, 1, 1, 0, 1, 1, 0, 0)
    vd = rd["m_VertexData"]
    vd["m_VertexCount"] = 4
    as_field = lambda old, b: b if isinstance(old, (bytes, bytearray)) else list(b)
    vd["m_DataSize"] = as_field(vd["m_DataSize"], pos + uv)
    rd["m_IndexBuffer"] = as_field(rd["m_IndexBuffer"], struct.pack("<6H", 3, 0, 1, 2, 1, 0))
    sm = rd["m_SubMeshes"][0]
    sm["indexCount"], sm["vertexCount"], sm["firstByte"], sm["baseVertex"], sm["firstVertex"] = 6, 4, 0, 0, 0
    rd["textureRect"] = {"x": 0.0, "y": 0.0, "width": rect["width"], "height": rect["height"]}
    rd["textureRectOffset"] = {"x": 0.0, "y": 0.0}
    o.save_typetree(t)
    log.append("sprite %s -> full quad" % n)

# background paintings without the floats that now move
for name, png, rot in (("T_v170BG_Rainbow", MAIN_PNG, True), ("T_v170SubBG", SUB_PNG, False),
                       ("T_v170SubBG_ToneSymbol", SYMBOL_PNG, False)):
    o = next(o for o in env.objects if o.type.name == "Texture2D" and o.peek_name() == name)
    t = o.read()
    img = Image.open(png).convert("RGBA")
    if rot: img = img.rotate(180)
    assert img.size == (t.m_Width, t.m_Height), (name, img.size, (t.m_Width, t.m_Height))
    t.set_image(img, target_format=TextureFormat(t.m_TextureFormat))
    t.m_MipCount = 1
    t.save()
    log.append("texture %s <- %s" % (name, os.path.basename(png)))

data = env.file.save()
assert len(data) > 60 * 1024 * 1024, "output unexpectedly small: %d bytes" % len(data)
with open(ASSETS + ".new", "wb") as f:
    f.write(data)
print("\n".join(log))
