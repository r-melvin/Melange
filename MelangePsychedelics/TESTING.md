# Melange Psychedelics: in-game checks

Nothing here has run in the game yet. The pure logic (`Logic/`: husbandry, venom quality, lifespan, the pond's windows
and the wildlife officer, Randy's prices, bad batches, designs and reputation, the batch ledger) is covered by
`Melange.Tests/PsychedelicsTests.cs`. Everything that touches the game is listed below as a probe, then every assumption
the code could not settle.

Install: `MelangeCore.dll` (0.2.x), `MelangePsychedelics.dll`, S1API (3.2.1-beta.7 was the build reference). The sewer
spoke is optional. Log lines come from the `Melange_Psychedelics` logger in `MelonLoader/Latest.log`; quoted text is what
to grep for. Console commands are the game's own (`give`, `settime`, `addxp`, `changecash`, `teleport`,
`setrelationship`). Item IDs are in `Logic/Ids.cs`.

## Probes

Each: setup, then what to look for. "Host" means single player or the co-op host.

### P1. Load and registration
- Setup: load any save.
- Log, in this order or close to it: `vanilla shroom line: spores '<id>', spawn '<id>', shroom '<id>'` (record the IDs),
  `ergot: spores, spawn and harvest registered`, `products: Toad ready, LSD ready`, `LSD solution recipe registered`,
  `ergot: melange_psy_ergot_spores in 1 shop(s) (<Fungal Phil's shop>)`, `loaded: N terrarium(s), 5 design(s), ...`,
  `LSD solution recipe locked`.
- No `could not register`, `MISSING`, `not found` warnings. Any `<name>: donor '...' is not a quality item` means a donor
  ID in `Items.cs` is wrong: record the real one.
- Quit to the menu and load a second save: the same lines, with no `build refused` and no duplicate-ID warnings from the
  game's Registry (the definitions are put back, not rebuilt: `Kept`).

### P2. Products
- `give melange.psy:products/toad 5` and `give melange.psy:products/lsd 3`. Check: amber flakes and pale tabs in hand and
  in storage (not shrooms, not invisible), and icons (S1API generates them during loading; record any "no visible pixels"
  warnings).
- Package Toad into a baggie and a jar at a packaging station; LSD into a baggie. Check the contents show.
- Product Manager: sections "Toad" and "LSD" once the icons exist (they are registered at the first load-complete with
  icons; a cold start may need a second load: record).
- Log for the shroom affinity path: sell one of each to a shroom customer; no exception from `Customer.AdjustAffinity`.
  NPCs play the shroom eating animation (expected, a known cost of compat Shrooms).

### P3. Randy's stall
- Setup: `teleport` to the Docks, behind Randy's Bait & Tackle. Log `Randy's stall at (x, y, z)`. Record whether the stall
  stands on the ground, out of the way of the dead drop, and reachable (the offset is a guess: `+1.6 m x`).
- 07:00-19:00: three prompts `Buy Terrarium ($350)`, `Buy Crickets x5 ($30)`, `Buy Toad Net ($80)`; the sign `Ask Randy
  about toads`. Buy each: cash drops, the item arrives, log `Randy's stall: bought ...`.
- `settime 2200`: middle prompt `Buy Toad ($240)`, then `$276`, `$312` ... until `Sold out tonight` (2-4 a night). Others
  read `Just toads tonight`. `settime 0030`: still open (last night's). `settime 0200`: `Closed: opens 7 am`.
- Record: whether the prompts show at all (they need a collider on a layer the interaction search includes; log
  `interactables on layer N (<name>), search mask <hex>`).

### P4. The pond and the wildlife officer
- Setup: carry a toad net. Log `pond at (x, y, z) (radius r m), 8 toad spots`. If `pond not found`, find the pond by eye,
  set `PondPosition` in `UserData/MelonPreferences.cfg` and record the coordinates (and the object's real name, to replace
  the search in `Wild.Locate`).
- `settime 1900`, wait until the log says `pond: toads out (n) from HHMM for M min`; go there. A notification "Toads at the
  pond" within 80 m. Toads sit at the water's edge on the ground (record any floating or buried).
- Catch several. Unseen: a Premium "Toad" in the pockets, log `pond: toad caught at spot i`. Seen (the officer is at the
  spot or a neighbour): `fined $250 and the toad's confiscated`, then $500, then $750 with "He's called it in" and a police
  investigation (pursuit level Investigating).
- Without a net: "You need a toad net". After the window closes the toads vanish. Reload during a window: caught spots stay
  empty (host).

### P5. Terrarium
- Setup: buy and place a terrarium at a property. Check the tank model stands in for the storage rack, at the rack's
  footprint (record the scale), and that opening it shows the rack's slots.
- Put 2 toads and 6 tubs of crickets in. Log `terrarium <guid>: 2 Wild toad(s) released (2/6)`; two toad models on the
  sand; the lid prompt `Milk toads (2 ready)`.
- Milk: two Toad venom (Standard on the first day: freshly caught), `Milked` notification, prompt `Milked today (2 toads)`.
- Sleep: log `terrarium <guid> day D: ate 2, fed 2, ...`; after 3 nights with food a `Toadlet` notification and a third toad
  (log `born 1`).
- Without crickets: `Hungry toads`, then after 4 nights `Toad starved`.
- `MilkingsBeforeDust = 2` in the preferences: the second milking reports `1 toad(s) shrivelled to dust` per toad.
- Check: handlers can take venom out (it's a storage rack underneath); the rack's stored-item visuals stay hidden.
- Co-op client: the prompt says "Only the host can milk toads in this version"; slots still sync.

### P6. Drying
- Put loose Toad on a drying rack: accepted, a grade up per 12 h. Put loose LSD (unpack a sheet): refused. If LSD is
  accepted, `IsItemDryable` was inlined into its two callers and the postfix never runs (record; then the filter has to
  be done another way, in the hub).

### P7. Ergot
- Setup: rank Shot Caller I (`addxp`). Fungal Phil's shop lists Ergot Spores ($180); below the rank it is locked (record
  whether a supplier's phone shop honours `RequiresLevelToPurchase` at all).
- Spores + grain bag at the spawn station: the task runs with the vanilla syringe visuals and gives Ergot Spawn.
- Apply to a mushroom bed: log `ergot colony in bed <guid>`. The colony grows (cold, as shrooms) with dark purple
  mushrooms. Harvest: "Ergot" items, never offered to customers, absent from the Product Manager.
- Save and reload mid-growth: the bed still grows ergot (the save holds `melange_psy_ergot_spawn`). Record the vanilla
  warning `Mushroom bed tried to load a colony with invalid spawn ID` if it appears.
- `ErgotGrowing = false`: Phil sells Ergot directly ($30), no spores.

### P8. Ana Slughin and the recipe
- Setup: good deals with Fiona Hancock, Lily Turner or Pearl Moore until one recommends Ana (`setrelationship` helps).
  Her text arrives; her phone shop sells Reagent, Blank Blotter Sheet and Blotter Frame; dead drop at the medical
  practice. Log `LSD solution recipe open`.
- Chemistry station: recipe "LSD Solution" (4 Ergot + 1 Reagent, 4 h) appears only after Ana. Cook it: LSD Solution with a
  quality. Record whether the station's task shows the reagent (it borrows the acid's station item) and the ergot (the
  shroom's), and whether the output needs a station item of its own.
- Reload: the recipe stays open (re-applied after load).

### P9. Blotter frame and brands
- Place a frame (model in place of the rack). Put 3 blank sheets and 2 solutions in. Prompts: base `Design: Plain (new)`,
  sheet `Dose 2 sheet(s)`.
- Press the base: cycles Plain, Sunburst, Third Eye, Checkers, Toadstool.
- Dose: two LSD sheets (brick packaging, 20 tabs each, the solution's quality), `Dosed` notification, log
  `dosed 2 sheet(s) of preset:..., k bad`. One sheet and no solution left.
- Sell tabs; when a customer takes one: log `trip: <name>, batch N (good|BAD, <tier>), design ... rep R (...)`. Force a bad
  batch with `BadBatchOdds = 100`: "had a bad trip" notification and a relationship drop. Good Premium/Heavenly batches
  raise the design to Known (5) then Loved (20): "Customers ask for the ... sheets by name" and small relationship gains.
- Record on which peer the NPC consumption hook runs (it should be the host; the handler ignores clients).

## Unverified assumptions

1. **Item donors**: `cocaleaf` (live toad), `banana` (crickets, net, sheets), `acid` (reagent, keeping its station item),
   `liquidmeth` with `cocaleaf` as fallback (solution), `smallstoragerack` (terrarium and frame). Their looks are the
   donors' until there is art; the toad item looks like a coca leaf.
2. **The storage-rack clone** keeps the rack's slot count and footprint (a clone shares the donor's built prefab). The
   models are scaled into the rack's footprint, not the 2 x 1 cells they were drawn for.
3. **Prompts** need a collider on a layer the interaction search includes; the layer is read from
   `InteractionManager.Interaction_SearchMask`. The lid and sheet targets overlap the rack's own collider; which one the
   look raycast picks where they meet is untested.
4. **Ergot**: the copies made with `Object.Instantiate` keep their native types and serialised fields; `AppearanceSettings`
   (not serialised) is set by hand. The colony patch is on `MushroomBed.CreateAndAssignColony`, which runs on the host only:
   a **co-op client** harvesting an ergot bed gets the colony prefab's vanilla shrooms. A registered ProductDefinition that
   ProductManager doesn't know about is assumed harmless (saved items resolve through the Registry).
5. **Chemistry**: ergot (a shroom copy) is accepted as an ingredient; the output (a quality item without a station item)
   lands in the output slot. S1API recipes live for the process, which is why item definitions are kept and re-added
   rather than rebuilt (`Kept`). Changing `ErgotSporesRank` needs a restart.
6. **Products**: compat Shrooms with our own presentation profile avoids the shroom visual setter. Icons are generated from
   plain boxes. LSD tabs are discovered at the first dosing, Toad at the first milking (host).
7. **Brick = 20**: a sheet is the game's brick packaging; its quantity is assumed 20 (the Brick Press consumes 20).
8. **Brands are approximate**: product items carry no per-item data, so tabs are matched to batches first in, first out
   by quality (`BatchLedger`). Exact per-sheet designs need per-item data (S1API custom item data or a product per
   design through the save provider). One NPC consumption is counted as one tab, whatever the deal size.
9. **The pond** is found by the object `StylizedWater2_Pond` (not seen in the data: the string is a material name in
   `sharedassets1`), else by any mesh whose material name contains "Pond". The toad spots are a ring 0.8 m outside the
   pond's bounds, dropped to the ground by a raycast. Its region is unknown.
10. **The wildlife officer** has no body yet: his lap is a clock (40 minutes round 8 spots, seeing his spot and its
    neighbours). A visible S1API NPC walking the ring needs the pond's coordinates first (P4).
11. **Randy**: Randy's Bait & Tackle has no shop screen in the data (an NPC building), so the stall is ours, beside the
    dead drop "Behind Randy's bait & tackle". The building's back door and Randy himself are not used.
12. **The sewer**: toads sit 0.6 m to the side of the game's `SewerMushrooms.MushroomLocations`. Route: spared King (from
    the sewer spoke) gives the mentor route; King defeated, goblin calmed, or the sewer simply unlocked gives the hard
    route, so without the sewer spoke the sewer opens with the vanilla key.
13. **Co-op**: the terrariums, dosing and design choice are host-only in this version (their state is the host's save
    data and there is no request channel yet). The pond, the sewer toads and the stall are per player and work for
    clients. The dice seed is a constant so every peer rolls the same pond.
14. **Ana**: S1API discovers her class itself; her hidden spawn is S1API's example spot. Her voice `female-2` and hair
    colour are guesses at a look.

## Painted designs (next spike)

The plan's blotter art is player-painted with the game's spray can. Not built, because it needs answers only the game
can give:

1. Can a `SpraySurface` (a NetworkBehaviour) live on a cloned buildable? Option A: the frame's prefab is the storage
   rack's, so a surface added at runtime is not a registered network behaviour. Option B: put a world surface
   (`WorldSpraySurface`) on the frame's position, owned by the host, and replicate its strokes with
   `SpraySurface.Set(conn, strokes, false)`.
2. Does `GetSerializedDrawing()` give something that serialises to text (for `Design.Drawing`) and back through
   `LoadSerializedDrawing`?
3. Can `DrawingOutputTexture` be copied into a texture for the sheet's material and a sprite for the icon?

When those are answered, `DesignBook.AddPainted(name, drawing)` already stores a painted design, and the frame cycles it
like a preset.
