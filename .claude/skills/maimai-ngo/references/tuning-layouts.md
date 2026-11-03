# Tuning layouts: select, result, partner room

Pattern for every screen: make a candidate sheet off screen, pick, apply live to confirm, then save (with a backup).
Values are restored after a sheet run; nothing is saved unless you save.

## Units

| screen | where the values live | unit |
|---|---|---|
| music select | `select_layout.ini` preset (`/api/music-select/*`) | Main-local px of `MusicSelectProcess/Canvas/Main`: (0, 0) = circle centre, +Y up. Group scales multiply the captured stock scale, offsets add to it. A node inside a scaled group moves in that group's units (tab-row nodes: screen px / 0.88 with tabs scale 0.88; board image: screen px / 0.84 with selector scale 0.84) |
| result, preview overlay | `partner.ini` `scale`, `offsetX`, `offsetY` | multiples of the stock partner's height, from the stock partner's bottom centre (measured once from the stock navi's graphics) |
| partner room | `partner.ini` `roomScale`, `roomOffsetX`, `roomOffsetY` | the same, in the room |

## Music select

- The partner is mounted as the FIRST child of `Canvas/Main`: behind every select element (song panel, tabs).
- Partner rect = preset `partner`: `bottomX` (-600..600), `bottomY` (-1400..400), `height` (100..2400), `cropBottom`
  (0..1 of the height cut off at the bottom). Width = height x the partner's aspect (1:1 for the sprite pair).
  The NGO pair uses height 800, bottomY -200; the earlier 1300 / -850 (tuned for a tall Live2D sample) hid the lower
  bodies behind the song panel, and the sprite canvas has margins, so the same rect shows the pair ~12 % smaller
  than a full-bleed Live2D model.
- The difficulty header (UI_CMN_TitleBase, band about +400..+520) only shows in the Difficulty phase, so a rect tuned
  in the Music phase can put a head under it. Check tuning shots in BOTH phases. Two tools:
  - `partner.under` (comma list of Main paths): each gets its own canvas one order below the screen canvas, so it
    draws under the partner (listed order = draw order). The NGO pair lists `UI_CMN_TitleBase/Null_UI_CMN_TitleBase/
    Base_Shadow, .../Base`: the pair overlaps the purple band while the MASTER text (`Title`) and the arrows stay on
    top. This is what the owner chose: "same height, Ame covers part of the band".
  - `partner.difficultyOffsetY` (-800..800, eased ~0.2 s) moves the pair in that phase instead. The NGO pair uses 0;
    the owner rejected moving it.
  Sibling order cannot do this: the header is Main's 17th child, after the song panel and buttons.
- With no custom partner equipped (`selectPartner = equipped`) the stock navi is shown in the same rect: its width is
  capped at 520 units, it hangs from the rect's top and is dropped 135 units to clear the freedom-mode banner.
- Candidate sheet: MCP `select_tune([{"label": "h800 y-200", "height": 800, "bottomY": -200}, ...])` ->
  `sheet.png` (lower screen of every candidate side by side), `cand_<label>.png`. Waits until the sprites are loaded
  (`loadedTextures == totalTextures`), so the first frame is not a half-loaded pair.
- Apply: `music_select_layout({"partner": {"height": 800, "bottomY": -200}})` on a running select screen, check with
  `screenshot`, then `music_select_save()` (backs up `select_layout.ini` + `settings.ini` into `<STATE>\backups`).
  With the game stopped you can edit the single `layout = {...}` JSON line instead (keep a copy).
- Nodes: find paths with `music_select_tree(depth=4)` (paths are relative to Main, e.g.
  `Null_GenreControll/SelectorTab(Clone)/Null_Btn_R`); `music_select_node(path, {"offsetX": 290, "offsetY": 210})`,
  `{"hidden": true}`, `{"sprite": "<png in select_sprites>"}` (only on an Image that has a stock sprite; `null` restores).
  Input / touch layers are protected (only leaf sprites under `UI_Buttons` / `UI_InfoButtons` may be replaced).
- Overlay images: `music_select_image("board", {"sprite": "board.png", "parent": "...", "siblingIndex": "first",
  "x": 0, "y": -249.2, "width": 1534, "height": 923.3})` - `first` draws behind its siblings.
- Tab labels: `tabLabelColor` (`""` = the game's colour), `tabLabelOffsetY`, `tabTitleOffsetY` (kept every frame).
- Genre phase shows only `UI_SelectorBG` and `Null_Indicator` of the genre selector; the stock backdrop shapes stay
  hidden so the board alone is the backdrop (`MusicSelectPhasePolicy`).
- `test_select_perf` shows the layout's cost; a fault (`music_select_info().failure`) disables the feature until a
  restart.

## Result screen

- Draw order under `ResultProcess` Canvas/Main: achievement band (`UI_Achievement_Base`) -> `Null_NavicharaRoot` (the
  partner) -> music data, achievement number, rank, `UI_DXRating`, medals, scoreboard. The partner therefore covers the
  achievement band but sits under everything after it.
- `resultUnder` (partner.ini) moves listed objects under the partner without touching the hierarchy the game's
  timelines bind to: each gets its own Canvas with override sorting one order below the screen canvas. The NGO pair
  uses `UI_DXRating/Base, UI_MusicData` (the rating band covered the legs); rank letters and numbers stay in front.
  A target that already is a Canvas, or is not found, is skipped with a log warning.
- Inspect: `test_tree(of="PartnerLive2D_<partner folder>", up=3, depth=8)` on a result screen shows the siblings and
  canvases around the partner.
- Candidate sheet: `select_tune(select_cands, result_candidates=[{"label": "s0.9", "scale": 0.9, "offsetX": -0.15,
  "offsetY": 0.15, "resultUnder": "UI_DXRating/Base, UI_MusicData"}, ...])` plays one chart (skipped), dumps
  `tree_near.json` / `tree_far.json` and writes `result_sheet.png` (the script tunes the partner folder `ngo_pair`).
- Apply: `set_partner_placement(name, "result", scale=..., offset_x=..., offset_y=..., result_under=..., save=True)`
  while a result screen shows it, or with the game stopped (offline edit; next start).
- Tier clips: `test_force_rank("Rank_SSSP")` on the test instance (testInput on) before the result; autoplay alone
  shows a reset score (DontRuinMyAccount) and so the Fail/low tiers.
- The partner appears on result only if the EQUIPPED partner id has a partner.ini.

## Partner room (collection)

- Reach it off screen: `to_collection()` (logs in, skips the credit's tracks, stops on `CollectionProcess` and leaves
  the game running), then `test_game_vkeys([...])` to open the partner tab, `screenshot`.
- `set_partner_placement(name, "room", scale=..., offset_x=..., offset_y=...)` live, check, `save=True`;
  finish with `test_game_stop`.
- The room plays the page clips (idle, random, tap); FunStart (closeness up) plays Confirm.

## Small pieces

- Collection icon: `icon.png` (128x128) - `tools\ngo_partner_icon.py` builds it from the median of the stock partner
  icons (frame, stripes, speaker badge) plus the partner art.
- Top-screen partner cards: textures listed in `<ASSETS>\ngo_open_cards.txt` (suffix ` front` = above the other
  pictures), art from `tools\ngo_partner_cards.py`.
- Backups of tuned files: `*.before-<date>-tune` next to the file (hand edits) or `<STATE>\backups` (MCP saves).
