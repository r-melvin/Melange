# Melange Psychedelics: in-game checks

**Run in game so far** (2026-10-02, slot 1, IL2CPP): P1 load and registration pass (vanilla shroom line `sporesyringe` /
`shroomspawn` / `shroom`; ergot registered and listed at Fungal Phil; Toad and LSD ready; the LSD recipe locked until Ana
Slughin); the pond is found at (93.78, 3.95, -121.23), radius 5.8 m, 8 spots; Randy's stall at (-95.32, 4.36, -36.96);
Clive Mossop, the wildlife warden, spawns, warps to his post and was photographed in his ranger kit; with the toads out
(window 19:45 for 210 min) and the player at the pond he walks the ring of spots, about a lap per 40 game minutes
(sampled every 5 game minutes). Far from the player, with Siesta installed, he stands still: Siesta's Deep level pauses
distant NPCs' movement, which is harmless here (nobody is at the pond to be seen). With the `psy` probe command
(`probe-cmds.txt`): Randy's stall sells the net, terrarium and crickets by day (cash taken, items given) and toads at
night ($240, then $276, 3/4 left); caught by the warden at spots 0 and 1 the fines are $250 then $500, the toad
confiscated; unseen catches go to the pockets; a terrarium placed in the motel room takes 2 toads and 5 cricket tubs,
milks 2 venom (then nothing until they recover), feeds over two nights (crickets 5 -> 3 -> 1, breeding 0 -> 1 -> 2);
Ana's unlock opens the LSD recipe; a blotter frame with 3 blank sheets and 2 solutions doses 2 sheets of Sunburst
(rolls 0.249 and 0.142 against 0.100: good); trip outcomes move the design's reputation. A full pocket (8 slots) refuses
a catch or a purchase with a notification and takes no cash. Day cycles (`terra days`, `terra cycle`): two wild toads
fed and milked daily bred a toad every 3 days up to the tank's 6, the two wild ones turned to dust after their 10th
milking (day 31: "3 to dust"), unfed nights count as hungry; ergot planted in a mushroom bed through the patched
`CreateAndAssignColony` (`ergot colony in bed <guid>`), saved spawn ID `melange_psy_ergot_spawn`, grown and harvested as
16 `melange_psy_ergot`; a dose rolled a bad sheet (0.096 vs 0.100); LSD given to a real customer (Mrs. Ming) through the
game's consumption path fires the trip (`trip: Mrs. Ming, batch 1 (good, Standard), design preset:sunburst rep 1`) and
raises the design's reputation (2 good after two sales; her relationship was already at the 5.00 cap). Not run yet: a
paid sale through the handover screen, painting, the sewer toads. Note: venom from many milkings fills the pockets (three
quality stacks). Everything past
loading (catching, the terrarium, the stall's prompts, dosing, painting) is not run yet.

The pure logic (`Logic/`: husbandry, venom quality, lifespan, the pond's windows
and the wildlife officer, Randy's prices, bad batches, designs and reputation, the batch ledger) is covered by
`Melange.Tests/PsychedelicsTests.cs`. Everything that touches the game is listed below as a probe, then every assumption
the code could not settle.

Install: `MelangeCore.dll` (0.2.x), `MelangePsychedelics.dll`, S1API (3.2.1-beta.7 was the build reference). The sewer
spoke is optional. Log lines come from the `Melange_Psychedelics` logger in `MelonLoader/Latest.log`; quoted text is what
to grep for. Console commands are the game's own (`give`, `settime`, `addxp`, `changecash`, `teleport`,
`setrelationship`). Item IDs are in `Logic/Ids.cs`.

## Probes

Each: setup, then what to look for. "Host" means single player or the co-op host.

**Scripted probes.** The console command `psy` (host only; `Probe.cs`) drives P2-P9 through the same code as the prompts and
logs `PROBE <what>: <result>`: `status` (clock, pond window and toads, the warden, Randy, terrariums, frames, designs,
batches, Ana, the recipe, pockets), `pond` (settime into today's pond window), `catch [spot] [force]` (`force` hands over a
net first), `randy <terrarium|crickets|net|toad>`, `terra <place|add|milk|night [day]>`, `frame <place|add>`, `ana
unlock`, `dose [design]` (lists each bad-batch roll), `trip <good|bad> [design]` (no customer, so no relationship change),
`terra days <n> [feed]` / `terra cycle <n> [feed]` (n midnight steps in a row on the nearest terrarium, `cycle` milking first
each day; `feed` tops the tray up to a day's crickets; the days run ahead of the game's clock; reports births, dust,
hunger and starvation per day), `sell <toad|lsd> [design] [contract]` (one item, from the pockets if there, to the nearest
conscious customer through `NPC.Behaviour.ConsumeProduct_Server`, the call a handover ends with, so `OnNpcTrip` runs with
a real customer; `contract` uses `Customer.ProcessHandover` on the customer's current contract instead, as a non-player
handover that pays nobody; a design picks the quality the ledger would put down to that design), `ergot
[plant|grow|harvest|status]` (ergot spawn into the nearest empty mushroom bed via `MushroomBed.CreateAndAssignColony_Server`,
as the spawn task does, `ShroomColony.SetFullyGrown`, `GrowingMushroom.Harvest` per mushroom, and the colony's saved spawn
ID; no step runs all four).
`place` puts the item on the first free tiles of an owned property other than the RV, without taking it from the pockets.
`probe-cmds.txt` is a ready-made run (one command per line, `wait N`, `#` comments).

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

### P4b. The wildlife officer in person (Clive Mossop)
- Setup: P4's pond found. Load a save, go to the pond.
- Log `wildlife warden warped to his post at (x, y, z)` soon after load (S1API spawns him at its example spot, or at the
  `PondPosition` override, then he is moved). Outside the window he stands 6 m outside spot 0, facing the water, in a
  khaki bucket hat and shirt, olive trousers, a dark green vest and brown boots. Record whether he spawns at all (no
  `wildlife warden:` warning), and how he looks (a ranger? the colours read as khaki and green?).
- `settime 1900` until the toads are out: he walks the ring 1.5 m outside the toads, about one spot per 5 game minutes
  (a lap in 40). Record whether he keeps up (if not, his real position still decides; he should never stand still), any
  `wildlife warden can't walk` warnings, and whether he gets stuck on the bank (repeated `warped` lines every 30 s mean
  the walk points are off the navmesh).
- Catch a toad next to him: "Oi! Those toads are protected." over his head, and the log says `caught by the officer (seen
  from spot i)`. Catch one on the far side of the pond from him: no fine. If the log says `(on the clock)` while he is
  plainly at the pond, `TrySpot` didn't find him (record his distance from the pond's centre).
- When the window closes he walks back to his post.
- Co-op client: he walks the same lap on the client's screen (the host moves him), and a client's catch next to him is
  fined. Record whether his movement shows on the client at all.
- Fallback: with S1API's NPC spawning broken (or the class removed), the pond still works on the clock (P4).

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
10. **The wildlife officer** is an S1API NPC (`WildlifeWarden`, ID `melange_psy_wildlife_warden`) with no schedule:
    `Wild` sends him round the ring with `SetDestination` on the host, one spot ahead of the clock's spot, and warps him
    when he is over 60 m from where he should be (at most every 30 s). Assumed: the destinations (dropped to the ground
    by a raycast) are on the navmesh; disabling his schedule stops the game sending him elsewhere; his position syncs to
    clients. What he sees is the spot nearest his real position and its neighbours, if he's within 15 m of the pond's
    edge; otherwise (not spawned, lost) the 40-minute clock decides as before. He is a locked contact (no texts).
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

## Painted designs (experimental, `PaintDesigns`)

Built behind the setting `PaintDesigns` (default **off**); see `SPRAY-SPIKE.md` for the game code it relies on. Nothing of
it has run. With the setting off, nothing below happens and P9 is unchanged. Turn it on in
`UserData/MelonPreferences.cfg` (`[MelangePsychedelics] PaintDesigns = true`) and load a save as host.

### P10. The canvas copy
- Place a blotter frame (or load a save with one). Log, once per session: `spray canvas template: <vehicle code> /
  <object> (W x H), components: ...` (record all of it), then per frame `spray canvas copy: network object <name>, N
  network object(s) inside` and `spray canvas on a frame: scale 0.200, centre (...), camera (...)`, maybe `turned round`.
- Failure lines to record verbatim: `no vehicle prefab with a spray surface`, `parts outside the copy: ...`, `points at a
  live network object`, any `spray canvas: <exception>`. Each turns painting off for that frame only; dosing still works.
- Check: **N network object(s) inside** is 1: the count includes the copy's own root, which carries the surface's
  NetworkObject, copied from the prefab and never spawned (a second one would be unexpected).
- **Passed in game** (2026-10-02, slot 1, `PaintDesigns = true` for the run only): `spray canvas template: veeper /
  SpraySurface (350 x 150), components: Transform, SpraySurface, SpraySurfaceInteraction, SprayDisplay, NetworkObject,
  MonoState; 6 child(ren)`; `spray canvas copy: network object Melange spray canvas, 1 network object(s) inside`; `spray
  canvas on a frame: scale 0.200, centre (-70.02, 1.70, 85.66), camera (-69.58, 1.74, 85.66)`; no failure lines.
  Painting itself (P11 on) needs a spray can in hand and the graffiti screen: a hands-on check.
- Check: nothing visible changes on the frame with no spray can in hand (no outline, no decal on the wall behind, no stray
  canvas in the world at 0,0,0). The base prompt reads `Design: Plain (new), or spray your own on the sheet`.

### P11. Painting
- Buy a spray can (the game's `spraypaint`). Equip it and look at the frame's sheet. Expect the game's outline on the
  sheet and the prompt **Use spray can** (the frame's `Dose` prompt hides while you hold the can; up to 1 s delay).
- Interact: the camera moves in front of the sheet (record: square on? close enough? on the front, not inside or behind
  the frame?), the graffiti menu opens. Paint in two colours and two brush sizes. Check: the paint appears **where the
  cursor is, on the sheet** and nowhere else (not on the wall or floor behind, not mirrored); the remaining-paint bar falls.
- Undo works. **Clear** clears (the mod's own handler; the game's does nothing on this copy). Record whether Clear needs
  more than one click, and whether it leaves the undo button in a sensible state.
- FishNet will log warnings like `Cannot complete action because client is not active` per stroke: expected, record how
  many and whether anything else (errors, exceptions) appears. No "vandalizing" reaction from police nearby.
- Done (or Esc, then confirm): one spray can is used, notification **New design**: `<Colour> design 1: dose sheets to
  print it.`, log `painted design painted:1 (<Colour> design 1): N stroke(s), M chars`. The painting stays on the sheet
  and the base prompt shows `Design: <Colour> design 1 (new)`.
- With the can still equipped, no `Use spray can` on the painted sheet; with the graffiti cleaner equipped, no `Clean
  graffiti` (its interaction is switched off while it shows a painted design).
- Dose sheets: `Dosed ... of <Colour> design 1`, batches recorded under `painted:1`; trips move its reputation (P9).

### P12. Keeping and reusing
- Press the base: the cycle is the five built-ins then `<Colour> design 1`; on a built-in the sheet goes blank (and can be
  painted: a second painting makes `painted:2`); back on the painted one it reappears **the same** (colours, brush sizes,
  position).
- Save, quit to menu, reload: log `loaded: ..., 6 design(s), ...`; the frame shows the painted design again; the
  reputation and batches are kept. A second frame can select the same painted design.
- Pick the frame up and put it down: a new canvas, same design shown.
- Co-op: clients see no canvas and cannot paint (host only, as the design choice); record that nothing of the host's
  painting shows up on any vehicle for the client (the copy must not reach the network).

### Assumptions in the painted designs
1. The vehicle prefabs in `VehicleManager.VehiclePrefabs` carry a `SpraySurface` with its `SpraySurfaceInteraction` on the
   same object, and all its parts (BottomLeftPoint, Projector, IntObj, CameraPosition, Canvas, SprayImg, sounds, State)
   are inside that object. The code checks this and refuses otherwise.
2. The copy's `_networkObjectCache` is the prefab's NetworkObject, never initialised, so its RPCs stay local (checked by
   the code; the IL2CPP generated RPC code is assumed the same as the Mono decompile's).
3. `BottomLeftPoint` faces out with the canvas camera on its +Z side; the copy is turned if the camera ends up behind.
4. Setting `DecalProjector.scaleMode = InheritFromHierarchy` makes the decal follow the 0.2 scale; its depth then catches
   the frame's sheet 0.01 m behind the canvas.
5. Width/Height set before the copy's Awake (it is made under an inactive parent) size the canvas, camera and prompt.
6. One-second polling is enough: the Clear handler is added within a second of the screen opening, and a finished
   painting is picked up within a second of closing.
7. The game's own `SprayStroke.Serialize` drops the brush size, so designs are kept as the mod's text
   (`s1:x0,y0,x1,y1,colour,size;...`); strokes the game could not draw on a 450 x 300 canvas are dropped.
8. Not done: the LSD item's look and icon per design (needs per-item data, assumption 8); naming or retiring a painted
   design; painting from a co-op client.
