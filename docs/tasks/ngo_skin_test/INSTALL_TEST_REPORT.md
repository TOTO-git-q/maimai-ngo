# NGO skin: on-device install test (2026-10-30)

Not an art sign-off. The owner has not approved these visuals yet.

## Machine and game
- This PC, maimai DX SDEZ 1.70 (Unity 2018.4.7f1), install at `<PKG>`.
- Tests ran off screen, muted, windowed 540×960, with the user's own card.

## What was installed
- Source: `TOTO-git-q/maimai-ngo-skin` at e8ebe1eb5fbd7148f385bd684573fc5df909f4dc, local copy `<ART_REPO>`.
- List builder: `tools\build_ngo_theme.py`.
  - Takes the registry selected outputs, with the 25 `current_selected` overlay items taking precedence.
  - Result: 978 PNGs in `<PKG>\LocalAssets\PartnerLive2D\theme_ngo_test\`.
  - The installed list is `_manifest.csv` and the excluded items with reasons are `_skipped.csv`, both in that folder.
- Kept original:
  - owner-rejected Star_00, UI_NOTES_Touch_01, UI_MSS_Infoicon_Utage_2P;
  - Nationaldata S/SS/SSS/SSSp (Nationaldata_Base *is* installed);
  - holds, failed and review-only items, drafts, and items with no output.
- Written straight into `Sinmai_Data\resources.assets` by `tools\ngo_patch_assets.py`. The log is `patch_all.csv`.
  - 994 Texture2D written, keeping each one's name, size and format (BC7 re-encoded with etcpak; 4 stay RGBA32).
  - 21 tight-trimmed sprites whose new art reaches past the old trim got their `textureRect` opened to the full rect.
  - 4 same-name textures of a different size were skipped; they are not the skin targets.
- The PartnerLive2D runtime theme override is turned off: `#theme = theme_ngo_test` in settings.ini.

## Results
- Batch 1 (5 textures), then all of them. In both cases the game starts, logs in, and walks attract → entry → info → mode select → area → ticket → music select (Genre / Music / Difficulty / Menu), then quits cleanly.
- Screenshots: `direct_all\` (sheet.png plus one PNG per screen) and `direct_batch1\`. Earlier runtime-override runs are in `run1\` and `run2\`.
- Not tested: popups and states not reached in these runs (other modes, event titles, ticket variants, result screens and so on).
- Not part of dot's handoff (still stock): screen backgrounds, i.e. the sky and clouds on the top screen and the area scenery in the circle.

## Backup and rollback
- Originals: `<GAME_ROOT>\backup_ngo_skin\resources.assets` and `resources.assets.resS`. SHA256 values are in `SHA256.txt` in that folder, and both were checked identical to the live files before patching.
- To roll back, close the game, copy both files back over `<PKG>\Sinmai_Data\`, and check the hashes.
- The `.resS` file was not modified. The new pixels now live inline in `resources.assets` (64 MB → 109 MB).

## Kept for now (clean up later, after sign-off)
- `<GAME_ROOT>\backup_ngo_skin\` (617 MB). Must stay while the patched file is in use.
- `theme_ngo_test\` (install copy of the PNGs).
- `tasks\ngo_skin_test\` (screenshots and logs).
