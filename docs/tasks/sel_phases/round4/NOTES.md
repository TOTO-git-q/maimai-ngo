# Round 4 candidate — incomplete, do not treat as accepted

This candidate builds the existing multi-phase select layout and makes the following additional changes:

- Difficulty: `UI_CMN_TitleBase` is pinned to the captured stock Main-space position and scale in Difficulty/UtageDifficulty. This has not yet been rendered in the game.
- Option: `UI_ChainLists/NormalChainList` uses a 0.80 baseline-relative scale and the existing -45 X offset. This is provisional; it is not established as the largest scale that fits.
- Frame-path allocation reductions: reuse the hidden-node set and Option transform array, test sprite-layer path segments without Split, and traverse children without allocating the Transform array. Input-layer protection and per-frame layout enforcement remain. The path helper has a headless allocation regression; there is no Unity-frame allocation or performance measurement for this candidate.

The previous round3 screenshots are under `../round3_test/`, with `../round3_compare.png`; they show the prior candidate and cannot validate the new geometry. The A/B hitch logs show 118–142 ms stalls with select view enabled. The frame logger reports GC markers when observed but these spikes have no markers; this does not identify a cause or exclude effects measured outside the same sampled frame. The root cause and any improvement remain unverified.

The requested equipped-partner unification has not been implemented: the existing settings explicitly select `hiyori`, result selection still resolves by navi ID, and the game preset is read-only. Changing the default for an explicitly configured value while also preserving explicit values needs a compatible migration decision. Sort/Character/preparation, circular fit, transitions, exclusions, return timing and two-player behavior still need acceptance. The test environment did not start a chart in previous runs. No Result-to-select frames are available. No final DLL has been staged.

Build: `powershell -ExecutionPolicy Bypass -File "<PROJECT>\build.ps1"`, no Install, zero warnings/errors. Headless: 12 phase-policy, 78 preset, 20 router, 201 input/sprite, 161 fix3 checks and all eight models passed. See build.log and headless.log. Candidate DLL SHA-256: 08A36AA76A08231A5F34CC72E266B1B2DBE8F3235ECE06EC9510753BF876B9CA. Disable the select layout via `selectView = false` in settings.ini; no preset or game files were edited in this continuation.
