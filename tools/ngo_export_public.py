"""Re-runnable export of the maimai x NEEDY GIRL OVERDOSE skin into the PRIVATE collaboration repo + release staging.

What it does, in order (each step can be run alone with --steps):
  stock    file-level diff of the live install against the stock SDEZ 1.70 archive (central-directory CRC32), and a
           scratch cache of the stock versions of every game file the skin changed (never written into the game);
  patch    object-level export of our edits from the INSTALLED game files: changed textures as PNG (+ exact encoded
           bytes for the release), every other changed field (sprite meshes, colours, materials, transforms,
           particles, MonoBehaviour fields, the font) as JSON data; StreamingAssets XML edits as line hunks;
           files we added (mod assets, A500 partner data, the mod DLL);
  code     mod project (src, mcp, tests, docs, tools, .claude/skills, build files) with private files left out;
  launcher our launcher scripts from the game root;
  art      dot's redraws into git; the Codex/imagegen working art (E:\\cxref\\maimai-ngo\\out) as a release zip;
  scan     privacy grep over everything that goes out (report: public_export/privacy_scan.json);
  commit   git add + commit in the repo (local only, never pushes);
  release  patcher exe (PyInstaller, onedir) + full patch folder + zip in the release staging folder.
Never writes into the game folder, the mod project, E:\\cxref\\maimai-ngo (except public_export\\) or maimai-ngo-dot.
usage: python ngo_export_public.py [--steps stock,patch,code,launcher,art,scrub,release,verify,scan,commit] [--no-art-zip]
scrub: every .md / .txt in the repo and the release folder loses local paths and machine names (placeholders)."""
import argparse, collections, datetime, difflib, fnmatch, hashlib, io, json, os, re, shutil, subprocess, sys, time
import zipfile, zlib

# ------------------------------------------------------------------------------------------------ locations
PROJECT = os.environ.get("NGO_PROJECT", r"E:\claude workspace\maimai-partner-live2d")
CXREF = os.environ.get("NGO_CXREF", r"E:\cxref\maimai-ngo")
DOT = os.environ.get("NGO_DOT", r"E:\claude workspace\maimai-ngo-dot")
GAME_ROOT = os.environ.get("NGO_GAME_ROOT", r"D:\maimai")
PKG = os.path.join(GAME_ROOT, "Package")
STOCK_ZIP = os.environ.get("NGO_STOCK_ZIP", r"D:\maimoller内容\Maimoller专用整合\MaiMai_Magical懒人包\SDEZ1.70_nobga.zip")
SCRATCH = os.environ.get("NGO_SCRATCH", r"E:\maimai-ngo-scratch")
STOCK = os.path.join(SCRATCH, "stockgame", "Package")
REPO = os.environ.get("NGO_REPO", r"E:\claude workspace\maimai-ngo-public")
RELEASE = os.environ.get("NGO_RELEASE", r"E:\claude workspace\maimai-ngo-public-release")
REPORTS = os.path.join(CXREF, "public_export")
VENV_PY = os.environ.get("NGO_VENV_PY", os.path.join(SCRATCH, "venv", "Scripts", "python.exe"))
GIT_NAME, GIT_MAIL = "TOTO-git-q", "TOTO-git-q@users.noreply.github.com"
DATE = time.strftime("%Y%m%d")
REL_NAME = "maimai-ngo-skin-" + DATE
STAGE = os.path.join(RELEASE, REL_NAME)              # release package folder (patcher + full patch)
HOLD = os.path.join(SCRATCH, "release_hold")          # exact texture packs + release-only files between steps
sys.path.insert(0, os.path.join(REPO, "patcher"))
import ngo_patcher as P                                   # noqa: E402  (value codec + Unity helpers, one source of truth)

# files under these roots are ours (everything else the install added is MelonLoader, songs packs, logs...)
ADD_ROOTS = ["LocalAssets/PartnerLive2D/", "Mods/PartnerLive2D.dll", "Sinmai_Data/StreamingAssets/A500/"]
ADD_SKIP = ["LocalAssets/PartnerLive2D/hiyori/*",            # Live2D sample model: its own licence
            "Sinmai_Data/StreamingAssets/A500/partner/partner000501/*",   # Hiyori test partner: its model is not shipped,
                                                                          # so it would only show the stock fallback navi
            "LocalAssets/PartnerLive2D/ngo_pair/*",                       # the Ame & KAngel partner (502) is not shared:
            "Sinmai_Data/StreamingAssets/A500/partner/partner000502/*",   # sprites, model, icon, config and partner data
            "*.bak", "*.before-*", "*.before*", "*-before", "*.off", "*.orig.png", "*.new", "*.ngo-new", "*.log", "*.tmp",
            "LocalAssets/PartnerLive2D/account_edit*",       # one-shot edit of the owner's account
            "*/_qa/*", "*/_sheet.png",                        # review sheets / QA captures
            "LocalAssets/PartnerLive2D/ngo_recolor/*",       # not read by the mod (kept in art/), pixels are in the patch
            "LocalAssets/PartnerLive2D/theme_*"]              # old runtime theme test
RELEASE_ONLY_BYTES = 12 * 1024 * 1024                     # big files stay out of git; so do all sprite packs (*.bin)
REENCODE_ONLY_MAX = 10                                    # a texture within this of stock is a re-encode, not our art


def log(*a):
    print(time.strftime("%H:%M:%S"), *a, flush=True)


def sha_file(p):
    return P.sha_file(p)


def rel_of(root, p):
    return os.path.relpath(p, root).replace("\\", "/")


def skip(rel, pats):
    return any(fnmatch.fnmatch(rel, p) or fnmatch.fnmatch(rel.lower(), p.lower()) for p in pats)


def write_json(p, obj):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    s = json.dumps(obj, ensure_ascii=False, indent=1)
    if os.path.isfile(p):
        with open(p, encoding="utf-8") as f:
            if f.read() == s:
                return
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(s)


def copy_if_changed(src, dst):
    if os.path.isfile(dst) and os.path.getsize(dst) == os.path.getsize(src) and sha_file(dst) == sha_file(src):
        return False
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)
    return True


def mirror(pairs, dst_root, owned=()):
    """make dst_root hold exactly the given (src, rel) files (plus owned rels); returns the rel list."""
    want = {}
    for src, rel in pairs:
        want[rel] = src
    for rel, src in want.items():
        copy_if_changed(src, os.path.join(dst_root, *rel.split("/")))
    if os.path.isdir(dst_root):
        for dp, dn, fn in os.walk(dst_root):
            for f in fn:
                rel = rel_of(dst_root, os.path.join(dp, f))
                if rel not in want and rel not in owned:
                    os.remove(os.path.join(dp, f))
        for dp, dn, fn in sorted(os.walk(dst_root), key=lambda x: -len(x[0])):
            if dp != dst_root and not os.listdir(dp):
                os.rmdir(dp)
    return sorted(want)


# ------------------------------------------------------------------------------------------------ text sanitising
OWNER_USER = os.environ.get("NGO_OWNER_USER") or os.environ.get("USERNAME", "")   # the Windows account to hide
_U = re.escape(OWNER_USER) if OWNER_USER else "(?!x)x"
USER_SUBS = [(re.compile(r"(?i)C:\\\\Users\\\\" + _U), r"%USERPROFILE%"),
             (re.compile(r"(?i)C:\\Users\\" + _U), r"%USERPROFILE%"),
             (re.compile(r"(?i)C:/Users/" + _U), "$HOME"),
             (re.compile(r"(?i)/c/Users/" + _U), "$HOME")]
TEXT_EXT = {".py", ".cs", ".md", ".txt", ".json", ".ini", ".ps1", ".bat", ".sh", ".csproj", ".html", ".js", ".cjs",
            ".csv", ".toml", ".xml", ".yml", ".yaml", ".log", ".diff", ".tsv", ".cfg"}


def sanitized_copy(src, dst):
    """copy; text files get the owner's Windows user folder replaced by %USERPROFILE% / $HOME."""
    ext = os.path.splitext(src)[1].lower()
    if ext in TEXT_EXT:
        with open(src, "rb") as f:
            raw = f.read()
        try:
            txt = raw.decode("utf-8")
        except UnicodeDecodeError:
            txt = None
        if txt is not None:
            new = txt
            for rx, rep in USER_SUBS:
                new = rx.sub(lambda m: rep, new)
            if new != txt:
                data = new.encode("utf-8")
                if os.path.isfile(dst):
                    with open(dst, "rb") as f:
                        if f.read() == data:
                            return
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                with open(dst, "wb") as f:
                    f.write(data)
                return
    copy_if_changed(src, dst)


# Documents (.md / .txt) must carry no local information: the owner's folders, drives and machines become placeholders.
# Code keeps its defaults (they are what makes the scripts run); docs explain the placeholders instead.
DOC_EXT = {".md", ".txt"}
DOC_SUBS = [(re.compile(p, re.I), r) for p, r in [
    (r"[A-Z]:[\\/]+claude workspace[\\/]+maimai-partner-live2d", "<PROJECT>"),
    (r"[A-Z]:[\\/]+claude workspace[\\/]+maimai-ngo-public-release", "<RELEASE_DIR>"),
    (r"[A-Z]:[\\/]+claude workspace[\\/]+maimai-ngo-public", "<REPO>"),
    (r"[A-Z]:[\\/]+claude workspace[\\/]+maimai-ngo-dot", "<ART_REPO>"),
    (r"[A-Z]:[\\/]+claude workspace", "<WORKSPACE>"),
    (r"[A-Z]:[\\/]+cxref[\\/]+maimai-ngo", "<WORK>"),
    (r"[A-Z]:[\\/]+cxref[\\/]+sinmai_src", "<DECOMPILED>"),
    (r"[A-Z]:[\\/]+cxref", "<WORK_ROOT>"),
    (r"[A-Z]:[\\/]+maimai-ngo-scratch", "<SCRATCH>"),
    (r"[A-Z]:[\\/]+maimai[\\/]+backup_ngo_skin", "<GAME_ROOT>\\backup_ngo_skin"),
    (r"[A-Z]:[\\/]+maimai[\\/]+Package", "<PKG>"),
    (r"[A-Z]:[\\/]+maimai(?![\w-])", "<GAME_ROOT>"),
    (r"\b[A-Z]:\\(?=[.,;)\s]|$)", "<GAME_DRIVE>\\"),
    (r"[A-Z]:[\\/]+maimoller[^\s`'\"]*", "<STOCK_SOURCE>"),
    (r"[A-Z]:[\\/]+tools[\\/]+", "<TOOLS>\\"),
    (r"[A-Z]:[\\/]+Users[\\/]+[^\\/\s`'\"]+", "%USERPROFILE%"),
    (r"/[a-z]/Users/[^/\s`'\"]+", "$HOME"),
    (r"\bGT103\b|toto-gt103|\b100\.110\.101\.18\b", "<OTHER_MACHINE>"),
]]


def scrub_text(txt):
    for rx, rep in USER_SUBS:
        txt = rx.sub(lambda m: rep, txt)
    for rx, rep in DOC_SUBS:
        txt = rx.sub(lambda m, rep=rep: rep, txt)
    return txt


def step_scrub():
    """documents in the repo (and the release text) lose every local path / machine name"""
    n = 0
    for root in (REPO, STAGE):
        if not os.path.isdir(root):
            continue
        for dp, dn, fn in os.walk(root):
            dn[:] = [d for d in dn if d not in (".git", "textures", "exact", "targets", "blobs")]
            for f in fn:
                if os.path.splitext(f)[1].lower() not in DOC_EXT:
                    continue
                p = os.path.join(dp, f)
                with open(p, "rb") as h:
                    raw = h.read()
                try:
                    txt = raw.decode("utf-8-sig") if raw.startswith(b"\xef\xbb\xbf") else raw.decode("utf-8")
                except UnicodeDecodeError:
                    continue
                new = scrub_text(txt)
                if new != txt:
                    with open(p, "wb") as h:
                        h.write((b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + new.encode("utf-8"))
                    n += 1
    log("scrub: %d documents changed" % n)


# ------------------------------------------------------------------------------------------------ step: stock
def crc32(p):
    c = 0
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 22), b""):
            c = zlib.crc32(b, c)
    return c & 0xFFFFFFFF


SUPPORT = ["Sinmai.exe", "Sinmai_Data/app.info", "Sinmai_Data/boot.config", "Sinmai_Data/globalgamemanagers",
           "Sinmai_Data/globalgamemanagers.assets", "Sinmai_Data/level0", "Sinmai_Data/sharedassets0.assets",
           "Sinmai_Data/sharedassets0.assets.resS", "Sinmai_Data/resources.assets", "Sinmai_Data/resources.assets.resS",
           "Sinmai_Data/Resources/unity_builtin_extra", "Sinmai_Data/Resources/unity default resources"]


def step_stock():
    log("stock: diff of", PKG, "against", STOCK_ZIP)
    z = zipfile.ZipFile(STOCK_ZIP)
    entries = {i.filename[len("Package/"):]: i for i in z.infolist()
               if not i.is_dir() and i.filename.startswith("Package/")}
    lower = {k.lower(): k for k in entries}
    since = time.mktime((2026, 10, 19, 0, 0, 0, 0, 0, -1))   # the game was unpacked 2026-10-19
    modified, unreadable = [], []
    for rel, i in entries.items():
        p = os.path.join(PKG, *rel.split("/"))
        if not os.path.isfile(p):
            continue
        st = os.stat(p)
        if st.st_size != i.file_size:
            modified.append(rel); continue
        if st.st_mtime > since:
            try:
                if crc32(p) != i.CRC: modified.append(rel)
            except OSError:
                unreadable.append(rel)
    added = []
    for dp, dn, fn in os.walk(PKG):
        for f in fn:
            rel = rel_of(PKG, os.path.join(dp, f))
            if rel.lower() not in lower:
                added.append(rel)
    managed = [k for k in entries if k.startswith("Sinmai_Data/Managed/")]
    need = sorted(set(SUPPORT + managed + [r for r in modified if r.startswith("Sinmai_Data/")]))
    n = 0
    for rel in need:
        dst = os.path.join(STOCK, *rel.split("/"))
        i = entries[rel]
        if os.path.isfile(dst) and os.path.getsize(dst) == i.file_size and crc32(dst) == i.CRC:
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with z.open(i) as fi, open(dst + ".part", "wb") as fo:
            shutil.copyfileobj(fi, fo, 1 << 22)
        os.replace(dst + ".part", dst)
        n += 1
    res = {"modified": sorted(modified), "unreadable": unreadable, "added": sorted(added), "stock_cache": STOCK}
    write_json(os.path.join(REPORTS, "stock_diff.json"), res)
    log("stock: %d stock files changed in the install, %d added, %d extracted to the cache" % (len(modified), len(added), n))
    return res


def load_stock_diff():
    with open(os.path.join(REPORTS, "stock_diff.json"), encoding="utf-8") as f:
        return json.load(f)


# ------------------------------------------------------------------------------------------------ step: patch
def flat(rel):
    if rel.startswith("Sinmai_Data/StreamingAssets/"):
        return rel[len("Sinmai_Data/StreamingAssets/"):].replace("/", ".")
    return rel.split("/")[-1]


def safe(name):
    return re.sub(r"[^A-Za-z0-9_.\-]", "_", name or "noname")


def classify(sd):
    targets, xmls, other = [], [], []
    for rel in sd["modified"]:
        low = rel.lower()
        if low == "sinmai_data/resources.assets" or (low.startswith("sinmai_data/") and low.endswith(".assets")):
            targets.append((rel, "assets"))
        elif low.startswith("sinmai_data/streamingassets/") and low.endswith(".ab"):
            targets.append((rel, "bundle"))
        elif low.startswith("sinmai_data/streamingassets/") and low.endswith(".xml"):
            xmls.append(rel)
        else:
            other.append(rel)
    return targets, xmls, other


def export_target(rel, kind, pdir, exact_dir, rep):
    import numpy as np
    with open(os.path.join(STOCK, *rel.split("/")), "rb") as f:
        sbytes = f.read()
    with open(os.path.join(PKG, *rel.split("/")), "rb") as f:
        ibytes = f.read()
    ea, fa = P.open_env(STOCK, rel, sbytes, kind)
    eb, fb = P.open_env(PKG, rel, ibytes, kind)
    if kind == "assets":
        ea.typetree_generator, eb.typetree_generator = P.generator(STOCK), P.generator(PKG)
    ia, ib = P.object_index(ea, kind), P.object_index(eb, kind)
    if set(ia) != set(ib):
        raise SystemExit("%s: objects added/removed (%d/%d) - not supported by the patch format"
                         % (rel, len(set(ib) - set(ia)), len(set(ia) - set(ib))))
    fl = flat(rel)
    tex_dir = os.path.join(pdir, "textures", fl)
    os.makedirs(exact_dir, exist_ok=True)
    pack_path = os.path.join(exact_dir, fl + ".bin")
    pack = open(pack_path + ".part", "wb")
    out, keep_png, reenc = [], set(), []
    blobs_used = set()
    for key in sorted(ia, key=lambda k: (str(k[0]), k[1])):
        oa, ob = ia[key], ib[key]
        ra, rb = oa.get_raw_data(), ob.get_raw_data()
        if ra == rb:
            continue
        ty = ob.type.name
        try:
            name = ob.peek_name()
        except Exception:
            name = ""
        ta, tb = oa.read_typetree(), ob.read_typetree()
        e = {"type": ty, "name": name, "path_id": key[1]}
        if key[0] is not None:
            e["cab"] = key[0]
        e["stock_raw_sha256"], e["patched_raw_sha256"] = P.sha_bytes(ra), P.sha_bytes(rb)
        skipf = ()
        if ty == "Texture2D":
            xa, xb = oa.read(), ob.read()
            ima, imb = xa.image.convert("RGBA"), xb.image.convert("RGBA")
            if ima.size == imb.size:
                dd = np.abs(np.asarray(ima, np.int16) - np.asarray(imb, np.int16))
                d, mean = int(dd.max()), float(dd.mean())
                # re-encode noise only (no deliberate recolour): stays stock, not shipped
                if d <= REENCODE_ONLY_MAX and mean < 0.5 and not P.tdiff(ta, tb, skip=P.TEX_FIELDS):
                    reenc.append({"name": name, "path_id": key[1], "max_abs": d, "mean_abs": round(mean, 4)})
                    continue
            png_rel = "textures/%s/%s__%s.png" % (fl, safe(name), key[1])
            png_abs = os.path.join(pdir, *png_rel.split("/"))
            os.makedirs(os.path.dirname(png_abs), exist_ok=True)
            buf = io.BytesIO()
            imb.save(buf, "PNG")
            data = buf.getvalue()
            if not (os.path.isfile(png_abs) and open(png_abs, "rb").read() == data):
                with open(png_abs, "wb") as f:
                    f.write(data)
            keep_png.add(png_rel)
            raw = bytes(xb.get_image_data())
            off = pack.tell()
            pack.write(raw)
            e["texture"] = {"png": png_rel, "pixels_sha256": P.pixels_sha(imb), "format": int(tb["m_TextureFormat"]),
                            "mip_count": int(tb.get("m_MipCount", 1)), "width": tb["m_Width"], "height": tb["m_Height"],
                            "stock": {"format": int(ta["m_TextureFormat"]), "width": ta["m_Width"], "height": ta["m_Height"]},
                            "exact": {"offset": off, "size": len(raw), "sha256": P.sha_bytes(raw)}}
            skipf = P.TEX_FIELDS
        changes = []
        for path, a, b in P.tdiff(ta, tb, skip=skipf):
            nv = P.enc(b)
            if isinstance(nv, dict) and set(nv) <= {"$b64", "$u8"} and len(next(iter(nv.values()))) > P.BIG_INLINE:
                kindb = "u8" if "$u8" in nv else "bytes"
                raw_b = bytes(b) if kindb == "bytes" else bytes(b)
                h = P.sha_bytes(raw_b)
                brel = "blobs/%s.bin" % h[:20]
                bp = os.path.join(pdir, *brel.split("/"))
                if not os.path.isfile(bp):
                    os.makedirs(os.path.dirname(bp), exist_ok=True)
                    with open(bp, "wb") as f:
                        f.write(raw_b)
                blobs_used.add(brel)
                nv = {"$blob": brel, "sha256": h, "kind": kindb}
            changes.append({"path": path, "old": P.old_repr(a), "new": nv})
        # self-check: the recorded changes turn the stock tree into the installed one
        chk = json.loads(json.dumps(P.enc(ta)))
        chk = P.dec(chk, pdir)
        for c in changes:
            P.set_at(chk, c["path"], P.dec(c["new"], pdir))
        if P.tdiff(chk, tb, skip=skipf):
            raise SystemExit("self-check failed for %s %s" % (ty, name))
        e["changes"] = changes
        out.append(e)
    pack.close()
    os.replace(pack_path + ".part", pack_path)
    # drop PNGs of textures that are no longer changed
    if os.path.isdir(tex_dir):
        for f in os.listdir(tex_dir):
            if "textures/%s/%s" % (fl, f) not in keep_png:
                os.remove(os.path.join(tex_dir, f))
    out.sort(key=lambda e: (e["type"], e["name"], e["path_id"]))
    obj_rel = "targets/%s.json" % fl
    write_json(os.path.join(pdir, *obj_rel.split("/")), {"file": rel, "objects": out})
    counts = collections.Counter(e["type"] for e in out)
    rep["targets"].append({"file": rel, "kind": kind, "objects": dict(counts), "reencode_only_skipped": reenc})
    log("patch: %s: %d objects (%s), %d re-encode-only textures skipped"
        % (rel, len(out), ", ".join("%s %d" % kv for kv in sorted(counts.items())), len(reenc)))
    t = {"file": rel, "kind": kind, "objects": obj_rel, "exact": "exact/%s.bin" % fl,
         "stock_sha256": P.sha_bytes(sbytes), "patched_sha256": P.sha_bytes(ibytes)}
    if kind == "bundle":
        t["packer"] = "lz4"
    return t, blobs_used


def xml_lines(data):
    return [l.rstrip(b"\r").decode("utf-8-sig" if i == 0 else "utf-8") for i, l in enumerate(data.split(b"\n"))]


def export_xml(rel):
    with open(os.path.join(STOCK, *rel.split("/")), "rb") as f:
        a = f.read()
    with open(os.path.join(PKG, *rel.split("/")), "rb") as f:
        b = f.read()
    la, lb = xml_lines(a), xml_lines(b)
    hunks = []
    for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, la, lb, autojunk=False).get_opcodes():
        if op != "equal":
            hunks.append({"at": i1, "old": la[i1:i2], "new": lb[j1:j2]})
    x = {"path": rel, "stock_sha256": P.sha_bytes(a), "patched_sha256": P.sha_bytes(b), "hunks": hunks}
    if b"\r\n" in a and b"\r\n" not in b:
        x["eol"] = "lf"
    elif b"\r\n" not in a and b.count(b"\r\n") == b.count(b"\n"):
        x["eol"] = "crlf"
    if P.apply_xml(x, a) != b:
        raise SystemExit("xml self-check failed: " + rel)
    return x


SETTINGS_RELEASE = [  # settings.ini lines changed for the release (they act on the player's account)
    (re.compile(r"(?m)^(charaLevel\s*=.*)$"), r"; \1   (off in the release: raises every character of the logged-in account)"),
    (re.compile(r"(?m)^(equipPartner\s*=.*)$"), r"; \1"),
    (re.compile(r"(?m)^frameLog\s*=.*\n?"), ""),
]


def step_patch():
    sd = load_stock_diff()
    targets, xmls, other = classify(sd)
    pdir = os.path.join(REPO, "patch")
    exact_dir = os.path.join(HOLD, "exact")
    rep = {"targets": [], "other_modified_stock_files_not_exported": other}
    mtargets, blobs = [], set()
    for rel, kind in sorted(targets):
        t, b = export_target(rel, kind, pdir, exact_dir, rep)
        mtargets.append(t); blobs |= b
    for d in ("targets", "blobs"):   # stale outputs of targets that are gone
        dd = os.path.join(pdir, d)
        if os.path.isdir(dd):
            for f in os.listdir(dd):
                r = "%s/%s" % (d, f)
                if (d == "targets" and r not in {t["objects"] for t in mtargets}) or (d == "blobs" and r not in blobs):
                    os.remove(os.path.join(dd, f))
    xs = [export_xml(rel) for rel in sorted(xmls)]
    write_json(os.path.join(pdir, "xml_patch.json"), {"files": xs})
    log("patch: %d XML files, %d line hunks" % (len(xs), sum(len(x["hunks"]) for x in xs)))
    # files we added
    files, git_pairs, rel_pairs = [], [], []
    for rel in sd["added"]:
        if not any(rel == r or (r.endswith("/") and rel.startswith(r)) for r in ADD_ROOTS) or skip(rel, ADD_SKIP):
            continue
        src = os.path.join(PKG, *rel.split("/"))
        size = os.path.getsize(src)
        release_only = size > RELEASE_ONLY_BYTES or rel.lower().endswith((".dll", ".bin"))
        if rel == "LocalAssets/PartnerLive2D/settings.ini":
            txt = open(src, encoding="utf-8").read()
            for rx, rep_s in SETTINGS_RELEASE:
                txt = rx.sub(rep_s, txt)
            tmp = os.path.join(SCRATCH, "work", "settings.release.ini")
            os.makedirs(os.path.dirname(tmp), exist_ok=True)
            with open(tmp, "w", encoding="utf-8", newline="") as f:
                f.write(txt)
            src = tmp
        e = {"path": rel, "src": "files/" + rel, "sha256": sha_file(src), "size": os.path.getsize(src)}
        if release_only:
            e["release_only"] = True
            rel_pairs.append((src, rel))
        else:
            git_pairs.append((src, rel))
        files.append(e)
    mirror(git_pairs, os.path.join(pdir, "files"))
    mirror(rel_pairs, os.path.join(HOLD, "files_release_only"))
    write_json(os.path.join(pdir, "files.json"), {"files": files})
    log("patch: %d added files (%d release-only, %.0f MB)" % (len(files), len(rel_pairs),
        sum(f["size"] for f in files if f.get("release_only")) / 1e6))
    manifest = {"format": 1, "name": "maimai NGO skin " + DATE, "game": "maimai DX SDEZ 1.70 (resources.assets SHA256 %s)"
                % next((t["stock_sha256"] for t in mtargets if t["file"] == "Sinmai_Data/resources.assets"), "?"),
                "created": datetime.datetime.now().isoformat(timespec="seconds"),
                "targets": [{k: v for k, v in t.items()} for t in mtargets], "xml": "xml_patch.json", "files": "files.json"}
    write_json(os.path.join(pdir, "manifest.json"), manifest)
    rep["files"] = len(files)
    write_json(os.path.join(REPORTS, "patch_export.json"), rep)
    return rep


# ------------------------------------------------------------------------------------------------ step: code
CODE_TREES = ["src", "mcp", "tests", "docs", "tools", "deploy", ".claude/skills"]
CODE_FILES = ["build.ps1", "PartnerLive2D.csproj", "deploy_select_layout_v1.ini"]
CODE_SKIP = ["*/__pycache__/*", "*/obj/*", "*/bin/*", "tests/Scratch/*", "*.pyc", "*.dll", "*.exe",
             "tools/shots/*", "tools/tour_overview/*", "tools/dxrating*", "tools/*.drivebak", "tools/drive-state.json",
             "tools/frames_compare.png", "*AquaMai.toml*", "*segatools*", "*aime.txt", "*.log",
             "deploy/LocalAssets/PartnerLive2D/hiyori/model/*", "*.moc3",
             ".claude/skills/*/settings*.json",
             "mcp/_test*", "*/_testpkg/*", "*/_testngo/*",          # MCP test fixtures (copies of game files, test output)
             "*/hiyori/model/*", "*Hiyori*", "*/sprites/_qa/*"]
TASK_DOCS = ["tasks/*.md", "tasks/ngo_skin_test/INSTALL_TEST_REPORT.md", "tasks/sel_phases/*.md", "tasks/sel_phases/*/NOTES.md",
             "tasks/theme_override/*.md"]
VIDEO_TOOLS = ["video/*.py", "video/*.timeline.json", "video/*.timeline.md"]
OWNED = {"tools/ngo_export_public.py"}


def step_code():
    pairs = []
    for t in CODE_TREES:
        root = os.path.join(PROJECT, *t.split("/"))
        if not os.path.isdir(root):
            continue
        for dp, dn, fn in os.walk(root):
            for f in fn:
                rel = rel_of(PROJECT, os.path.join(dp, f))
                if not skip(rel, CODE_SKIP):
                    pairs.append((os.path.join(dp, f), rel))
    for rel in CODE_FILES:
        if os.path.isfile(os.path.join(PROJECT, rel)):
            pairs.append((os.path.join(PROJECT, rel), rel))
    for pat in TASK_DOCS + VIDEO_TOOLS:
        base = pat.split("/")[0]
        for dp, dn, fn in os.walk(os.path.join(PROJECT, base)):
            for f in fn:
                rel = rel_of(PROJECT, os.path.join(dp, f))
                if fnmatch.fnmatch(rel, pat) and rel.count("/") == pat.count("/"):
                    dst = ("docs/" + rel) if base == "tasks" else ("tools/video/" + rel.split("/", 1)[1])
                    pairs.append((os.path.join(dp, f), dst))
    n = 0
    want = set()
    for src, rel in pairs:
        sanitized_copy(src, os.path.join(REPO, *rel.split("/")))
        want.add(rel)
        n += 1
    # remove files of these trees that left the project (repo-owned files stay)
    for t in CODE_TREES + ["docs/tasks", "tools/video"]:
        root = os.path.join(REPO, *t.split("/"))
        if not os.path.isdir(root): continue
        for dp, dn, fn in os.walk(root):
            for f in fn:
                rel = rel_of(REPO, os.path.join(dp, f))
                if rel not in want and rel not in OWNED and not rel.startswith(("docs/RELEASE", "docs/PATCH")):
                    os.remove(os.path.join(dp, f))
        for dp, dn, fn in sorted(os.walk(root), key=lambda x: -len(x[0])):
            if dp != root and not os.listdir(dp):
                os.rmdir(dp)
    log("code: %d files" % n)
    return n


# ------------------------------------------------------------------------------------------------ step: launcher
LAUNCHER = ["Play-maimai.bat", "launcher.ps1", "play-monitor.ps1", "fit-window.ps1", "auto-login.py", "使用说明.txt",
            "gpu-lock/setup-gpu-lock.bat", "gpu-lock/remove-gpu-lock.bat"]
LAUNCHER_EXTRA = {"gpu-lock/gpu-lock-on.bat": os.path.join(PROJECT, "tools", "patch_extras", "gpu-lock-on.bat"),
                  "gpu-lock/gpu-lock-off.bat": os.path.join(PROJECT, "tools", "patch_extras", "gpu-lock-off.bat")}


def step_launcher():
    pairs = [(os.path.join(GAME_ROOT, *r.split("/")), r) for r in LAUNCHER if os.path.isfile(os.path.join(GAME_ROOT, *r.split("/")))]
    pairs += [(s, r) for r, s in LAUNCHER_EXTRA.items() if os.path.isfile(s)]
    d = os.path.join(REPO, "launcher")
    want = set()
    for s, r in pairs:
        sanitized_copy(s, os.path.join(d, *r.split("/")))
        want.add(r)
    for dp, dn, fn in os.walk(d):
        for f in fn:
            if rel_of(d, os.path.join(dp, f)) not in want:
                os.remove(os.path.join(dp, f))
    log("launcher: %d files" % len(pairs))


# ------------------------------------------------------------------------------------------------ step: art
ART_DIR_SKIP = {"originals", "candidate_originals", "decompiled", "code", "trees", "evidence", "research",
                "research_candidates", "for_repo", "investigation", "__pycache__", "baseline"}
ART_NAME_SKIP = ["review*", "preview*", "*sheet*", "compare*", "mock*", "contact*", "*_compare*", "*screenshot*",
                 "*.new", "*.assets", "*.ab", "*.resS", "*.log", "*.mp4"]
ART_PATH_SKIP = ["t3/candidates/*", "t3/*candidates*", "t1/*"]   # terra round 3/1 dumps of stock textures
ART_TOP_SKIP = re.compile(r"^(m[1-6]_|select_bake$|t6_transition$|r1_review_dot$|audit$|t1$)")   # t1 = stock texture dumps


class StockPixels:
    """name -> list of decoded stock textures (resources.assets + the bundles we touch), lazily."""

    def __init__(self):
        import UnityPy
        self.by_name = collections.defaultdict(list)
        self.cache = {}
        sd = load_stock_diff()
        files = [os.path.join(STOCK, "Sinmai_Data", "resources.assets")]
        files += [os.path.join(STOCK, *r.split("/")) for r in sd["modified"] if r.lower().endswith(".ab")]
        self.envs = []
        for p in files:
            e = UnityPy.load(p)
            self.envs.append(e)
            for o in e.objects:
                if o.type.name == "Texture2D":
                    try:
                        self.by_name[o.peek_name()].append(o)
                    except Exception:
                        pass

    def identical(self, png_path):
        from PIL import Image
        stem = os.path.splitext(os.path.basename(png_path))[0]
        names = [stem, stem.split("__")[0], re.sub(r"^\d+_", "", stem)]
        m = re.search(r"((?:UI|T)_[A-Za-z0-9_]+)$", stem)
        if m:
            names.append(m.group(1))
        objs = []
        for n in names:
            objs += [o for o in self.by_name.get(n, []) if o not in objs]
        if not objs:
            return False
        try:
            mine = Image.open(png_path).convert("RGBA")
        except Exception:
            return False
        h = P.pixels_sha(mine)
        for o in objs:
            k = (id(o.assets_file), o.path_id)
            if k not in self.cache:
                try:
                    self.cache[k] = P.pixels_sha(o.read().image.convert("RGBA"))
                except Exception:
                    self.cache[k] = None
            if self.cache[k] == h:
                return True
        return False


def step_art(make_zip=True):
    rep = {"dot_kept": 0, "dot_stock_identical": [], "out_kept": 0, "out_stock_identical": [], "out_skipped_dirs": []}
    sp = StockPixels()
    # dot's redraws -> git art/dot_redrawn
    pairs = []
    src_root = os.path.join(DOT, "redrawn")
    for dp, dn, fn in os.walk(src_root):
        for f in fn:
            p = os.path.join(dp, f)
            rel = rel_of(src_root, p)
            if f.lower().endswith(".png") and sp.identical(p):
                rep["dot_stock_identical"].append(rel); continue
            if skip(f, ART_NAME_SKIP):
                continue
            pairs.append((p, rel))
    for extra in ("README.md", "DELIVERY.md"):
        if os.path.isfile(os.path.join(DOT, extra)):
            pairs.append((os.path.join(DOT, extra), "_dot_repo_" + extra))
    mirror(pairs, os.path.join(REPO, "art", "dot_redrawn"))
    rep["dot_kept"] = len(pairs)
    # unused runtime recolours (the patch already carries their pixels) -> git art/ngo_recolor
    rr = os.path.join(PKG, "LocalAssets", "PartnerLive2D", "ngo_recolor")
    if os.path.isdir(rr):
        mirror([(os.path.join(rr, f), f) for f in os.listdir(rr) if f.endswith(".png") and not f.startswith("_")],
               os.path.join(REPO, "art", "ngo_recolor"))
    # spec notes of the art work (small text) -> git art/notes
    notes = [(os.path.join(CXREF, f), f) for f in ("SPEC.md",) if os.path.isfile(os.path.join(CXREF, f))]
    mirror(notes, os.path.join(REPO, "art", "notes"))
    # Codex / imagegen working art -> release zip
    out_root = os.path.join(CXREF, "out")
    keep = []
    for top in sorted(os.listdir(out_root)):
        if ART_TOP_SKIP.match(top):
            rep["out_skipped_dirs"].append(top); continue
        for dp, dn, fn in os.walk(os.path.join(out_root, top)):
            dn[:] = [d for d in dn if d not in ART_DIR_SKIP]
            for f in fn:
                if skip(f, ART_NAME_SKIP):
                    continue
                p = os.path.join(dp, f)
                if skip(rel_of(out_root, p), ART_PATH_SKIP):
                    continue
                if f.lower().endswith(".png") and sp.identical(p):
                    rep["out_stock_identical"].append(rel_of(out_root, p)); continue
                keep.append(p)
    rep["out_kept"] = len(keep)
    rep["out_bytes"] = sum(os.path.getsize(p) for p in keep)
    if make_zip:
        zp = os.path.join(RELEASE, "maimai-ngo-art-source-%s.zip" % DATE)
        with zipfile.ZipFile(zp + ".part", "w", zipfile.ZIP_STORED) as z:
            for p in keep:
                z.write(p, "art-source/" + rel_of(out_root, p))
        os.replace(zp + ".part", zp)
        rep["zip"] = zp
    write_json(os.path.join(REPORTS, "art_export.json"), rep)
    log("art: dot %d kept (%d stock-identical dropped); out %d files %.0f MB (%d stock-identical dropped)"
        % (rep["dot_kept"], len(rep["dot_stock_identical"]), len(keep), rep["out_bytes"] / 1e6, len(rep["out_stock_identical"])))
    return rep


# ------------------------------------------------------------------------------------------------ step: scan
SCAN = [("20-digit number", re.compile(r"(?<![0-9A-Za-z])\d{20}(?![0-9])")),
        ("keychip-like id", re.compile(r"\b[A-Z][0-9]{2}[A-Z]-[0-9A-Z]{2}[A-Z][0-9]{8}\b")),
        ("dashed id ????-?????????", re.compile(r"\b[A-Z0-9]{4}-[A-Z0-9]{9,11}\b")),
        ("owner user name", re.compile(r"(?i)" + _U)),
        ("@icloud", re.compile(r"(?i)@icloud")), ("@qq", re.compile(r"(?i)@qq")),
        ("github token gho_", re.compile(r"gh[opsu]_[A-Za-z0-9]{20,}")), ("sk- key", re.compile(r"\bsk-[A-Za-z0-9_\-]{16,}")),
        ("keychip word", re.compile(r"(?i)keychip")), ("aime word", re.compile(r"(?i)aime")),
        ("QQ number", re.compile(r"(?i)qq\D{0,6}\d{6,11}"))]
# owner-specific ids (QQ numbers, card tails...) come from a file OUTSIDE the repo, one per line
_IDS = os.path.join(REPORTS, "private_ids.txt")
if os.path.isfile(_IDS):
    _ids = [l.strip() for l in open(_IDS, encoding="utf-8-sig") if l.strip() and not l.startswith("#")]
    if _ids:
        SCAN.append(("private id (private_ids.txt)", re.compile("|".join(re.escape(i) for i in _ids))))


def scan_tree(root, label, hits, counts):
    for dp, dn, fn in os.walk(root):
        if ".git" in dn: dn.remove(".git")
        for f in fn:
            p = os.path.join(dp, f)
            ext = os.path.splitext(f)[1].lower()
            textual = ext in TEXT_EXT or f in (".gitignore", ".gitattributes") or (ext == "" and os.path.getsize(p) < 1 << 20)
            if not textual:
                if ext == ".zip":
                    with zipfile.ZipFile(p) as z:
                        for n in z.namelist():
                            for lab, rx in SCAN[:9]:
                                if rx.search(n): hits.append({"where": label, "file": rel_of(root, p) + "!" + n, "rule": lab, "text": n})
                continue
            try:
                txt = open(p, encoding="utf-8", errors="replace").read()
            except OSError:
                continue
            for lab, rx in SCAN:
                for m in rx.finditer(txt):
                    counts[lab] += 1
                    a = max(0, m.start() - 50); b = min(len(txt), m.end() + 50)
                    hits.append({"where": label, "file": rel_of(root, p), "rule": lab,
                                 "line": txt.count("\n", 0, m.start()) + 1, "text": txt[a:b].replace("\n", " ")})


def step_scan():
    hits, counts = [], collections.Counter()
    scan_tree(REPO, "repo", hits, counts)
    if os.path.isdir(STAGE):
        scan_tree(STAGE, "release", hits, counts)
    # file names that should never be shipped
    bad_names = re.compile(r"(?i)(segatools\.ini|aime\.txt|AquaMai\.toml|config_(common|server|client|hook)\.json|hinata\.log|"
                           r"account_edit|launcher\.log|Live2DCubismCore\.dll|Hiyori|\.moc3$|resources\.assets$|\.resS$|"
                           r"Assembly-CSharp|MelonLoader\.dll|AquaMai\.dll|MuMod\.dll|sinmai_src)")
    names = []
    for root in (REPO, RELEASE):
        for dp, dn, fn in os.walk(root):
            if ".git" in dn: dn.remove(".git")
            for f in fn:
                if bad_names.search(f):
                    names.append(rel_of(root, os.path.join(dp, f)))
    big = []
    for dp, dn, fn in os.walk(REPO):
        if ".git" in dn: dn.remove(".git")
        for f in fn:
            p = os.path.join(dp, f)
            if os.path.getsize(p) > 50 * 1024 * 1024: big.append(rel_of(REPO, p))
    res = {"counts": dict(counts), "bad_file_names": names, "repo_files_over_50MB": big, "hits": hits}
    write_json(os.path.join(REPORTS, "privacy_scan.json"), res)
    log("scan: %s; bad names %d; >50MB in repo %d" % (dict(counts), len(names), len(big)))
    return res


# ------------------------------------------------------------------------------------------------ step: commit
def git(*a, check=True):
    r = subprocess.run(["git", "-C", REPO] + list(a), capture_output=True, text=True, encoding="utf-8")
    if check and r.returncode != 0:
        raise SystemExit("git %s failed: %s" % (" ".join(a), r.stderr))
    return r.stdout


def step_commit(msg=None):
    if not os.path.isdir(os.path.join(REPO, ".git")):
        git("init", "-b", "main")
    git("config", "user.name", GIT_NAME)
    git("config", "user.email", GIT_MAIL)
    git("config", "core.autocrlf", "false")
    git("add", "-A")
    if not git("status", "--porcelain").strip():
        log("commit: nothing to commit"); return
    git("commit", "-q", "-m", msg or ("Export from the working install " + time.strftime("%Y-%m-%d %H:%M")))
    log("commit:", git("log", "--oneline", "-1").strip())


# ------------------------------------------------------------------------------------------------ step: release
def step_release():
    os.makedirs(STAGE, exist_ok=True)
    # 1. patch folder = repo patch/ + exact packs + release-only files
    pairs = []
    pdir = os.path.join(REPO, "patch")
    for dp, dn, fn in os.walk(pdir):
        for f in fn:
            pairs.append((os.path.join(dp, f), rel_of(pdir, os.path.join(dp, f))))
    ro = os.path.join(HOLD, "files_release_only")
    for dp, dn, fn in os.walk(ro):
        for f in fn:
            pairs.append((os.path.join(dp, f), "files/" + rel_of(ro, os.path.join(dp, f))))
    ex = os.path.join(HOLD, "exact")
    for f in os.listdir(ex):
        pairs.append((os.path.join(ex, f), "exact/" + f))
    full = os.path.join(STAGE, "patch")
    mirror(pairs, full)
    # 2. patcher exe (rebuilt when the patcher source changed)
    src = os.path.join(REPO, "patcher", "ngo_patcher.py")
    bps = os.path.join(REPO, "patcher", "build_exe.ps1")
    exe_dir = os.path.join(STAGE, "ngo_patcher")
    stamp = os.path.join(SCRATCH, "pyi", "built_from.sha256")
    if not (os.path.isfile(os.path.join(exe_dir, "ngo_patcher.exe")) and os.path.isfile(stamp)
            and open(stamp).read() == sha_file(src) + sha_file(bps)):
        r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                            os.path.join(REPO, "patcher", "build_exe.ps1"), "-Python", VENV_PY,
                            "-Work", os.path.join(SCRATCH, "pyi"), "-Out", STAGE], text=True)
        if r.returncode != 0:
            raise SystemExit("patcher build failed")
        with open(stamp, "w") as f:
            f.write(sha_file(src) + sha_file(bps))
    # 3. launch scripts + readme
    for f in ("install.bat", "uninstall.bat", "status.bat", "README_release.txt"):
        copy_if_changed(os.path.join(REPO, "patcher", "release", f), os.path.join(STAGE, f))
    # 4. zip (stored: the payload is PNG / compressed data already)
    zp = os.path.join(RELEASE, REL_NAME + ".zip")
    root = STAGE
    with zipfile.ZipFile(zp + ".part", "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for dp, dn, fn in os.walk(root):
            for f in sorted(fn):
                p = os.path.join(dp, f)
                arc = REL_NAME + "/" + rel_of(root, p)
                comp = zipfile.ZIP_STORED if f.lower().endswith((".png", ".bin", ".zip")) else zipfile.ZIP_DEFLATED
                z.write(p, arc, compress_type=comp)
    os.replace(zp + ".part", zp)
    sums = []
    for f in sorted(os.listdir(RELEASE)):
        p = os.path.join(RELEASE, f)
        if os.path.isfile(p) and f.endswith(".zip"):
            sums.append("%s  %s" % (sha_file(p), f))
    with open(os.path.join(RELEASE, "SHA256SUMS.txt"), "w", encoding="ascii", newline="\n") as f:
        f.write("\n".join(sums) + "\n")
    log("release: %s (%.0f MB)" % (zp, os.path.getsize(zp) / 1e6))


# ------------------------------------------------------------------------------------------------ step: verify
def step_verify(use_exe=True):
    """install on a fresh COPY of the stock game files, compare with the live install, re-run, uninstall."""
    tg = os.path.join(SCRATCH, "testgame", "Package")
    if os.path.isdir(os.path.dirname(tg)):
        shutil.rmtree(os.path.dirname(tg))
    shutil.copytree(STOCK, tg)
    patch_dir = os.path.join(STAGE, "patch")
    exe = os.path.join(STAGE, "ngo_patcher", "ngo_patcher.exe")
    run = [exe] if (use_exe and os.path.isfile(exe)) else [sys.executable, os.path.join(REPO, "patcher", "ngo_patcher.py")]
    res = {"runner": run[0], "patch": patch_dir, "testgame": tg}

    def call(*a):
        t0 = time.time()
        r = subprocess.run(run + list(a), capture_output=True, text=True, encoding="utf-8", errors="replace")
        return {"args": list(a), "rc": r.returncode, "seconds": round(time.time() - t0, 1), "out": r.stdout[-4000:] + r.stderr[-2000:]}

    res["install_1"] = call("install", tg, "--patch", patch_dir, "--yes")
    patch = P.Patch(patch_dir)
    snap1 = {rel: sha_file(os.path.join(tg, *rel.split("/"))) for rel in patch.touched() if os.path.isfile(os.path.join(tg, *rel.split("/")))}
    rep = os.path.join(REPORTS, "verify_compare.json")
    cmp = P.cmd_compare(tg, PKG, patch, rep)
    res["compare_summary"] = {
        "targets": [{"file": t["file"], "file_identical": t["file_identical"], "objects": t["objects"],
                     "raw_identical": t["raw_identical"], "differing": len(t["differing"])} for t in cmp["targets"]],
        "xml_identical": sum(x["identical"] for x in cmp["xml"]), "xml": len(cmp["xml"]),
        "files_identical": sum(1 for f in cmp["files"] if f.get("identical")), "files": len(cmp["files"]),
        "files_differing": [f["path"] for f in cmp["files"] if not f.get("identical")]}
    res["install_2"] = call("install", tg, "--patch", patch_dir, "--yes")
    snap2 = {rel: sha_file(os.path.join(tg, *rel.split("/"))) for rel in snap1}
    res["rerun_identical"] = snap1 == snap2
    res["uninstall"] = call("uninstall", tg, "--yes")
    back = {}
    for rel in patch.touched():
        p, s = os.path.join(tg, *rel.split("/")), os.path.join(STOCK, *rel.split("/"))
        back[rel] = ("stock" if os.path.isfile(s) and os.path.isfile(p) and sha_file(p) == sha_file(s)
                     else "removed" if not os.path.isfile(p) and not os.path.isfile(s) else "WRONG")
    res["after_uninstall"] = dict(collections.Counter(back.values()))
    res["after_uninstall_wrong"] = [r for r, v in back.items() if v == "WRONG"]
    res["backup_dir_left"] = os.path.isdir(os.path.join(tg, "NGO_skin_backup"))
    write_json(os.path.join(REPORTS, "verify.json"), res)
    log("verify: install rc %d (%.0fs), rerun identical %s, uninstall rc %d -> %s"
        % (res["install_1"]["rc"], res["install_1"]["seconds"], res["rerun_identical"], res["uninstall"]["rc"], res["after_uninstall"]))
    for t in res["compare_summary"]["targets"]:
        log("   %s: file identical %s, %d/%d objects raw-identical" % (t["file"], t["file_identical"], t["raw_identical"], t["objects"]))
    return res


STEPS = ["stock", "patch", "code", "launcher", "art", "scrub", "release", "verify", "scan", "commit"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--steps", default=",".join(STEPS))
    ap.add_argument("--no-art-zip", action="store_true")
    ap.add_argument("--message")
    a = ap.parse_args()
    os.makedirs(REPORTS, exist_ok=True)
    # the repo copy is the source of truth; public_export\export.py is refreshed from it on every run
    me, mirror_copy = os.path.join(REPO, "tools", "ngo_export_public.py"), os.path.join(REPORTS, "export.py")
    if os.path.isfile(me) and (not os.path.isfile(mirror_copy) or sha_file(me) != sha_file(mirror_copy)):
        shutil.copy2(me, mirror_copy)
    for s in a.steps.split(","):
        t0 = time.time()
        if s == "stock": step_stock()
        elif s == "patch": step_patch()
        elif s == "code": step_code()
        elif s == "launcher": step_launcher()
        elif s == "art": step_art(not a.no_art_zip)
        elif s == "scrub": step_scrub()
        elif s == "scan": step_scan()
        elif s == "commit": step_commit(a.message)
        elif s == "release": step_release()
        elif s == "verify": step_verify()
        elif s == "verify-src": step_verify(use_exe=False)
        else: raise SystemExit("unknown step " + s)
        log("step %s done in %.0fs" % (s, time.time() - t0))


if __name__ == "__main__":
    main()
