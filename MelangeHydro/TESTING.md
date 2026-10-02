# Melange Hydro: in-game checks

Registration and placement have run in the game (Results so far, at the end); the rest has not. The build compiles against 0.4.7f7 IL2CPP and the pure logic (`Logic/`) is unit
tested (`Melange.Tests/HydroTests.cs`); everything below needs the game. Each probe says what to set up, what to read in
`MelonLoader/Latest.log` (lines from this mod start `[Melange_Hydro]`), and what should happen. Run them in order: later
ones assume earlier ones passed.

Install: `MelangeCore.dll` (0.2 or later), `MelangeHydro.dll`, S1API (the IL2CPP build the repo pins in `lib/`). For the
probes set `Verbose = true` in `UserData/MelonPreferences.cfg` under `[MelangeHydro]` (it logs every top-up, curing step
and bud-site change). A test save at Baron III or above (Kingpin III for P9) with a few thousand dollars cash saves time.

## Which placement path is active

**Individual is the default** (`Placement = "Individual"`): every growing site is its own item (Hydro Tray Section,
Aeroponic Tower Section), bought and placed on its own; each draws its own share of the frame (a length of tray on legs, or
a short column with one port). The frames (Hydro Tray, 5 sites; Aeroponic Tower, 12 sites) are registered either way, so a
save that has them loads, but they are only sold with `Placement = "Grouped"`, the experimental path where one placement
creates the frame's other holes (P11). The grouped path is unproven; switch to it only to run P11, with no game loaded.

## Probes

### P1. Registration, shops, rank lock
- Set up: load any save.
- Log, at load: `Hydro Tray Section: registered ($450, from 'growtent', unlocks at rank 8 tier 3)`, the same for
  Aeroponic Tower Section ($900, rank 9 tier 3), Hydro Tray, Aeroponic Tower, `Reservoir Pump: registered ($1,200, from
  'potsprinkler', ...)` and `Grow N Juicer: registered (+0.15 quality, $75)`; then `...: in 2 shop(s)` for each item for
  sale; then `N hole(s) in this save`.
- Failure lines to look for: `the Grow Tent ('growtent') is not in the registry` (wrong donor ID: find the tent's ID and
  change `Units.DonorId`), the same for `potsprinkler` (`Units.PumpDonorId`) and `fertilizer` (`Units.FertiliserId`);
  `no hardware store found` (the shop codes `handy_hanks` / `dans_hardware` are wrong: list `ShopInterface.AllShops`
  codes and fix `Items.HardwareShopCodes`).
- Expect: at Handy Hank's and at Dan's Hardware, the sections, pump and nutrient listed; locked with the rank shown below
  their rank, buyable at or above it. With `Placement = "Individual"` the two frames are not listed.

### P2. Rank-up screen
- Set up: a save just short of Underlord III; grant XP to cross it. Then Baron III, Kingpin III.
- Expect: "Hydroponics (trays, pump, botanist training)" with the section's icon at Underlord III; "Aeroponics (towers,
  botanist training)" at Baron III; "Grow N Juicer (premium nutrient)" at Kingpin III.

### P3. Placing a section (Individual)
- Set up: buy one Hydro Tray Section and one Aeroponic Tower Section; place both in an owned property (any 2x2 floor spot:
  a section takes the Grow Tent's footprint).
- Log (every player's game, with `Verbose` on): `Hydro Tray Section <guid>: drain 0.139/h of 5, yield x1`;
  `Aeroponic Tower Section <guid>: drain 0.208/h of 5, yield x1.5` (5 is the tent's capacity read from the game; the drain
  is capacity / 36 and / 24 hours. If capacity is not 5, drain = capacity / hours still holds.)
- Log (host, within a second): `...: grow medium in (20 harvests), reservoir full`.
- Expect:
  - the tent is gone from view; a white channel on legs with a hole at 0.60 m (hydro), a column with one port at about 1 m
    (aero); no grow light needed (the tent's built-in light still works: check the pot's light reading in its UI).
  - the pot UI shows soil full (Extra Long-Life Soil) and water full.
  - interacting (sow, additives, water can) and picking up work; picking up returns a Hydro Tray Section, not a Grow Tent.
- With Production Expansion Reborn installed: the drain is still 0.139/h (ours runs after its scaling). If not, its
  postfix runs later than `Priority.Last`: record what it is.

### P4. Growth speed
- Set up: OG Kush in a hydro section, a second in an aero section, a third in a vanilla Grow Tent, all at 20 C or cooler,
  watered, no other light. Note the in-game time when sown.
- Expect fully grown after about: vanilla tent 9 h / 1.333 = 6 h 45 m; hydro 9 h / (1.333 x 1.15) = 5 h 52 m; aero 9 h /
  (1.333 x 1.35) = 5 h 0 m. (Assumes the tent's speed multiplier is 1.333 and its built-in light gives exposure 1.0; with
  Production Expansion Reborn the tent's own figure changes, the 1.15 / 1.35 ratio should not.)
- Warm room: the hydro factor multiplies the game's temperature factor (1.0 at 20 C up to 1.5 at 40 C).

### P5. Reservoir and hand top-ups
- Set up: a hydro section with a plant, reservoir full, no pump. Pass 18 in-game hours.
- Expect: the water bar at about half (0.139/h x 18 = 2.5 of 5); after 36 h, empty and growth stops (as vanilla at zero).
- Botanist: see P8 (only trained botanists may tend sections). When a trained botanist waters a section, log (host)
  `...: topped up by a botanist to 9x%`, and in co-op the client's water bar jumps at the same moment (the game itself never
  sends a botanist's watering to clients; this spoke does for holes).

### P6. Pump
- Set up: place a Reservoir Pump within 10 m of the property's tap (most properties have one; scene names "Tap" /
  "Tap (1)"), and a few sections within 15 m of the pump, reservoirs below 90%.
- Log: `pump <guid>: hosed to the tap D m away`; with Verbose, every 2 real seconds of passing time
  `pump <guid>: X L over N min into K reservoir(s)`.
- Expect: a dark green hose from the pump's spigot to the tap. Reservoirs fill at 0.5 L per in-game minute in all,
  emptiest first (a dry 5 L reservoir is full in 10 in-game minutes if it is alone), and stop at full; those at or above
  90% are left alone. The player can still use the tap for a watering can while the pump runs.
- Place a pump with no tap within 10 m: log `pump <guid>: no tap within 10 m; ...` once; it does nothing. Move it nearer:
  within 15 real seconds it finds the tap.
- Save and reload: the same tap (the save keeps its position), hose redrawn.

### P7. Yield
- Set up: OG Kush in a hydro section, in an aero section, and in an aero section with PGR; harvest by hand and count buds.
- Log (aero, every game): `Aeroponic Tower Section <guid>: plant has 24 bud sites` (16 + 8; coca 20 + 8 = 28).
- Expect buds: hydro 12 (a vanilla Grow Tent gives 8); aero 18; aero + PGR 24 (the cap is now 24). Coca: aero 18, aero +
  PGR 27.
- The extra buds look like the plant's own, spread around the stem; clicking each harvests one. With a botanist
  harvesting (P8), the same counts reach the destination.
- Save with buds left on an aero plant, reload: the same buds are present (including any with index 16 or above). Log
  must NOT show `hole(s) were set up late`; if it does, the spoke's save loaded after the buildings (the
  `BeforeBaseGame` load order did not hold) and those buds were lost: report it.

### P8. Botanist training
- Set up: hire a botanist (note what Manny charges: base fee + extra), at Underlord III or above, cash on hand.
- Talk to the botanist. Expect a choice `Train in hydroponics ($X)` where X = 2 x what Manny charges now. At Baron III and
  after hydroponics, `Train in aeroponics ($Y)`, Y = 5 x. Below the rank, or once had, the choice is not shown. Co-op
  clients never see it (training lives in the host's save).
- Choose it without enough cash: he says `That course costs $X, cash up front.`; nothing taken.
- With cash: log `<name> trained in hydroponics for $X; pot limit 16`; cash drops by X; he says he can run 16 sites. The
  clipboard now accepts up to 16 assignments (24 after aeroponics). If EmployeeTweaks or Lithium set a higher limit
  first, that limit stays.
- An untrained botanist assigned a section: within a second, log `<name>: unassigned from 1 Hydro site(s) (training:
  none)` and he says `I'm not trained for hydro trays.` The assignment disappears from his clipboard. A hydroponics-only
  botanist assigned an aero section: the same, `aeroponic towers`.
- Save, reload: training kept (choices reflect it, limits re-applied, assignments of 9-16 pots intact after load).

### P9. Grow N Juicer
- Set up: Kingpin III. Buy Grow N Juicer and fertiliser.
- Expect: the nutrient applies to sections (by hand and through a pot's additive slots for a botanist) and is refused by
  vanilla pots (the game's own "not allowed" path, since only holes list it).
- Fertiliser + Grow N Juicer on a hydro section: harvested buds are Heavenly (0.95). Juicer alone: Standard (0.65).

### P10. Aeroponic curing
- Set up: an aero section with fertiliser only (grown quality 0.8, Premium). Let it finish; do not harvest. Note the
  in-game time it became fully grown (T).
- Log (each tier change): `Aeroponic Tower Section <guid>: curing Rising, Premium (0.8, grown 0.8)`, later
  `curing Rising, Heavenly (0.9xx, grown 0.8)`, then `curing Falling, Premium ...`.
- Expect, harvesting one bud at each point (or reading the log):
  | Time after T | Quality | Tier |
  |---|---|---|
  | 0 | 0.800 | Premium |
  | 6 h | 0.867 | Premium |
  | 9 h | 0.900 (crossing) | Heavenly just after |
  | 12 h to 24 h | 0.933 | Heavenly |
  | 27 h | 0.900 (crossing back) | Premium just after |
  | 36 h and on | 0.800 | Premium |
  A Standard plant (no additives, 0.5) peaks at 0.793, Premium, from about 10 h 15 m to 25 h 45 m.
- Sleep through part of it: the curve follows the clock (it is computed from T, not counted).
- Save at 15 h and reload: still at the peak (host: T is saved). Harvest the plant and sow again: the new plant starts its
  own curve when it is grown (the old T is dropped while it grows).
- Co-op: a client's own value comes from when its game saw the plant fully grown (it has no save); expect it within a
  few minutes of the host's. Harvests by botanists use the host's.

### P11. Grouped placement (experimental; `Placement = "Grouped"`)
- Set up: with no game loaded set `Placement = "Grouped"`; load; buy a Hydro Tray; place it.
- Log (host, next frame): `Hydro Tray <guid>: placed 4 more hole(s) on its tiles`, then (every game) 4 more
  `Hydro Tray Section <guid>: drain ...` lines and, with Verbose, `... hole N of Hydro Tray <guid>` for N = 1-4.
- What decides the spike:
  1. `hole N was refused by the game` instead: the game does not let code put several pots on the same tiles. Grouping
     is not possible this way; stay on Individual.
  2. The tray model shows five holes with a plant position in each; sow each hole by looking at it. Record whether the
     player's look picks the right hole (the holes' colliders are shrunk to 25 cm; if the wrong pot is picked, colliders
     need another approach).
  3. Botanists: assign all five (one click each), confirm each hole is sown, watered and harvested.
  4. A botanist's top-up of one hole fills the other four (shared reservoir); log one `topped up` line for the hole
     he watered.
  5. Pick the tray up with all holes empty: log `Hydro Tray picked up: 4 empty hole(s) removed, 0 with plants left in
     place`; one Hydro Tray back in the inventory. With a plant in a hole, that hole stays (as a section).
  6. Co-op: the client sees the same hole positions (they are numbered by GUID, which every game shares).
  7. Save, reload: no extra holes placed (log has no `placed 4 more`), same positions.
- Aeroponic Tower: the same with 11 more holes and plants at 55% size.

### P12. Saving and removing the mod
- Set up: a save with a hydro section (plant growing), an aero section (plant with 18+ buds), a pump, and a Juicer-fed
  plant. Save.
- Expect, in the save's property folder, every hole's item ID is `growtent` (not `melange_...`); the pump's is
  `melange_hydro_pump`. Log: nothing about tents; after the save, picking up a hole still returns the section (the real
  item came back at save end).
- Reload with the mod: each hole is a section again, same plant, same soil uses, same water.
- Quit, remove `MelangeHydro.dll`, load: every hole is a Grow Tent with its plant, soil and water kept; the pump is gone
  (cloned item, as expected); buds with index 16+ on the aero plant are dropped by the game. Unverified: what the game
  does with a saved additive ID it doesn't know (Grow N Juicer) on load. If that pot fails to load, the uninstall advice
  must say "harvest Juicer-fed plants first".
- Put the mod back without having saved in between and load: each hole is a section again (the spoke's record was never
  overwritten). If the game was saved while the mod was absent, the record is gone and they stay Grow Tents: expected.

### P13. Menu round trip
- Load save A (with holes and trained botanists), quit to the menu, load save B (never had the mod). Expect: no holes, no
  training, no pumps carried over from A (the spoke's data is reset at the menu).

### P14. Clipboard bulk assignment
- Set up: a trained botanist (P8; aeroponics for a 24 limit) and, in one property, a row of 4 Hydro Tray Sections placed
  edge to edge, a separate row of 3, 2 Aeroponic Tower Sections side by side, and one vanilla pot. `ClipboardBulk = true`
  (the default) and `Verbose = true` under `[MelangeHydro]`.
- Open the clipboard on the botanist, click his pot list. Expect the top-of-screen title to read
  `<the game's title> [hole: whole tray, Crouch+click: one hole, Reload: every tray] (0/24)`. With no holes in the
  property, or on another employee's list, the title is the game's own.
- Click the third section of the row of 4. Expect all four outlined as selected, `(4/24)`, log
  `clipboard: added 3 more hole(s) of a 4-hole Hydro tray`. Click any of the four again: all four deselected,
  `removed 3 more`. Hold Crouch and click one: only that one changes (the game's own click).
- The vanilla pot and a lone section: one click, one pot, as in the game.
- Limit: with a hydroponics-trained botanist (16), select 14 pots, then click a fresh row of 4: expect 2 added (the
  clicked one and its nearest neighbour), the list full at 16, and the selector closing on its
  own (the game closes a full list), the clipboard showing 16.
- Training: a hydroponics-only botanist, click an aero section with an aero neighbour: the neighbour is NOT added (log has
  no `added` line); the game still adds the clicked one and, within a second, Courses relieves him of it (P8's message).
- Every tray: on a fresh list press Reload. Log `clipboard: every tray: N hole(s) added (N/limit)`. Expect whole rows
  nearest the player first, a row skipped rather than split while a later whole one fits, then the room left filled from
  the skipped rows; the vanilla pot is never added; holes already assigned to another botanist are skipped (the game's
  "Already assigned to" rule); if it fills the list the selector closes.
- Submit (E or Enter) and check the clipboard list and the pots' own "assigned botanist" field (look at a hole with the
  clipboard): every hole picked in bulk shows him. Save, reload: kept (the game saves the list itself).
- Co-op client opening the host's botanist: bulk clicks work; training is not checked on the client (it has no training
  data), the host relieves him of what he isn't trained for.
- Grouped (P11 set-up): clicking any hole of a placed Hydro Tray selects all 5; an Aeroponic Tower all 12. Two frames placed
  edge to edge count as one tray (expected: they touch).
- If the patch failed at start the log has `clipboard bulk assign: patch failed (...)` and clicks behave as in the game.

## Unverified assumptions

Each has a probe above; none has been seen in the game.

- **Donor IDs**: `growtent`, `potsprinkler`, `fertilizer`, `extralonglifesoil` as item IDs (from the Mono build's assets).
- **Shop codes**: `handy_hanks`, `dans_hardware` (from the task brief).
- **Cloning the Grow Tent keeps its built-in light** working (`_lightSourceOverride` is on the shared prefab).
- **Grow Tent prefab values**: moisture capacity 5, speed 1.333, yield 0.667 (from the Mono assets; the yield is overwritten
  per hole, the drain recomputed from the live capacity).
- **The pump donor** (Pot Sprinkler) is a free-standing grid item small enough to stand beside a tap, and its own sprinkler
  function (player-activated) is harmless.
- **Harmony on these methods runs under IL2CPP**: `Pot.InitializeGridItem`, `Pot.GetTemperatureGrowthMultiplier`,
  `WaterPotBehaviour.OnActionSuccess`, `Plant.Initialize`, `Plant.MinPass` (all virtual overrides; none is a one-line
  non-virtual method). A failed patch logs `<what>: patch failed (...)` at start.
- **Priority.Last puts our placement postfix after Production Expansion Reborn's** drain scaling.
- **S1API `SaveableLoadOrder.BeforeBaseGame`** makes the spoke's save available while the game creates the buildings;
  otherwise P7's late-setup warning shows and aero buds above index 15 are lost on that load.
- **Swapping a placed pot's item to the Grow Tent's between `OnSaveStart` and `OnSaveComplete`** is enough for the
  property's save to record the tent ID (the game gathers save data across several frames in between).
- **Extra bud sites**: copies of a site's GameObject under the same parent, appended to `FinalGrowthStage.GrowthSites`,
  work for clicking (the game finds a bud's index with `GrowthSites.IndexOf(parent)`) and for the host's bud RPCs.
- **Writing `Plant._QualityLevel_k__BackingField`** changes the quality the harvest reads (the property has no public setter).
- **The botanist's pot limit is per botanist** (`Botanist.MaxAssignedPots`, a prefab field copied per instance) and raising
  `configuration.Assigns.MaxItems` lets the clipboard take more.
- **Adding a choice to an employee's `DialogueController`** shows it in his conversation (the hub does the same for Oscar;
  the game does it for every employee's own choices).
- **`Fixer.GetAdditionalSigningFee()` and `Employee.SigningFee`** give what Manny charges (the decompile's own sum).
- **Moving the pot's plant container** up into the model keeps the buds clickable and botanists working.
- **Grouped placement** (P11): several pots created on the same tiles; their colliders shrunk; numbering by GUID.
- **Clipboard bulk assignment** (P14): a Harmony prefix on `ObjectSelector.Update` (a Unity message) runs on IL2CPP;
  the interop's `GetHoveredObject`, `IsObjectTypeValid`, `SetSelectionOutline`, `CloseAndSubmit` and the `selectedObjects`
  list behave as in the Mono decompile; `GameInput.GetButtonDown(Reload)` / `GetButton(Crouch)` read the keys while the
  selector is open; `ManagementInterface.Configurables` holds the botanist being edited; the clipboard's list shows all
  16/24 entries (the UI's entry rows are a prefab array; if it has fewer rows the count still reads right but rows past
  it are not drawn).
- **Moisture sync**: `SyncMoistureData` after a botanist's watering and in pump steps keeps clients' bars right.

## Known limits (v1)

- The pump connects to the nearest tap within 10 m; there is no hose-laying interaction yet.
- Untrained botanists are relieved of holes after the fact (within a second), not refused at the clipboard (bulk clicks
  skip them on the host, but a single click on one is still the game's).
- A "tray" for bulk clicks is any run of touching holes of one kind on one grid: two trays placed edge to edge are picked
  together (Crouch+click picks one hole).
- Training is host-only in co-op.
- Curing on a co-op client follows its own first sighting of the grown plant (no extra network message).
- Holes have the Grow Tent's 2x2 footprint in Individual mode, so they are no denser than pots.
- Without the mod, a Juicer-fed plant's saved additive is unknown to the game (see P12).

## Results so far

- **Registration passed in game** (2026-10-02, slot 1, IL2CPP): the tray and tower sections, the pump and Grow N Juicer
  are listed at Handy Hank's and Dan's Hardware, rank-locked by the game at Underlord III, Baron III, Underlord III and
  Kingpin III; placement mode Individual. The full Hydro Tray and Aeroponic Tower items are registered but not listed
  (grouped placement only).
- **P3 placement and persistence passed in game** (2026-10-02, slot 1, scripted with the probe's `S1P.place`): a Hydro
  Tray Section and an Aeroponic Tower Section (2x2 each) and the pump (2x1) placed in the motel room. Both sections log
  `grow medium in (20 harvests), reservoir full`; the pump `hosed to the tap 3.4 m away`. After a save and reload all three
  are back with their GUIDs; the sections' holes are in `Modded/Saveables/MelangeHydroData/holes.json` (`2 hole(s) in
  this save`), the pump in `Properties/Motel Room.json`. Growing, the botanist and the clipboard are not run yet.
