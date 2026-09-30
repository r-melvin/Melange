# Melange

A collection of mods for **Schedule I** (IL2CPP, MelonLoader 0.7) by r-melvin. Each mod is its own
project and its own DLL, and none needs another: install only the ones you want. Helper code two mods
share is compiled into each of them, never shipped as a separate library.

| Mod | What it does | Status |
|---|---|---|
| [Paper Trail](PaperTrail/) | Rolling auto-saves every 2 in-game hours at a safe moment, a snapshot of every save, and a save/load screen from the main menu and pause menu. | In development |

Looking for the fixes for other people's mods? Those are
[Schedule I Unofficial Mod Fixes (S1UMF)](https://github.com/r-melvin/S1UMF).

## Building

`dotnet build -c Release -p:GameDir="<Schedule I folder>"` (or set `S1_GAME_DIR`). The game needs
MelonLoader installed and run once, so the interop assemblies exist. Shared build settings are in
`Directory.Build.props`.

## Licence

MIT - see [LICENSE](LICENSE).
