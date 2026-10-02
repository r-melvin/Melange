# Melange

A collection of mods for **Schedule I** (IL2CPP, MelonLoader 0.7) by r-melvin. Each mod is its own
project and its own DLL, and none needs another: install only the ones you want. Helper code two mods
share is compiled into each of them, never shipped as a separate library.

| Mod | What it does | Status |
|---|---|---|
| [Paper Trail](PaperTrail/README.md) | Save/load manager: rolling auto-saves at safe moments, every save kept and loadable from the main menu or pause menu, and backups of each slot. | 0.4.0 |

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
