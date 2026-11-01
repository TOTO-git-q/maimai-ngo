# Task: sprite backend runtime (Part B of tasks/sprite_pair_task.md)

Working directory: `<PROJECT>`.

## Status

- **Part A is done.** The reviewer accepted it:
  - packs in `stage/ngo_pair_sprites/` (50 clips, 1,498 drawings, 579 MiB);
  - `manifest.json`;
  - keying, composition and preview checked by eye.
- **Do NOT re-export or touch** `stage/` or `tools/ngo_sprites_export.py`.
- **Your job:** Part B only.
  - Read `tasks/sprite_pair_task.md` in full. Its Rules section and its Part B section are the specification.
  - Read the end of `tasks/sprite_pair_worklog.md`, and keep appending to that worklog.
- **Reuse** what already exists in `src/Core/SpritePairData.cs`: manifest parser, picker, timeline, fade weights, tier policy.

## Order: implement all three steps in one run

### B1 — backend core
1. The sprite backend loads `manifest.json` plus the `.bin` packs from the partner folder:
   - BC7 textures via `LoadRawTextureData`;
   - file reads on a worker thread;
   - texture creation spread over frames;
   - only the current page's clips are loaded;
   - everything is destroyed on close.
2. It renders both characters into the partner RenderTexture: pair canvas onto the RT square, Ame then K, premultiplied blending, 0.12 s crossfades.
3. PartnerView integration:
   - the `sprites = ...` key in partner.ini switches this partner to the sprite backend;
   - fallback to Live2D with one log line;
   - `PlayMotion(group, index)`, `Info()` and `FramePng()` work.
4. Build, plus the default headless run.

### B2 — page triggers
1. **Music select:**
   - Idle loop, plus the per-character Random scheduler (8–15 s);
   - Confirm on entering the Menu phase;
   - Tap from touch areas, using the computed list with the `settings.ini selectTapAreas` override.
2. **Result:** the tier Loop; `TouchPartnerCharacter` plays the tier Reaction on both characters.
3. Build, plus headless.

### B3 — Enter transition
1. Add the ReleaseWait Harmony patch: hold until both Enter clips have finished, cap 4.0 s, original behaviour on any failure or special mode, `settings.ini enterTransition = full|off` (default full), and wait for both players when there are two.
2. Build, plus headless.

## Stop rule

- The run is complete only when B1, B2 and B3 are all implemented and the build plus headless run pass.
- **Do not end the run early.** If you are about to run out of context, write precise worklog notes and end the worklog with `CONTINUE: <next step>`.
- `BLOCKED` is only for something that makes Part B impossible, with evidence.

## Done =

- All three steps are wired in.
- `build.ps1` passes and the headless run passes.
- Final message, at most 30 lines:
  - files changed;
  - how each trigger is detected (class and method hooked);
  - the computed select tap areas;
  - the default value of `enterTransition`;
  - what can only be verified in game.
- **Do not** run the install helper, start the game, or write to `<GAME_ROOT>`.
