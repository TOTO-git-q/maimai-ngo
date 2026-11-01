# Music-select POC worklog

## 2026-10-20 - Orientation and commitment
- Read <WORK_ROOT>\select_poc\task.md and normative FEASIBILITY.md sections 1-7; inspected game source and existing Unity rendering, config, HTTP, deploy and build paths.
- No existing select worklog; repository is a plain folder without Git metadata. Preserved touched upstream originals in tasks/select_poc_before for scoped comparison/rollback.
- Scope: five outer content containers, normal Music only, per-monitor sessions, baseline-relative transforms, fixed hints, compact difficulty display, standalone select partner, observing reactions, validated HTTP tuning and dedicated select preset.
- Default OFF; no install, no game launch, no writes to <GAME_ROOT>. Build exclusively via build.ps1; run existing headless harness afterward.
- Remaining runtime boundaries: actual prefab layout, mod patch ordering, masks, readability, 2P and performance require manual game validation.

## Implementation checkpoint - layout, view, reactions, HTTP
- Added MusicSelectSettings validation/persistence and MusicSelectLayout with one baseline per outer container. A1/A8, UI_Buttons/UI_InfoButtons remain game-owned; tab hint sockets counter-transform without reparenting; compact synced difficulty graphic suppresses only central art; Allclear and ticket content explicitly positioned.
- Added no-NavigationCharacter musicSelect PartnerView mounting first in Main, fixed pixel rectangle, aspect-preserving size, raycast disabled, immediate suspend and guarded rendering failures.
- Added per-process/per-player sessions, Music-only eligibility, restore on phase/wait/release, guarded observations of metadata/input/preview/decide, 200 ms song debounce, bounded event history, small gaze/nod and TapBody reactions.
- Extended HTTP status/routes and page controls for all group transforms, ticket content, difficulty label and partner rectangle; request validation is atomic; settings.ini alone opts in at startup.
- Started first build via the required build.ps1 entry point; no install or game launch.

## Step 1 - Session and layout complete
- Captured entered-player baselines after OnStart; five supported outer groups only; Next/OnUpdate restore unsupported phases and waiting/special modes; release removes sessions.
- Kept parent/rotation/active state game-owned because this feature never modifies them. Added finite-baseline checks and zero-scale animation protection; same-frame re-enable waits for deferred temporary-graphic cleanup.

## Step 2 - Select partner complete
- Equipped custom partner first, explicit selectPartner fallback second. Main first-sibling mount, aspect-derived width, raycastTarget=false, bottom (0,-20), height 500.
- Disabled/reset/fault paths close select views; phase changes suspend Tick/Render and reuse the same model when returning. Partial view-init failures destroy the new object.

## Step 3 - Reaction routing complete
- Song/difficulty/category snapshots, frame-deduplicated raw input (including NEXT transition), scroll-method direction observations, preview request and cue-correlated engine-start evidence, final ReleaseWait decision event.
- 200 ms settled song reaction; TapBody capped at 650 ms; 320 ms small nod; ring buffer 64 events per side. Placeholder metadata is explicit and does not flood repeated initial events.

## Step 4 - HTTP tuning complete
- GET /api/music-select, POST layout/reset/save; /api/status includes musicSelect. Existing localhost/header/Origin protections retained.
- Every required group scale/offset, partner rect, ticket-content offset and difficulty-label rect has slider/number input. Latest-value queue; atomically validated presets; reset/disable immediate restore. Debug labels show fixed touch zones.
- Save uses select_layout.ini plus only selectView/selectPartner in settings.ini. Default OFF still requires settings.ini opt-in. Added 23 headless regression assertions covering validation and persistence.

## Step 5 - Build and tests checkpoint
- First build passed. A later build found the debug labels needed UnityEngine.TextRenderingModule; added reference to the existing game assembly (no new package).
- Subsequent required-entry-point build passed: mod and headless, zero warnings/errors. Headless execution with DOTNET_ROOT=<TOOLS>\dotnet passed 23 new checks, existing rig/math checks and ALL 8 MODELS PASSED.
- JavaScript syntax check passed with node --check. Saved headless output to tasks/select_poc_headless.log.

## Step 6 - Documentation and cohesive review
- Added docs/SELECT_VIEW.md with implementation, settings/schema/ranges, endpoints/events/evidence boundaries, step-by-step in-game acceptance and known gaps; source rollback copies preserved.
- Reviewed the 8 modified originals against tasks/select_poc_before plus all five new Unity files. Saved scoped original-file diff to tasks/select_poc_changes.diff.
- Consolidated remediation: avoid nonfinite hint transforms during animation, defer same-frame reapply after cleanup, avoid repeated placeholder events, and preserve metadata/input/decision semantics. Final build/test and artifact hashing follow.

## Final verification and handoff
- Final build: powershell -ExecutionPolicy Bypass -File build.ps1; mod + harness success, zero warnings/errors (tasks/select_poc_build.log).
- Final headless run: DOTNET_ROOT=<TOOLS>\dotnet; exit 0. 23 select-preset assertions passed; existing rig/camera/math checks and ALL 8 MODELS PASSED (tasks/select_poc_headless.log).
- JavaScript syntax passed. Minimal DOM/fetch mock passed 24 numeric fields / 48 paired controls and tune/enable/save/reset routing. Initial mock-only missing-style/GET-body stubs were corrected; no browser/game was launched (tasks/select_poc_ui_check.cjs).
- Consolidated final remediation also keeps added label/debug graphics below fixed buttons/warnings and retains slider request coalescing during in-flight requests.
- Final source/output SHA-256 and byte sizes recorded in tasks/select_poc_manifest.json; final scoped diff regenerated in tasks/select_poc_changes.diff. Built mod is bin/PartnerLive2D.dll.
- docs/SELECT_VIEW.md documents settings, endpoints, event semantics, manual in-game checks and remaining visual/runtime limitations. No deploy assets changed; no -Install, game launch, or game-directory mutation. The required build reads existing game reference assemblies.
- Remaining human validation: real prefab/animation behavior, masks/circular clipping, readability, physical touch, 2P, other-mod Harmony order, network/timeout paths and frame-time/resource counts. No runtime acceptance claim.
DONE
- Artifact verification follow-up: corrected a verification-script-only default GBK read to explicit UTF-8; all 22 recorded file sizes/SHA-256 values, endpoint/mount markers and worklog terminal line verified. No source or binary changes.
DONE

## QA orientation - 2026-10-20
- Read qa.md, worker task, normative feasibility study, saved original-file diff, and select session/layout/router/settings plus PartnerView integration.
- Confirmed default-off startup, baseline-relative group transforms and observing input. Found unconditional per-frame metadata/key/enum/delegate allocations in the new update path; investigating minimal remediation and fault containment before build.
- QA scope: select feature only; preserve result-screen routing. Required build will read existing reference assemblies but will not install, write to <GAME_ROOT>, or launch the game.

## QA initial validation and confirmed defects
- Required build.ps1 (without -Install) passed, mod and harness zero warnings/errors; initial Headless exit 0, ALL 8 MODELS PASSED. Logs: select_poc_qa_build_before.log and select_poc_qa_headless_before.log.
- Confirmed unconditional allocations: new update patch closures, reflected enum reads/ToString, per-frame metadata dictionaries/string identity keys, Enum.GetValues in input observation, interface fallback iteration, and select partner's existing touch-direction enum names.
- Preserved the three QA-edited sources in tasks/select_poc_qa_before. Remediation will preserve result-screen touch behavior, cache select input/phase access, and allocate metadata/events only on changes.

## QA remediation checkpoint
- Removed recurring select update/scroll guard closures; cached Harmony field refs and phase names, placeholder identities, input enums/names, and fallback iteration. Metadata snapshots now change only with selection/category/difficulty changes; old event snapshots remain immutable.
- The select avatar reuses cached touch names while result/room behavior stays on its original path. No layout targets or input interception were added.
- Added actual-router headless regression checks with small game/input doubles: initial pass 13 assertions, including 0 managed bytes for 1000 stationary frames, 200 ms debounce, snapshot preservation, input deduplication/observation, ReMaster fallback and bounded events. Existing 23 preset assertions and all eight model checks still pass.
- Build surfaced only unassigned-double-field warnings; extended fixture coverage for extra/level/category/score type instead of suppressing them. Final build and verification follow.

## QA final verification and handoff
- FIXED: recurring select-frame allocations; production changes limited to MusicSelectSession.cs, MusicSelectReactionRouter.cs and the select-only cached-name branch in PartnerView.cs. Reviewed final scoped diff in select_poc_qa_changes.diff.
- Final required command: powershell -ExecutionPolicy Bypass -File build.ps1. Exit 0; both projects zero warnings/errors (select_poc_qa_build.log).
- Final required harness: DOTNET_ROOT=<TOOLS>\dotnet; tests\Headless\bin\Release\net8.0\Headless.exe. Exit 0; 23 preset assertions, 17 router QA assertions, existing rig/math checks and ALL 8 MODELS PASSED (select_poc_qa_headless.log). Actual router source with game/input doubles allocates 0 managed bytes across 1000 stationary frames.
- Source review confirms default-off, captured-baseline transforms, idempotent restore, five outer containers plus explicitly study-required hint/title/content handling, observing input, guarded new select patches, and cached steady-state object access. Existing result patches unchanged; QA shared-view edit is select-only.
- Wrote tasks/select_poc_qa.md with PASS/FIXED findings, exact commands/results, remediation and runtime evidence boundaries. Final 10-entry source/binary/log manifest: select_poc_qa_manifest.json. Comparison against the worker manifest found only the five intended existing-source/test-project changes and rebuilt mod binary; the new router test is separately recorded.
- No -Install, game launch, deployment or writes to <GAME_ROOT>. Required build read existing reference assemblies. Unity baseline/restore execution, Harmony runtime/fault injection, visuals, physical input, 2P/network/timeout and full performance remain untested as documented.
DONE

## Startup exception fix - orientation
- Read exc_task.md and exception.txt. Stack identifies MainThread.Call<T> finally -> EventWaitHandle.Set after waiter timeout/disposal, not an HTTP/file handle. CallAsync has the same lifetime hazard.
- Scope: guard completion signaling in both helpers, preserve timeouts/queued work, add actual-source Headless regressions. Preserve originals in tasks/exception_fix_before. Build only via build.ps1, no install or game launch.

## Startup exception fix - implementation
- Moved unchanged dispatcher API into MainThread.cs so Headless compiles the actual production implementation. Added a completion wrapper that serializes Set with disposal and ignores late signals; both Call and CallAsync retain default timeout messages and queued execution.
- Added deterministic timeout/late-callback and success/error propagation checks plus 100 bounded completion/timeout race iterations. Starting required build and Headless validation.

## Startup exception fix - final verification
- FIXED: Call/CallAsync completion can no longer Set a disposed wait event; common lock serializes signal and disposal. Timeouts and late queued operation behavior preserved. ControlServer comparison confirms dispatcher extraction only; no HTTP/select/settings changes.
- Required build.ps1 exit 0: mod and harness zero warnings/errors (tasks/exception_fix_build.log). Headless exit 0: 13 lifetime assertions, 100 race iterations, existing 23 preset/17 router checks, rig/math and ALL 8 MODELS PASSED (tasks/exception_fix_headless.log).
- Cause/fix/evidence and runtime boundaries: tasks/exception_fix.md. Original copies: tasks/exception_fix_before; reviewed source/test diff: tasks/exception_fix_changes.diff.
- No install, game launch or game-directory writes. Startup delay/triggering endpoint and Mono runtime remain unverified.
DONE
