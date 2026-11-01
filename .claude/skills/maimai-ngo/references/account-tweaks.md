# Account tweaks

All of these change the local copy of the logged-in card's data after the game restored it; the change reaches the
server with the game's next save / the logout upload (`UpsertUserAll`). Guests are never touched. Each line or file
can be removed once the server has the new state.

## settings.ini `charaLevel = <n>` (`AccountTweaks.cs`)

At login (`PlInformationProcess.RestoreCharadata`, registered cards only) every character below level n (capped at
the game's `UserChara.LevelMax`) is raised to it; display level, reincarnation and awakening are recomputed with the
game's own tables (`ClacDisplayLevel`, `ClacReincarnationLevel`, `CalcLevelToAwake`, `UpdateTotalAwake`).
Log: `[account] player 1: <raised> of <total> characters raised to level <n>`. A later login logging "raised 0" means
the server kept the change.

## settings.ini `equipPartner = <id>` (`AccountTweaks.cs`)

Equips that partner at EVERY login (only if the game has data for the id). Use it once to equip a new custom partner
(e.g. 502), then comment it out with `#`: left on, it undoes any partner change made in the game.

## `<ASSETS>\account_edit.ini` (`AccountEdit.cs`, one shot)

```ini
# whole-line comments only (# or ;): text after a value becomes part of the value
# name: characters the game's name entry supports
name = <user name>
icon = <icon id>
plate = <nameplate id>
frame = <frame id>
# the five travel-partner slots, 0 = empty
chara = <id>,<id>,<id>,<id>,<id>
```

Applied when music select starts (by then the server copy has been downloaded; applying at PlInformation was
overwritten by the download that finishes later). Missing items / characters are added to the owned lists. At the
logout upload (`UserDataULProcess`) the file is renamed `account_edit.applied.ini`; rename it back to apply again.
Comments: `#` or `;`. The file holds account details: never commit or pack it (the release packer skips it).

## Test only: `testFirstPlay = true` (with `testInput = true`) (`TestFirstPlay.cs`)

The registered card logs in as a never-played card in the local copy (play count 0, the partner a new card starts
with), so the first-play paths run (tutorial select, first-play info windows, normal mode with the stock partner).
Every upload of the user detail goes through `VOExtensions.Export(UserDetail)`; there the real play count (plus the
plays of this credit) and the real partner are put back, so the server keeps the true values (also for AquaMai
ImmediateSave). New cards play NORMAL mode: see the `hideMatching` / PartyExec pitfall.

The first-play path also fills the travel-partner team (`CharaSlot`) with the area's starter characters. The first
version of the switch did not restore it, so one test run uploaded a stock team over the real one. The upload now also
puts back `CharaSlot` and `CharaLockSlot`. Rule for any fake-state test: restore every field the faked path can
change, not only the fields you set yourself. A lost team can be put back with `account_edit.ini` (`chara = ...`).

## Card for tests

`<PKG>\DEVICE\aime.txt` holds the card number segatools reads on a scan (Enter held 3 s). It is private: never
commit, pack, log or paste it. Test runs log in with it because guest play skips the profile, partner, collection
and tweak paths.
