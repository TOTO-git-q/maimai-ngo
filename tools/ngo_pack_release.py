"""Build a private, directly upgradeable full NGO skin folder and ZIP.

python ngo_pack_release.py <out dir> [--date 20261103] [--no-zip]
python ngo_pack_release.py <out dir> --date 20261103 --zip-only
Inputs are read-only. Existing release folders/archives are never overwritten.
"""
import argparse
import datetime
import hashlib
import json
from pathlib import Path, PureWindowsPath
import re
import shutil
import zipfile

PKG = Path(r"D:\maimai\Package")
ZIP = Path(r"D:\maimoller内容\Maimoller专用整合\MaiMai_Magical懒人包\SDEZ1.70_nobga.zip")
TOOLS = Path(__file__).resolve().parent
SETTINGS = r"LocalAssets\PartnerLive2D\settings.ini"
SINCE = datetime.datetime(2026, 10, 19).timestamp()
INCLUDE_TREES = [r"LocalAssets\PartnerLive2D"]
INCLUDE_FILES = [r"Sinmai_Data\resources.assets", r"Mods\PartnerLive2D.dll"]
INCLUDE_CHANGED_UNDER = [r"Sinmai_Data\StreamingAssets"]
SKIP = [r"LocalAssets\PartnerLive2D\theme_ngo_test",
        r"LocalAssets\PartnerLive2D\settings.ini.before-ngo-skin",
        r"LocalAssets\PartnerLive2D\ngo_tint.txt.off",
        r"LocalAssets\PartnerLive2D\account_edit.applied.ini"]


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def safe_rel(rel):
    p = PureWindowsPath(rel)
    if p.is_absolute() or p.drive or p.root or not p.parts or ".." in p.parts or ":" in rel:
        raise ValueError(f"Unsafe package path: {rel}")
    return str(p)


def local(root, rel):
    return root.joinpath(*PureWindowsPath(safe_rel(rel)).parts)


def skipped(rel):
    low = rel.lower()
    return (any(low == s.lower() or low.startswith(s.lower() + "\\") for s in SKIP)
            or any(x.lower() in ("_qa", "logs", "cache", "caches", "__pycache__") for x in PureWindowsPath(rel).parts)
            or ".before-" in low or ".before_" in low
            or low.endswith((".log", ".tmp", ".bak", ".cache", ".orig", ".new", ".orig.png"))
            or PureWindowsPath(rel).name.lower() == "_sheet.png")


def candidates(pkg):
    rels = set(INCLUDE_FILES)
    for tree in INCLUDE_TREES + INCLUDE_CHANGED_UNDER:
        for f in local(pkg, tree).rglob("*"):
            if f.is_file():
                rel = str(PureWindowsPath(f.relative_to(pkg)))
                if not skipped(rel) and (tree in INCLUDE_TREES or f.stat().st_mtime > SINCE):
                    rels.add(rel)
    return sorted(rels, key=str.lower)


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def release_settings(previous):
    text = local(previous / "files", SETTINGS).read_text("utf-8-sig")
    for key in ("equipPartner", "presentationVSync", "presentationTargetFps", "presentationDiagnostics"):
        if re.search(r"(?im)^\s*" + key + r"\s*=", text):
            raise ValueError(f"Historical settings contain unexpected override: {key}")
    # Scan/cache and in-song pause are automatic, with no new font settings.
    if not re.search(r"(?im)^\s*lateFrameLog\s*=", text):
        text = text.rstrip("\n") + "\n\n; Per-frame late/stall logging is opt-in; normal chart summaries remain.\nlateFrameLog = false\n"
    return text.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8")


def make_zip(root):
    out = root.with_suffix(".zip")
    partial = root.parent / (root.name + ".zip.partial")
    if out.exists() or partial.exists():
        raise FileExistsError(f"Refusing to overwrite archive: {out}")
    files = sorted((p for p in root.rglob("*") if p.is_file()), key=lambda p: str(p).lower())
    if any(any(part.startswith("backup_") for part in p.relative_to(root).parts) for p in files):
        raise ValueError("Release folder contains private install backups")
    with zipfile.ZipFile(partial, "w", zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as z:
        for i, p in enumerate(files, 1):
            z.write(p, (Path(root.name) / p.relative_to(root)).as_posix())
            if p.stat().st_size > 100_000_000 or i % 60 == 0:
                print(f"ZIP {i}/{len(files)}: {p.relative_to(root)}", flush=True)
    partial.rename(out)
    digest = sha(out)
    out.with_suffix(".zip.sha256").write_text(f"{digest}  {out.name}\n", encoding="ascii")
    print(f"ZIP complete: {out.stat().st_size} bytes, SHA256 {digest}", flush=True)


def build(args):
    name = "maimai_NGO_skin_" + args.date
    root = args.out.resolve() / name
    if args.zip_only:
        if not (root / "manifest.json").is_file():
            raise FileNotFoundError(root)
        return make_zip(root)
    stage = root.parent / (name + ".building")
    if root.exists() or stage.exists():
        raise FileExistsError(f"Refusing to replace release/stage: {root}")
    previous, stock_hashes = [], {}
    for date in ("20261031", "20261101"):
        r = args.out.resolve() / ("maimai_NGO_skin_" + date)
        m = json.loads((r / "manifest.json").read_text("utf-8-sig"))
        added = []
        for f in m["files"]:
            rel = safe_rel(f["path"])
            if sha(local(r / "files", rel)) != f["sha256"]:
                raise ValueError(f"Changed historical payload: {r.name}/{rel}")
            if f["stock_sha256"]:
                if sha(local(r / "original", rel)) != f["stock_sha256"]:
                    raise ValueError(f"Changed historical stock: {r.name}/{rel}")
                if rel in stock_hashes and stock_hashes[rel] != f["stock_sha256"]:
                    raise ValueError(f"Historical stock disagreement: {rel}")
                stock_hashes[rel] = f["stock_sha256"]
            else:
                added.append(rel)
        previous.append({"name": m["name"], "manifest_sha256": sha(r / "manifest.json"), "added_files": sorted(added)})
    settings = release_settings(args.out.resolve() / "maimai_NGO_skin_20261101")
    rels = candidates(args.package)
    stage.mkdir(parents=True)
    payload, sources = [], []
    with zipfile.ZipFile(args.stock_archive) as stock:
        names = set(stock.namelist())
        all_stock = dict(stock_hashes)
        for i, rel in enumerate(rels, 1):
            src, dst = local(args.package, rel), local(stage / "files", rel)
            if src.is_symlink():
                raise ValueError(f"Refusing symlink input: {src}")
            before = src.stat()
            dst.parent.mkdir(parents=True, exist_ok=True)
            source_hash = sha(src)
            if rel == SETTINGS:
                dst.write_bytes(settings)
            else:
                shutil.copy2(src, dst)
                if sha(dst) != source_hash:
                    raise ValueError(f"Copy/hash failure: {rel}")
            after = src.stat()
            if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                raise ValueError(f"Input changed while packing: {rel}")
            sources.append({"path": rel, "sha256": source_hash, "bytes": before.st_size, "mtime_ns": before.st_mtime_ns})
            member = "Package/" + rel.replace("\\", "/")
            original_hash = None
            if member in names:
                original = local(stage / "original", rel)
                original.parent.mkdir(parents=True, exist_ok=True)
                with stock.open(member) as fi, original.open("wb") as fo:
                    shutil.copyfileobj(fi, fo, 1 << 20)
                original_hash = sha(original)
                if rel in stock_hashes and original_hash != stock_hashes[rel]:
                    raise ValueError(f"Archive/historical stock mismatch: {rel}")
                all_stock[rel] = original_hash
            elif rel in stock_hashes:
                raise ValueError(f"Historical stock missing from archive: {rel}")
            payload.append({"path": rel, "sha256": sha(dst), "stock_sha256": original_hash, "bytes": dst.stat().st_size})
            if i % 50 == 0 or before.st_size > 100_000_000:
                print(f"Payload {i}/{len(rels)}: {rel}", flush=True)
        # Also retain stock for historical replacements no longer in this payload.
        for rel, expected in all_stock.items():
            dst = local(stage / "original", rel)
            if not dst.exists():
                dst.parent.mkdir(parents=True, exist_ok=True)
                with stock.open("Package/" + rel.replace("\\", "/")) as fi, dst.open("wb") as fo:
                    shutil.copyfileobj(fi, fo, 1 << 20)
            if sha(dst) != expected:
                raise ValueError(f"Stock provenance failure: {rel}")
    manifest = {"schema_version": 2, "name": name, "game": "SDEZ 1.70 (maimai DX)",
                "settings_path": SETTINGS, "settings_seed": "maimai_NGO_skin_20261101",
                "files": payload, "previous_versions": previous,
                "stock_files": [{"path": r, "sha256": h} for r, h in sorted(all_stock.items())]}
    write_json(stage / "manifest.json", manifest)
    for part in ("install", "uninstall", "common"):
        body = (TOOLS / ("ngo_release_" + part + ".ps1")).read_text("ascii")
        output = "release_common" if part == "common" else part
        (stage / (output + ".ps1")).write_bytes(body.replace("\r\n", "\n").replace("\n", "\r\n").encode("ascii"))
    for action in ("install", "uninstall"):
        body = ('@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0' + action +
                '.ps1" %*\r\nset "result=%errorlevel%"\r\npause\r\nexit /b %result%\r\n')
        (stage / (action + ".bat")).write_bytes(body.encode("ascii"))
    added = sum(f["stock_sha256"] is None for f in payload)
    obsolete = sorted(set(r for v in previous for r in v["added_files"]) - set(rels))
    readme = (TOOLS / "ngo_release_readme.txt").read_text("utf-8-sig").format(
        date=args.date, files=len(payload), replaced=len(payload)-added, added=added, obsolete=len(obsolete))
    (stage / "说明.txt").write_text(readme, encoding="utf-8-sig")
    write_json(stage / "build-info.json", {"date": args.date, "source_files": sources,
               "settings_seed_sha256": sha(local(args.out / "maimai_NGO_skin_20261101/files", SETTINGS)),
               "stock_archive_bytes": args.stock_archive.stat().st_size,
               "excluded_source_files": sorted(str(PureWindowsPath(f.relative_to(args.package)))
                    for f in local(args.package, INCLUDE_TREES[0]).rglob("*") if f.is_file() and skipped(str(PureWindowsPath(f.relative_to(args.package))))),
               "obsolete_added_files": obsolete})
    stage.rename(root)
    print(f"Built {root}: {len(payload)} files, {added} added, {len(obsolete)} obsolete", flush=True)
    if not args.no_zip:
        make_zip(root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("out", type=Path)
    parser.add_argument("--date", default=datetime.date.today().strftime("%Y%m%d"))
    parser.add_argument("--package", type=Path, default=PKG)
    parser.add_argument("--stock-archive", type=Path, default=ZIP)
    parser.add_argument("--no-zip", action="store_true")
    parser.add_argument("--zip-only", action="store_true")
    args = parser.parse_args()
    datetime.datetime.strptime(args.date, "%Y%m%d")
    build(args)


if __name__ == "__main__":
    main()
