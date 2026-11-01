# Architecture

## The cabinet software stack

| piece | where | role |
|---|---|---|
| `Sinmai.exe` | `<PKG>` | the game: Unity 2018.4.7f1, Mono runtime with the **Boehm GC** (no generations, no ephemerons) |
| `inject.exe` + `mai2hook.dll` -> `amdaemon.exe` | `<PKG>` | segatools; amdaemon reads buttons, Test/Service, coin and the card scan key (`segatools.ini`: scan `0x0D` Enter, test `0x70` F1, service `0x71` F2, coin `0x72` F3; ring buttons 1-8 = W E D C X Z A Q) |
| `hinata.dll` | `<PKG>` | Aime / Maimoller IO bridge inside amdaemon; it updates itself at launch |
| MelonLoader | `<PKG>\MelonLoader` | loads `Mods\*.dll`; proxy DLL is `winhttp.dll` (the `version.dll` proxy stopped being loaded on one machine; UnityPlayer imports WINHTTP, so a copy in the app folder loads) |
| MuMod -> AquaMai | `Mods\MuMod.dll`, `AquaMai.toml` | MuMod keeps AquaMai updated; do not also install a local AquaMai.dll (double load) |
| **PartnerLive2D** | `Mods\PartnerLive2D.dll`, `Live2DCubismCore.dll` next to `Sinmai.exe` | this project's mod; without the Cubism core DLL it disables itself |
| skin assets | `Sinmai_Data\resources.assets` (+ `.resS`), `StreamingAssets\A000\AssetBundleImages\*.ab`, StreamingAssets XMLs | written by `tools\*.py`; stock copies in `<GAME_ROOT>\backup_ngo_skin\` |

AquaMai features the project relies on: `UX.OneKeyEntryEnd` (Service long press on music select ends the credit with
a proper upload), `UX.OneKeyRetrySkip` (Service held in a song skips it), `UX.DontRuinMyAccount` (autoplayed scores
are not saved), `Utils.FreedomTimer` InfinityTime (endless freedom mode), `Tweaks.TimeSaving.CheckSkip`,
`Tweaks.TimeSaving.EntryToMusicSelection` (tests only, set by drive.ps1), `GameSystem.DisableTimeout`,
`GameSettings.ForceAsServer`, `Utils.AntiLag` (needed for background running on a cabinet, disabled during tests),
`Utils.InstantQuit` (Esc quits), WASAPI exclusive audio (`GameSystem.Sound`).

## Repository layout

| path | content |
|---|---|
| `src\Core` | Cubism core P/Invoke (`CubismCore`, `CubismModel`, `MocModel`), `RigModel` (own `*.rig3.json` model format, see `docs\RIG3_FORMAT.md`), `SpritePairData` (sprite manifest + timing, no Unity) |
| `src\Framework` | port of the Cubism Native Framework: motions, physics, expressions, eye blink, breath, pose, `CubismAvatar` facade, JSON |
| `src\Unity` | the mod: `Mod.cs` (entry, settings), `ControlServer.cs` (HTTP), `PartnerView.cs`, `Patches.cs`, `MusicSelect*.cs`, `Ngo*.cs`, `TestHarness.cs`, ... ; `ui.html` = tuning page embedded in the DLL |
| `deploy\` | files `build.ps1 -Install` copies into `<PKG>`: sample partner `hiyori` (Live2D sample, naviChara 90, partner 501) and its `Partner.xml` |
| `tests\Headless` | net8.0 harness: rig / sprite-pair / select / PNG-cache / theme / main-thread checks + all sample models; `Headless.exe spritepack <manifest.json>` checks a sprite export |
| `tests\Net472Compile` | compile check against net472 |
| `tools\` | Python / PowerShell tools: asset patchers, test drivers, reports (docstring at the top of each `.py`) |
| `mcp\` | the `partnerlive2d` MCP server |
| `docs\SELECT_VIEW.md` | design notes of the music-select layout (its value ranges are older than the code: trust `MusicSelectSettings.cs`) |

Build: `PartnerLive2D.csproj` (net472, C# 7.3) references the game's own `Sinmai_Data\Managed\*.dll`, MelonLoader and
0Harmony from `<PKG>` (`-p:GamePackage=`), so a game install is needed to compile.

## Rendering the partner

- **Live2D / rig**: `Live2DRenderer` draws the model into a RenderTexture with built-in shaders only (the game ships
  none for Live2D and new ones cannot be compiled): `Sprites/Default` for colour + alpha, `UI/Default` with
  ColorMask RGB and stencil for clipping masks, legacy particle Additive / Multiply for blend modes. The RT is
  premultiplied and shown by a `RawImage` with `Legacy Shaders/Particles/Alpha Blended Premultiply`.
- **Sprite pair** (`SpritePairBackend`, used when `partner.ini` has `sprites = ...`): BC7 frames drawn with
  `Sprites/Default` into a square RT (`renderSize`), crossfaded on clip switches. Falls back to the model on any
  failure (BC7 unsupported, missing loop clips) - so a sprite partner still needs a valid `model`.
- `PartnerView` is the component: contexts `musicSelect`, `result`, `room`, `overlay` (the API preview). It hides the
  stock navi with a `CanvasGroup` (alpha 0), measures the stock navi's footprint once and places itself
  bottom-centre anchored. Result / overlay use `scale` / `offsetX` / `offsetY`, the room uses `roomScale` /
  `roomOffsetX` / `roomOffsetY` (units: stock partner heights), music select uses the select preset's pixel rect.

## Harmony hooks, by screen

Many hooks are installed only when their data file exists (`Prepare()`), so removing a file switches a feature off
without a rebuild (restart needed).

| screen / moment | hook | file | what it does | switch |
|---|---|---|---|---|
| power-on self check | `ProcessManager.AddProcess` | `NgoBootScreen` | covers the boot / self-check screens with the NGO boot logo + loading sprite (circle) and the game's login card, filled in letter by letter as the check progresses (top screen) | `<ASSETS>\boot\windose_boot.png` |
| attract | `AdvertiseProcess.OnUpdate` | `NgoTitleHold` | plays the SEGA / ALL.Net logos at 30x and freezes the title clip half way, so the attract loop is only the NGO title | always |
| attract / card | `TestHarness.PollInput` (OnUpdate) | `TestHarness` | counts coin-key presses (F3) on the attract / card screen for the quick-login helper (`/api/test/quicklogin`) | always |
| card screen | `EntryProcess.OnStart` | `TestHarness` | remembers the entry state machine (`/api/test/entry`) | always |
| login | `PlInformationProcess.RestoreCharadata` | `AccountTweaks`, `TestFirstPlay` | raise character levels / equip a partner; test-only "never played" card | `charaLevel`, `equipPartner`; `testInput`+`testFirstPlay` |
| login -> select | `ProcessManager.AddProcess` | `AccountEdit` | one-shot profile edit from `account_edit.ini` (applied at MusicSelectProcess, renamed `.applied.ini` at the logout upload) | file exists |
| upload | `VOExtensions.Export(UserDetail)` | `TestFirstPlay` | puts the real play count / partner back into every upload | test only |
| every screen change | `Image.OnEnable` | `NgoTransitionVariant` | change-screen medallion + banners: one Ame/KAngel roll per transition; KAngel art from `ngo_transi_kangel\` | folder exists |
| any Image | `Image.OnPopulateMesh` | `NgoTint` | vertex tint for textures listed in `ngo_tint.txt` that the game colours from code (e.g. the collection screen's green) | `ngo_tint.txt` |
| any TMP text | `TextMeshProUGUI.OnEnable` | `NgoPixelFont` | builds a dynamic SDF font from the pixel font patched into the game's LiberationSans Font object; each text's material cloned onto it | `pixel_font.txt` |
| partner cards (top screen) | `Image.sprite` setter | `NgoPartnerCards` | for cards listed in `ngo_open_cards.txt`: capsule mask off, picture 300 px above the frame (` front` suffix = above the other pictures too) | `ngo_open_cards.txt` |
| menu backgrounds | `BGController.Awake`, `SubBGController.Initialize` | `NgoBackground` | runtime flat background (superseded by the patched 3D background; kept) | `ngoBackground = <folder>` |
| music select | `MusicSelectProcess.OnStart/OnUpdate/Next/OnRelease/OnGameStart`, `SoundManager.PreviewPlay/PreviewEnd`, `MusicSelectMonitor.ScrollMusicLeft/Right` | `MusicSelectSession` (+ `Layout`, `ReactionRouter`, `StockPartner`, `PhasePolicy`) | applies the select preset in every phase, mounts the partner (sprite / Live2D / stock navi) as the first child of Canvas/Main, plays page clips, holds START (ReleaseWait) until the Enter clip ends (max 4 s) | `selectView = true` |
| music select | `MusicSelectSequence.Update/OnStartSequence` | `MusicSelectCategoryInput` | extra category keys on the stock E2/E8 branches | `categoryKeys` |
| music select | `MusicSelectProcess.SetConnectData/IsConnectStart/IsConnectStop` | `MusicSelectHideMatching` | drops the in-store matching category on a single cabinet; `PartyExec` keeps running because in NORMAL mode it turns START into the track start | `hideMatching` (default on) |
| music select (freedom) | `MusicSelectProcess.OnStart`, `PleaseWaitMonitor.SetTime` | `FreedomEntry` | first select as a freedom play; hide / replace the frozen 10:00 clock | `freedomEntry`, `freedomInfinity` |
| track start | `TrackStartProcess.OnStart` | `NgoTrackStartWalker` | hides the bear puppet's views and draws Ame's 4-frame walk on the puppet root | `ngo_ame_walk\frame_1.png` |
| song | `GameProcess.OnStart/OnRelease` | `PlayStats` | one `[play]` log line per chart (frames > 25 / 50 ms, worst, load ms, GC count) | always |
| every fade + song start | `SystemConfig.unloadUnusedAndGC` | `NgoCleanupTiming` | skips a second unload+GC within `cleanupMinGap` s (keeps it under the fade); logs `[clean]` | `cleanupMinGap` (20; 0 = stock) |
| result | `ResultMonitor.Initialize/SetStartData/TouchPartnerCharacter` | `Patches` | attaches the partner when the equipped partner id matches a `partner.ini`; picks the sprite tier from clear / achievement / AP; applies `resultUnder`; tap -> Reaction clip | partner.ini |
| result (test) | `ResultTimelineManager.SetData` | `TestHarness` | forced clear-rank animation (`/api/test/rank`) | testInput |
| any navi animation | `NavigationCharacter.Play` | `Patches` | maps the stock navi's state (Default, Welcom, FunStart, FunStartLoop, Sad01) to motions / clips | partner.ini |
| partner room (collection) | `PartnerRoom.CreatePartner` | `Patches` | partner by naviChara id; page clips; FunStart (closeness up) plays Confirm | partner.ini |
| icons / result photo | `AssetManager.GetPartnerTexture2D/GetPartnerResultTexture2D` | `Patches` | `icon` / `resultImage` PNG for the custom partner id | partner.ini |
| upload screen | `UserDataULProcess.OnStart`, `MapProcess.OnRelease` | `NgoUploadScene` | hides the mascot group `DataSave/Chara` (one CanvasGroup) and walks one picture on the strip | `ngo_upload\upload_scene.png` |
| runtime theme | patched on demand: `Image.sprite`, `RawImage.texture`, `GameObject.SetActive` | `ThemeOverride` | swaps sprites by name from a theme folder (not used by the shipped skin) | `theme = <folder>` + `/api/theme` |
| every process | `ProcessManager.AddProcess` | `TestHarness` | process log (`[proc] + Process.X` lines, `/api/test/processes`) | always |

Screens not handled yet (the stock fallback navi shows): first-play-of-the-day settings (SimpleSettingMonitor),
login bonus, shop partner-get window, friend-battle result.

## `<ASSETS>` = `<PKG>\LocalAssets\PartnerLive2D`

| entry | used by |
|---|---|
| `settings.ini` | mod settings (table below), read once at game start |
| `select_layout.ini` | music-select preset: `# comment` + one line `layout = {json}` (same JSON as `POST /api/music-select/layout`) |
| `<partner>\partner.ini` (+ `model\`, `sprites\`, `icon.png`) | one custom partner per folder (keys below) |
| `select_sprites\*.png` | PNGs for preset node sprite overrides and overlay images (e.g. `board.png`, `tabbase_flat.png`); `*.orig.png` = pre-recolour copies |
| `boot\` | NgoBootScreen art (`windose_boot.png`, `loading_0..7.png`, `top_static.png`, `checkmark.png`, login card pieces) |
| `ngo_transi_kangel\` | KAngel variants of the transition textures (`01_01`, `Sub_01`, `Sub_02`, `sub_03`) |
| `ngo_upload\upload_scene.png`, `ngo_ame_walk\frame_1..4.png` | upload / track-start art |
| `ngo_tint.txt`, `pixel_font.txt`, `ngo_open_cards.txt` | feature switches + lists (see hooks) |
| `ngo_bg\`, `ngo_recolor\` | runtime background art (off) / recolour outputs kept for reference |
| `account_edit.ini` / `account_edit.applied.ini` | one-shot profile edit (private: never pack or commit) |
| `heapwalk\` | heap-walk TSVs written by the test API (move them out; `test_heapwalk` does) |

### settings.ini keys

`key = value` lines; comment lines start with `#` (a `;` line that contains `=` is parsed as a junk key - harmless
only because its name starts with `;`). Restart the game after editing; `music_select_save` rewrites only
`selectView`, `selectPartner`, `categoryKeys`.

| key | default | meaning |
|---|---|---|
| `http`, `port` | true, 8791 | control API on 127.0.0.1 |
| `selectView` | false | music-select layout + select partner on |
| `selectPartner` | `equipped` | `equipped` = the partner.ini whose `partnerId` is the equipped partner, else the stock navi; `stock`; or a partner folder name (forces it) |
| `categoryKeys` | true | extra category keys on music select |
| `hideMatching` | true (anything but `false`) | drop the in-store matching category |
| `enterTransition` | on (`off` disables) | hold START until the sprite Enter clip ends |
| `selectTapAreas` | rect-derived | comma list of select touch areas that tap the partner |
| `freedomEntry` | false | first select after login as freedom play (needs AquaMai EntryToMusicSelection) |
| `freedomInfinity` | off | `true` = infinity mark instead of the frozen 10:00, `hidden` = nothing |
| `cleanupMinGap` | 20 | seconds; 0 = stock cleanup behaviour |
| `charaLevel`, `equipPartner` | 0 (off) | account tweaks at login (see account-tweaks.md) |
| `theme`, `ngoBackground` | off | runtime theme folder / runtime background folder |
| `testInput` | false | TEST ONLY: in-game key hook (useless for buttons, see pitfalls), enables autoplay / rank / first-play routes |
| `testFirstPlay` | false | TEST ONLY (with testInput): the card plays as never-played locally |
| `inputLog` | false | `[input] 1P ButtonNN down/up (screen)` for every button edge the game sees |
| `frameLog` | false | TEST ONLY: `[frame]` hitches, 10 s summaries, per-minute `[mem]` census (the census itself hitches) |

### partner.ini keys

An unknown key makes the mod skip the whole partner (PartnerConfig.Set throws), so markers must be comments
(`# created_by = mcp`). The folder is skipped unless `model` exists and `naviCharaId` is set.

| key | meaning |
|---|---|
| `model` | `*.model3.json` (moc3 or `*.rig3.json` inside), relative path; required even for sprite partners (fallback) |
| `sprites` | sprite manifest (`sprites/manifest.json`); enables the sprite backend on select, room and result tiers |
| `naviCharaId` | navi chara id the partner draws for (0-28 stock; a stock id replaces that navi) |
| `partnerId` | partner id (1-38 stock; custom from 501) - result / select / icon matching uses it |
| `icon`, `resultImage` | PNGs for the collection icon / result photo |
| `motion.<State>`, `expression.<State>` | State = Default, Welcom, FunStart, FunStartLoop, Sad01 -> motion group / expression (Live2D only) |
| `idle`, `tap` | idle / tap motion groups (Live2D) |
| `scale`, `offsetX`, `offsetY` | result screen + preview overlay: size in stock-partner heights (0.05-10), offset in heights (-5..5) |
| `roomScale`, `roomOffsetX`, `roomOffsetY` | the same for the partner room |
| `resultUnder` | comma list of result objects (name or path end) given an own canvas one order below the screen, so they draw under the partner |
| `renderSize` | RT size (64-4096; 1536 for the sprite pair) |
| `flipY`, `lookStrength` | flip; look-at strength (0 = off) |
| `debugDump`, `debugOverlay` | write debug PNGs into the folder / show an overlay ~20 s after boot cycling states |

A new partner also needs `Sinmai_Data\StreamingAssets\A500\partner\partner<6 digits>\Partner.xml` (UTF-8 with BOM,
`isDefault` true so every card owns it, naviChara = a free id >= 29), read only at game start. The game has no
prefab for a new naviChara id, so it falls back to a stock navi, which the mod hides. `create_partner` writes both.

## Sprite-pair backend

- Export: `tools\ngo_sprites_export.py` -> `stage\ngo_pair_sprites\` (`manifest.json`, one `<clip>.bin` per clip,
  `_qa\` keying sheets); check with `tests\Headless\...\Headless.exe spritepack <manifest>`; install with
  `tools\ngo_sprites_install.ps1` (copies into `<ASSETS>\ngo_pair\sprites`, sets the `sprites` line, backs up the ini).
- `manifest.json` v1: `pairCanvas [1024, 1024]`, `drawOrder ["Ame", "K"]` (K is drawn in front of Ame),
  `crossfadeSeconds 0.12`, `clips[]` = `{name, character Ame|K, group, tier?, fps 24, totalFrames, textureWidth,
  textureHeight (multiples of 4), rect [x, y, w, h] on the pair canvas, drawings[] {offset, length = w*h, hold}}`.
  Each `.bin` is the BC7 drawings back to back: straight alpha with nearest-colour bleed, rows bottom-up (Unity's
  `LoadRawTextureData` order). Sprites/Default premultiplies when drawing; set `_RendererColor` and `_Flip` or it draws
  nothing.
- Groups and triggers:

| page / event | clips |
|---|---|
| music select, always | `Idle` (both characters loop) |
| music select, every 8-15 s per character | `Random` |
| music select / room: touch on a partner area, API tap | `Tap` |
| entering the Menu phase (and room FunStart) | `Confirm` |
| START | `Enter`; ReleaseWait is held until it ends, at most 4 s (`enterTransition`) |
| result | `Result<Tier>Loop`; tap (TouchPartnerCharacter) -> `Result<Tier>Reaction` |

  Tier: not cleared -> Fail; AP or >= 100.0000 % -> Top; >= 97 % -> Good; else Clear. Within a group the same clip
  never plays twice in a row.
- Loading: one reader thread, one drawing in flight, uploads on the main thread under a time budget (about one
  texture per frame). Result loads only the tier's Loop + Reaction; select loads the page groups (the NGO pair:
  622 textures, ~261 MB VRAM, ~25 s off screen). `views/<id>` reports `loadedTextures` / `totalTextures`.
- The partner shows on result only when the equipped partner (`EquipPartnerID`) has a partner.ini; select uses
  `selectPartner`.
