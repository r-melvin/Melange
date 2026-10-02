# Melange Core

The hub the Melange spokes build on. It has no gameplay of its own. It owns everything that must exist only once in the
game, a Harmony patch on a game method or a game event subscription, and offers it to spokes as events and services.
Paper Trail doesn't use it.

## For players

Install `MelangeCore.dll` in `Mods/` alongside any Melange spoke that needs it (each spoke's page says so). It needs
MelonLoader 0.7.x. On its own it changes nothing.

## For spoke authors

```csharp
[assembly: MelonAdditionalDependencies("MelangeCore")]   // the assembly name, not "Melange Core"

public override void OnInitializeMelon()
{
    if (!Melange.Core.Core.Require(new Version(0, 3, 0), out string problem)) { LoggerInstance.Error(problem); return; }
    Events.Subscribe<SaveLoaded>(_ => /* the save is fully loaded */ { });
}
```

Rules: a spoke never patches a game method the hub hooks, never references another spoke (they talk through hub
events), and keeps its pure logic in a `Logic/` folder, which `Melange.Tests` compiles and CI runs. On IL2CPP, never
Harmony-patch a one-line method: it gets inlined and the patch silently never runs (seen with
`LevelManager.RpcLogic___AddXP` and `ShopListing.Price`).

| Service | What it gives |
|---|---|
| `Events` | Typed publish/subscribe. A throwing handler is logged and the others still run. |
| Game events | `MainSceneLoaded`, `SaveLoaded` / `SaveLeaving` (entered / about to leave a save, each with `IsHost`), `MenuLoaded`, `XpAwarding` (change the amount) / `XpAwarded` (on the awarding peer), `TierUp`, `TierReached` (once per tier crossed), `DayPassed`, `WeekPassed` |
| Shared events | `UnderbossCandidateUnlocked`, `BulkDiscountStepChanged`, `PrestigeChanged`, `ManagerLoyaltyChanged` |
| `Host.IsHost` | Only the host (or single player) changes the world; co-op clients follow the game's networking |
| `RankUpScreen.Register(rank, tier, title, icon)` | An entry on the game's rank-up screen, re-added every load |
| `EmployeeSlots.SetBonus(source, propertyCode, n)` | Extra employee capacity with idle points; set again after each load (the game doesn't save capacity). `CanExtend` says whether a property has idle points to copy |
| `Prices.Register(PriceModifier)` | Per-listing price multipliers. The hub is the only writer of `OverridePrice`/`OverriddenPrice`, keeps and restores the original, and re-prices after loads |
| `OrderTotals.Register(OrderModifier)` | Whole-order multipliers (shop checkout, supplier phone orders, the delivery app) |
| `Managers` | The manager role (site, daily chores, cut, loyalty): subclass `Manager`, `Managers.Register`; chores run each in-game day on the host |

Saving: each spoke owns one S1API `Saveable` (inheriting it directly: S1API registers only direct subclasses) with a
prefixed type name (e.g. `MelangeLevelsData`), because S1API names saves by short type name. It implements
`IResettableSaveData` and calls `SaveData.Track(this)` in its constructor; the hub calls its `ResetToDefaults()` on
returning to the menu: S1API keeps one instance per type for the session and only sets the fields a save has
files for, so without it a save with no data for the spoke would inherit the previous save's.

Versioning: additions bump the minor version; anything a spoke could break on bumps the major. `Core.Require` accepts the
same major and at least the minor a spoke was built against.
