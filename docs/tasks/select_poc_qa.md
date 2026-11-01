# Music-select POC QA

Result: **FIXED** — confirmed recurring-allocation defects corrected; final build and Headless checks pass. No known failing item remains within this build/source-review scope. This is not in-game acceptance.

Date: 2026-10-20. Instructions: `<WORK_ROOT>\select_poc\qa.md`, worker `task.md`, and normative `<WORK_ROOT>\select_ui\FEASIBILITY.md`.

## Findings and disposition

| Check | Status | Evidence / boundary |
|---|---|---|
| Feature OFF by default | PASS | `MusicSelectSettings.Enabled` starts false; Load clears preset-enabled before reading explicit `settings.ini` opt-in. Existing Headless persistence checks confirm a saved enabled preset cannot opt in by itself. Disabled session updates restore rather than apply. |
| Baseline / restore idempotence | PASS, source review | `MusicSelectLayout.Baseline` captures once after process start. Apply uses captured position + offset and captured scale × factor, never the previously modified transform. Restore writes captured values, is gated by Applied, and defers same-frame reapply until temporary graphic destruction completes. Returning to Music reuses the same baseline. No Unity re-entry execution was performed. |
| Layout scope / fixed hints | PASS, source review | Exactly five outer group transforms: UI_ChainLists, Null_GenreControll, Null_SelectorBackgroundTarget, Null_UI_MSS_MusicPlayStatus, Null_UI_MSS_MyStatus. The study also explicitly requires tab-hint counter-transforms, Allclear relocation, ticket-content offset and compact difficulty art; these existing implementation details were reviewed, not expanded. Central Title/Base/Base_Shadow alpha is restored; difficulty input sockets, UI_Buttons/UI_InfoButtons, Sub, warnings and input logic remain game-owned. No card-by-card transforms or extra root-container movement. |
| Input observation | PASS | New patches do not return false, alter input results or write input state. Raw input reads are deduplicated by frame/player; select graphics have raycastTarget=false. Router tests prove one button/touch event and nod despite repeated reads in the same frame, with the input double still asserted. Physical input remains untested. |
| Select Harmony failure guards | PASS, source review | All nine new select patch classes route work through Guard or explicit try/catch → Fail. Fail disables the feature and attempts restore/disposal per side. Select avatar Update/LateUpdate use the same failure callback. Field-ref initialization now runs inside the initialization guard. Existing result/room patches were left unchanged. No runtime Harmony fault injection was performed. |
| Recurring per-frame allocations | FIXED | Previously: update/scroll closures, reflected enum boxing/ToString, fresh song dictionaries/identity strings each Tick, Enum.GetValues/input names every input poll, interface fallback iteration, and select-avatar touch-name conversion. Now: cached field refs/phase names/input arrays/names; numeric identity comparisons; metadata rebuilt only on changes; direct guarded hot patches; indexed fallback lookup; cached select touch names. Real router source with game/input doubles measures **0 managed bytes across 1,000 stationary frames**. Event publication, selection transitions, model creation and HTTP serialization still allocate when needed. This does not measure the Unity engine, real game accessors, renderer or native model runtime. |
| No per-frame scene scans | PASS, source review | Hierarchy/component discovery is at session/layout creation or first application; steady-state select update uses cached objects. Debug hierarchy is created only on opt-in/entry. No global scene scan was introduced. |
| Existing result partner | PASS, source review | `Patches.cs` and result routing remain untouched. The sole QA edit to shared PartnerView uses cached names only when Context == musicSelect; result/room retain their prior ToString path. Existing model/rig regressions pass; result-screen Unity behavior was not run. |
| Build / regression harness | PASS | Required entry-point rebuild: zero warnings/errors for both projects; Headless exit 0, 23 preset checks + 17 router QA checks + existing rig/math and ALL 8 MODELS PASSED. |

## Minimal remediation

Production edits: `src/Unity/MusicSelectSession.cs`, `MusicSelectReactionRouter.cs`, and one select-only expression in `PartnerView.cs`. No layout preset, UI, deployment asset, result patch, or renderer change was needed.

Regression coverage: added `tests/Headless/SelectRouterChecks.cs` with minimal game/input doubles and linked the actual router source from `Headless.csproj`; called it from SelectChecks. Checks cover immutable earlier snapshots, zero stationary allocations, 200 ms debounce, no repeat reaction, input observation/deduplication, ReMaster fallback, extra/level chart difficulty, score type/category/sort identity, placeholder metadata, suspend cancellation and the 64-event bound. These doubles compile only into Headless, not the mod.

## Commands and results

Run from `<PROJECT>`:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
$env:DOTNET_ROOT = '<TOOLS>\dotnet'
& tests\Headless\bin\Release\net8.0\Headless.exe
```

- Initial build: exit 0; mod and Headless each zero warnings/errors. `tasks/select_poc_qa_build_before.log`.
- Initial Headless: exit 0; existing checks and ALL 8 MODELS PASSED. `tasks/select_poc_qa_headless_before.log`.
- Final build after remediation/tests: exit 0; mod and Headless each zero warnings/errors. `tasks/select_poc_qa_build.log`.
- Final Headless: exit 0; Select preset checks passed: 23; Select router QA checks passed: 17; stationary 1000-frame managed allocations=0 bytes; ALL 8 MODELS PASSED. `tasks/select_poc_qa_headless.log`.
- Reviewed production QA diff: `tasks/select_poc_qa_changes.diff`. Original three production sources: `tasks/select_poc_qa_before/`. Final source/binary/log sizes and hashes: `tasks/select_poc_qa_manifest.json`. The worker manifest remains a historical pre-QA record.

## Remaining verification boundary

Source review supports baseline idempotence, scope and guarded restoration; the harness executes settings/router/model behavior without Unity. Actual prefab/Animator timing, repeated on-screen re-entry, Harmony application/ordering and failure recovery, circular clipping/Canvas masks, physical touch, 2P/network/timeout paths and full frame-time/resource measurements still require the manual checks in `docs/SELECT_VIEW.md`. No runtime visual/performance or fault-injection acceptance is claimed.

Build-only operation throughout: no -Install, no game launch, no copy/deploy or writes into `<GAME_ROOT>`. The required existing build reads game reference assemblies there. Progress was appended to `tasks/select_poc_worklog.md`.

DONE
