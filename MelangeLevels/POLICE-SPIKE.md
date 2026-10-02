# Spike: "the police look away" (Prestige offer)

How Schedule I 0.4.7's police decide to search, stop or chase you, and where Melange Levels can make them lenient on
IL2CPP without a Harmony patch. Claims are from the Mono decompile of the same version (`~/src/decomp/s1-mono-0.4.7f7`)
and, where marked, the IL2CPP interop (`MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll`). Nothing here has been run in
game yet; see the Testing section of `README.md`.

## Result

Implemented without patching any game method:

| Covers | Lever | Where it runs |
|---|---|---|
| Body searches (foot patrols, sentries, the "suspicious player" stop, foot checkpoints) | keep `Player.Local.CrimeData.TimeSinceLastBodySearch` at 0 every frame | `OfferActions.Tick`, from `Mod.OnUpdate`, host only |
| Curfew stops | during the hard curfew, `Player.Local.VisualState.RemoveState("DisobeyingCurfew")` | same |
| A search for you (pursuit level Investigating) | on purchase, `CrimeData.SetPursuitLevel(EPursuitLevel.None)` | `OfferActions.PoliceLookAway` |

Not covered: vehicle checkpoint searches, an Arresting/NonLethal/Lethal pursuit, a body search already under way, other
crimes committed in front of officers, and co-op clients (the host only).

## How the police work (verified in the decompile)

### Pursuit levels: `ScheduleOne.PlayerScripts.PlayerCrimeData`

- `enum EPursuitLevel { None, Investigating, Arresting, NonLethal, Lethal }` (same order in the interop, so the numbers
  0..4 that `Offers.MaxDroppedPursuit` uses are right).
- `CurrentPursuitLevel` is a FishNet SyncVar. `SetPursuitLevel(EPursuitLevel)` calls the ServerRpc
  `SetPursuitLevel_Server` (RunLocally, RequireOwnership = false); going from a level to `None` calls `ClearCrimes()`
  and removes the "Wanted" visual state.
- `OnSleepStart` does exactly `SetPursuitLevel(None); ClearCrimes();`, which is the precedent for calling a pursuit off.
- Officers give up when the level is `None`: `PursuitBehaviour.IsTargetValid()` and `VehiclePursuitBehaviour`'s target
  check both return false for `CurrentPursuitLevel == None`.
- Escalation (`UpdateEscalation`) and timeouts (`UpdateTimeout` / `TimeoutPursuit`) run only on the player's owner.
  `TimeoutPursuit` awards XP for escaping Arresting+ (20/40/60); calling a pursuit off with `SetPursuitLevel` doesn't.
- `PlayerCrimeData.Update` sets Investigating and calls `LawManager.PoliceCalled` after 3 vehicle collisions in 30 s.
- `ScheduleOne.Law.LawManager.PoliceCalled(Player, Crime)` dispatches 1-2 officers from the closest `PoliceStation`.

### Body searches: `ScheduleOne.Police.PoliceOfficer`

Three ways a search starts, and all three skip a player whose `CrimeData.TimeSinceLastBodySearch < 60`:

1. **Random investigation.** `CheckNewInvestigation()` runs every second (`InvokeRepeating` in `Awake_UserLogic...`);
   progress is updated on the server only in `UpdateBodySearch()` → `UpdateExistingInvestigation()` →
   `ConductBodySearch(player)`. Both go through `CanInvestigatePlayer(Player)`, which returns false if
   `TimeSinceLastBodySearch < 60f` (also: dead, `BodySearchPending`, any pursuit, arrested, tutorial).
2. **The suspicious-player stop.** `UpdateVision()` enables the `EVisualState.Suspicious` sightable state per player only
   when `!BodySearchPending && TimeSinceLastBodySearch > 30f`. Sighted, `NPCAwareness.VisionEvent` →
   `NPCResponses_Police.NoticedSuspiciousPlayer` → `BeginBodySearch_Networked`, only when `player.IsOwner`.
3. **Foot checkpoints.** `ScheduleOne.NPCs.Behaviour.CheckpointBehaviour.PlayerWalkedThroughCheckPoint` (server)
   returns early if `TimeSinceLastBodySearch < 60f`, a pursuit is on, or `CurfewManager.IsCurrentlyActive`.

`TimeSinceLastBodySearch` is an auto-property `{ get; set; }` on `PlayerCrimeData`, not synced: each peer adds
`Time.deltaTime` in `Update` and resets it when a search finishes (`SetBodySearchProgress`, `ResetBodysearchCooldown`).
Interop: a public `TimeSinceLastBodySearch` property with a setter (`set_TimeSinceLastBodySearch`) and the backing field
`_TimeSinceLastBodySearch_k__BackingField`. Holding it at 0 closes all three paths and changes nothing else (its only
readers are the ones above).

Rejected levers:

- `PoliceOfficer.BodySearchChance` (public get/set): overwritten by `FootPatrolBehaviour` (0.4/0.25/0.1),
  `VehiclePatrolBehaviour` (0.1) and `SentryBehaviour` (0.1/0.75) as officers change duty, applies to everyone, and
  only covers path 1.
- `PoliceOfficer.IgnorePlayers` (SyncVar, set via the ServerRpc `SetIgnorePlayers`): per officer, for all players, and
  makes officers blind to every crime (`UpdateVision` turns off crime noticing), so far more than "lenient".

### Curfew: `ScheduleOne.Law.CurfewManager`

- Constants: `CURFEW_START_TIME = 2100`, `HARD_CURFEW_START_TIME = 2115`, `CURFEW_END_TIME = 500`. `IsEnabled`
  (curfew exists, set by `CurfewInstance`), `IsCurrentlyActive` (21:00-05:00) and `IsHardCurfewActive` (21:15-05:00),
  updated in `OnUncappedMinPass`. Interop exposes `IsCurrentlyActive` and `IsHardCurfewActive`.
- Enforcement is a visual state, not a timer: `ScheduleOne.Vision.PlayerVisibility.Update()` calls
  `AddFlag_DisobeyingCurfew()` (`ApplyState("DisobeyingCurfew", EVisualState.DisobeyingCurfew)` and sets the private
  `disobeyingCurfewStateApplied = true`) when hard curfew starts, and removes it when it ends. It only re-adds the state
  when that flag is false, so removing the state from outside keeps it gone for the rest of the night.
- Noticed, `NPCAwareness.VisionEvent` → `NPCResponses_Police.NoticedViolatingCurfew`, which (only when
  `player.IsOwner` and the officer isn't `IgnorePlayers`) adds `ViolatingCurfew`, sets `Arresting` and starts a foot
  and/or vehicle pursuit. The base `NPCResponses.NoticedViolatingCurfew` (civilians) is empty.
- `EntityVisibility.RemoveState(string, float)` is a ServerRpc with RunLocally; its logic is a no-op for a missing
  label. Interop: `RemoveState(string, float)`, `GetState(string)` on `EntityVisibility`; `Player.VisualState` is a
  `PlayerVisibility`, `Player.CrimeData` a `PlayerCrimeData`, `Player.Local` static.
- The offer always ends at 06:00, after the curfew's 05:00 end, so there's never a curfew state to put back.

### Vehicle checkpoints: `ScheduleOne.Police.RoadCheckpoint` + `CheckpointBehaviour`

The car search (`CheckpointBehaviour.StartSearch` → `OnActiveTick` → `ConcludeSearch`, which on illicit items sets
`Arresting` and begins a foot pursuit for `Player.Local`) has no player-side gate to hold open. Covering it would need a
Harmony prefix on a multi-line method such as `CheckpointBehaviour.RpcLogic___ConcludeSearch_2166136261` or
`RpcLogic___StartSearch_3694055493` (not tried; neither is hooked by Melange Core). Left out on purpose.

## Time: `ScheduleOne.GameTime.TimeManager`

`CurrentTime` is 24-hour hhmm; `ElapsedDays` goes up at midnight (2359 → 0 in the minute tick, and when a time skip
wraps past midnight). So "until the next 06:00" is `ElapsedDays * 1440 + minutes(CurrentTime)` compared with a saved
absolute minute (`MelangeLevelsData.LenientUntil`). Time stops at 04:00 until the player sleeps; sleeping ends any
pursuit (`OnSleepStart`) and `SleepController.WakeTime = 700` (skipped to with `TimeManager.SkipToTimeAndSync`), so the offer ends with the night.

## IL2CPP notes

- No Harmony patch, so the inlining problem doesn't arise. Everything is property setters and public methods called
  through the interop, from a per-frame `MelonMod.OnUpdate` (a game-time check once a second, the work every frame
  only while the offer is on).
- Reads of `TimeSinceLastBodySearch` inside game code may be inlined to a field read on IL2CPP; the setter writes that
  same backing field, so it's covered either way.
- Melange Core hooks only `LevelManager.RpcWriter___Server_AddXP_3316948804`, `DialogueController_Oscar.ModifyDialogueText`,
  `Cart.GetPriceSum`, `PhoneShopInterface.GetOrderTotal` and `DeliveryShop.GetCartCost`; none are touched here.

## Co-op

The search and curfew notices that matter run on the player's owner (`player.IsOwner` in `NoticedSuspiciousPlayer` and
`NoticedViolatingCurfew`), and `TimeSinceLastBodySearch` is per peer. The offer is bought and applied on the host, for
`Player.Local`, so it covers the host only. A client would have to apply the same two levers on its own peer.

## Unverified in game

- That holding `TimeSinceLastBodySearch` at 0 stops all searches on IL2CPP (code paths identical; not run).
- That removing "DisobeyingCurfew" isn't re-added by anything else and officers don't notice it within the frame it
  exists.
- That officers already dispatched for an Investigating pursuit leave once it's set to None, and the HUD clears.
- Saving and loading `lenientUntil` through S1API.
