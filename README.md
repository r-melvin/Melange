# Melange

A collection of mods for **Schedule I** (IL2CPP, MelonLoader 0.7) by r-melvin. Each mod is its own
project and its own DLL, and none needs another: install only the ones you want. Helper code two mods
share is compiled into each of them, never shipped as a separate library.

| Mod | What it does | Status |
|---|---|---|
| [Paper Trail](PaperTrail/README.md) | Save/load manager: rolling auto-saves at safe moments, every save kept and loadable from the main menu or pause menu, and backups of each slot. | 0.4.0 |
| [Melange Core](MelangeCore/) | The hub the Melange spokes build on: shared game events (scenes, save loaded, XP, rank-ups, days), an event bus, rank-up screen entries, employee slots, a shop price pipeline, a manager role, host and version checks. No gameplay of its own; not needed by Paper Trail. | 0.3.0 (unreleased) |
| [Melange Levels](MelangeLevels/) | Rewards for the late ranks, where the game unlocks nothing: extra employees at a property, bulk discounts, underbosses to hire (with the cartel mod), and Prestige from Kingpin on, in a Connections phone app. Needs Melange Core. | 0.1.0 (in development) |
| [Melange Mixers](MelangeMixers/) | Mixing stations that take two, three or four ingredients in one pass, unlocked by the late ranks. Employees run them like the Mk2. Needs Melange Core. | 0.1.0 (in development) |
| [Melange Sewer](MelangeSewer/) | A story for the sewers: Jerry's job, Jen's key, the Sewer King and the Goblin, and what you do with the King when you find him. Needs Melange Core. | 0.1.0 (in development) |
| [Melange Hydro](MelangeHydro/) | Late-game hydroponic trays and aeroponic towers where each hole is a pot, botanist training, a pump to the tap and Grow N Juicer nutrient. Needs Melange Core. | 0.1.0 (in development) |
| [Melange Smuggling](MelangeSmuggling/README.md) | Dafydd "Turnip Night" Seabiscuit's speedboat at the Docks: bulk orders that sail at night with or without your product, and imports on the way back. Oscar introduces you. Needs Melange Core. | 0.1.0 (in development) |
| [Melange Psychedelics](MelangePsychedelics/README.md) | Toad and LSD: toads from the pond (mind the wildlife officer) or Randy's night stall, terrariums, ergot from Fungal Phil, Ana Slughin's LSD and blotter designs as brands. Needs Melange Core. | 0.1.0 (in development) |

Looking for the fixes for other people's mods? Those are
[Schedule I Unofficial Mod Fixes (S1UMF)](https://github.com/r-melvin/S1UMF).

## Paper Trail - save/load manager

A save manager that looks and feels like part of the game: rolling auto-saves and key-moment saves, every save
kept as a snapshot you can load from the main menu or the pause menu, and backups of each slot before a new game,
an import or an older save replaces it. Snapshots are standard zips in the game's own export layout and follow
you through Steam Cloud.

**[What it does, with screenshots, settings and install steps](PaperTrail/README.md)**

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

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports use the issue form; pull requests need evidence from a running game.

## Licence

MIT - see [LICENSE](LICENSE).
