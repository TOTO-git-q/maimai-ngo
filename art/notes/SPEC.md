# maimai NEEDY GIRL OVERDOSE theme - shared spec (read fully before your task)

Goal: reskin a local maimai DX 1.70 ("MAGiCAL") install so it reads as **maimai NEEDY GIRL OVERDOSE**,
in the visual language of the game NEEDY GIRL OVERDOSE (NGO): the Windose (Win95-style) desktop, pastel
pink / lavender / cyan, crisp pixel art, original NGO UI parts. Personal local use only.

## Inputs (all READ-ONLY)
- Game: <PKG>. UI art lives in <PKG>\Sinmai_Data\resources.assets (+ .resS):
  ~5437 Texture2D / 5991 Sprite. Prefixes: UI_ADV (title/attract), UI_TTR, UI_ENT (entry/mode), UI_CMN (common),
  UI_MSS (music select), UI_GAM (gameplay), UI_RSL (result), T_v170*/T_UI_BGv170 (MAGiCAL backgrounds),
  kari_LOGOv170, T_UI_LogoKariv170, UI_ADV_Img_LogoMaimaiDX, UI_ADV_Logo_* (layered title logo), v170transi.
  Data-driven art (jackets, icons, frames...) is in StreamingAssets\A000\AssetBundleImages\*.ab.
- Python 3.12 has UnityPy 1.25.2, Pillow, numpy, opencv, texture2ddecoder, etcpak, dnfile (run python WITHOUT -I).
  Open the game assets with UnityPy read-only; never save back into <GAME_DRIVE>\.
- Current screens (what the player sees, 540x960 captures, upper 1/4 = top sub-monitor banner, circle = main screen):
  <PROJECT>\tools\tour_overview\maimai_screens_20261027.png (28 labelled screens)
  and the single frames in ...\tour_overview\frames\ (file name = order_process).
- NGO unpacked game: <WORKSPACE>\NGO桌宠\素材源\9DM NEEDY GIRL OVERDOSE_完整解包_v2\
  01_Unity资源\{Texture2D,Sprite,Font,VideoClip,AudioClip,MonoBehaviour,...}: files are named resources__N.png with a
  sibling *.texture.json / *.json whose "name" field is the original asset name (e.g. bg_boot_en, boot_logo,
  BaseButton*, button_close, cursor_hourglass, icon_desktop_*, bg_stream*, JINE_*). Sprites may also be atlas crops.
  02_台本单独分离 = all game text; 05_桌面壁纸素材; 06_游戏BGM素材.
  **USER DECISION 2026-10-27: do NOT paste NGO's original textures into maimai - pasted originals look cheap.
  Every texture is REDESIGNED: new art made with image_gen.imagegen in NGO's visual language, using NGO assets
  only as style references (palette, pixel density, window chrome, line weight, motifs).** The only exception is
  the "NEEDY GIRL OVERDOSE" logo itself, which stays the original. The result must look like a deliberate
  maimai x NGO collaboration skin designed for a 1080x1920 arcade screen, not a collage of NGO UI parts.
  Also: <WORKSPACE>\NGO桌宠\素材源\桌面素材（手工重绘版） (hand-cleaned desktop parts) and statusbar\.

## Design rules (the user is picky; these are hard rules)
1. Faithful to NGO: Windose desktop / window chrome / JINE / stream UI. NGO's tone is needy and dark-cute;
   do not make it cheerful-generic, do not sanitise it.
2. No "AI look": no glossy gradients, glows, sparkles, lens flares, emoji, rounded pill badges, drop-shadow cards.
   Pixel art stays pixel-crisp (nearest-neighbour scaling only, integer factors where possible).
3. No explanatory small print, no "how to read" labels. Show, don't tell.
4. Text baked into images: Japanese (the game is Japanese), phrased like NGO's own UI/lines (take wording from
   02_台本单独分离 when something fits). Keep it short.
5. Readability first on gameplay/score elements; decoration never covers notes or numbers.
6. A replacement texture must keep the ORIGINAL texture's pixel size, alpha usage and layout (same 9-slice
   margins, same safe areas, same position of any baked text block) so it drops in without code changes.
   If the original is an atlas sprite, deliver the sprite-sized piece AND say which atlas/rect it belongs to.
7. Generated art: use Codex image generation (bitmap) with NGO reference crops given as image inputs; never hand-write
   SVG or draw programmatically. Programmatic work is limited to cropping, compositing, palette-sampling and resizing.
   Generate at a large size, then fit to the original texture's exact size/alpha mask. Rich, finished art: a blank
   pastel field with one small icon in the middle is not acceptable.
8. Title screen (user's explicit order): remove the MAGiCAL characters and illustration behind the logo and leave
   that area EMPTY (a Live2D Ame x KAngel will go there later); keep the sky / circular stage / card-touch bar;
   keep the small "maimai でらっくす" logo; replace the big "MAGiCAL" wordmark with NGO's classic
   "NEEDY GIRL OVERDOSE" logo (original asset from the unpack if it exists; otherwise a faithful recreation).

9. **USER DECISION 2026-10-27: the maimai screens are ANIMATED, built from many separately moving layers.**
   Before drawing, unpack the screen's prefab hierarchy (GameObject/RectTransform tree, Image/RawImage/
   SpriteRenderer components, Animator/AnimationClip curves that move/scale/fade/swap them) and list every
   element: background, frames, panels, glows, sweeps, particles, decorations, text plates, masks.
   Redraw ELEMENT BY ELEMENT: one image_gen generation per element (or per small family of identical elements),
   each delivered at its own original texture size and alpha, never flattening several layers into one picture.
   Effect layers (glow/flash/sweep/particle/mask) stay separate effect textures so the animation still works.
   The screen mock is then assembled from the redrawn elements in their default pose; also deliver a short
   animation preview (GIF or 6-frame strip) of any element group whose animation matters (e.g. title loop,
   loading spinner, transitions), driven by the real AnimationClip curves where you can read them.
   Keep elements.csv per sample: element path, texture/sprite name, size, role, animated properties, status.

10. **USER DECISION 2026-10-27: this is an OPERATING INTERFACE, not an illustration.** Every operable element
   (buttons, tabs, arrows, selectable cards, cursors/highlights, confirm/back tabs, scroll indicators) must read
   instantly as operable: same position, same hit size, clear normal/pressed/selected states, labels fully legible
   at arcade distance, strong contrast against what is behind it. Selected vs unselected must stay obvious.
   Decoration never sits on or competes with operable parts or with numbers/text the player must read.
11. **Review sheet (required):** out\<task>\review_sheet.png (and review_sheet_2.png ... if long): one row per
   element = element path + texture name | ORIGINAL element | REDRAWN element (same scale, on a neutral
   checkerboard so alpha is visible) | one-line note. The user reviews every element against its original and
   sends back the ones to redo, so every redrawn element must appear there, numbered (e.g. s6-03).

12. **USER DECISION 2026-10-27: many elements are SHARED across screens (transitions, backgrounds, common frames,
   decorations); some only need a RECOLOR.** <WORK>\out\shared\REGISTRY.csv (built by task c0) lists
   every element used from login to music select once, with columns: texture/sprite name, screens, owner task,
   treatment = redraw | recolor | keep. Designers: do Step A now, but before drawing anything wait for
   out\shared\REGISTRY_READY.txt (poll every 2 min, up to 60 min), then draw ONLY elements whose owner is your
   task and treatment is redraw. Elements owned by another task: do not draw; reference that task's output in your
   mock (or the original if not ready). treatment=recolor is done by task c1 for everyone. Scope is login ->
   music select only (no gameplay, no result).

## Safety
- Never modify anything under <GAME_ROOT>, never launch the game, never install anything into it.
- Write only inside your own output folder named in your task (and the mod source folder for task 5 only).

## Deliverables format (every art task)
- out/<task>/replacements/<OriginalTextureName>.png  (drop-in, original size)
- out/<task>/originals/<OriginalTextureName>.png     (exported original for comparison)
- out/<task>/compare_<n>_<topic>.png  side-by-side original | replacement, plus an in-context mock where the
  sample is a whole screen (paste onto the matching tour frame so the user sees the screen as it would look)
- out/<task>/NOTES.md: per sample - which textures, source of every NGO piece used (file + name), what was generated.

## Worklog protocol
Keep out/<task>/worklog.md: append after every step (what was verified, files written, next step).
If worklog.md exists when you start, read it and continue from the last step; do not redo finished steps.
Finish with a final line `DONE` in worklog.md.
