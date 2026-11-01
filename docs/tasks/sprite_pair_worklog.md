# Sprite pair worklog

## Orientation
- Did: Read task and repository engineering guidance; located source drawings, layout, reference art and existing runtime entry points. No previous sprite-pair worklog or Git checkout was present.
- Verified: `Test-Path` returned True for both source trees and both composition inputs; listing drawings found 50 named clip folders plus `_sheets`. `build.ps1` performs installation only with `-Install`.
- Next: inspect runtime integration, source metadata, and exporter prerequisites before implementation.

## Spec notes
- Treat six QA clips as the two Idle clips plus two additional clips for each character.

## Focused inspection
- Did: Traced the existing PartnerView rendering/control API, MusicSelectSession phase management, and touch router; inspected layout matrices and build entry point.
- Verified: `python --version` -> 3.12.10; `python -c "import numpy, cv2, PIL, etcpak; print('imports OK')"` -> imports OK. Source enumeration found 50 clip directories. Pair matrices are axis-aligned scale/translation.
- Next: establish baseline build, inspect the exact page transition and result data paths, and implement the exporter and backend in scoped source files.

## Baseline and transition evidence
- Did: Built the unmodified project and traced the game transition and result setup.
- Verified: `powershell -ExecutionPolicy Bypass -File build.ps1` succeeded with 0 warnings and 0 errors for both projects. Decompiled `MusicSelectProcess` uses a 1000 ms ReleaseWait, and `OnGameStart` switches to it; `ResultProcess` passes the game's clear flag to `SetStartData` and separately sets achievement.
- Next: inspect encoder API and write the export pipeline; integrate runtime after validating a representative encoded drawing.

## BC7 probe
- Did: Probed the installed etcpak API before exporter implementation.
- Verified: `python -c "import etcpak; ... compress_bc7(...,64,64) ... decompress_bc7(...)"` produced a 4096-byte BC7 payload and 16384 decoded bytes. A 4x4 decode instead raised MemoryError, so tiny synthetic images cannot establish orientation; test a representative crop instead.
- Next: implement and validate export against actual drawings, then perform the full run.

## Exporter prototype and decoder failure
- Did: Added an exporter prototype and an ASCII install helper (not run); compressed all 19 drawings in a representative 8-K-A pack. Produced a diagnostic composite with a separate installed decoder solely to inspect the provisional output.
- Verified: `python -m py_compile tools/ngo_sprites_export.py` passed. A metadata scan found 50 clips, 1498 drawings and zero hold-total mismatches. `etcpak.decompress_bc7` on the 664x648 real payload fails with MemoryError, including the package's non-SIMD extension; an independent decoder returned a 648x664x4 image. The upstream wrapper calls `PyByteArray_AsString` on a bytes object, consistent with the observed exception. This is not a substitute for the required etcpak decode verification. PowerShell parser and ASCII check of the install helper passed.
- Next: determine whether the specified decoder can be repaired without altering the user environment; keep the export clearly provisional until decoding and visual QA succeed.

## Isolated sprite data and headless checks
- Did: Added a standalone manifest parser, no-repeat picker, timeline, fade weights and tier policy; added headless coverage. These are not yet connected to the Unity runtime.
- Verified: `powershell -ExecutionPolicy Bypass -File build.ps1` succeeded for mod and headless with 0 warnings/errors. `<TOOLS>\dotnet\dotnet.exe tests/Headless/bin/Release/net8.0/Headless.dll` passed the sprite checks and all 8 existing model tests.
- Next: resolve the required decoder and complete export QA, then implement/integrate the runtime backend, triggers, page loading and select area calculations.

## Select-area estimate and remaining boundary
- Did: Computed candidate touch areas using MusicSelectTouchDebug's fixed zone-center formula and the default square sprite rect from MusicSelectSettings: x = -250..250, y = -20..480. This is a default-layout calculation, not a measurement of a configured/live view.
- Verified: a Python calculation over the A/B/C/D/E centers returned `A1, A8, B1, B8, C1, C2, E1, E2, E3, E7, E8`. Changes in layout, parent transforms or hardware calibration can change the result; no runtime hit test has been implemented.
- Next: use the actual on-screen rect and verified zone coordinates for each view and honor selectTapAreas, then test in game.

## Handoff
- Did: Reviewed the partial files and kept the install helper unexecuted. No game was started; no files under the forbidden paths were intentionally changed. `stage/ngo_pair_sprites/8-K-A.bin` and `_qa/diagnostic_8-K-A.png` are provisional diagnostic artifacts, not a valid install package.
- Verified: build and default headless run passed after the partial code changes; the required etcpak decoder still raises MemoryError on the real BC7 payload. The separate decoder diagnostic does not satisfy the task's mandated decode path or visual acceptance.
- Next: fix or replace the broken etcpak decoder within the permitted environment, finish and verify all 50 exports/QA and sizes, implement and integrate the complete Unity backend, triggers and page lifecycle, then run the prescribed checks and arrange reviewer in-game testing. No pack totals or VRAM estimates are yet available.

BLOCKED: required etcpak.decompress_bc7 fails on representative input; exporter QA and runtime integration remain incomplete.

## Reviewer note (2026-11-01)
- The decoder blocker is resolved: use `texture2ddecoder.decode_bc7(payload, w, h)` (installed 1.0.6, returns BGRA; verified on a real 664x648 drawing, mean abs error 0.26). The task file was updated accordingly.
- Continue from here: finish Part A (all 50 clips + QA files), then implement and integrate Part B completely. The BLOCKED line above is superseded.

## Decoder continuation
- Did: Continued the prior partial implementation and replaced the failing decoder with texture2ddecoder, converting its BGRA output to RGBA and restoring top-down rows for QA.
- Verified: `python -c "import numpy as n; ..."` confirmed the channel-indexing expression preserves H x W x 4 shape; source and task inspection confirms 50 clip folders and the required decoder.
- Next: run the exporter on all clips, inspect decoded QA, and integrate the page-aware runtime.

## Export continuation and QA correction
- Did: Verified both idle packs decode with the required BGRA decoder; visually inspected the Ame idle keying output. Corrected the preview compositor to combine weighted premultiplied drawings before placing a character over the background, and constrained the zoom search to the upper silhouette.
- Verified: `Get-Content stage\sprite_export.log -Tail 5` showed 5 successful clips; Idle mean absolute errors were 0.37 (Ame) and 0.70 (K). Visual inspection found no obvious green fringe at display scale, but the initial automatic zoom selected clothing rather than hair.
- Next: complete all pack encodes, regenerate QA with the corrected compositor and zoom, and integrate the runtime.

## Preview timing and verification
- Did: Corrected the preview to preserve the outgoing drawing, fade to the incoming first drawing, and combine premultiplied layers per character; cached decoded frames to reduce preview generation cost.
- Verified: `python -m py_compile tools/ngo_sprites_export.py` passed; the export log showed successful orientation checks through 14-Ame-R2. Visually inspected both idle QA images.
- Next: regenerate QA after pack encoding using the updated implementation and verify all output artifacts.

## Spec notes
- For preview transitions, hold the incoming first drawing during the 0.12-second fade, then start its timeline. This follows the literal wording and will require the runtime to use the same convention.

## Build and headless regression
- Did: Ran the required controlled build and default headless executable while export continued.
- Verified: `powershell -ExecutionPolicy Bypass -File build.ps1` succeeded with 0 warnings and 0 errors in both projects. `<TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll` passed all checks, including the sprite data tests and all eight existing models.
- Next: complete export and updated QA, then implement the missing Unity runtime and page integration. These headless tests do not verify the Unity backend.

## Regenerable QA
- Did: Added `--qa-only` to regenerate reviewer artifacts from the existing manifest and BC7 packs after correcting preview blending and zoom selection, without re-encoding source assets.
- Verified: `python -m py_compile tools/ngo_sprites_export.py` passed. `Get-ChildItem stage\ngo_pair_sprites -Filter *.bin | Measure-Object Length -Sum` showed 29 packs including the in-progress pack.
- Next: wait for all pack encodes and rerun QA using the corrected code, then check output sizes and visuals.

## Real-pack validation harness
- Did: Added an optional `spritepack <manifest.json>` headless check that parses the actual generated manifest and checks every pack length against its last drawing boundary.
- Verified: `powershell -ExecutionPolicy Bypass -File build.ps1` passed both projects with 0 warnings and 0 errors. A synthetic weighted crossfade check returned white for two half-weight opaque white layers, confirming no darkening.
- Next: run the new check against the generated 50-clip manifest, and rerun the corrected QA.

## Complete pack encode
- Did: Exported all 50 packs and generated the manifest; validated the real manifest and final offsets against each pack with the C# parser.
- Verified: `<TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll spritepack stage\ngo_pair_sprites\manifest.json` passed: 50 clips, 1498 drawings. Pack size is 607267872 bytes; estimated texture bytes are select 242431648, Fail 92982848, Clear 93840432, Good 87572128, Top 90440816. Every encode reported correct orientation.
- Next: finish regenerating corrected QA from packs; runtime backend and event integration still remain.

## QA regeneration and composition check
- Did: Started corrected `--qa-only` regeneration and inspected the regenerated idle Ame zoom plus the pair-rest triptych. The rest composition has both characters in the intended order.
- Verified: the reference foreground difference computed with `python -c` and a green-background mask has median 2, mean 12.72 and p99 135 RGB levels; concentrated edge/texture differences remain visible in the absolute-difference panel. The corrected zoom now shows hair edges.
- Next: verify the completed H.264 preview and all six image outputs; the Unity backend remains unimplemented.

## Final verification and outstanding work
- Did: Regenerated all six keying QA images, pair-rest comparison, and preview from the completed BC7 packs; inspected preview samples at 0, 4.5, 9.5, 14.5 and 19 seconds. Re-ran the default headless executable after adding real-pack validation.
- Verified: `python -u tools/ngo_sprites_export.py --qa-only` completed and printed the same size estimates. OpenCV reported H.264, 480 frames, 24 fps, 1024 x 1024, and successfully decoded the first and last frames. `<TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll` passed all tests and all eight models. `python -m py_compile tools/ngo_sprites_export.py` passed.
- Next: implement the actual Unity sprite backend, integrate PartnerView, page-aware asynchronous loading, tap/confirm/enter/result triggers, safe fallback, control API and ReleaseWait logic; then build, rerun checks and have a reviewer test in game. The existing area list is only an estimate. Do not run the install helper until this work is complete.

BLOCKED: This run ended without implementing the required Unity runtime integration; no external tool or environment blocker was established.

## Part B continuation: orientation
- Did: Read both task specifications and the prior worklog; resumed with Part A accepted and its assets/exporter protected.
- Verified: git status --short reports this directory is not a Git repository; the task identifies the existing sprite data helpers and mandates B1-B3 and the default build/headless checks.
- Next: trace PartnerView, page events, loading, rendering and game ReleaseWait before implementing the runtime.

## Spec notes
- Treat the prior incomplete-run BLOCKED line as superseded by this continuation; do not modify stage or the exporter.

## Runtime integration inspection
- Did: Traced PartnerView, select session/router, result creation, the game's OnGameStart and ReleaseWait implementation, and the existing touch-zone center formula.
- Verified: powershell -ExecutionPolicy Bypass -File build.ps1 passed baseline, zero warnings/errors. ResultProcess uses GamePlayManager.GetAchivement/GetComboType; SetStartData receives the clear flag. Select Decide currently hides the view immediately; ReleaseWait uses _timeCounter in milliseconds.
- Next: implement page-scoped worker loading and rendering, then wire events and the guarded ReleaseWait path.

## Page-scoped loader prototype
- Did: Added SpritePairLoader with a one-drawing worker/main-thread handoff, page group filtering, loop-first jobs, BC7 uploads with a frame budget, pack-length checks and texture destruction on dispose. It is not yet wired into PartnerView.
- Verified: manifest group inventory confirms select and result group names. The first build exposed a Monitor namespace collision; qualifying System.Threading.Monitor fixed it. powershell -ExecutionPolicy Bypass -File build.ps1 then passed with zero warnings and errors.
- Next: implement composition and playback, wire fallback/API and page events, then guard ReleaseWait.

## Loader prototype regression checks
- Did: Tightened loop validation to require both Ame and K. Added a headless Unity-double test for page group filtering, first-loop drawing priority, worker byte handoff and texture disposal. This loader is currently unused by the application.
- Verified: powershell -ExecutionPolicy Bypass -File build.ps1 succeeded with zero warnings/errors; <TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll passed, including the new loader test and all eight existing models. These doubles do not validate Unity BC7 uploads, GPU rendering or in-game timing.
- Next: complete B1 first: design and implement correct premultiplied-alpha composition/crossfades using available built-in shaders, integrate SpritePairLoader with PartnerView, config, safe async fallback, Info/PlayMotion/FramePng and page lifetime. Then implement B2 phase/touch/result hooks and B3 guarded dual-player ReleaseWait, with build and headless checks after each stage. Do not touch stage or exporter, run the game, install helper, or write <GAME_ROOT>.

CONTINUE: Implement B1 renderer/playback and PartnerView integration; the current SpritePairLoader is only a tested prototype and B1-B3 are not complete.

## Part B continuation: implementation orientation
- Did: Read the full task and the current worklog; traced the existing loader, PartnerView, select session/router, result hook and game ReleaseWait.
- Verified: the workspace is not a Git checkout; ReleaseWait advances using _timeCounter in milliseconds and select Decide currently hides views. The existing ASCII install helper is present and was not run.
- Next: integrate page-scoped playback and rendering, then wire page triggers and guarded transition, validating each milestone.

## Spec notes
- Compute touch zones from the actual select view rectangle using the same zone centers as MusicSelectTouchDebug; the prior default-layout estimate remains a geometry estimate pending in-game confirmation.

## Playback-state foundation
- Did: Added a page-local two-character playback controller and headless checks for pair startup, delayed Enter, return to loop and refusal of incompletely loaded pair actions. This controller is not yet connected to PartnerView or rendering.
- Verified: powershell -ExecutionPolicy Bypass -File build.ps1 passed (zero warnings/errors); <TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll passed all checks and eight models.
- Next: resolve first-loop-drawing readiness and delayed-start semantics, implement RGBA-correct built-in-shader compositing and connect the loader/controller to PartnerView; then B2/B3.

## B1 integration checkpoint
- Did: Connected asynchronous manifest/pack loading, page-local playback, render targets, configuration, Live2D fallback, and control API branches to PartnerView. Built-in specular shader compositing and the actual game graphics path remain subject to in-game verification; B1 is not yet fully validated.
- Verified: powershell -ExecutionPolicy Bypass -File build.ps1 passed with zero warnings/errors; <TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll passed all checks and eight models.
- Next: wire result tier, select touch/phase triggers and settings, then ReleaseWait; review shader, lifecycle, fallback and allocation behavior before final verification.

## B2 trigger checkpoint
- Did: Connected select touchDown and Menu phase changes to sprite actions, with settings.ini selectTapAreas override and dynamic rectangle hit testing; connected result tier choice to the game's clear flag, achievement and combo, and existing result touch hook to the reaction.
- Verified: powershell -ExecutionPolicy Bypass -File build.ps1 passed with zero warnings/errors; <TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll passed all checks and eight models. Game hooks and actual touch positions require in-game testing.
- Next: implement guarded Enter/ReleaseWait including two players, then review the complete path and resolve any defects found.

## Spec notes
- Default select geometry and MusicSelectTouchDebug centers yield A1, A8, B1, B8, C1, C2, E1, E2, E3, E7, E8; the runtime calculates against each active view and the override can replace that list.

## B3 transition checkpoint
- Did: Wired OnGameStart/OnUpdate ReleaseWait logic, participant tracking, 4-second cap, settings.ini enterTransition (default full), special-mode bypass and original-timing fallback. Stop page loading and close sprite views before gameplay.
- Verified: powershell -ExecutionPolicy Bypass -File build.ps1 passed with zero warnings/errors; <TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll passed all checks and eight models. Game transition and Harmony behavior remain unverified without the game.
- Next: complete one focused review of the integrated pipeline, remediate defects, add bounded regression checks and rerun final build/headless.

## Final integrated review and verification
- Did: Reviewed the B1-B3 execution path and fixed delayed-start playback, combined motion indexes, cancellation/join of both I/O threads before gameplay, Live2D result fallback isolation, initial RT clearing, and sprite state-preview handling. Added checks for transition decisions, settings defaults/overrides, delayed playback and steady-state allocations.
- Verified: powershell -ExecutionPolicy Bypass -File build.ps1 passed, zero warnings/errors; <TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll passed all checks and all eight models; the same executable with spritepack stage\ngo_pair_sprites\manifest.json passed for 50 clips and 1,498 drawings. The actual Enter clips last 3.5 s (Ame) and approximately 2.42 s (K), before the up-to-0.2 s offset.
- Next: reviewer should test actual BC7/shader output, alpha/crossfades, visual placement, touch calibration, result tiers, dual-player ReleaseWait and loading/GC behavior in game. No game or install helper was run.

## Spec notes
- Sprite compositing uses the built-in Standard (Specular setup) shader with emission and configurable One/One or One/OneMinusSrcAlpha blending; shader variant availability and exact output require the game renderer. The existing premultiplied RawImage path is retained.
- VRAM estimates refer to BC7 texture payloads; runtime Info additionally counts two square ARGB32 render targets. Default enterTransition is full.

DONE
