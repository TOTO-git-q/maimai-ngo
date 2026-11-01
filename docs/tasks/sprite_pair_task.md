# Task: sprite-clip backend for the NGO pair partner (PartnerLive2D)

Working directory: `<PROJECT>`. This is a C# MelonLoader mod for maimai DX: Unity 2018.4 (Mono), .NET Framework 4.7.2, **C# LangVersion 7.3 — no newer syntax**.

## Worklog protocol (mandatory)

- Keep `tasks/sprite_pair_worklog.md`.
- If it already exists when you start, read it and continue from the last verified step instead of starting over.
- After each step, append three things: what you did, what you verified (command + result), and what is next.
- End the worklog with a line `DONE`, or `BLOCKED: <reason>`.

## Rules

- **Build:** use only `powershell -ExecutionPolicy Bypass -File build.ps1`. Never pass `-Install`; build.ps1 already sets the dotnet environment.
- **Files you must not touch:**
  - anything under `<GAME_ROOT>`;
  - `mcp/`, `deploy/`, `release/`;
  - the NGO skin and theme code (`ThemeOverride.cs`, `Ngo*.cs`), beyond a one-line hook if it is unavoidable.
- **Never start the game.** The reviewer runs the in-game tests.
- **Sources are read-only:**
  - the sprite clips: `<WORK_ROOT>\ngo-motion-v2\work\sprites\drawings\`;
  - the decompiled game source: `<DECOMPILED>\`.
  - Never modify anything under `<WORK_ROOT>`.
- **Memory and GC:**
  - Never use `ConditionalWeakTable`. It leaks on this Mono/Boehm runtime.
  - No managed allocations per frame in update or render paths.
  - The game turns GC off during songs (`GameProcess`), so never load files or create textures during gameplay.
- **Shaders:** only the game's built-in shaders are available. The mod already renders the Live2D partner into a premultiplied-alpha RenderTexture and shows it through a RawImage with "Particles/Alpha Blended Premultiply". Reuse that pipeline.
- **Ambiguity:** take the most literal reading, record it under "Spec notes" in the worklog, and continue.
- **Do not stop on a replaceable tool.** If a library or tool named in this spec fails, use an equivalent, record it under "Spec notes", and continue. `BLOCKED` is only for things that make the whole task impossible.
- **Part B is the main deliverable.** Parts A and B must both be complete and integrated. Isolated helper classes that are not wired into PartnerView do not count.

## Background

The NGO pair partner currently renders a rig3 Live2D model:
- folder: `<PKG>\LocalAssets\PartnerLive2D\ngo_pair\`;
- partnerId 502, naviCharaId 91.

We now play pre-made sprite clips instead of the Live2D model.
- **Clips:** 50 in total, 25 per character: K (超天酱) and Ame (糖糖).
- **One clip** = a folder of lossless green-screen PNG drawings plus a `timing.json`:

```json
{"name": "16-K-L1", "fps": 24, "frames": 103, "drawings": [{"file": "00.png", "src": 0, "frames": 3, "smear": 812, "dropped": 0}]}
```

- **Drawing hold:** `frames` per drawing is how many 24 fps frames that drawing is held (mostly 3).
- **Rest pose:** every clip starts and ends at the same rest pose.
- **Canvas sizes:** K drawings are 1344×768, Ame drawings are 768×768.
- **Green key:** a pixel is background when, in OpenCV HSV ranges, 40 < H < 85 and S > 120 and V > 80. Everything else is character.

**Composition.**
- The two characters share one composition: K is in front of Ame, and they overlap.
- `tasks/sprite_pair/pair_layout.json` gives each character's 2×3 affine from its drawing canvas to a shared 1024×1024 pair canvas. Verified: the rest drawings land on the original pair art within about 2 levels median.
- `tasks/sprite_pair/pair_reference.png` is that original pair art in the pair canvas, on a green background, for checking.

**Clip groups.** The clip name prefix decides the group, and each character has its own clips (`<c>` = K or Ame):

| group | clips per character | when it plays |
|---|---|---|
| Idle | `1-K-15s` / `1-Ame-15s` (15 s) | music select: loops forever |
| Random | `2-<c>-1`, `2-<c>-2` | music select: every 8–15 s (uniform random, one independent timer per character) |
| Tap | `tap-K-A`, `tap-K-B`, `tap-K-C` / `tap-Ame-A`, `tap-Ame-B1`, `tap-Ame-F` | music select: the player touches the partner |
| Confirm | `7-<c>-A`, `7-<c>-B` | the song is confirmed (entering the Menu phase, the last screen before START) |
| Enter | `8-K-A` / `8-Ame-A` | START pressed (`MusicSelectProcess.OnGameStart` → ReleaseWait) |
| Result<Tier>Loop | `1x-<c>-L1`, `1x-<c>-L2` | result screen: loops for the whole screen |
| Result<Tier>Reaction | `1x-<c>-R1`, `1x-<c>-R2` | result screen: the player touches the partner (`ResultMonitor.TouchPartnerCharacter`) |

**Tiers** (by the clip prefix):

| prefix | tier | condition |
|---|---|---|
| 13 | Fail | the song was not cleared |
| 14 | Clear | cleared, achievement < 97.0000 % |
| 15 | Good | 97.0000 % ≤ achievement < 100.0000 % |
| 16 | Top | achievement ≥ 100.0000 %, or All Perfect |

Use the game's own clear flag and achievement value. Look at how `Patches.cs` and `TestHarness.cs` already read the result; TestHarness also has a rank-forcing test patch.

**Picking rules.**
- **Random pick:** within a group, pick at random, but never the same clip twice in a row for the same character.
- **Independent characters:** the two characters play independently.
- **Triggers for "the partner"** (Tap, Confirm, Enter, result Reaction):
  - the group clip starts on both characters;
  - the second character starts 0–0.2 s later (random);
  - each character picks its own clip.
- **Random group:** fires per character on its own timer. It never interrupts a non-idle clip; reschedule it instead.
- **Return to loop:** after any one-shot clip ends, the character returns to its page's loop: Idle on music select, the tier's Loop on the result screen.

**Switching clips.** The rest drawings of different clips differ by 1–2 px plus a little texture noise; they come from separately generated videos.
- Every switch between two clips of one character is a **0.12 s crossfade**, from the outgoing current drawing to the incoming first drawing.
- A loop restarting itself needs no crossfade.

## Part A — exporter

Use Python 3.12 via the system `python`; numpy, opencv-python, Pillow and etcpak are installed. Write `tools/ngo_sprites_export.py`. It reads the 50 clip folders and writes `stage/ngo_pair_sprites/` inside the working directory.

1. **Keying.**
   - Produce soft alpha from the green key, not a hard 0/1 mask (for example, alpha from the distance to the background colour inside a 1–2 px band at the edge).
   - Despill green on edge pixels: clamp G toward max(R, B).
   - No green fringe may be visible on a mid-grey or a white background.
   - Store **premultiplied** RGBA. It is needed for bilinear filtering and for the premultiplied RenderTexture.
2. **Cropping.** For each clip, crop to the union bounding box of all its drawings plus 2 px, and pad width and height to multiples of 4.
3. **Encoding.**
   - Encode each drawing as **BC7** with `etcpak.compress_bc7`, no mipmaps.
   - Flip the rows for Unity's bottom-up texture layout. Verify the orientation by decoding back with **`texture2ddecoder.decode_bc7(payload, w, h)`**, which returns BGRA. Do NOT use `etcpak.decompress_bc7`: on this install it raises MemoryError on bytes and TypeError on bytearray. The reviewer checked `texture2ddecoder` on a real 664×648 drawing: mean abs error 0.26, p99 6.
   - Write one pack file per clip, `<clip>.bin`, made of the BC7 payloads concatenated.
4. **`manifest.json`.**
   - **Top level:**
     - version;
     - pair canvas size (1024×1024);
     - draw order (Ame, then K);
     - crossfade seconds (0.12).
   - **Per clip:**
     - name, character, group, and tier if any;
     - fps (24) and total frames;
     - texture width and height;
     - the crop rect in pair-canvas coordinates, as float x, y, w, h after applying the character's matrix;
     - the drawings list: byte offset, byte length, hold frames.
5. **QA output** for the reviewer, in `stage/ngo_pair_sprites/_qa/`:
   - **`keying_<clip>.jpg` for 6 clips** (two K, two Ame, and the two Idle loops). Each file holds 3 drawings: the first, the middle, and the drawing whose silhouette differs most from the first.
     - Composite each drawing at 1:1 over mid-grey (#808080) and over white, side by side.
     - Add a 3× zoom crop of an edge region with hair or ribbons.
   - **`pair_rest.png`:** both characters' first Idle drawings composited in the pair canvas in draw order. Put it next to `pair_reference.png` and show their absolute difference.
   - **`preview_select.mp4`:** 24 fps, about 20 s, pair canvas 1024×1024 over mid-grey, H.264 (OpenCV or imageio). It simulates the runtime on music select:
     - both Idle loops;
     - a Random clip on K at about 4 s;
     - a Random clip on Ame at about 9 s;
     - a Tap on both characters at about 14 s;
     - all with the 0.12 s crossfades.
     It must decode the BC7 packs back, the same path the runtime uses, so it also proves the packs.
6. **Sizes.** Print the total pack size and the estimated GPU memory per page: the music select set, and the result set for each tier.

## Part B — runtime backend (C#)

1. **Config and fallback.**
   - `partner.ini` gets an optional key `sprites = <path to manifest.json>`, relative to the partner folder.
   - When the key is present and valid, this partner's PartnerView uses a new sprite backend instead of the Live2D avatar.
   - When it is missing, invalid or unsupported (for example `SystemInfo.SupportsTextureFormat(TextureFormat.BC7)` is false), log one line and fall back to the existing Live2D model, unchanged.
2. **Rendering.**
   - Draw the two characters into the same partner RenderTexture the Live2D path uses, with the pair canvas mapped onto the RenderTexture square.
   - Draw Ame first, then K. Each current drawing is one textured quad at its rect.
   - Use premultiplied blending (One, OneMinusSrcAlpha) with a built-in shader.
   - A crossfade draws the outgoing and the incoming quad with their weights.
   - The existing layout code must keep working untouched: partner.ini scale/offset, the select layout, and the result placement.
3. **Clock.** Advance by `Time.deltaTime` on a 24 fps timeline, using the hold frames from the manifest. Loops wrap exactly.
4. **Textures.**
   - **Creation:** `new Texture2D(w, h, TextureFormat.BC7, false)`, then `LoadRawTextureData`, then `Apply(false, true)`.
   - **What to load:** only the clips the current page can play.
     - Music select: Idle, Random, Tap, Confirm, Enter.
     - Result: the tier's Loop and Reaction.
   - **How to load:**
     - Read pack files on a worker thread into reusable buffers.
     - Create textures on the main thread, spread over frames with a budget of a few ms per frame, so the select screen never hitches.
     - Load the Idle/Loop clips first, and show the loop's first drawing as soon as it is loaded.
   - **Unloading:** destroy all textures when the page's partner view closes.
   - **Gameplay:** never load during a song.
5. **Triggers.**
   - **Music select:** run Idle plus the Random scheduler. `SelectSongReaction` and `Nod` do nothing for the sprite backend.
   - **Tap on music select:**
     - A touchDown triggers the Tap group when it lands on a touch-panel area whose screen position lies inside the partner's on-screen rect. `MusicSelectReactionRouter` already observes the touch areas.
     - Make the area list overridable with `settings.ini selectTapAreas = <comma list>`. If the computed list is empty, select taps are off.
     - Record in the worklog which areas you computed and why.
   - **Confirm:** fires on entering the Menu phase. See `MusicSelectPhasePolicy` and `MusicSelectSession` for how the mod detects phases.
   - **Enter:**
     - Add a Harmony patch for the ReleaseWait after `MusicSelectProcess.OnGameStart`. When this partner is shown and its Enter clips are loaded, ReleaseWait lasts until both characters' Enter clips have finished, capped at 4.0 s, instead of the fixed 1 s.
     - Keep the original 1 s behaviour on any exception, when the clips are missing, and in course mode or other special modes.
     - Add `settings.ini enterTransition = full|off`, default full.
     - With two players, wait for both.
   - **Result screen:**
     - When the sprite partner is attached, play the tier's Loop group for the whole result screen.
     - `TouchPartnerCharacter` plays the tier's Reaction on both characters.
     - The sprite backend ignores the Live2D-era state mapping (Default, FunStart, FunStartLoop, Sad01).
6. **Control API.** These must work with the sprite backend, so the existing MCP tools (`play_motion`, `screenshot`) can drive it:
   - `PlayMotion(group, index)`;
   - `Info()`, which reports:
     - the motion groups (= sprite groups, with clip counts);
     - loaded and total textures;
     - estimated VRAM;
   - `FramePng()`.
7. **Install helper.** Write `tools/ngo_sprites_install.ps1`, ASCII only.
   - It copies `stage/ngo_pair_sprites` to `<PKG>\LocalAssets\PartnerLive2D\ngo_pair\sprites\`.
   - It adds or updates the line `sprites = sprites/manifest.json` in that folder's partner.ini, after backing the file up.
   - **Do NOT run it.**

## Tests

- **Headless tests** in `tests/Headless`:
  - manifest parsing;
  - the picker: no immediate repeats over 1000 picks per group;
  - the timeline: drawing index at given times, and loop wrap;
  - crossfade weights;
  - tier selection at these edge cases: 96.9999 %, 97.0000 %, 99.9999 %, 100.0000 %, and All Perfect at 99 %.
- **Build:** `build.ps1` succeeds, and the default Headless run passes.

## Done =

- The exporter has run over all 50 clips and written the QA files.
- The build passes and the headless tests pass.
- Final message, at most 30 lines:
  - files changed and added;
  - pack size and VRAM estimate per page;
  - the computed select tap areas;
  - spec notes;
  - the exact commands you ran and their results;
  - anything that could not be verified without the game.
