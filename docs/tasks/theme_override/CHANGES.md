# Theme override: implementation and handoff

## Files changed

- `src/Unity/ThemeOptions.cs`: game-relative folder resolution and exact-name PNG indexing.
- `src/Unity/ThemeOverride.cs`: opt-in component replacement, sprite/texture caches, animator reassertion, status and restoration.
- `src/Unity/Mod.cs`: read `theme` from the existing settings and shut down the feature on exit.
- `src/Unity/ControlServer.cs`: main-thread `GET /api/theme` and `POST /api/theme`.
- `tests/Headless/Headless.csproj`, `Program.cs`, `ThemeChecks.cs`: settings and name-matching checks.
- `bin/PartnerLive2D.dll` and build intermediate/headless outputs were produced by the required build script. The game directory was not modified.

The source baseline used to produce `changes.diff` is in `baseline/`; the workspace has no Git metadata. The diff covers the seven source and test files listed above, not build artifacts or this handoff.

## Enable and control

After the user separately deploys the built mod, set `theme = theme` in the existing `Package/LocalAssets/PartnerLive2D/settings.ini`. The path is resolved relative to `LocalAssets/PartnerLive2D` under the running game's directory; a different relative subfolder is allowed, but rooted paths and paths outside this root are rejected. If the key is absent or empty the theme is off, no hooks or scanner are installed, and it cannot be turned on via the API until a folder is configured and the mod restarted. The configured folder must already exist.

Place `OriginalTextureName.png` files directly in that folder. Matching is exact and case-sensitive by the Unity texture's `name` (PNG extension itself is case-insensitive). For a sprite use a PNG either matching the texture's full dimensions, or matching the logical sprite dimensions; the latter is suitable for a single sprite-sized piece of an atlas. The original sprite's normalized pivot, pixels-per-unit, border and corresponding rectangle are used. `RawImage` requires a full texture-sized PNG.

With the existing local HTTP server enabled, use `GET http://127.0.0.1:8791/api/theme` to inspect `enabled`, `folder`, `loaded`, `missed` and `errors` (use the configured port if different). `loaded` is the set of file base names successfully applied at least once during this enabled session. `missed` is the set of indexed PNG base names not successfully applied yet, including files never encountered and files rejected for size/decoding; `errors` records observed failures by texture name. A name may have both a successful application and an error when multiple different sprites use the same atlas name. POST JSON `{"enabled":false}` to restore current bindings and free the replacement sprites/textures; POST `{"enabled":true}` reloads the folder and enables again. As with the existing API, non-GET requests need `X-PartnerLive2D: 1`. The toggle is in-memory and does not change settings.ini. Editing PNGs requires disable then enable to reload.

## Scope and limits

- Managed `Image.sprite` and `RawImage.texture` setters are patched only while enabled. Existing objects and `SpriteRenderer` are discovered with an enabled-only scan every two seconds; tracked objects are revisited after animation and in the canvas callback. Newly created `SpriteRenderer` objects may therefore show the stock art for up to the discovery interval. Inactive scene objects are included. No hooks, runner or scans are installed when the setting is absent.
- Same-size atlas replacement uses the sprite's unrotated, untrimmed texture rectangle. Tight, rotated or trimmed atlas packing needs a logical sprite-sized piece; because matching is by texture name, only one piece per texture name can be supplied. A single atlas PNG is preferable when several sprites share it. Duplicate texture names in different assets share one PNG and must be compatible with its dimensions.
- The feature targets `Image.sprite`, `RawImage.texture`, and `SpriteRenderer.sprite`; materials, shader texture properties, `Image.overrideSprite`, custom rendering and other texture consumers are not covered. It does not intercept game resource loads directly. `MusicSelectSprites` or other mods that also write the same component can conflict; their combined precedence has not been verified.
- The component hooks, atlas rendering, animator ordering, scene transitions, runtime restoration, memory behavior and HTTP endpoint were not tested in Unity/game. The shared specification explicitly prohibits launching or installing into the game. The Task 1 mechanism report was absent when checked, so no claim is made about AquaMai interoperability.
- The existing mod performs its Live2D Core availability check before reading settings; if the mod disables itself at startup, theme control is unavailable too.

## Verification

The only build command was `powershell -ExecutionPolicy Bypass -File "<PROJECT>\build.ps1"`; the final build succeeded with zero warnings and errors for both mod and headless projects. The headless executable passed the new settings/name checks and the existing suite, including all 8 sample models. The final changes after that run affected only Unity-specific lifecycle handling and were rebuilt; their runtime behavior remains untested as above. No game was launched and no files were copied into `<GAME_ROOT>`.


## Title layout override (t2b)

Added optional `layout.ini` in the configured theme folder. Each non-comment line is `GameObject path or unique name = scale [offsetX offsetY]`. Numbers use invariant culture; scale must be finite, positive and no greater than 100, and offsets finite. A slash path matches a case-sensitive hierarchy suffix; a bare name must be unique across scene transforms. Duplicate targets, malformed values, and invalid paths are rejected at enable. Rules are applied to local scale and position and reasserted for tracked active objects, including after canvas animation; SetActive triggers a scan, while newly created already-active objects can wait until the existing two-second scan. Ambiguous bare names and overlapping rules do not apply to affected bindings and are exposed as theme errors. Disabling restores the most recently observed game-authored values when the mod still owns the current values. Scaled target nodes use point filtering for their theme-owned replacement texture, which can affect other users of that replacement texture. Layout changes reload after disable/enable.

The title example and static visual comparison are in `<WORK>/out/t2b/`. The original replacement PNG remains 676 ? 248; `Null_Logo/Magical_01 = 2 0 19.5` compensates the logo's off-center padding, and `Null_Logo/LogoDX = 1 0 82` raises the small mark. The exact 2? mock crosses the circle and the card-touch bar; a conservatively fitted alternative is provided. These positions derive from the static mock and prefab data, not a live title test.

Changed `src/Unity/ThemeOptions.cs`, `src/Unity/ThemeOverride.cs` and `tests/Headless/ThemeChecks.cs`. The required build command succeeded for both projects with zero warnings and errors; the headless suite including layout parser checks and eight models passed. Unity activation, animation, filtering and restoration remain untested because the game was neither launched nor installed into. No `<GAME_ROOT>` files were modified.
