# Contributing to Melange

Melange is a set of separate mods for **Schedule I** (IL2CPP, MelonLoader 0.7). Each one touches your saves or
the game's menus, so the bar for a change is **evidence from a running game**, not plausibility. Please read
this before opening an issue or a pull request.

## Before you open anything

- **Is it this mod?** Remove the mod's DLL from `Mods/` and try again. If the problem stays, it belongs to the
  game or to another mod.
- **Is it a missing game method?** A `MissingMethodException` or `MissingFieldException` from another mod
  after a game update is usually [Polyfill](https://github.com/DooDesch-Mods/ScheduleOne-Polyfill)'s area, or
  that mod's author's.
- **Are you on the latest release?** Check the mod's version in its load line in `MelonLoader/Latest.log`
  against the [releases](../../releases).

## Issues

Use the issue form. It asks for:

- the Schedule I version and branch, the mod's version, the MelonLoader version, and your OS;
- what you did, what you expected, and what happened;
- `MelonLoader/Latest.log` attached as a file. A pasted excerpt is not enough: the start-up lines are where
  most answers are.

One problem per issue. For **Paper Trail**, never attach a save or snapshot unless asked: they contain your
whole game. A screenshot of the save screen and the log are usually enough.

## Pull requests

1. **One change per PR**, for one mod. Say which mod in the title.
2. **Say why, with sources.** If the change works around game behaviour, the comment above it names the cause,
   with file and line references in the game (decompiled source is fine), the way the existing code does.
3. **Evidence from a running game** in the PR description: the game build, what you did in game to exercise the
   change, and the log lines or a screenshot before and after.
4. **Saves are sacred.** Anything that writes, moves or deletes save data must be unable to lose a save: write
   to a temporary name and rename, keep a copy before replacing anything, and never delete what you did not
   create. Say in the PR how you tested a failure halfway through.
5. **Look like the game.** UI is built from the game's own pieces (Paper Trail copies them from the game's own
   screens) and behaves like the game's menus: Escape goes back, no extra close buttons.
6. **Cheap on hot paths.** Per-frame code does no reflection lookups, LINQ, allocations or logging per call.
   Disk work runs off the main thread.
7. **No new dependencies.** Each mod needs only MelonLoader and stands alone; shared helper code is compiled
   into each mod, not shipped as a library.
8. **Debugging aids go behind the mod's dev define** (`-p:Dev=true`). The release build must contain nothing a
   player can trip over.
9. **Update the README** for anything a player would notice, in the same PR. Don't bump the version: that
   happens at release.

### Building

See [Building](README.md#building) in the README. CI cannot build the mods (they compile against the game's own
generated assemblies, which can't be published), so build and test locally before opening the PR.

## Licence

By contributing you agree that your contribution is licensed under the repository's [MIT licence](LICENSE).
