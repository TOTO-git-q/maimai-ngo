"""PartnerLive2D MCP: tools for the mod's HTTP routes (src\\Unity\\ControlServer.cs) that the core tools in
server.py do not cover: the runtime theme switch, music-select layout tuning, live partner values incl. the
result / partner-room placement keys, and the test / debug routes under /api/test/.

Registered by server.py (register(mcp, server_module)); every function is also a plain callable for tests.
Live tools need the game running with the mod (else ok=false, game_running=false, like the core live tools).
"""
from __future__ import annotations

import re
import shutil
import time
import urllib.parse
from pathlib import Path
from typing import Any, Optional

import ngo_common as C

AUTOPLAY_MODES = ("None", "Critical", "Perfect", "Great", "Good", "Miss", "Random")
RANK_RE = re.compile(r"^(None|Rank_[A-Z]{1,6})$")
IMAGE_ID_RE = re.compile(r"^[A-Za-z0-9_-]{1,64}$")


def _get(path: str, query: Optional[dict] = None, timeout: float = 12.0) -> dict:
    if query:
        q = {k: v for k, v in query.items() if v is not None}
        if q:
            path += "?" + urllib.parse.urlencode(q)
    return C.core._api("GET", path, None, timeout)


def _post(path: str, body: Optional[dict] = None, timeout: float = 12.0) -> dict:
    return C.core._api("POST", path, body if body is not None else {}, timeout)


def _png(path: str, query: Optional[dict], save_path: Optional[str]) -> Any:
    if query:
        q = {k: v for k, v in query.items() if v is not None}
        if q:
            path += "?" + urllib.parse.urlencode(q)
    png, err = C.core._api_png(path)
    if err:
        return err
    return C.core._png_result(png, save_path)


# ======================================================================================
# theme
# ======================================================================================

def theme(enabled: Optional[bool] = None) -> dict:
    """Runtime theme override (ThemeOverride.cs): swaps UI sprites / RawImage textures by name for the PNGs in the
    folder named by settings.ini `theme = <folder>` (read at game start; theme\\layout.ini may move/scale nodes).
    enabled=None: status only (enabled, folder, loaded / missed sprite names, per-file errors).
    enabled=True/False: switch it on/off live (on needs the theme line in settings.ini and a game restart first).
    The NGO skin itself does not rely on it: its textures are patched into resources.assets, because the runtime
    swap shows stock art for a moment on every screen and costs a scan per frame."""
    if enabled is None:
        return _get("/api/theme")
    return _post("/api/theme", {"enabled": bool(enabled)})


# ======================================================================================
# music select (MusicSelectSession / MusicSelectSettings / select_layout.ini)
# ======================================================================================

def music_select_info() -> dict:
    """Live music-select state: the preset in use (enabled, selectPartner, groups, nodes, images, partner rect,
    tab label options), a feature fault message if the layout disabled itself, and for every open select session
    each player's phase (Genre / Music / Difficulty / Menu / Option ...), the reason the layout is not applied
    (null = applied), layout info, the partner view id (sprite / Live2D) or stock partner, and reaction-router
    state. Works on any screen; sessions is empty outside music select."""
    return _get("/api/music-select")


def music_select_layout(changes: dict, save: bool = False) -> dict:
    """Change the live music-select preset (POST /api/music-select/layout). Everything is validated by the mod
    first; a bad key or range changes nothing (HTTP 400 with the reason). Returns music_select_info.

    changes (any subset; same JSON as the `layout = {...}` line of LocalAssets\\PartnerLive2D\\select_layout.ini):
      partner: {bottomX -600..600, bottomY -1400..400, height 100..2400, cropBottom 0..1} - the partner rect in
               Main-local px (bottom centre anchored; width follows the sprite/model aspect)
      groups: {<chains|tabs|selector|status|myStatus|difficultyHeader|achievementRate|rivalData|extraInfo|festaDeco|
               challengeBG|challengeDeco>: {scale .35..1.2, offsetX/offsetY -540..540, hidden bool}}
      nodes: {"<path under Main>": {scale .35..2, offsetX/offsetY -540..540, hidden, sprite "<png in select_sprites>"|null}}
             (input/touch layers are protected; prefer music_select_node for one node)
      images: {"<id>": {sprite, parent, siblingIndex int|"first"|"last", x, y, width, height, hidden} | null}
      myStatusContent: {offsetX, offsetY}; difficultyLabel: {x, y, width, height}
      tabLabelColor "#RRGGBB" or "" (game colour), tabLabelOffsetY / tabTitleOffsetY -540..540
      enabled bool, selectPartner "equipped" | "stock" | <partner folder name>, debugTouchZones, categoryKeys
    save=True also writes select_layout.ini and the selectView / selectPartner / categoryKeys lines of
    settings.ini (prefer music_select_save, which backs both up first).
    Example: {"partner": {"height": 800, "bottomY": -200}}."""
    if not isinstance(changes, dict) or not changes:
        return C.core._err("changes must be a non-empty object, e.g. {\"partner\": {\"height\": 800}}")
    if "save" in changes:
        return C.core._err("pass save as the tool parameter, not inside changes")
    body = dict(changes)
    body["save"] = bool(save)
    return _post("/api/music-select/layout", body)


def music_select_node(path: str, values: dict) -> dict:
    """Tune one node of the music-select hierarchy live (POST /api/music-select/node; not saved - call
    music_select_save to keep it). path: node path under Canvas/Main as music_select_tree shows it.
    values: {scale .35..2, offsetX / offsetY -540..540, hidden bool, sprite "<file>.png" in
    LocalAssets\\PartnerLive2D\\select_sprites | null (= back to the game's sprite)}. Re-posting the same sprite
    name reloads an edited PNG. Main itself, its ancestors and input/touch layers are refused by the mod."""
    if not isinstance(path, str) or not path.strip():
        return C.core._err("path must be a node path under Main, e.g. 'Null_GenreControll/SelectorTab(Clone)/Null_Btn_R'")
    if not isinstance(values, dict) or not values:
        return C.core._err("values must be a non-empty object such as {\"hidden\": true}")
    if "path" in values:
        return C.core._err("values must not contain 'path'")
    body = {"path": path}
    body.update(values)
    return _post("/api/music-select/node", body)


def music_select_image(image_id: str, values: Optional[dict] = None, delete: bool = False) -> dict:
    """Add / change / delete one overlay image of the music-select preset live (POST /api/music-select/image;
    not saved - call music_select_save). image_id: 1-64 letters, digits, _ or -.
    values: {sprite "<file>.png" in select_sprites (required for a new image), parent "<node path>",
    siblingIndex int | "first" | "last" (draw order inside the parent), x, y (-10000..10000), width, height
    (0.01..8192), hidden bool}. delete=True removes the image (values must be empty)."""
    if not isinstance(image_id, str) or not IMAGE_ID_RE.match(image_id):
        return C.core._err("image_id must be 1-64 letters, digits, '_' or '-'")
    body: dict[str, Any] = {"id": image_id}
    if delete:
        if values:
            return C.core._err("delete=True takes no values")
        body["delete"] = True
    else:
        if not isinstance(values, dict) or not values:
            return C.core._err("values must be a non-empty object (or pass delete=True)")
        if "id" in values or "delete" in values:
            return C.core._err("values must not contain 'id' or 'delete'")
        body.update(values)
    return _post("/api/music-select/image", body)


def music_select_reset(confirm: bool = False) -> dict:
    """Replace the live music-select preset with the built-in defaults (feature off) and restore every select
    screen object. Disk files are untouched, but there is no route to re-read select_layout.ini: getting the
    tuned preset back needs a game restart, and a music_select_save after this would overwrite the saved
    preset with the defaults. Requires confirm=True."""
    if not confirm:
        return C.core._err("refusing without confirm=True (see the description: a later save would lose the saved preset)")
    return _post("/api/music-select/reset")


def music_select_save(backup: bool = True) -> dict:
    """Write the live music-select preset to LocalAssets\\PartnerLive2D\\select_layout.ini and the selectView /
    selectPartner / categoryKeys lines of settings.ini (POST /api/music-select/save). backup=True first copies
    both files into the MCP state folder (PARTNERLIVE2D_STATE\\backups\\<time>), outside the game folder so a
    release pack never picks them up. Returns music_select_info plus the backup folder."""
    backup_dir = None
    if backup:
        files = [C.assets_root() / "select_layout.ini", C.settings_path()]
        backup_dir = C.state_dir() / "backups" / (C.stamp() + "-music_select_save")
        try:
            backup_dir.mkdir(parents=True, exist_ok=True)
            for f in files:
                if f.exists():
                    shutil.copy2(f, backup_dir / f.name)
        except OSError as e:
            return C.core._err(f"backup failed, nothing saved: {e}")
    r = _post("/api/music-select/save")
    if isinstance(r, dict) and backup_dir is not None:
        r["backup"] = str(backup_dir)
    return r


def music_select_tree(root: Optional[str] = None, depth: int = 3, player: Optional[int] = None,
                      save_path: Optional[str] = None) -> dict:
    """Hierarchy of the live music-select screen (GET /api/music-select/tree) as the layout code sees it: node
    names, paths usable in music_select_node / images.parent, and their layout state. root: start node path
    (default Main), depth 0..20, player 0/1 (default: the first active side). Needs an open music select
    (HTTP 409 otherwise). save_path: write the full JSON there and return only a preview."""
    try:
        depth = int(depth)
    except (TypeError, ValueError):
        return C.core._err("depth must be an integer 0..20")
    if not 0 <= depth <= 20:
        return C.core._err("depth must be 0..20")
    if player is not None and player not in (0, 1):
        return C.core._err("player must be 0 or 1")
    r = _get("/api/music-select/tree", {"root": root or None, "depth": depth, "player": player})
    return C.save_or_return(r, save_path)


def music_select_sprite(path: str, player: Optional[int] = None, save_path: Optional[str] = None) -> Any:
    """PNG export of the sprite an Image node of the live music-select screen currently shows
    (GET /api/music-select/sprite.png), e.g. to study a stock texture before redrawing it. path: node path as in
    music_select_tree. Returned as an image; save_path also writes the PNG."""
    if not isinstance(path, str) or not path.strip():
        return C.core._err("path must be a node path")
    if player is not None and player not in (0, 1):
        return C.core._err("player must be 0 or 1")
    return _png("/api/music-select/sprite.png", {"path": path, "player": player}, save_path)


# ======================================================================================
# partners (live values; placement keys)
# ======================================================================================

def get_live_partner(name: str) -> dict:
    """The running game's view of one partner (GET /api/partners/<name>): parsed values incl. unsaved live edits
    (scale / offsetX / offsetY, roomScale / roomOffsetX / roomOffsetY, resultUnder, renderSize, motions,
    expressions, the raw key/value map) and the model's motion groups. get_partner reads the file on disk instead."""
    return _get(f"/api/partners/{C.core._q(name)}")


def set_partner_placement(name: str, screen: str, scale: Optional[float] = None, offset_x: Optional[float] = None,
                          offset_y: Optional[float] = None, result_under: Optional[str] = None,
                          save: bool = False) -> dict:
    """Size / position of a custom partner on one screen; maps to partner.ini keys and goes through
    set_partner_values (live when the game runs, save=True writes the file; offline edit when it does not).
      screen="result": scale (0.05..10, times the stock partner's height), offset_x / offset_y (-5..5, in stock
        partner heights, from the stock partner's bottom centre), result_under = comma list of result-screen
        objects (name or path end, e.g. "UI_DXRating/Base, UI_MusicData") given their own canvas one order below
        the screen so they draw UNDER the partner ("" = none). Also used by the preview overlay.
      screen="room": the collection's partner room, keys roomScale / roomOffsetX / roomOffsetY.
    The music-select partner is NOT placed here: it uses the select preset's partner rect
    (music_select_layout {"partner": {...}})."""
    screen = (screen or "").strip().lower()
    values: dict[str, Any] = {}
    if screen == "result":
        keys = ("scale", "offsetX", "offsetY")
        if result_under is not None:
            values["resultUnder"] = result_under
    elif screen == "room":
        keys = ("roomScale", "roomOffsetX", "roomOffsetY")
        if result_under is not None:
            return C.core._err("result_under only applies to screen='result'")
    elif screen in ("select", "musicselect", "music_select"):
        return C.core._err("the music-select partner uses the select preset rect: music_select_layout({\"partner\": "
                           "{\"height\": ..., \"bottomY\": ..., \"bottomX\": ...}})")
    else:
        return C.core._err("screen must be 'result' or 'room'")
    for k, v in zip(keys, (scale, offset_x, offset_y)):
        if v is not None:
            values[k] = v
    if not values:
        return C.core._err("nothing to change: give scale, offset_x, offset_y" + (" or result_under" if screen == "result" else ""))
    r = C.core.set_partner_values(name, values, save)
    if isinstance(r, dict):
        r.setdefault("screen", screen)
    return r


# ======================================================================================
# test / debug routes (TestHarness.cs, HeapWalk.cs, MusicSelectSession.MusicProbe)
# ======================================================================================

def test_screen_state(last: int = 10) -> dict:
    """Where the game is: the current screen (last process the game added, e.g. AdvertiseProcess, EntryProcess,
    MusicSelectProcess, GameProcess, ResultProcess, CollectionProcess), the last `last` process-log entries
    (name, time in s), whether testInput is on, the card screen's entry state (ConfirmEntry / ConfirmPlay ...
    while EntryProcess runs) and the quick-login counter (coin key on the attract screen). Always available,
    no testInput needed."""
    procs = _get("/api/test/processes")
    if procs.get("ok") is False:
        return procs
    lst = procs.get("processes") or []
    names = [str(p.get("name", "")) for p in lst]
    short = [n.split(".")[-1] for n in names]
    non_fade = [s for s in short if not s.endswith("FadeProcess")]
    try:
        n = max(1, min(int(last), 200))
    except (TypeError, ValueError):
        n = 10
    out: dict[str, Any] = {
        "ok": True,
        "screen": short[-1] if short else "",
        "lastNonFade": non_fade[-1] if non_fade else "",
        "testInput": procs.get("testInput"),
        "processCount": procs.get("count"),
        "recent": [{"i": p.get("i"), "name": str(p.get("name", "")).split(".")[-1], "t": p.get("t")} for p in lst[-n:]],
    }
    entry = _get("/api/test/entry")
    out["entry"] = {k: v for k, v in entry.items() if k != "ok"} if entry.get("ok") is not False else {"error": entry.get("error")}
    ql = _get("/api/test/quicklogin")
    out["quickLoginCount"] = ql.get("count") if ql.get("ok") is not False else None
    return out


def test_music_probe(find_id: int = 0) -> dict:
    """Music select (read only): category index / name / size and list index under the cursor plus the song id;
    with find_id > 0 also the nearest category and index holding that music id (findCategory / findIndex /
    findCategoryName), so a script can walk there with the left/right buttons. Empty outside music select."""
    try:
        fid = int(find_id)
    except (TypeError, ValueError):
        return C.core._err("find_id must be an integer")
    return _get("/api/test/music", {"find": fid if fid > 0 else None})


def test_ui_dump(contains: Optional[str] = None, limit: int = 200, save_path: Optional[str] = None) -> dict:
    """Every visible UI Image / SpriteRenderer on screen right now (GET /api/test/ui): sprite, texture, screen
    rect x/y/w/h in px (origin bottom-left), Image type (Simple/Sliced/Tiled/Filled), tint colour RRGGBBAA and
    parent/name path. Ties what a screenshot shows to the texture that draws it (first step before any redraw).
    contains: case-insensitive filter on sprite / texture / path; limit: max items returned (the total is always
    reported); save_path: write the full unfiltered dump there."""
    r = _get("/api/test/ui")
    if r.get("ok") is False:
        return r
    items = r.get("items") or []
    saved = None
    if save_path:
        saved = C.save_or_return(r, save_path).get("saved")
    if contains:
        c = contains.lower()
        items = [i for i in items if any(c in str(i.get(k, "")).lower() for k in ("sprite", "texture", "path"))]
    try:
        n = max(1, min(int(limit), 5000))
    except (TypeError, ValueError):
        n = 200
    out = {"ok": True, "screen": r.get("screen"), "total": len(r.get("items") or []), "matching": len(items),
           "items": items[:n]}
    if saved:
        out["saved"] = saved
    return out


def test_tree(of: str, up: int = 0, depth: int = 4, save_path: Optional[str] = None) -> dict:
    """Scene hierarchy around the first ACTIVE object named `of` (GET /api/test/tree): go `up` parents, then list
    children down to `depth` (0..30) with off flags, canvases (override sorting, layer, order), Graphic type and
    sprite, renderer sorting, and RectTransform pos/size. Sibling order = draw order inside one canvas, so this is
    how to find what draws over the partner (e.g. of="PartnerLive2D_<partner folder>", up=3 on the result
    screen). save_path: full JSON to a file, preview returned."""
    if not isinstance(of, str) or not of.strip():
        return C.core._err("of must be an object name")
    try:
        up, depth = int(up), int(depth)
    except (TypeError, ValueError):
        return C.core._err("up and depth must be integers")
    if not 0 <= depth <= 30:
        return C.core._err("depth must be 0..30")
    if up < 0:
        return C.core._err("up must be >= 0")
    r = _get("/api/test/tree", {"of": of, "up": up, "depth": depth})
    return C.save_or_return(r, save_path)


def test_renderers(save_path: Optional[str] = None) -> dict:
    """Menu-background renderers (GET /api/test/renderers): every renderer under UI_CMN_BG / UI_CMN_SubBG with
    enabled / visible, the inactive ancestor that hides it, sorting layer + value + order, material render queue,
    shader, world bounds, sprite colour, particle count, and which cameras see it where; plus all cameras.
    Used when patching the 3D background in resources.assets. save_path: full JSON to a file."""
    return C.save_or_return(_get("/api/test/renderers"), save_path)


def test_select_perf(reset: bool = False) -> dict:
    """Cost of the music-select layout code (GET /api/test/selectperf): per call kind (preload / apply /
    maintain ...) calls, total / avg / max ms, plus frame-time median / p95 / max over the last minute of select
    frames. reset=True clears the counters after reading (idle on the music list, then read again)."""
    return _get("/api/test/selectperf", {"reset": "1" if reset else None})


def test_autoplay(mode: str = "Critical", force: bool = False) -> dict:
    """The game's own autoplay for the next / current chart (POST /api/test/autoplay; needs settings.ini
    testInput = true at game start, else HTTP 409). mode: None (off), Critical, Perfect, Great, Good, Miss,
    Random. With AquaMai UX.DontRuinMyAccount the autoplayed score is not saved (and the result shows the
    reset score, so autoplay cannot show high ranks - use test_force_rank). Only on the off-screen test instance
    started by drive.ps1 (force=True skips that check)."""
    if mode not in AUTOPLAY_MODES:
        return C.core._err("mode must be one of " + ", ".join(AUTOPLAY_MODES))
    g = C.guard_test_instance("test_autoplay", force)
    if g:
        return g
    return _post("/api/test/autoplay", {"mode": mode})


def test_force_rank(rank: str, force: bool = False) -> dict:
    """Make the result screen play the clear-rank ANIMATION of `rank` (POST /api/test/rank; testInput needed):
    Rank_D .. Rank_SSSP, or None to stop forcing. Only the background clip changes; letters, score, record and
    upload stay real. Used to look at the high-rank effects and the partner's tier clips. Only on the off-screen
    test instance (force=True skips that check)."""
    if not isinstance(rank, str) or not RANK_RE.match(rank):
        return C.core._err("rank must be None or Rank_<letters>, e.g. Rank_SSSP")
    g = C.guard_test_instance("test_force_rank", force)
    if g:
        return g
    return _post("/api/test/rank", {"rank": rank})


def test_heapwalk(out_dir: Optional[str] = None, force: bool = False) -> dict:
    """Walk the whole managed heap from its roots (POST /api/test/heapwalk, HeapWalk.cs): every static field and
    live MonoBehaviour / ScriptableObject, objects and bytes per type and per root field. The game FREEZES while
    it runs (tens of seconds to minutes) - only on the off-screen test instance (force=True skips that check),
    ideally on the song list a few tracks apart. The mod writes LocalAssets\\PartnerLive2D\\heapwalk\\walk_<time>.tsv;
    this tool moves it to out_dir (default PARTNERLIVE2D_STATE\\heapwalk) as walk_<date>_<time>.tsv so it never ends
    up in a release pack. Compare two walks with heapwalk_diff."""
    g = C.guard_test_instance("test_heapwalk", force)
    if g:
        return g
    r = _post("/api/test/heapwalk", {}, timeout=900.0)
    if r.get("ok") is False or not r.get("file"):
        return r
    src = Path(str(r["file"]))
    dest_dir = Path(out_dir) if out_dir else (C.state_dir() / "heapwalk")
    try:
        dest_dir.mkdir(parents=True, exist_ok=True)
        dest = dest_dir / ("walk_" + time.strftime("%Y%m%d_") + src.stem.replace("walk_", "") + src.suffix)
        shutil.move(str(src), str(dest))
        r["movedTo"] = str(dest)
        try:
            src.parent.rmdir()   # only when empty
        except OSError:
            pass
    except OSError as e:
        r["moveError"] = f"walk left at {src}: {e}"
    return r


# ======================================================================================

TOOLS = [
    theme,
    music_select_info, music_select_layout, music_select_node, music_select_image, music_select_reset,
    music_select_save, music_select_tree, music_select_sprite,
    get_live_partner, set_partner_placement,
    test_screen_state, test_music_probe, test_ui_dump, test_tree, test_renderers, test_select_perf,
    test_autoplay, test_force_rank, test_heapwalk,
]


def register(mcp: Any, server_module: Any) -> None:
    C.bind(server_module)
    for fn in TOOLS:
        mcp.tool()(fn)
