# Music-select layout and partner POC

Implemented against `<WORK_ROOT>\select_ui\FEASIBILITY.md` §6.1 steps 1–5, with §6.2 initial values. This is a **build-only POC**. No game launch, installation, physical input test, screenshot acceptance, runtime compatibility or performance measurement was performed during implementation.

## Built behavior

- `MusicSelectSession` captures each entered monitor after `MusicSelectProcess.OnStart`, updates after `OnUpdate` and `Next`, and disposes before `OnRelease`. Sessions are keyed by process and contain separate monitor/player layouts, views, metadata and event histories.
- The layout can apply during Init, Fade, FirstStaging and Update for eligible active players, including Genre, Music, Difficulty, Menu, Option, SortSetting, Character and normal preparation/wait. It restores on release, inactive sides and excluded modes/folders. Course, KaleidxScope, MagicalPass, active Festa appearance, special ROM/tournament, Utage, challenge and random/wait selections deliberately use stock layout.
- Five non-Animator outer containers receive baseline-relative uniform XY scale and position offsets. No individual song cards are reparented or repositioned. `Main`, `Sub`, `UI_Buttons`, `UI_InfoButtons`, warning/task layers and input APIs retain their game behavior.
- Tab-button **sockets** counter-transform the modified tab ancestor to retain their captured Main-space positions and size; their original button Animator children stay attached. Allclear rank/medal graphics are explicitly reduced and placed at Main y=-30, with x=45% of captured Main x. They are not silently hidden. `MyStatusBase` receives a further content offset if present.
- A new 130×36 difficulty Image at (300,-30) copies the game's current sprite/color/visibility. Only the central Title/Base/Base_Shadow art gets temporary zero-alpha CanvasGroups. The original controller, freedom information and A1/A8 sockets remain intact. Temporary graphics/groups are removed on restore.
- The select partner mounts as the **first sibling under `MusicSelectProcess/Canvas/Main`**, above the shared landscape and behind select UI. Custom models use `PartnerView` with context `musicSelect` and a non-raycasting RawImage. `selectPartner = stock` uses a separate `MusicSelectStockPartner` with the game's own `NavigationCharacter` prefab (full-body RawImage fallback). Neither presentation hides an existing game character. Default bottom center is (0,-20), height 500; width preserves presentation aspect.
- `selectPartner = equipped` follows each player's current `Detail.EquipPartnerID`: a matching `partnerId` uses its custom model, and otherwise the game character is used. The result hook uses the same partner-ID match. Explicit `stock` always uses the game character in selection; a custom folder value retains the legacy custom fallback behavior. A missing valid presentation or any layout/model/reaction exception disables this feature for the run, logs the failure, restores both sides and closes its views. Fix the cause and restart before enabling after a fault.
- One presentation per active side is reused across songs (custom: avatar/RT; stock: native prefab or texture, no Cubism avatar/RT). Changing equipped stock partner or switching presentation closes the old view. The partner remains visible across eligible phases; reactions run only in Music. Reset/disable/fault/release close it. Destruction of Unity resources completes at end of frame. Game textures/prefab assets remain owned by AssetManager.

The baseline records parent, sibling, active flag, rotation, position and scale. Restoration writes only the position/scale and temporary alpha changes made by this feature; it does not overwrite phase-owned active state, reparent objects or rewind game animation. Repeated restore is a no-op. The five original transforms are never recaptured from transformed values.

## Settings and persistence

Nothing changes visually until explicitly enabled. Startup requires this exact key in `LocalAssets\PartnerLive2D\settings.ini`:

```ini
# Existing http / port settings can stay here.
selectView = true
selectPartner = equipped
```

Without `selectView = true`, startup is OFF, even when a saved layout contains `enabled: true`. Set `selectPartner = equipped` (the default) to use a custom model whose `partnerId` matches the equipped partner, or the stock character if no match exists. The result screen also selects by equipped `partnerId`. Set `selectPartner = stock` to force the stock select character, or use a loaded custom partner folder name as the legacy select fallback. An explicit legacy value such as `hiyori` remains unchanged; change the settings.ini line to `equipped` to activate unified selection. The reserved value `stock` always selects the game partner, even if a custom folder has that name. Existing `partner.ini` layout keys retain their result/room meaning; both select presentations use the same independent pixel rectangle.

For the stock presentation, opt in explicitly with:

```ini
selectView = true
selectPartner = stock
```

Stock identity follows the result-screen chain: `UserData.Detail.EquipPartnerID` → `DataManager.GetPartner(id).naviChara.id` → `NaviChara/Prefab/UI_Navichara_{naviId:D2}.prefab`. The component is instantiated under a dedicated fitting container, as in `ResultMonitor.SetStartData` / `PartnerRoom.CreatePartner`. The initial default-state UI bounds determine uniform height scaling and bottom-center placement; child animation transforms remain game-owned. Graphics reject raycasts and nested Canvases inherit sorting. Exact asset paths use `AssetManager.LoadAsset` to avoid the game's silent partner-1 dummy fallbacks and this mod's custom result-image replacement hook.

If native prefab creation or fitting fails, the presenter logs the reason and tries the equipped partner's `Partner/UI_PartnerResult_{partnerId:000000}.png`, the full-body path used by `GetPartnerResultTexture2D`. `GetPartnerTexture2D` is an icon and is not used here. If both exact assets are unavailable, the existing feature fault/restore path runs. `/api/music-select` reports each side's `stockPartner` identity, `presentation` (`navigationCharacter` / `fullBodyTexture`), reaction support and fallback reason. Stock views have no Live2D `viewId` and do not expose the Live2D motion/expression/frame-preview endpoints.

The HTTP Save action writes `LocalAssets\PartnerLive2D\select_layout.ini` and updates only `selectView`, `selectPartner` and `categoryKeys` in `settings.ini`, preserving other settings/comments. The dedicated INI has a `layout = {JSON object}` line using the same schema and validation as HTTP. Do not add `enabled` to `partner.ini`. Reset is in-memory until explicitly saved.

All positions below use **Main-local pixels**, with (0,0) at the round screen center and +Y upward. Offsets are added to captured local positions; scales multiply captured XY scale once, preserving Z.

| JSON field | Default | Accepted values |
|---|---|---|
| `enabled` | false | boolean |
| `selectPartner` | `equipped` | reserved `equipped` or `stock`, or custom fallback folder name; max 100, no control characters |
| `phases` | `["Music"]` | legacy compatibility marker; only `["Music"]` is accepted, but it does not restrict layout eligibility to Music |
| `groups.chains` → `UI_ChainLists` | scale .55, x 0, y -200 | scale .35–1.2; offsets -540…540 |
| `groups.tabs` → `Null_GenreControll` | .45, 0, -140 | same |
| `groups.selector` → `Null_SelectorBackgroundTarget` | .55, 0, -220 | same |
| `groups.status` → `Null_UI_MSS_MusicPlayStatus` | .55, 0, -160 | same |
| `groups.myStatus` → `Null_UI_MSS_MyStatus` | .55, 0, -220 | same |
| Each group's members | `scale`, `offsetX`, `offsetY`, `hidden` | numeric transform values; boolean hidden; partial updates allowed |
| `myStatusContent.offsetX/offsetY` | -200, -40 | -540…540; extra Main-space displacement after parent transform |
| `difficultyLabel.x/y` | 300, -30 | -500…500 |
| `difficultyLabel.width/height` | 130, 36 | width 80…400, height 24…120 |
| `partner.bottomX/bottomY/height` | 0, -20, 500 | x -400…400, y -500…100, height 100…900 |
| `debugTouchZones` | false | boolean; shows fixed A/B/C/D/E zone labels, yellow while held |
| `save` | false | boolean; optional in layout request |

All supplied fields are validated before committing a new preset. Unknown keys/groups, unsupported phases, nonnumeric values, NaN/Infinity, undersized scale/text rectangles and excessive offsets are rejected with HTTP 400. Range validation is not a proof that every allowed combination fits the circular screen or preserves readability.

## HTTP and tuning page

The existing server remains localhost-only, normally `http://127.0.0.1:8791/`. The Music-select POC panel has sliders and number inputs for every group scale/offset, ticket-content offsets, difficulty rectangle and partner rectangle, plus enable, partner choice, debug, reset and save. The partner selector always includes **Stock (player's equipped game partner)**, including when no custom configs are loaded. Other choices retain the equipped-custom/fallback behavior. Changes merge at approximately 12.5 Hz with one request in flight; the process applies the latest preset at most once per side/frame. Unity operations stay on the existing `MainThread` queue/process hooks.

Every POST needs `X-PartnerLive2D: 1`; the existing Origin restriction remains. JSON requests also use `Content-Type: application/json`.

| Endpoint | Behavior |
|---|---|
| `GET /api/music-select` | Preset, failure reason and active process sessions; each side includes phase/reason, layout-applied state, full parent path, Canvas sorting, baseline/current positions/scales, view ID, song and recent events |
| `POST /api/music-select/layout` | Atomically validate and commit a partial preset; applied on next process frame; disabling restores immediately |
| `POST /api/music-select/reset` | Restore stock transforms, close select avatars/debug views, reset initial values and disable; also works outside selection |
| `POST /api/music-select/save` | Persist preset and startup switch/fallback; works outside selection |
| `GET /api/status` | Includes the same snapshot under `musicSelect` |
| Existing `/api/views/{id}/…` | State, motion, expression, gaze and frame preview also work on the select avatar; IDs are temporary |

Example partial request:

```json
{
  "enabled": true,
  "selectPartner": "hiyori",
  "phases": ["Music"],
  "groups": {
    "chains": {"scale": 0.55, "offsetX": 0, "offsetY": -200},
    "tabs": {"scale": 0.45, "offsetX": 0, "offsetY": -140}
  },
  "partner": {"bottomX": 0, "bottomY": -20, "height": 500},
  "save": false
}
```

Events are per-side, monotonic-ID, bounded to the last 64. Snapshot fields include music ID, title, music genre, effective difficulty/name, display level (including `+` via the game's level-name API), nominal BPM, current list category, index and score type. Extra/level folders and unavailable ReMaster follow card difficulty rules. Genre and current list category are distinct. Random/wait metadata has `kind=random/placeholder`, with null difficulty/level/BPM.

Events: `initial`, `phase`, `songObserved`, `songChanged` (200 ms stability debounce), `difficultyChanged`, `categoryChanged`, `buttonDown`, `touchDown`, `previewRequested`, `previewStarted`, `previewEnded`, `selectionConfirmed`, `songDecided`. Input is sampled once per frame/player/zone, including the last Music frame when NEXT changes phase. Scroll direction observes the original monitor scroll path, including list boundaries. Gaze is small (0.3); button/touch causes a 320 ms nod; stable song change starts `TapBody` if present, capped at 650 ms before returning to idle. No group is invented for models without it.

`previewRequested` means the game's request was observed. `previewStarted` additionally requires the request's cue to match the current song and a stopped→playing engine-state observation for the music player. It may conservatively be absent if the stopped interval is not observed. It does not prove audible sound. `selectionConfirmed` is NEXT to difficulty, and can be reversed; `songDecided` is emitted once only after actual entry into ReleaseWait, with the final `GameManager.SelectMusicID/SelectDifficultyID` values.

Both presentations receive the same observing reaction hooks. Stock has no gaze API, so scroll gaze is a no-op. Stable song changes and button/touch nod hooks play the navi's own `FunStartLoop`, then return to `Default` after 1.3333334 seconds, matching `PartnerRoom` timing. These reactions require both states on every participating animator at the navi's configured animation layer; unsupported states and the texture fallback do nothing. Reactions do not overlap, and suspension cancels the reaction and restores idle. No synthetic gaze/nod transforms, custom model motions or audio are applied to stock.

## Manual in-game acceptance checklist (not performed here)

1. After a separately authorized installation, start with `selectView` absent/false. Enter a normal 1P **Music song-card screen**, not Genre or the later START/Menu screen. Confirm stock layout and no select partner. Record baseline screenshot and `GET /api/music-select`.
2. Open the local page, pick a valid fallback partner and enable. Confirm `mainSequence=Update`, `phase=Music`, `layout.applied=true`, correct monitor path and Canvas sorting. Check the partner is behind song UI, bottom near y=-20, top near y=480, with correct aspect and no black-gap/other-monitor leakage.
3. Check the five container coordinates against baseline+offset / baseline×scale. Confirm fixed outer arrows, BACK/NEXT, A1/A8, E2/E8 hints, info buttons, freedom information, warnings and Sub remain semantically correct. Check the compact difficulty sprite, Allclear rank/medal and ticket/distance content for overlap. Enable debug briefly to identify physical touch zones, then disable it.
4. Scroll once each way, long-hold both directions, use center swipe, and cross category boundaries. Include separator cards, longest titles, highest levels, ReMaster and standard/DX switches. Verify title/genre/difficulty/level/BPM against the actual center card, correct gaze direction and only one debounced song reaction per settled selection. Confirm button/touch events do not consume gameplay input.
5. Change difficulty and classification; compare the HTTP category and music genre separately. Wait for preview; inspect request and engine-start evidence. Confirm motion stays small and no model/RT reload occurs per song. Try a model without TapBody: song/input handling must continue.
6. NEXT→Difficulty→Menu→Option and BACK through each screen; test Genre, SortSetting and Character as available. Confirm shared transforms persist without jumping, phase-specific panels fit, and the partner stays visible; reactions suspend outside Music. Return to Music and compare the exact preset again to detect cumulative scaling. Confirm final START produces one `songDecided` and restores before TrackStart.
7. Toggle off, reset twice, leave and re-enter selection repeatedly. Confirm stock coordinates/alpha, working controls, no lingering labels/RawImages/materials/RTs after release, and no retained session. Test 2P with different difficulties/phases; only the eligible side may change, and each side has its own view/events. Include preparation, network waiting/cancel, timeout and Freedom expiry when available.
8. Tune, Save, restart and verify persistence; remove `selectView` and verify OFF again even with a saved enabled preset. Submit an invalid mixed update and verify HTTP 400 with all previous values intact. Reset and Save to persist stock layout.
9. Exercise special modes/folders (Course, Utage, challenge, Festa, random/wait, special ROM) and confirm fallback. Inspect Harmony owners/order with any other UI mods before making compatibility claims. Measure frame time and model/RT counts for 1P/2P scrolling and repeated entry/release.
10. Choose Stock on the tuning page, including with no custom configs and with an equipped partner that also matches a custom config. Confirm the actual equipped game character wins, correct `partnerId`/`naviCharaId`, `presentation` and reaction support in status. Test two players with different partners, change the equipped partner, switch stock/custom repeatedly, tune/save/restart, suspend mid-reaction, disable/reset/release. Check native idle and one FunStartLoop followed by Default when supported, no gaze manipulation, correct bottom/height/aspect and no duplicate images. With a missing/unusable native prefab, check the exact full-body texture fallback, `reactions=false` and fallback reason; missing both must fault/restore. Do not alter game assets merely to run a fallback test.

## Validation and remaining gaps

Build command: `powershell -ExecutionPolicy Bypass -File build.ps1` only. Run harness with `DOTNET_ROOT=<TOOLS>\dotnet`: `tests\Headless\bin\Release\net8.0\Headless.exe`. New headless preset checks cover default-off, immutable/atomic invalid updates, numeric bounds/nonfinite values, phase restriction, injection rejection, save/load and preservation of unrelated settings. Existing model, motion, math and rig checks remain in that harness. These do **not** execute Unity, Harmony or the physical input system.

Original POC verification passed: mod and harness builds reported zero warnings/errors; 23 preset assertions, existing rig/math checks and all eight model regressions passed. Logs, scoped diff, preserved originals and SHA-256 manifest are under `tasks/select_poc_*`.

Stock-option verification: the required build passed with zero warnings/errors for mod and harness. Headless passed 27 preset assertions (including stock choice, persistence, common rectangle and startup opt-in), 20 router assertions (including dispatch through the shared stock reaction interface), existing main-thread/rig/math checks and all eight model regressions. The actual router still allocates zero managed bytes across 1,000 stationary fixture frames. `node --check` passed; a DOM/fetch mock checked stock availability without custom configs, option deduplication, default-off stock POST, saved stock selection and all 24 numeric fields / 48 paired controls plus tune/save/reset wiring. This mock does not render a browser. These checks do not instantiate the native Unity prefab or prove animation/layout/fallback behavior in the game. Stock logs, scoped diff, originals and manifest are under `tasks/select_stock_*`; worklog: `tasks/select_stock_worklog.md`.

Custom Live2D uses the existing Legacy premultiplied RawImage shader; stock uses its prefab materials or the default RawImage material for a texture. Full UI stencil/RectMask2D compatibility remains unverified. First-sibling ordering is implemented, but actual Canvas overrides, masks, animations and other mods can affect output. Native graphic bounds (including transparent margins/emotion graphics), root animation and subsequent layout changes require runtime visual acceptance; fitting captures the initial footprint once and scales its wrapper. Tab-hint sockets use captured initial geometry; their animated appearance must be checked at runtime. Allclear positions and ticket offset are first-round POC arrangements; long titles, sprite visibility, chart readability, circular clipping and transparent model margins need real screenshots. Optional `MyStatusBase` is adjusted only when created in that monitor. Active Festa appearance is conservatively excluded globally. The current cross-phase placement, difficulty header and option carousel require in-game visual acceptance; Result-to-select timing is not yet verified. Reactions are provisional; motion truncation may need model-specific fade design. No measured FPS, GPU/CPU budget, 2P/network/timeout acceptance or Harmony-owner compatibility result is claimed.

Original touched upstream files are preserved in `tasks/select_poc_before/`; restore those and remove the new `MusicSelect*.cs`, `SelectChecks.cs` and this document to revert the POC source. The worklog records final artifacts and check results. The normal build reads existing game reference assemblies; no install/copy or game execution is part of this task.

To revert only the stock option while retaining the earlier POC/QA/startup fixes, restore files at their matching relative paths from `tasks/select_stock_before/` and remove the new `src/Unity/MusicSelectStockPartner.cs` and `src/Unity/IMusicSelectPartnerReactions.cs`, then rebuild with the same build command. No deployment settings were changed by this task.


## Layout inspection and tuning infrastructure (2026-10-20)

This extension preserves every existing default scale, offset, rectangle and enable setting. It makes no new layout or visibility selection. `nodes` starts empty, every group starts with `hidden: false`, and `partner.cropBottom` starts at `0`. The prior compact difficulty label, decoration suppression, fixed tab sockets, medal placement and ticket-content displacement retain their existing behavior.

Seven additional named groups use **scale 1, offsetX 0, offsetY 0, hidden false**:

| Preset group key | Target under Main |
|---|---|
| `difficultyHeader` | `UI_CMN_TitleBase` |
| `achievementRate` | `Null_UI_MSS_MusicAchievementRate` |
| `rivalData` | `Null_UI_MSS_RivalData` |
| `extraInfo` | `UI_MSS_ExtraInfo`, or `Null_UI_MSS_ExtraInfo` when the parent has that name |
| `festaDeco` | `Null_UI_FST_MSSelecter_Deco` |
| `challengeBG` | `Null_TestChallengeBGTarget` |
| `challengeDeco` | `Null_TestChallengeBGDecoTarget` |

The seven objects come from the feasibility study's asset hierarchy, checked against the monitor's dynamic initialization and difficulty-controller code in the read-only decompiled source. Optional absent objects are omitted from runtime `layout.groups`; their stock-default preset entries remain available. Stock transforms do not write the newly added animated objects. The difficulty header includes its visual button sockets; the operator chooses any nonstock transform live. The feature still applies only in ordinary active Music, with the existing special-mode/phase exclusions.

`hidden` is available on all twelve groups and all explicit nodes. Hiding sets a CanvasGroup's alpha to zero and blocksRaycasts to false, leaving GameObjects, scripts and Animators running. Unhiding and exit restore the original alpha and raycast setting, including non-default values. Components created for hiding are restored and removed on exit; pre-existing components remain. A one-frame reentry guard respects Unity's deferred destruction. Parent, sibling, rotation and game-owned active state are never rewritten by these controls. The legacy compact difficulty label is also suppressed when `groups.difficultyHeader.hidden` is true.

### Generic node API

```http
POST /api/music-select/node
X-PartnerLive2D: 1
Content-Type: application/json

{"path":"UI_ChainLists/MusicChainList","scale":1,"offsetX":0,"offsetY":0,"hidden":false}
```

`path` is relative to an entered side's `Canvas/Main`, or a full hierarchy path copied from the tree. Relative presets apply on entered sides; a full path selects the matching side only. Partial transform/hidden updates merge with that path's existing entry; a new entry starts at identity/visible. The accepted numeric ranges match groups: scale `.35..1.2`, offsets `-540..540`. `hidden` requires a JSON boolean. Missing path, unknown keys, nonfinite/out-of-range numbers, traversal, and protected paths are rejected before commit. Main, its ancestors, the `UI_Buttons` and `UI_InfoButtons` layers, input/touch-named layers, their descendants, and their ancestors are protected. Resolved targets must be below the selected Main. With a live session, missing targets return 404; without one, syntactically valid presets may be staged and resolve when a session starts.

The same data can be supplied through `POST /api/music-select/layout` as `nodes: {"<path>": {"scale":1,"offsetX":0,"offsetY":0,"hidden":false}}`. `/save` includes nodes, group hidden values and cropBottom in `select_layout.ini`. `/reset` clears nodes and returns to the unchanged disabled default preset; Save persists that reset. Identity transform plus hidden false clears a node's explicit adjustment in memory without removing its preset entry.

Requested node transforms apply after named groups and the existing socket/medal/content adjustments. An exact object shared by a group and a node uses one original baseline, and the explicit node transform takes precedence. Parent/child adjustments compose through the normal Unity hierarchy. Offsets add to that object's captured **parent-local** position (as for the original groups); the returned world rectangle is expressed in Main units. Existing Main-relative groups retain their prior units. Existing objects owned by the layout reuse their original captures; newly requested objects capture once before their first node mutation. Repeated updates and phase reentry never multiply an already modified scale. Use one canonical path per object to avoid multiple preset entries competing for the same target.

### Hierarchy API and editor

```http
GET /api/music-select/tree?depth=3&root=
GET /api/music-select/tree?depth=2&root=Null_GenreControll&player=1
```

`depth` defaults to 3, accepts integers 0..20, and counts child edges; zero returns just the root. Blank root means Main. Root also accepts a full hierarchy path. The optional `player` is 0 (1P) or 1 (2P); otherwise the first entered side is selected. No matching entered session returns 409. Tree reads work while tuning is disabled and outside Music, without applying changes.

Each tree node reports name, full path, relativePath, activeSelf (`active`), activeInHierarchy, localPosition, localScale, sizeDelta, min/max anchors, childCount and short component names. `worldRectInMain` gives the four world-space RectTransform corners transformed into Main coordinates plus their axis-aligned x/y/width/height envelope; non-RectTransforms return null rectangle/anchor/size fields. Inactive children are included. This exposes the jacket band, genre frame/arrows, title header, distance strip and dynamically created children without guessing names from screenshots. HTTP hierarchy operations run through MainThread.Call, and existing localhost, request-header and Origin restrictions continue to apply.

The tuning page now has controls for all twelve groups, their hidden flags, `partner.cropBottom`, and a tree viewer with root/depth/side selection. Clicking a tree row copies its full path into the node editor and loads saved values. Apply node writes the in-memory preset; Save select preset persists it. Tree details show geometry and components. The editor sends finite numeric values through the same server validation as the API.

### Partner cropping

`partner.cropBottom` accepts `0..1`, the fraction of the original presentation height removed from its bottom. The full-body top, width, scale and original bottom coordinates remain the reference: the visible bottom moves up by `height * cropBottom`, and visible height becomes `height * (1-cropBottom)`. Zero retains the full presentation; one clips it entirely. Native NavigationCharacter uses a RectMask2D enabled only for a positive crop, preserving its animation container and scripts. Stock full-body textures and custom Live2D use matching UV rectangles and resized display rectangles without stretching the retained upper body; custom FlipY is respected. Cropping does not overwrite result-screen/room partner configuration. Views are suspended/closed through the existing phase/reset/dispose paths.

### Verification boundary

Build through `powershell -ExecutionPolicy Bypass -File build.ps1` only, without Install. The headless suite includes the actual shared transform/visibility state engine and checks repeated apply, retuning, identity reset, exact/idempotent restore, reentry, preserved Z, original alpha/raycast restoration, untouched stock transforms, new defaults, node deep-copy/partial merge, protected-path rejection, crop bounds, and node/hidden/crop save/load. Developer review also checks the routes, Unity adapters and tuning-page script. These checks do not establish live asset compatibility, animator timing, mask/shader behavior or artistic acceptance; no game launch or installation is part of this infrastructure task.


## Extra category inputs and node sprites (2026-10-21, build only)

`categoryKeys` defaults to `true`, but has effect only with `selectView = true` / preset `enabled: true`. Set `categoryKeys = false` in settings.ini, or POST `{"categoryKeys":false}` to `/api/music-select/layout`, to disable the extra inputs. The tuning page exposes a Category keys checkbox. Save persists the flag in both the preset and settings.ini; settings.ini takes precedence at startup. Reset restores categoryKeys=true and disables the select view.

In ordinary, idle Music, physical Button02 and touch A2 perform the game's E2 action (CategoryScrollRight); Button07 and A7 perform E8 (CategoryScrollLeft). Each category method is resolved once and invoked through cached reflection. The existing stock branch then calls ChangeBGM and ChangeTargetRate, retaining stock animations, sounds and input locks. The integration extends the exact E2/E8 conditions inside MusicSelectSequence.Update; earlier NEXT/BACK, scroll, difficulty and EachPushDecision branches keep their priority. Stock E2/E8 behavior remains available when the extra inputs are disabled.

Physical single presses wait **70 ms**. An opposite button down within that window (including the boundary and even after the first button was released), simultaneous downs, or two held buttons cancel the extra action. Stock EachPushDecision and its 50 ms score-kind window are unchanged; no input state is consumed or rewritten. Touch downs act immediately, coalescing with a pending physical press. Holding a button does not repeat the extra action. Windows are independent per sequence/player, cleared on Music entry, ineligibility, or a higher-priority stock branch. Shared scrolling and per-player input locks are respected, along with the select feature's normal-phase/special-mode/network/wait exclusions; an open mission popup and expired Freedom timer also exclude extra input. Options, category animation, loading and other phases do not enqueue actions.

### Sprite replacement

Place the edited PNG under `LocalAssets\PartnerLive2D\select_sprites\` during a separately authorized deployment. This task does not create or change that live directory. Example request:

```http
POST /api/music-select/node
X-PartnerLive2D: 1
Content-Type: application/json

{"path":"<relative or full node path from tree>","sprite":"edited.png"}
```

The target must have a UnityEngine.UI.Image and an existing sprite. Only one filename is accepted: subdirectories, traversal, drive names, non-PNG extensions and control/invalid filename characters are rejected. Missing files return 404; malformed/nondecodable PNGs and unsupported targets return 400 before committing a live preset. Files are limited to 32 MiB, 8192 pixels per dimension and 16M pixels total. Without a session, target resolution is deferred; the file must still be valid. Saved presets are also checked when first applied.

The PNG becomes a full-rect Sprite with the original sprite's normalized pivot, border and pixelsPerUnit. Image.sizeDelta is never changed and SetNativeSize is never called; use the exported original dimensions to preserve the same nine-slice proportions. The override is reapplied after game updates while the existing normal Music layout is active. Clearing, phase exit, disable/reset, fault or release restores the captured original sprite and destroys only this feature's Sprite/Texture2D resources. Re-entry captures the current stock sprite again. Game-owned sprites/textures remain intact.

`{"path":"<node path>","sprite":null}` clears the override. Omitting sprite preserves the previous value during partial edits. Re-posting a filename to `/node` reloads its PNG, supporting repeated art edits under one name. The same nullable `sprite` field is accepted inside `nodes` on the layout endpoint and persists through Save/load. Existing node path protections continue to apply. The node editor has a filename input (blank clears), Apply node and an Export current sprite PNG link.

### Current sprite export

```http
GET /api/music-select/sprite.png?path=<URL-encoded node path>
GET /api/music-select/sprite.png?path=<URL-encoded node path>&player=1
```

The optional player selector is 0 for 1P or 1 for 2P; otherwise the first matching entered side is used. Full hierarchy paths select the matching side. A missing path returns 400, an absent target 404, a target without a current Image sprite 400, and no matching entered session 409. Export works with tuning disabled or outside Music and reads the sprite currently assigned at request execution; during an active override it therefore exports the edited art.

On the Unity main thread, a temporary RenderTexture blit plus ReadPixels makes the atlas readable without requiring texture importer changes. Rectangular sprites copy the exact atlas texels within their sprite rectangle. Tight or rotated packing uses the sprite mesh UVs to reconstruct its logical sprite rectangle, retaining transparent margins without adjacent atlas art. PNG pixels use straight alpha from the atlas copy, with no UI blend/premultiplication, Image color/tint, material effects or screen background baked in. GPU readback does not modify the source texture. The previous active render target and sRGB-write state are restored in finally; temporary GPU/CPU resources are released on success or error.

### Developer verification and remaining acceptance

Build only with `powershell -ExecutionPolicy Bypass -File build.ps1`, without Install. Run the built harness with DOTNET_ROOT=<TOOLS>\dotnet. New headless checks execute the actual shared 70 ms arbiter with input doubles, covering both orders, simultaneous/1/16/49/50/60/69/70 ms combos, released taps, long holds, touch coalescing, cancellation, phase transitions, reset and independent sides. Preset tests cover nullable sprite merging, filename rejection, isolation and save/load, plus categoryKeys defaults/startup precedence/persistence. The tuning-page DOM/fetch mock covers categoryKeys, filename/clear payloads and export URLs alongside the prior controls.

These checks do not run Unity or apply Harmony to the live process. A separately authorized installation/runtime review must still confirm physical combo timing, 1P/2P priority and waits, category animations, popup/timeout handling, Image dimensions/pivot/border, animator-driven sprite restoration, alpha/color/packing/orientation of actual atlas exports, resource lifetime and artistic acceptance. The game remains running and untouched by this build-only task. Evidence and preserved source are under `tasks/select_input_sprite_*`; worklog: `tasks/select_input_sprite_worklog.md`.

Final build-only verification for this extension: mod and Headless built with zero warnings/errors. Full harness passed 71 new input/sprite assertions, 68 existing select assertions, 20 router assertions, main-thread/rig suites and all 8 models. JavaScript syntax and the updated DOM/fetch mock passed. Logs: `tasks/select_input_sprite_build.log`, `tasks/select_input_sprite_headless.log`, `tasks/select_input_sprite_ui.log`; scoped review: `tasks/select_input_sprite_changes.diff`; artifact/source hashes: `tasks/select_input_sprite_manifest.json`.

## Sprite-only input hints and matching-card correction (2026-10-21, build only)

A leaf `Image` below `UI_Buttons` or `UI_InfoButtons` may now receive a sprite-only node override, using either a relative or full path:

```json
{"path":"UI_Buttons/UI_Button04/FlatButton(Clone)/IMG_Button","sprite":"next_flat.png"}
```

Only `path` and `sprite` are accepted for these protected descendants; `sprite: null` clears the replacement. Supplying any `scale`, `offsetX`, `offsetY` or `hidden` field remains rejected, including identity/zero/false values, and mixed requests fail atomically. The layer itself, ancestors of input layers, nonleaf objects, Images without a stock sprite, and other input/touch-named layers remain protected. Without a session, Image/leaf checks are deferred until resolution. The settings parser and live resolver share the same layer policy; protected entries serialize with only `sprite` so Save/load retains this restriction. Use a direct sprite-only API request for these nodes: the general tree editor submits transform/hidden fields and is therefore still rejected on protected paths.

These protected sprite nodes never capture or write their transform or visibility. PNG validation and the existing sprite lifetime remain unchanged: clear/reload/phase exit/disable/reset/fault/release restore the captured original sprite, destroy only mod-owned assets and recapture on reentry. Overrides still apply only while the eligible Music layout is active.

Extra category keys no longer inherit `NormalReason` layout exclusions. They are enabled by the select feature and categoryKeys setting (and disabled after a feature fault), then sampled only with the stock sequence validity and shared/per-player input locks. Actions are still dispatched solely at the original E2/E8 conditions inside MusicSelectSequence.Update, after NEXT/BACK, music scroll, difficulty and EachPushDecision branches. Thus matching, random and other special categories can be left whenever stock E2/E8 can leave them. There is no additional mission-popup, Freedom-timer, song-ID, mode, network or presentation gate on the extra category action. The existing 70 ms physical combo arbitration, immediate A2/A7, cancellation on locks/earlier branches and independent player windows remain in effect. This supersedes the narrower extra-key eligibility described above; stock sequence scheduling and its branch guards remain authoritative.

The matching card (music ID 2, displayed as the in-store matching list) is **the same normal Music phase and MusicSelectChainList**: MusicSelectMonitor.SetDeployList activates `_musicChainList`, and MusicSelectChainList.SetChainData routes ID 2 to SetWaitConnectData. The old absence of layout was the explicit `id == 2 || id == 3` guard, rather than phase detection or a separate active ChainList. Layout therefore now applies to ID 2 when all other normal Music guards pass. Random ID 3 and the existing special modes/folders retain their exclusions; eligible normal preparation/network-wait states retain the layout. Metadata still identifies ID 2 as a placeholder with null difficulty/level/BPM.

Headless regressions exercise the shared live/parser protection policy, both known layers and relative/full paths, sprite clear, mixed and identity-field rejection, atomicity, sparse serialization/save/load, and the complete stock sampling-gate truth table plus pending-input cancellation. Existing input-window, router/placeholder, transform/visibility, preset and model regressions continue to run. Headless does not execute Unity/Harmony or prove physical controls, actual leaf hierarchy, animator restoration or visual placement; those remain live acceptance items. This correction builds only and does not install or control the running game. Evidence and source rollback snapshots are under `tasks/select_fix2_*`; worklog: `tasks/select_fix2_worklog.md`.

## Late transform replay and custom overlay images (2026-10-21, build only)

The process hooks still choose eligible sides and apply the current preset during Update. A per-layout `MusicSelectLateRunner` now replays modified transforms in `LateUpdate` with execution order 32000, and again from `Canvas.willRenderCanvases`. The late path performs no hierarchy searches, JSON work, resource creation or baseline capture. It uses preallocated transform-state buffers; unmodified groups and restored states produce no writes. Existing hint/medal/content adjustments retain their ordering, and explicit node values win last, including an explicit identity node. Both callbacks become no-ops immediately on phase exit, disable, reset, fault or release. Release removes the runner and its canvas subscription.

Stock local position/scale values are captured once at session construction, before later tuning or animation samples. Protected input/touch subtrees are excluded from this capture. Shared group/node targets reuse the same immutable baseline. Offsets/scales remain baseline-relative and preserve Z; late callbacks never adopt animated or previously modified values as a new baseline. Returning an ordinary group to identity restores its stock value once and returns control to the game. Explicit node identity retains the prior node-override behavior.

Read-only decompiled source evidence: `TabController.Set/Change` invokes `SelectorTab.PlayInAnimation/PlayMoveRightAnimation/PlayMoveLeftAnimation`; `SelectorTab` delegates In, Out, In_Change, Move_Left and Move_Right to `AnimationParts.Play`, which calls `_animator.Play(animName, 0, 0f)`. These classes do not directly assign the six mini-tab sockets' position/scale. The precise serialized clip bindings are not present in those C# files. The late adapter covers both `Transform.localPosition` (including RectTransform position updates) and `Transform.localScale`; a particular clip/property binding is not claimed from decompiled C# alone. In-game rendering remains an acceptance check.

### Overlay API and persistence

`POST /api/music-select/image` uses the existing localhost, Origin, JSON and `X-PartnerLive2D: 1` requirements. Create or update an image by ID:

```json
{
  "id": "board",
  "sprite": "board.png",
  "parent": "Null_SelectorBackgroundTarget/UI_CMN_SelectorBG(Clone)/Null_CanvasGroup",
  "siblingIndex": "first",
  "x": 0, "y": 0, "width": 800, "height": 500,
  "hidden": false
}
```

The PNG must already exist under `LocalAssets\PartnerLive2D\select_sprites`. Filename, size, pixel-count and PNG-decoding checks match node sprite replacement. `parent` accepts a relative path below Main or a full hierarchy path for a specific side. It must resolve to a permitted descendant; Main/ancestors and input/touch layers or their ancestors remain protected. Relative parents apply independently to eligible sides. Files are validated before preset commit; absent-session hierarchy checks are deferred until application.

The added object has a centered pivot, `Image.raycastTarget = false`, default white tint and an exact width/height rectangle. **Overlay x/y are in the parent's local space relative to its pivot**, with +Y up; ancestor transforms then affect the image. There is no aspect-preserving fit or native-size adjustment. `siblingIndex` accepts `"first"`, `"last"` or a nonnegative 32-bit integer (clamped to the available child range). Using `"first"` at the example parent draws the board behind the existing song-card children, subject to the parent's actual masks/Canvas sorting.

New IDs require `sprite` and `parent`. Other fields default to x/y=0, width/height=100, hidden=false, siblingIndex="last". Existing IDs support partial updates and retain omitted fields. IDs contain 1–64 ASCII letters, digits, underscores or hyphens; at most 64 images are allowed. X/y accept -10000..10000 and width/height .01..8192; nonfinite values, fractional/negative sibling indices, unknown keys and invalid mixed updates are rejected atomically. Reposting `sprite` on `/image` reloads an edited PNG, including the same filename.

Delete with `{"id":"board","delete":true}`; deleting an absent ID is harmless. Delete requests accept only id/delete. Upsert and delete are applied on the next eligible process frame; an inactive phase only stores the preset. Images appear exclusively while the existing normal Music layout is applied. Leaving Music, disabling, resetting, faulting or releasing deactivates their GameObjects immediately and schedules destruction of their owned GameObjects/Sprites/Textures; stock children and game-owned assets are preserved. Reentry recreates images from the preset. PNG resources are reused across unchanged frames.

The layout JSON stores an `images` object keyed by ID, whose values contain sprite, parent, siblingIndex, x, y, width, height and hidden. `/layout` accepts partial image entries; a null image value deletes that ID. `/save` persists images in `select_layout.ini`; reset clears them in memory, and Save persists that reset. Startup still requires the existing settings.ini opt-in. The **Custom overlay images** tuning panel provides saved-image selection, ID/file/parent/sibling/rect/hidden fields, create/update and delete. Its default parent/sibling demonstrate the board use case; the shared Save button persists edits.

### Verification boundary

`SelectFix3Checks` exercises the actual transform-state engine under simulated post-Update Animator writes, immutable baselines, Z preservation, explicit identity, retuning/reentry, exact/idempotent restore and zero managed allocations over 1000 Apply+late-replay frames. Image tests execute the production request/preset parser, sibling calculation, partial/deep-copy updates, delete, numeric/file/path/id rejection, atomic mixed updates, count limits and real INI save/load. The updated DOM/fetch mock covers overlay create/load/update, all sibling forms, hidden, delete, subsequent layout updates, Save and reset, alongside existing page controls. Headless and the page mock do not execute Unity or prove visual composition, native object lifetime, clip binding/timing, masking or 1P/2P behavior. Worklog, source snapshots and verification artifacts: `tasks/select_fix3_*`.

Final fix3 verification: Headless built with 0 warnings/errors and passed 161 new fix3 assertions, all existing suites and all 8 model regressions. Page syntax and DOM/fetch checks passed. The changed Unity layout/image/late-runner adapters also passed a C# 7.3/net472 compile check against offline Unity references on E: with game/Harmony boundary doubles (one expected stub-only unassigned-root warning). **The target-game mod build has not been performed**: its reference assemblies reside under <GAME_ROOT>, and the task forbids touching that path. Read-only reference access was requested and remains unconfirmed. The existing `bin/PartnerLive2D.dll` has not been rebuilt for fix3. No game start/stop, deployment or access to <GAME_ROOT> occurred.
