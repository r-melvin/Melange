# Melange Mixers: in-game checks

Nothing here has run in the game yet. The build compiles against 0.4.7f7 IL2CPP and the pure logic (`Logic/`) is unit
tested (`Melange.Tests/MixersTests.cs`); everything below needs the game. Each probe says what to set up, what to read in
`MelonLoader/Latest.log` (lines from this mod start `[Melange_Mixers]`), and what should happen. Run them in order: later
ones assume earlier ones passed.

Install: `MelangeCore.dll`, `MelangeMixers.dll`, S1API (the IL2CPP build the repo pins in `lib/`). Optional: Melange Levels
(not needed: the rank lock is the game's own). A fresh test save at Underlord I or above saves time; otherwise grant XP.

## Probes

### P1. Registration and shop
- Set up: load any save.
- Log, at load: `Two-Ingredient Mixer: registered at $X (Mk2 $Y), unlocks at rank 8 tier 1` (and Three/Four with ranks 9
  and 10), then `...: in 1 shop(s) (<Oscar's shop name>)` for each.
- Expect: Y is the Mk2's price (wiki: $2,000). X = 3Y, 6Y, 10Y rounded to $100 ($6,000 / $12,000 / $20,000 at Y = 2000).
  If `Mixing Station Mk2 ('mixingstationmk2') is not in the registry` appears, the Mk2's ID is wrong: find it (e.g.
  `Registry.LogOrderedUnlocks`) and change `Tiers.Mk2Id`.
- Visit Oscar (warehouse, Dark Market unlocked). Below Underlord the three machines show locked with the rank; at or above
  each rank, buyable. Check the price shown matches X (and the levels spoke's bulk discount if Oscar is in its list).

### P2. Rank-up screen
- Set up: a save at Block Boss V with XP just short of Underlord I; grant enough XP to cross.
- Expect: the rank-up screen lists "Two-Ingredient Mixer" with the Mk2's icon. Same at Baron I and Kingpin I.

### P3. Placing a machine
- Set up: buy one of each, place them in an owned property.
- Log, per machine (on every player's game): `<Tier> <guid>: N extra mixer slot(s); Mk2 base B min/item, batch M`.
- Record B and M: they are the Mk2's real prefab values (the code defaults are 15 and 10; the web says the Mk2 batches 20).
  Both are unverified and the timing settings are multiples of B.
- Expect: the machine looks like a Mk2, scaled-down Melange model inside its footprint if `scripts/art/out/mixerN.mesh.json`
  was present at build time (it was for this build), otherwise the Mk2 tinted teal / purple / gold. Check:
  - the model faces the side the player works from (if it faces away, set `Looks.ModelYaw` to 180);
  - the Mk2's screen, light and clock still show (they are left visible over the model);
  - interacting, the management clipboard and picking up all work.

### P4. Station screen: slots, preview, Begin
- Set up: open a Three-Ingredient Mixer. Put OG Kush (unpackaged) in the product slot, Cuke in slot 1.
- Expect: two more mixer slots numbered 2 and 3 next to slot 1 (layout unverified: they are placed after slot 1 in its
  layout group, or 1 slot-height apart below it if it has none). Slot numbers in the top-left corner of each slot.
  Instruction reads "Fill mixer slots 1 to 3 (mixed in that order)"; preview is empty; Begin is disabled.
- Fill slots 2 and 3 (e.g. Banana, Paracetamol), by dragging and by shift-click quick move.
- Expect: preview shows the result of OG Kush + Cuke, then + Banana, then + Paracetamol (compare with three Mk2 passes or a
  calculator such as schedule1-lab.com with the same order); instruction reads "Mixed in slot order: 1 > 2 > 3"; Begin
  enabled. Swap slots 2 and 3: the preview changes (order matters).
- Put a modded product (e.g. Drug Expansion MDMA) in the product slot: "This mixer takes the base game's products only",
  Begin disabled.

### P5. Batch size is the smallest stack
- Set up: product 20, slot 1: 20, slot 2: 7, slot 3: 20 (Mk2 batch M from P3 at least 7).
- Press Begin. Log: `Three-Ingredient Mixer: mixing 7x ogkush + cuke + banana + paracetamol`.
- Expect: 7 taken from product and slot 1 at once (the game's own take); slots 2 and 3 keep their stacks, locked (lock icon,
  can't be dragged out) for the whole mix. Mk2 screen shows "7x" and the expected output's icon (black with "?" if new).
- Time: minutes remaining at start = 7 x round(B x 1.25). Change `ThreeIngredientTime` in
  `UserData/MelonPreferences.cfg` [MelangeMixers] mid-mix (or before the next): the countdown follows.

### P6. Known result: output and nothing else
- Set up: a chain whose result is an existing product (do the three single mixes on a Mk2 first so the product exists).
- Let P5's mix finish.
- Log (host): `Three-Ingredient Mixer: 7x OG Kush + cuke + banana + paracetamol -> <name>`.
- Expect: 7 of that product in the output at the input's quality; slots 2 and 3 each lose 7 and unlock; no naming prompt; no
  new product in the products app; and no new mix recipe (check in a recipe viewer such as ExpandRecipe, or the save's
  `Products.json` MixRecipes: the count must not grow).

### P7. New result: naming
- Set up: a chain whose final result is new, where product + slot-1 mixer alone is a known product (case A), and one where it
  is also new (case B).
- Finish the mix with the station screen open, and again with it closed then opened.
- Log: `...: new mix, waiting for a player to name it` (host), then after naming `...: named '<name>' (<id>): N effects`.
- Expect in both cases: one naming prompt, showing the final effects and value (not product + slot-1 mixer's); the game's own
  prompt, if it flashes up in case B, is replaced in the same frame or the next. Confirm a name: +80 XP once; the new
  product appears in the products app with that name and the final effects; output appears; the products app gains exactly
  one product (no intermediates). Case B especially: product + slot-1 mixer must NOT have been created.
- Save, quit to menu, reload WITHOUT Melange Mixers: the named product is still there, sellable (vanilla data only).

### P8. Chemists
- Set up: assign a Two-Ingredient Mixer to a chemist; stock product and both mixer slots by hand; set the start threshold.
- Expect: the chemist starts it like a Mk2 (log `mixing ...` from the host), using one of their four stations. With slot 2
  empty the chemist does not start (the station's batch cap is held at 0) and does not loop on start/refund (no repeated
  `start refused` lines). With a known result, output appears and the chemist moves it to its destination. With a new
  result the station waits (like the vanilla station) until a player opens it and names the product.
- Handlers: route mixers into the machine with per-slot filters set (slot 1 Cuke, slot 2 Banana): each lands in its slot.
  While mixing, a handler bringing more of slot 2's mixer must not leave slot 2 unlocked for more than half a second.

### P9. Save and load mid-mix
- Set up: start a mix (P5), save while it runs, quit to menu, reload.
- Log: `<Tier> <guid>: extra slots restored; chain cuke + banana + paracetamol`.
- Expect: slots 2 and 3 show their stacks (still locked), the countdown continues, and on finishing the output is as in P6.
  Also save with an idle machine holding mixers and filters in slots 2-3: they come back with their filters.
- Pick a machine up (empty it first: the game refuses while slots hold items) and save: its record is pruned (check the save
  folder's Melange Mixers data: no entry for its GUID).

### P10. Without the mod
- Set up: a save with placed machines, then remove `MelangeMixers.dll` and load.
- Expect: the game logs that it could not load those grid items and skips them; the save otherwise loads; products named by
  the machines survive (P7); no mix recipe was ever recorded for more than one mixer, so plain mixes on a Mk2 behave as
  before. The machines and anything in them are lost (documented in the plan).

### P11. Co-op (if a second player is available)
- Both players need the mod and the same settings.
- Client places a machine: the host and client both log the extra-slot line. Client starts a mix: the host logs nothing at
  start but its tick records the chain; the mix finishes on the host. Client names a new result: one product, one +80 XP for
  the client. A player joining mid-mix sees the extra slots filled and locked.

## Assumptions not verified without the game

1. **Mk2 item ID is `mixingstationmk2`** (from FurnitureDelivery's strings). P1 shows it.
2. **Mk2 prefab values** (minutes per item, batch size) are read at runtime; the docs' 15/20 are unverified. P3.
3. **Oscar's shop** is found through the dark market's Oscar (`DarkMarket.Oscar.ShopInterface.ShopName`), falling back to the
   shop selling `brickpress`. P1.
4. **Patch targets fire on IL2CPP**: `MixingStation.InitializeGridItem` (postfix; virtual, multi-line),
   `RpcWriter___Server_SendMixingOperation_2669582547` (prefix; multi-line, the hub hooks XP the same way) and
   `RpcReader___Server_TryCreateOutputItems_2166136261` (prefix; called only through FishNet's delegate, so it can't be
   inlined). It is assumed FishNet runs a host's own ServerRpc through the reader. If the finish prefix never fires, P6 would
   show the vanilla output (product + slot-1 mixer) and a recorded recipe: stop and report. If the writer prefix never fires,
   the log has no `mixing` line and slots 2-4 are never locked or used.
5. **Extra slots sync**: they are appended to `ItemSlots` (indexes 3+) at placement on every player's game, before the host
   sends slot data to a late joiner (BuildableItem.OnSpawnServer sends the init first). P11.
6. **Station batch cap trick**: `MaxMixQuantity` is set per station to the smallest extra stack (0 when not ready), so the
   game's `GetMixQuantity`/`CanStartMix` (and chemists, and the Begin button) count the extra slots without a patch. If another
   mod rewrites `MaxMixQuantity` after placement (Production Expansion Reborn patches Awake, before us, which is fine), the
   two will fight. P5, P8.
7. **Speed**: the station's own `MixTimePerItem` = round(Mk2's x multiplier), whole minutes (a 1.25x machine with B = 15 takes
   19 a unit). Production Expansion Reborn's time patches apply on top (it treats the machines as Mk2s); completion follows
   the game's own `IsMixingDone`, so it stays consistent with them.
8. **Locks**: the extra slots are locked with the game's networked slot lock (owner: the station) from start to finish.
   A handler reserving the same slot replaces the lock; the host's half-second tick puts it back. P8.
9. **Naming**: the game's naming prompt is replaced by setting `NewMixScreen.onMixNamed` to ours while a machine is naming;
   the product is created with `ProductManager.CreateX_Server` from the final effect IDs and
   `<Type>Definition.GetAppearanceSettings`, the ID made as the game makes it (made unique if taken). Recorded nowhere else.
   S1API's custom product kinds are refused (v1), so its naming interception is not needed. P7.
10. **ItemSet round trip**: extra slots are saved with the game's own `ItemSet` JSON and read back with
    `JsonUtility.FromJson(json, Il2CppType.Of<ItemSet>())`. P9.
11. **UI layout**: the extra slot copies, their numbers (a copy of the preview label, 18pt) and the instruction text are
    placed blind. P4: check nothing overlaps the Begin button or the preview.
12. **Model**: origin bottom centre and front +Z per `scripts/art/MODELS.md`; scaled down to the Mk2's footprint (the models
    are drawn up to 4 x 2 cells, but a machine is the Mk2's placed object and keeps its footprint; full size needs a placed
    object per tier). The placement ghost and the item icon are the Mk2's. P3.
13. **Prices** are a proposal: 3x / 6x / 10x the Mk2. Tune in `Logic/Tiers.cs`.
14. **Settings** live in `UserData/MelonPreferences.cfg` under `[MelangeMixers]` (display name "Melange Mixers"):
    `TwoIngredientTime` 1.0, `ThreeIngredientTime` 1.25, `FourIngredientTime` 1.5, clamped to 0.1-10.

## Known limits (v1)

- Vanilla products and ingredients only (IDs with `:` or `/` are refused).
- No recipe for multi-step products in recipe viewers (by design: the game's recipes can't hold more than one mixer).
- One naming gives 80 XP, where chaining could give up to 80 per new intermediate.
- Removing the mod loses the machines and their contents; empty them first.
