"""maimai DX 1.70 (SDEZ) x NEEDY GIRL OVERDOSE skin patcher.

Writes the skin's exported data into the player's OWN game files:
  - texture pixels, sprite meshes, colour fields, materials and scene edits into Sinmai_Data/resources.assets and a
    few StreamingAssets bundles (object by object, the rest of each file is left as it is);
  - line edits into the sort-colour XML files under StreamingAssets/A000;
  - the files the skin adds (PartnerLive2D mod, its LocalAssets folder, the A500 partner data).
Every original file is copied to <Package>/NGO_skin_backup first. Running install again rebuilds everything from
that backup (so it is safe to re-run and to update), uninstall puts the originals back.

usage:
  ngo_patcher install   [<Package folder>] [--patch <dir>] [--yes] [--rebase]
  ngo_patcher uninstall [<Package folder>] [--yes]
  ngo_patcher status    [<Package folder>] [--patch <dir>]
  ngo_patcher compare   <Package A> <Package B> [--patch <dir>] [--report <json>]   (developer check)
Without a Package folder it looks next to itself and in its parent folders, then asks.
"""
import argparse, base64, ctypes, datetime, hashlib, io, json, math, os, shutil, sys, tempfile, time

VERSION = "1.0.0"
BACKUP_DIR = "NGO_skin_backup"
STATE = "state.json"
UNITY_VERSION = "2018.4.7f1"
# Texture2D fields the texture step owns (set from the PNG / encoded data, not from the generic field diff)
TEX_FIELDS = ("image data", "m_StreamData", "m_Width", "m_Height", "m_TextureFormat", "m_MipCount",
              "m_CompleteImageSize", "m_MipMap")
BIG_INLINE = 4096


# ----------------------------------------------------------------------------------------------- value codec
def b64(b):
    return base64.b64encode(bytes(b)).decode("ascii")


def enc(v):
    """typetree value -> JSON-safe value (bytes and long byte lists become base64)."""
    if isinstance(v, (bytes, bytearray, memoryview)):
        return {"$b64": b64(v)}
    if isinstance(v, list):
        if len(v) > 64 and all(type(x) is int and 0 <= x < 256 for x in v):
            return {"$u8": b64(bytes(v))}
        return [enc(x) for x in v]
    if isinstance(v, tuple):
        return [enc(x) for x in v]
    if isinstance(v, dict):
        return {str(k): enc(x) for k, x in v.items()}
    if isinstance(v, float) and (math.isnan(v) or math.isinf(v)):
        return {"$f": repr(v)}
    return v


def dec(v, patch_dir=None):
    """inverse of enc; {"$blob": file} loads bytes kept in a separate file of the patch."""
    if isinstance(v, dict):
        if len(v) == 1:
            k = next(iter(v))
            if k == "$b64": return base64.b64decode(v[k])
            if k == "$u8": return list(base64.b64decode(v[k]))
            if k == "$f": return float(v[k])
        if "$blob" in v:
            with open(os.path.join(patch_dir, v["$blob"]), "rb") as f:
                data = f.read()
            if sha_bytes(data) != v["sha256"]:
                raise PatchError("damaged patch data file: " + v["$blob"])
            return list(data) if v.get("kind") == "u8" else data
        return {k: dec(x, patch_dir) for k, x in v.items()}
    if isinstance(v, list):
        return [dec(x, patch_dir) for x in v]
    return v


def canon(v):
    return json.dumps(enc(v), sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def old_repr(v):
    """how a field's original value is recorded: itself when small, else its hash."""
    s = canon(v)
    return {"$sha256": sha_bytes(s.encode("ascii"))} if len(s) > 512 else json.loads(s)


def matches(cur, rec):
    if isinstance(rec, dict) and set(rec) == {"$sha256"}:
        return sha_bytes(canon(cur).encode("ascii")) == rec["$sha256"]
    return canon(cur) == json.dumps(rec, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def same(a, b):
    # canonical text, so -0.0 and 0.0 (different bytes in the file) count as different
    return canon(a) == canon(b)


def tdiff(a, b, path=None, out=None, skip=()):
    """field-level diff of two typetrees: [(path, old, new)]; lists of equal length are compared element-wise."""
    path = path or []
    out = [] if out is None else out
    if isinstance(a, dict) and isinstance(b, dict) and a.keys() == b.keys():
        for k in a:
            if not path and k in skip:
                continue
            tdiff(a[k], b[k], path + [k], out)
    elif (isinstance(a, list) and isinstance(b, list) and len(a) == len(b) and a
          and all(isinstance(x, (dict, list)) for x in a)):
        for i, (x, y) in enumerate(zip(a, b)):
            tdiff(x, y, path + [i], out)
    elif not same(a, b):
        out.append((path, a, b))
    return out


def get_at(tree, path):
    for p in path:
        tree = tree[p]
    return tree


def set_at(tree, path, v):
    get_at(tree, path[:-1])[path[-1]] = v


# ----------------------------------------------------------------------------------------------- helpers
class PatchError(Exception):
    pass


def sha_bytes(b):
    return hashlib.sha256(b).hexdigest()


def sha_file(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for blk in iter(lambda: f.read(1 << 22), b""):
            h.update(blk)
    return h.hexdigest()


def pixels_sha(img):
    return sha_bytes(("%dx%d:" % img.size).encode("ascii") + img.tobytes())


def say(*a):
    print(*a, flush=True)


def native(pkg, rel):
    return os.path.join(pkg, *rel.split("/"))


def here():
    return os.path.dirname(os.path.abspath(sys.executable if getattr(sys, "frozen", False) else __file__))


def default_patch_dir():
    for c in (os.path.join(here(), "patch"), os.path.join(here(), "..", "patch")):
        if os.path.isfile(os.path.join(c, "manifest.json")):
            return os.path.abspath(c)
    raise PatchError("patch folder not found next to the patcher (expected patch\\manifest.json)")


def is_package(p):
    return bool(p) and os.path.isfile(os.path.join(p, "Sinmai.exe")) and \
        os.path.isfile(os.path.join(p, "Sinmai_Data", "resources.assets"))


def find_package(arg, interactive):
    if arg:
        p = os.path.abspath(arg.strip().strip('"'))
        if is_package(p): return p
        if is_package(os.path.join(p, "Package")): return os.path.join(p, "Package")
        raise PatchError("not a maimai Package folder (Sinmai.exe + Sinmai_Data): " + p)
    d = here()
    for _ in range(4):
        for c in (d, os.path.join(d, "Package")):
            if is_package(c): return c
        d = os.path.dirname(d)
    if not interactive:
        raise PatchError("give the Package folder (the one with Sinmai.exe)")
    say("找不到游戏文件夹。请把 Package 文件夹（有 Sinmai.exe 的那个）拖进这个窗口，然后按回车：")
    say("Game folder not found. Drag the Package folder (with Sinmai.exe) into this window and press Enter:")
    return find_package(input("> "), False)


# ---------------------------------------------------------------- is the game of THIS folder running? (no psutil)
class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", ctypes.c_uint32), ("cntUsage", ctypes.c_uint32), ("th32ProcessID", ctypes.c_uint32),
                ("th32DefaultHeapID", ctypes.c_size_t), ("th32ModuleID", ctypes.c_uint32),
                ("cntThreads", ctypes.c_uint32), ("th32ParentProcessID", ctypes.c_uint32),
                ("pcPriClassBase", ctypes.c_long), ("dwFlags", ctypes.c_uint32), ("szExeFile", ctypes.c_wchar * 260)]


def running_games(pkg):
    """Sinmai.exe processes with live threads: (pid, path or None) for this Package or of unknown path."""
    if os.name != "nt":
        return []
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateToolhelp32Snapshot.restype = ctypes.c_void_p
    k32.OpenProcess.restype = ctypes.c_void_p
    snap = k32.CreateToolhelp32Snapshot(0x2, 0)
    if not snap or snap == ctypes.c_void_p(-1).value:
        return []
    want = os.path.normcase(os.path.abspath(os.path.join(pkg, "Sinmai.exe")))
    found = []
    e = PROCESSENTRY32W()
    e.dwSize = ctypes.sizeof(e)
    ok = k32.Process32FirstW(ctypes.c_void_p(snap), ctypes.byref(e))
    while ok:
        if e.szExeFile.lower() == "sinmai.exe" and e.cntThreads > 0:
            path = None
            h = k32.OpenProcess(0x1000, False, e.th32ProcessID)   # PROCESS_QUERY_LIMITED_INFORMATION
            if h:
                buf = ctypes.create_unicode_buffer(1024)
                n = ctypes.c_uint32(1024)
                if k32.QueryFullProcessImageNameW(ctypes.c_void_p(h), 0, buf, ctypes.byref(n)):
                    path = buf.value
                k32.CloseHandle(ctypes.c_void_p(h))
            if path is None or os.path.normcase(os.path.abspath(path)) == want:
                found.append((e.th32ProcessID, path))
        ok = k32.Process32NextW(ctypes.c_void_p(snap), ctypes.byref(e))
    k32.CloseHandle(ctypes.c_void_p(snap))
    return found


def refuse_if_running(pkg):
    procs = running_games(pkg)
    if procs:
        raise PatchError("游戏正在运行，请先关掉游戏 / The game in this folder is running, close it first: " +
                         ", ".join("pid %d %s" % (pid, path or "(path unknown)") for pid, path in procs))
    # second guard: the game keeps resources.assets open; an exclusive open fails while it does
    p = os.path.join(pkg, "Sinmai_Data", "resources.assets")
    try:
        fd = os.open(p, os.O_RDWR | getattr(os, "O_BINARY", 0))
        os.close(fd)
    except OSError as ex:
        raise PatchError("resources.assets is in use (game running?) / 文件被占用: %s" % ex)


# ----------------------------------------------------------------------------------------------- patch data
class Patch:
    def __init__(self, d):
        self.dir = os.path.abspath(d)
        with open(os.path.join(self.dir, "manifest.json"), encoding="utf-8") as f:
            self.m = json.load(f)
        if self.m.get("format") != 1:
            raise PatchError("unsupported patch format")
        self.targets = []
        for t in self.m["targets"]:
            with open(os.path.join(self.dir, t["objects"]), encoding="utf-8") as f:
                t = dict(t, entries=json.load(f)["objects"])
            self.targets.append(t)
        self.xml = []
        if self.m.get("xml"):
            with open(os.path.join(self.dir, self.m["xml"]), encoding="utf-8") as f:
                self.xml = json.load(f)["files"]
        self.files = []
        if self.m.get("files"):
            with open(os.path.join(self.dir, self.m["files"]), encoding="utf-8") as f:
                self.files = json.load(f)["files"]

    def path(self, rel):
        return os.path.join(self.dir, *rel.split("/"))

    def touched(self):
        return [t["file"] for t in self.targets] + [x["path"] for x in self.xml] + [f["path"] for f in self.files]


# ---------------------------------------------------------------------------------- Unity file read / write
def open_env(pkg, rel, data, kind):
    """(environment, file object) for one game file given as bytes (usually the backup copy)."""
    import UnityPy
    if kind == "assets":
        # externals (globalgamemanagers.assets, unity_builtin_extra, the .resS) resolve in the game's own folder
        env = UnityPy.Environment(path=os.path.dirname(native(pkg, rel)))
        f = env.load_file(data, name=os.path.basename(rel))
    else:
        env = UnityPy.load(data)
        f = env.file
    return env, f


_GEN = {}


def generator(pkg):
    if pkg not in _GEN:
        from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
        g = TypeTreeGenerator(UNITY_VERSION)
        g.load_local_dll_folder(os.path.join(pkg, "Sinmai_Data", "Managed"))
        _GEN[pkg] = g
    return _GEN[pkg]


def object_index(env, kind):
    idx = {}
    for o in env.objects:
        idx[(o.assets_file.name if kind == "bundle" else None, o.path_id)] = o
    return idx


def encode_texture(img, fmt, mips):
    from PIL import Image
    from UnityPy.export import Texture2DConverter
    data, tf = Texture2DConverter.image_to_texture2d(img, fmt)
    w, h = img.size
    n = 1
    re_img = img
    for _ in range(max(0, mips - 1)):
        w //= 2; h //= 2
        if w < 4 or h < 4:
            break
        re_img = re_img.resize((w, h), Image.Resampling.BICUBIC)
        data += Texture2DConverter.image_to_texture2d(re_img, fmt)[0]
        n += 1
    return data, int(tf), n


class Exact:
    """optional pack with the reference install's exact encoded texture bytes (release package only)."""

    def __init__(self, patch, name):
        self.f = None
        if name and os.path.isfile(patch.path(name)):
            self.f = open(patch.path(name), "rb")

    def get(self, spec):
        if not self.f or "exact" not in spec:
            return None
        e = spec["exact"]
        self.f.seek(e["offset"])
        data = self.f.read(e["size"])
        return data if sha_bytes(data) == e["sha256"] else None

    def close(self):
        if self.f: self.f.close()


def build_target(pkg, patch, t, src_bytes, stats):
    """apply one target's object entries to src_bytes; returns the new file bytes. Raises on any conflict."""
    from PIL import Image
    env, fobj = open_env(pkg, t["file"], src_bytes, t["kind"])
    if any(e["type"] == "MonoBehaviour" for e in t["entries"]):
        env.typetree_generator = generator(pkg)
    idx = object_index(env, t["kind"])
    exact = Exact(patch, t.get("exact"))
    conflicts = []
    try:
        for e in t["entries"]:
            o = idx.get((e.get("cab"), e["path_id"]))
            label = "%s %s (%s)" % (e["type"], e.get("name", ""), e["path_id"])
            if o is None or o.type.name != e["type"]:
                conflicts.append(label + ": object missing / type differs"); continue
            tree = o.read_typetree()
            bad = []
            for c in e["changes"]:
                try:
                    cur = get_at(tree, c["path"])
                except (KeyError, IndexError, TypeError):
                    bad.append("/".join(map(str, c["path"]))); continue
                if not matches(cur, c["old"]) and not same(cur, dec(c["new"], patch.dir)):
                    bad.append("/".join(map(str, c["path"])))
            if bad:
                conflicts.append(label + ": unexpected original value in " + ", ".join(bad[:4])); continue
            for c in e["changes"]:
                set_at(tree, c["path"], dec(c["new"], patch.dir))
            spec = e.get("texture")
            if spec:
                img = Image.open(patch.path(spec["png"])).convert("RGBA")
                data = exact.get(spec) if pixels_sha(img) == spec["pixels_sha256"] else None
                if data is not None:
                    fmt, mips = spec["format"], spec["mip_count"]
                    stats["exact"] += 1
                else:
                    data, fmt, mips = encode_texture(img, spec["format"], spec["mip_count"])
                    stats["encoded"] += 1
                tree["m_Width"], tree["m_Height"] = img.size
                tree["m_TextureFormat"] = fmt
                if "m_MipCount" in tree: tree["m_MipCount"] = mips
                if "m_MipMap" in tree: tree["m_MipMap"] = mips > 1
                tree["m_CompleteImageSize"] = len(data)
                tree["image data"] = data
                sd = tree.get("m_StreamData")
                if isinstance(sd, dict):
                    sd["offset"], sd["size"], sd["path"] = 0, 0, ""
            o.save_typetree(tree)
            stats["objects"] += 1
    finally:
        exact.close()
    if conflicts:
        raise PatchError("%s: %d object(s) do not match SDEZ 1.70, nothing written. First: %s"
                         % (t["file"], len(conflicts), "; ".join(conflicts[:3])))
    if t["kind"] == "bundle":
        return fobj.save(packer=t.get("packer", "lz4"))
    return fobj.save()


def apply_xml(x, data):
    """line hunks on the raw bytes (line endings and BOM kept)."""
    lines = data.split(b"\n")
    for h in sorted(x["hunks"], key=lambda h: -h["at"]):
        at, old, new = h["at"], h["old"], h["new"]
        cur = [l.rstrip(b"\r").decode("utf-8-sig" if at + i == 0 else "utf-8") for i, l in enumerate(lines[at:at + len(old)])]
        if cur != old:
            raise PatchError("%s: line %d is not the SDEZ 1.70 original" % (x["path"], at + 1))
        cr = lines[at].endswith(b"\r") if at < len(lines) else False
        bom = b"\xef\xbb\xbf" if at == 0 and lines[0].startswith(b"\xef\xbb\xbf") else b""
        repl = [(bom if i == 0 else b"") + s.encode("utf-8") + (b"\r" if cr else b"") for i, s in enumerate(new)]
        lines[at:at + len(old)] = repl
    if x.get("eol") == "lf":          # the skin's files were written back with LF line ends
        lines = [l.rstrip(b"\r") for l in lines]
    elif x.get("eol") == "crlf":
        lines = [l if (i == len(lines) - 1 or l.endswith(b"\r")) else l + b"\r" for i, l in enumerate(lines)]
    return b"\n".join(lines)


# ----------------------------------------------------------------------------------------------- backup state
def load_state(pkg):
    p = os.path.join(pkg, BACKUP_DIR, STATE)
    if os.path.isfile(p):
        with open(p, encoding="utf-8") as f:
            return json.load(f)
    return None


def save_state(pkg, st):
    d = os.path.join(pkg, BACKUP_DIR)
    os.makedirs(d, exist_ok=True)
    tmp = os.path.join(d, STATE + ".tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(st, f, ensure_ascii=False, indent=1)
    os.replace(tmp, os.path.join(d, STATE))


def backup_path(pkg, rel):
    return os.path.join(pkg, BACKUP_DIR, "files", *rel.split("/"))


def write_replace(dst, data):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    tmp = dst + ".ngo-new"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, dst)


def copy_verified(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    tmp = dst + ".ngo-new"
    shutil.copyfile(src, tmp)
    if sha_file(tmp) != sha_file(src):
        os.remove(tmp)
        raise PatchError("copy check failed: " + dst)
    os.replace(tmp, dst)


# ----------------------------------------------------------------------------------------------- commands
def cmd_install(pkg, patch, yes=False, rebase=False):
    refuse_if_running(pkg)
    say("游戏 / game:  ", pkg)
    say("补丁 / patch: ", patch.m.get("name"), "(%d 个对象改动 / object edits, %d XML, %d 个文件 / files)"
        % (sum(len(t["entries"]) for t in patch.targets), len(patch.xml), len(patch.files)))
    missing = [f for f in patch.files if not os.path.isfile(patch.path(f["src"]))]
    hard = [f for f in missing if not f.get("release_only")]
    if hard:
        raise PatchError("patch folder incomplete, missing: " + ", ".join(f["src"] for f in hard[:5]))
    if missing:
        say("注意 / note: %d release-only files are not in this patch folder (download the release zip):" % len(missing))
        for f in missing[:5]: say("   ", f["path"])
    if not yes:
        ans = input("开始安装？/ Install now? [Y/n] ").strip().lower()
        if ans not in ("", "y", "yes"):
            say("cancelled"); return 1
    st = load_state(pkg) or {"format": 1, "entries": {}}
    entries = st["entries"]
    stock = {t["file"]: t.get("stock_sha256") for t in patch.targets}
    stock.update({x["path"]: x.get("stock_sha256") for x in patch.xml})
    sources = {}
    # 1. backups first
    for rel in patch.touched():
        cur = native(pkg, rel)
        exists = os.path.isfile(cur)
        is_game_file = rel in stock
        if rel in entries:
            ent = entries[rel]
            src = backup_path(pkg, rel) if ent["existed"] else None
            if exists and is_game_file and ent.get("written_sha256") and sha_file(cur) not in (ent["written_sha256"], ent.get("orig_sha256")):
                cur_sha = sha_file(cur)
                if cur_sha == stock[rel] or rebase:
                    say("   %s changed since the last install (game update / other tool): taking it as the new original" % rel)
                    copy_verified(cur, backup_path(pkg, rel))
                    ent.update(existed=True, orig_sha256=cur_sha)
                    src = backup_path(pkg, rel)
                else:
                    raise PatchError("%s was changed by something else since the skin was installed. Run uninstall, "
                                     "or install --rebase to treat the current file as the original." % rel)
            if src and sha_file(src) != ent["orig_sha256"]:
                raise PatchError("backup damaged: " + src)
            sources[rel] = src
        else:
            if exists:
                if is_game_file and stock[rel] and sha_file(cur) != stock[rel]:
                    say("   注意 / note: %s is not the stock SDEZ 1.70 file; trying object by object" % rel)
                copy_verified(cur, backup_path(pkg, rel))
                entries[rel] = {"existed": True, "orig_sha256": sha_file(cur)}
                sources[rel] = backup_path(pkg, rel)
            else:
                entries[rel] = {"existed": False, "orig_sha256": None}
                sources[rel] = None
    st.update(patch_name=patch.m.get("name"), patcher=VERSION, updated=datetime.datetime.now().isoformat(timespec="seconds"))
    st.setdefault("installed", st["updated"])
    save_state(pkg, st)
    say("1/3 原文件已备份 / originals backed up to", os.path.join(pkg, BACKUP_DIR))
    # 2. build every game file in memory / next to its target, commit only when all succeeded
    stats = {"objects": 0, "exact": 0, "encoded": 0}
    built = {}
    for t in patch.targets:
        src = sources[t["file"]]
        if src is None:
            raise PatchError("game file missing: " + t["file"])
        with open(src, "rb") as f:
            data = f.read()
        say("   patching", t["file"], "(%d objects)..." % len(t["entries"]))
        built[t["file"]] = build_target(pkg, patch, t, data, stats)
    for x in patch.xml:
        src = sources[x["path"]]
        if src is None:
            raise PatchError("game file missing: " + x["path"])
        with open(src, "rb") as f:
            built[x["path"]] = apply_xml(x, f.read())
    refuse_if_running(pkg)
    for rel, data in built.items():
        write_replace(native(pkg, rel), data)
        entries[rel]["written_sha256"] = sha_bytes(data)
    say("2/3 游戏数据已写入 / game data written: %d objects (%d exact textures, %d re-encoded), %d XML"
        % (stats["objects"], stats["exact"], stats["encoded"], len(patch.xml)))
    # 3. added files
    edited = []
    n = 0
    for f in patch.files:
        srcp = patch.path(f["src"])
        if not os.path.isfile(srcp):
            continue
        dst = native(pkg, f["path"])
        ent = entries[f["path"]]
        if os.path.isfile(dst) and ent.get("written_sha256") and sha_file(dst) != ent["written_sha256"]:
            keep = os.path.join(pkg, BACKUP_DIR, "user-edits", *f["path"].split("/"))
            os.makedirs(os.path.dirname(keep), exist_ok=True)
            shutil.copyfile(dst, keep)
            edited.append(f["path"])
        copy_verified(srcp, dst)
        ent["written_sha256"] = f["sha256"]
        n += 1
    save_state(pkg, st)
    say("3/3 模组和素材文件 / mod and asset files: %d" % n)
    for e in edited:
        say("   你改过的 %s 已另存到 NGO_skin_backup\\user-edits / your edited copy was kept" % e)
    for t in patch.targets:
        if t.get("patched_sha256") and entries[t["file"]].get("written_sha256") == t["patched_sha256"]:
            say("   %s: byte-identical to the reference install" % t["file"])
    if not os.path.isfile(os.path.join(pkg, "Live2DCubismCore.dll")):
        say("注意 / note: Live2DCubismCore.dll is not next to Sinmai.exe; the PartnerLive2D mod stays off without it "
            "(see README: Cubism SDK for Native, Core/dll/windows/x86_64).")
    say("安装完成 / Installed.")
    return 0


def cmd_uninstall(pkg, yes=False):
    refuse_if_running(pkg)
    st = load_state(pkg)
    if not st:
        say("没有安装记录 / The skin is not installed here (no %s)." % BACKUP_DIR); return 1
    if not yes:
        ans = input("卸载并恢复原版文件？/ Uninstall and restore the original files? [Y/n] ").strip().lower()
        if ans not in ("", "y", "yes"):
            say("cancelled"); return 1
    restored = removed = 0
    dirs = set()
    for rel, ent in st["entries"].items():
        dst = native(pkg, rel)
        if ent["existed"]:
            b = backup_path(pkg, rel)
            if sha_file(b) != ent["orig_sha256"]:
                raise PatchError("backup damaged, nothing more restored: " + b)
            copy_verified(b, dst)
            restored += 1
        elif os.path.isfile(dst):
            os.remove(dst)
            removed += 1
            dirs.add(os.path.dirname(dst))
    for d in sorted(dirs, key=len, reverse=True):     # remove the folders the skin created, if now empty
        while d.startswith(pkg) and os.path.isdir(d) and not os.listdir(d) and os.path.normcase(d) != os.path.normcase(pkg):
            os.rmdir(d)
            d = os.path.dirname(d)
    keep = os.path.join(pkg, BACKUP_DIR, "user-edits")
    if os.path.isdir(keep):
        dst = os.path.join(pkg, "NGO_skin_user_edits_" + time.strftime("%Y%m%d_%H%M%S"))
        shutil.move(keep, dst)
        say("   your edited files were kept in", dst)
    shutil.rmtree(os.path.join(pkg, BACKUP_DIR))
    say("卸载完成 / Uninstalled: %d original files restored, %d added files removed." % (restored, removed))
    return 0


def cmd_status(pkg, patch):
    st = load_state(pkg)
    say("game:", pkg)
    if not st:
        say("skin not installed (no %s)" % BACKUP_DIR)
    else:
        say("installed:", st.get("patch_name"), "at", st.get("installed"), "updated", st.get("updated"))
    stock = {t["file"]: t.get("stock_sha256") for t in patch.targets} if patch else {}
    if patch:
        stock.update({x["path"]: x.get("stock_sha256") for x in patch.xml})
    rels = patch.touched() if patch else list((st or {}).get("entries", {}))
    counts = {}
    for rel in rels:
        p = native(pkg, rel)
        ent = (st or {}).get("entries", {}).get(rel, {})
        if not os.path.isfile(p):
            s = "missing"
        else:
            h = sha_file(p)
            s = ("skin" if h == ent.get("written_sha256") else "original" if h == ent.get("orig_sha256")
                 else "stock" if stock.get(rel) == h else "other")
        counts[s] = counts.get(s, 0) + 1
        if s == "other" and rel in stock:
            say("   changed by something else:", rel)
    say("files:", ", ".join("%s %d" % kv for kv in sorted(counts.items())))
    return 0


def cmd_compare(a, b, patch, report):
    """developer check: every file the patch touches, Package A vs Package B (objects, pixels, fields, bytes)."""
    try:
        import numpy as np
    except ImportError:
        raise PatchError("compare needs numpy: run it from source (python patcher/ngo_patcher.py compare ...)")
    out = {"targets": [], "xml": [], "files": []}
    for t in patch.targets:
        ra = {"file": t["file"]}
        fa, fb = native(a, t["file"]), native(b, t["file"])
        ra["file_identical"] = sha_file(fa) == sha_file(fb)
        with open(fa, "rb") as f1, open(fb, "rb") as f2:
            ea, eb = open_env(a, t["file"], f1.read(), t["kind"])[0], open_env(b, t["file"], f2.read(), t["kind"])[0]
        ea.typetree_generator, eb.typetree_generator = generator(a), generator(b)
        ia, ib = object_index(ea, t["kind"]), object_index(eb, t["kind"])
        keys = set(ia) | set(ib)
        ra["objects"] = len(keys)
        ra["only_a"] = len(set(ia) - set(ib)); ra["only_b"] = len(set(ib) - set(ia))
        raw_same = 0
        diffs = []
        for k in sorted(set(ia) & set(ib), key=lambda k: (str(k[0]), k[1])):
            oa, ob = ia[k], ib[k]
            if oa.get_raw_data() == ob.get_raw_data():
                raw_same += 1; continue
            d = {"cab": k[0], "path_id": k[1], "type": ob.type.name}
            try:
                d["name"] = ob.peek_name()
            except Exception:
                pass
            if ob.type.name == "Texture2D":
                ta, tb = oa.read(), ob.read()
                pa, pb = np.asarray(ta.image.convert("RGBA"), np.int16), np.asarray(tb.image.convert("RGBA"), np.int16)
                if pa.shape != pb.shape:
                    d["pixels"] = "size %s vs %s" % (pa.shape, pb.shape)
                else:
                    dd = np.abs(pa - pb)
                    d["pixels"] = {"identical": bool(dd.max() == 0), "mean_abs": round(float(dd.mean()), 4),
                                   "max_abs": int(dd.max()), "pct_px_changed": round(100.0 * float((dd.max(2) > 0).mean()), 3)}
                d["fields"] = ["/".join(map(str, p)) for p, x, y in tdiff(oa.read_typetree(), ob.read_typetree(), skip=("image data",))]
            else:
                try:
                    d["fields"] = ["/".join(map(str, p)) for p, x, y in tdiff(oa.read_typetree(), ob.read_typetree())]
                except Exception as ex:
                    d["fields"] = "typetree read failed: %s" % ex
            diffs.append(d)
        ra["raw_identical"] = raw_same
        ra["differing"] = diffs
        out["targets"].append(ra)
        say("%s: file identical=%s, objects %d, raw-identical %d, differing %d"
            % (t["file"], ra["file_identical"], len(keys), raw_same, len(diffs)))
    for x in patch.xml:
        out["xml"].append({"path": x["path"], "identical": sha_file(native(a, x["path"])) == sha_file(native(b, x["path"]))})
    say("XML identical: %d / %d" % (sum(r["identical"] for r in out["xml"]), len(out["xml"])))
    for f in patch.files:
        pa, pb = native(a, f["path"]), native(b, f["path"])
        r = {"path": f["path"], "a": os.path.isfile(pa), "b": os.path.isfile(pb)}
        if r["a"] and r["b"]:
            r["identical"] = sha_file(pa) == sha_file(pb)
        out["files"].append(r)
    say("added files identical: %d / %d" % (sum(1 for r in out["files"] if r.get("identical")), len(out["files"])))
    if report:
        with open(report, "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(prog="ngo_patcher", description="maimai NGO skin patcher " + VERSION)
    ap.add_argument("command", nargs="?", choices=["install", "uninstall", "status", "compare"])
    ap.add_argument("package", nargs="?")
    ap.add_argument("package_b", nargs="?")
    ap.add_argument("--patch")
    ap.add_argument("--yes", action="store_true")
    ap.add_argument("--rebase", action="store_true")
    ap.add_argument("--report")
    ap.add_argument("--no-pause", action="store_true")
    a = ap.parse_args(argv)
    interactive = sys.stdin is not None and sys.stdin.isatty() and not a.yes
    try:
        cmd = a.command
        if not cmd:
            say("maimai NGO skin patcher", VERSION)
            say("  1 = 安装 install   2 = 卸载 uninstall   3 = 状态 status")
            cmd = {"1": "install", "2": "uninstall", "3": "status"}.get(input("> ").strip(), "status")
        if cmd == "compare":
            patch = Patch(a.patch or default_patch_dir())
            cmd_compare(os.path.abspath(a.package), os.path.abspath(a.package_b), patch, a.report)
            return 0
        pkg = find_package(a.package, interactive)
        if cmd == "install":
            return cmd_install(pkg, Patch(a.patch or default_patch_dir()), a.yes, a.rebase)
        if cmd == "uninstall":
            return cmd_uninstall(pkg, a.yes)
        patch = None
        try:
            patch = Patch(a.patch or default_patch_dir())
        except Exception:
            pass
        return cmd_status(pkg, patch)
    except PatchError as ex:
        say("错误 / ERROR:", ex)
        return 2
    except KeyboardInterrupt:
        say("cancelled")
        return 1


if __name__ == "__main__":
    sys.exit(main())
