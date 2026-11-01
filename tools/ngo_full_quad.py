"""Give the named sprites in resources.assets a plain full-rect quad mesh (stock sprites carry meshes traced around the
stock art, which clip redrawn art that reaches elsewhere). Writes resources.assets.new; the caller copies it over.
usage: python ngo_full_quad.py <sprite name> [...]"""
import os, struct, sys
import UnityPy

ASSETS = r"D:\maimai\Package\Sinmai_Data\resources.assets"
names = set(sys.argv[1:])
env = UnityPy.load(ASSETS)
tex_size = {o.path_id: (o.read().m_Width, o.read().m_Height) for o in env.objects if o.type.name == "Texture2D"
            and o.peek_name() in names}
done = []
for o in env.objects:
    if o.type.name != "Sprite" or o.peek_name() not in names: continue
    t = o.read_typetree()
    rd, rect, ppu, piv = t["m_RD"], t["m_Rect"], t["m_PixelsToUnits"], t["m_Pivot"]
    TW, TH = tex_size[rd["texture"]["m_PathID"]]
    W, H = rect["width"], rect["height"]
    assert abs(W - TW) < 1 and abs(H - TH) < 1, (o.peek_name(), rect, TW, TH)
    x0, x1 = -piv["x"] * W / ppu, (1 - piv["x"]) * W / ppu
    y0, y1 = -piv["y"] * H / ppu, (1 - piv["y"]) * H / ppu
    u1, v1 = W / TW, H / TH
    pos = struct.pack("<12f", x0, y1, 0, x1, y0, 0, x1, y1, 0, x0, y0, 0)
    uv = struct.pack("<8f", 0, v1, u1, 0, u1, v1, 0, 0)
    as_field = lambda old, b: b if isinstance(old, (bytes, bytearray)) else list(b)
    vd = rd["m_VertexData"]
    vd["m_VertexCount"] = 4
    vd["m_DataSize"] = as_field(vd["m_DataSize"], pos + uv)
    rd["m_IndexBuffer"] = as_field(rd["m_IndexBuffer"], struct.pack("<6H", 3, 0, 1, 2, 1, 0))
    sm = rd["m_SubMeshes"][0]
    sm["indexCount"], sm["vertexCount"], sm["firstByte"], sm["baseVertex"], sm["firstVertex"] = 6, 4, 0, 0, 0
    rd["textureRect"] = {"x": 0.0, "y": 0.0, "width": float(W), "height": float(H)}
    rd["textureRectOffset"] = {"x": 0.0, "y": 0.0}
    o.save_typetree(t)
    done.append(o.peek_name())
assert set(done) == names, names - set(done)
data = env.file.save()
assert len(data) > 60 * 1024 * 1024, "output unexpectedly small: %d bytes" % len(data)
with open(ASSETS + ".new", "wb") as f:
    f.write(data)
print("full quad:", ", ".join(sorted(done)))
