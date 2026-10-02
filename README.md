# Melange

A collection of mods for **Schedule I** (IL2CPP, MelonLoader 0.7) by r-melvin. Each mod is its own
project and its own DLL, and none needs another: install only the ones you want. Helper code two mods
share is compiled into each of them, never shipped as a separate library.

| Mod | What it does | Status |
|---|---|---|
| [Paper Trail](PaperTrail/) | Rolling auto-saves every 2 in-game hours at a safe moment, a snapshot of every save, and a save/load screen from the main menu and pause menu. | 0.3.0 |

Looking for the fixes for other people's mods? Those are
[Schedule I Unofficial Mod Fixes (S1UMF)](https://github.com/r-melvin/S1UMF).

## Paper Trail

A rolling auto-save and a save manager that looks and feels like part of the game.

- **Auto-save** every 2 in-game hours (keeps the last 10), only at a safe moment: not mid-deal, mid-fight or while
  the game is saving.
- **Key-moment saves** when a quest is completed, a dealer is recruited, a property or business is bought, you
  rank up, the cartel situation changes, or a new area or supplier is unlocked. (Customer unlocked is off by default.)
- **Main menu:** *Continue* loads your last played save straight away; *Load Game*, below it, opens the load screen.
- **Save and load screens** from the main menu's *Load Game*, the pause menu's *Save* and *Load* buttons and the
  safehouse *Intercom Save Point*. Saving offers Save, Overwrite, Rename, Pin and Delete; loading offers Load, Rename,
  Pin and Delete, for any campaign (`<` and `>` switch between them, in a game too), and at the main menu the game's
  own *Import* and *Export* of a slot. Escape closes them, as it does the game's own menus. Built from the game's own
  panels and buttons.
- **Archived campaigns:** when a new game or an import replaces a campaign in its slot, that campaign's saves are
  archived instead of mixed into the new one's. Archived campaigns appear on the load screen after the slots; loading
  one puts it back in its slot and archives whatever was there, so nothing is lost.
  Holding **Shift** while the game starts skips "continue last game on start-up".
- **Follows the game's 60-second save wait** after a save-point save or a load.
- **Safeguards:** a copy is kept before a save is loaded into a newer game version or with a changed mod list.
  Each snapshot is checked for looking incomplete (far fewer or smaller files than the one before it) and is then
  marked and not allowed to push older saves out.
- **Steam Cloud:** snapshots are worked on locally and mirrored to a Steam Cloud folder, so they follow you between
  computers. Turn it off with *Sync snapshots with Steam Cloud*.
- **Light on the game:** copying, zipping and syncing run on a low-priority background thread.

Settings are in `UserData/MelonPreferences.cfg` and in the Mod Manager phone app under **PaperTrail**:
continue last game on start-up, auto-save on/off, interval and count, each key-moment toggle and count,
manual-save limit (default 30), safeguard copies kept, and cloud sync.

### Your snapshots without the mod

Every snapshot is a standard zip in the game's own export layout (`SaveGame_N/...`), so removing Paper Trail
loses nothing. Snapshots are in `UserData/PaperTrail/Snapshots/Slot_N/<snapshot>/save.zip`, archived campaigns' in
`Snapshots/Archive/<date>-slot<N>-<organisation>/` (all mirrored in `Saves/<SteamID>/PaperTrail/`). To use one, use
**Import** on the zip from the game's save slot screen, or unzip it over the save folder yourself.

### Installing

Needs MelonLoader 0.7.x. Put `PaperTrail.dll` in the game's `Mods` folder.

## Building

`dotnet build -c Release -p:GameDir="<Schedule I folder>"` (or set `S1_GAME_DIR`). The game needs
MelonLoader installed and run once, so the interop assemblies exist. Shared build settings are in
`Directory.Build.props`. Add `-p:Dev=true` to compile in Paper Trail's test driver (never in releases).

## Releasing a mod

Each mod is released on its own, tagged `<mod-name>-v<version>` (for example `paper-trail-v0.1.0`). The version is
the one in the mod's `MelonInfo` line. The mods build against the game's own generated assemblies, which can't be
stored in a public repo, so the release is built on your machine:

```
export S1_GAME_DIR="/path/to/Schedule I"
scripts/release.sh PaperTrail --dry-run   # build and package only
scripts/release.sh PaperTrail             # tag, push and publish the GitHub release
```

It refuses to run with uncommitted changes, off `main`, out of sync with origin, or if the tag already exists.
A new mod joins the pipeline by having its own folder with `<Mod>/<Mod>.csproj` and a `MelonInfo`. CI
(`.github/workflows/check.yml`) checks that every mod declares a version and that a pushed tag matches it.

## Licence

MIT - see [LICENSE](LICENSE).
