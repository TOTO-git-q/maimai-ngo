# maimai × NEEDY GIRL OVERDOSE skin: elements to redraw

A UI reskin for a private, local maimai DX 1.70 ("MAGiCAL") install. The goal is a game that reads as
**maimai NEEDY GIRL OVERDOSE** and uses the visual language of NEEDY GIRL OVERDOSE (NGO): the Windose (Win95-style)
desktop, pastel pink / lavender / cyan with dark accents, and crisp pixel art.
Personal use only. **Keep this repository private**: it contains game assets.

## Scope
Every screen from login to music select: screens 2–26 in `context/maimai_screens_20261027.png`.
Gameplay and result screens are out of scope.

## What is in here
- `originals/<group>/<TextureName>.png`: the original texture of each element to replace (678 files).
  Each `group` is one family of elements:
  - `s2_banner`: top banner
  - `s3_popup`: windows and popups
  - `s5_buttons`: bottom tab buttons
  - `s6_mode`: mode select
  - `s7_stamp`: stamp card / login bonus
  - `s8_ticket`: tickets
  - `s9_musicselect`: music select
  - `s11_login`: login
  - `s12_photo`: icon photo
  - `s13_area`: area select
  - `s14_tutorial`: tutorial select
  - `t2`: title
  - `recolor`: backgrounds, transitions, glows and generic patterns. A recolor is enough for these.
- **2026-10-27 addition: 1716 more originals.** These are layers that only appear during animation, effect layers,
  hidden branches, and state sprites (pressed / selected / variants), found by tracing the prefabs, animation
  clips and script sprite tables. The transition is also included: the bear-chip medallion `01_02`, the diagonal
  closing panels `UI_CMN_Parts_08_*`, the arrow sweeps `UI_CMN_Parts_06_*`, the rails `UI_CMN_Parts_07_*`, and the
  top-screen `Sub_*` images. They are listed in `registry/elements_added.csv`, including how each one is shown.
  `s9_musicselect` is the largest group: many of its files are state and variant sprites of the same parts.
  Draw one master per family and derive the rest.
- `registry/elements.csv`: one row per file, with its size, which screens use it, its role, and the treatment:
  - `redraw`: needs new art;
  - `recolor`: keep the shape, change the colors.

  It also lists the atlas sprites that the file contains.
- `registry/sprite_inventory.csv`: sprite metadata for every UI texture of the game (rect, pivot, 9-slice border).
- `context/`:
  - the 28 labelled screens;
  - single frames per screen (540×960 captures);
  - the finished title mock. Its logo is the original NGO logo, which stays as it is.
- `style_reference/`: a style board from an earlier round, for reference only. The user rejected the results drawn
  from it (see below), so do not treat it as a must-follow guide.

## Hard requirements
1. **Drop-in.** Every replacement keeps the original's exact pixel size and alpha usage, its 9-slice margins
   and safe areas, and the position of any baked text. Atlas textures keep every sprite rect in place.
   The game loads files by texture name.
2. **It is an operating interface.**
   - Buttons, tabs, arrows, selectable cards and highlights must read instantly as operable.
   - They keep the same position and the same hit size.
   - Normal, pressed and selected states must be clearly distinct.
   - Labels must be legible at arcade distance.
   - Decoration never covers operable parts or text the player must read.
3. **The screens are animated** and built from many separately moving layers.
   - Redraw each element on its own.
   - Never flatten layers together.
   - Glow, flash, sweep, particle and mask layers stay separate effect textures.
4. **Redesign, do not paste.** Do not paste NGO's original UI textures. Make new art in NGO's visual language.
   The only exception is the title logo.
5. **No "AI look":**
   - no glossy gradients, lens flares, emoji, rounded pill badges or drop-shadow cards;
   - pixel art stays pixel-crisp;
   - no explanatory small print.
6. **Baked text stays Japanese**, short, in NGO's own tone. The game's tone is needy and dark-cute: do not sanitise it.

## What the user rejected so far (avoid)
- Round 1:
  - blank pastel fields with one tiny icon;
  - maimai's rounded/hex window silhouettes with Win95 buttons glued on;
  - pasted NGO UI parts.
- Round 2: the bottom tab buttons and the other first-batch elements ("都不太行", none of them good enough).
  The popup only had its title band recolored, which was far too timid.

## Deliverables
- `redrawn/<group>/<TextureName>.png`: the same names and sizes as in `originals/`.
- A review sheet per group: one numbered row per element, showing original | new. The user goes through it
  element by element and sends back the ones to redo.
- Optional: in-context mocks on `context/frames/`.
