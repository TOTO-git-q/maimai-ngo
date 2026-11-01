# Select stock partner worklog

## 2026-10-20 - Orientation and commitment
- Read <WORK_ROOT>\select_poc\stock_task.md, docs/SELECT_VIEW.md, tasks/select_poc_worklog.md, current session/layout/settings/router/view/UI/tests and applicable workspace guidance. Plain source folder, no Git metadata.
- Read-only game evidence: Process/ResultProcess.cs maps EquipPartnerID through DataManager.GetPartner(id).naviChara.id; Monitor/ResultMonitor.cs and PartnerRoom.cs instantiate NavigationCharacter from the navi prefab. NavigationCharacter.cs exposes Default/FunStartLoop; PartnerRoom returns to Default after 1.3333334 seconds.
- Scope: explicit stock choice takes priority over custom matching; separate stock presenter behind UI, same layout rectangle/lifecycle, shared observing reaction hooks, tuning option, docs and bounded regressions. Native prefab preferred, exact partner full-body texture fallback; missing both triggers existing fail/restore guard.
- Preserve touched originals in tasks/select_stock_before for scoped diff and rollback. No changes to result/room replacement hooks or deployment settings.
- Build only with powershell -ExecutionPolicy Bypass -File build.ps1 (without -Install), then Headless. No game launch or game-directory writes; normal build reads existing game reference assemblies.

## Implementation checkpoint
- Added MusicSelectStockPartner: exact equipped-partner -> navi ID mapping, game's NavigationCharacter prefab instantiated under a fitting container, aspect-preserving shared Main-local rectangle, first-sibling ordering, inherited Canvas sorting, no raycasts.
- Native Default/FunStartLoop states are checked on the configured animator layer. Shared router hooks dispatch to native reactions with PartnerRoom's 1.3333334-second return-to-default timing; no stock gaze API, so gaze is a no-op. Unsupported states/texture fallback do nothing.
- Exact full-body result texture fallback uses AssetManager.LoadAsset to bypass both custom result-image patches and unrelated game dummy assets. Missing both exact presentations triggers existing guarded fail/restore.
- Sessions reuse each side's stock presentation, follow equipped-partner changes, suspend outside Music, and close on mode switch/disable/reset/fault/release. Stock telemetry is separate from Live2D view IDs.
- Tuning page always offers stock, including no custom configs. Added stock persistence/default-off and shared reaction dispatch regressions, plus a dedicated UI DOM/fetch mock. Updated SELECT_VIEW.md behavior/settings/fallback/reaction boundaries.
- Starting required build-only validation; no install/game launch.

## Bounded review and initial validation
- Required build.ps1 exit 0; both projects zero warnings/errors. Headless exit 0: 27 preset assertions, 20 router assertions, 13 main-thread checks/100 race iterations, rig/math checks and ALL 8 MODELS PASSED. Stationary actual-router fixture remains 0 managed bytes/1000 frames.
- UI DOM/fetch mock and embedded-script node --check passed: stock exists without custom configs, reserved stock option deduplicates, disabled stock POST and saved selection work, all 24 numeric fields/48 controls and tune/save/reset remain wired.
- Cohesive source review covered ID mapping, explicit stock precedence, absence of result/room hook changes, exact asset ownership/fallback, wrapper-only fitting, raycasts/sorting, state support/cooldown, same-frame switching and suspend/close/fault paths. Runtime native prefab/visual/fallback execution remains unverified.
- One bounded remediation: clear native-reaction support when falling back after a partially initialized navi, and retain the native failure reason when both asset presentations fail. Tightened the saved-enabled-stock/default-off assertion and documented manual native/texture checks plus stock-only rollback.
- Starting final required build, Headless and UI verification after this remediation, then recording final scoped diff and artifact hashes.

## Final verification and handoff
- Final required command: powershell -ExecutionPolicy Bypass -File build.ps1. Exit 0, mod and Headless builds both zero warnings/errors (tasks/select_stock_build.log). No -Install or alternate build command used.
- Final Headless: DOTNET_ROOT=<TOOLS>\dotnet; tests\Headless\bin\Release\net8.0\Headless.exe. Exit 0: 27 preset assertions, 20 router assertions, 13 main-thread lifetime assertions/100 race iterations, existing rig/math checks and ALL 8 MODELS PASSED (tasks/select_stock_headless.log).
- Final UI mock and node --check both exit 0 (tasks/select_stock_ui.log); no browser/game rendering claim.
- Final scoped source diff: tasks/select_stock_changes.diff. Originals: tasks/select_stock_before at matching relative paths. New production files: IMusicSelectPartnerReactions.cs and MusicSelectStockPartner.cs. Source/binary/log/worklog SHA-256 inventory: tasks/select_stock_manifest.json.
- Implemented selectPartner=stock and tuning choice, equipped-partner/native navi mapping, native animation presentation with exact full-body texture fallback, shared layout/reaction/session lifecycle and diagnostics. Default remains OFF. docs/SELECT_VIEW.md records configuration, semantics, verification, runtime acceptance checklist and stock-only rollback.
- No deployment settings, existing result/room hooks, game assets or decompiled source changed; no installation, game launch or writes to <GAME_ROOT>. The required build read its existing reference assemblies.
- Remaining boundary: native prefab fitting/animations, texture fallback execution, visual masks/order/circular clipping, physical input, 2P behavior, mod compatibility and frame-time/resource acceptance require in-game validation. Headless tests use real settings/router source with doubles and do not instantiate Unity prefabs.
DONE
