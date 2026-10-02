# Melange Smuggling: in-game checks

Nothing here has run in the game yet. The pure logic (`Logic/`) is covered by `Melange.Tests/SmugglingTests.cs`
(unlock roll and counting, order sizing and pricing, timing, payment, police risk with and without the route, imports,
the voyage state machine, the tanker's rules). Everything that touches the game is listed below as a probe scenario,
then every assumption the code could not settle.

Install: `MelangeCore.dll` (0.2.x), `MelangeSmuggling.dll`, S1API. Log lines come from the `Melange_Smuggling` logger
in `MelonLoader/Latest.log`; quoted text below is what to grep for.

## Probes

The spoke registers one console command through S1API, `smuggling <what>`, so every scenario can be typed in the game's
console or scripted (`S1API.Console.ConsoleHelper.Submit("smuggling status")`, or a dev-runner marker that submits a
list). Each logs `PROBE <what>: <result>`. Host only.

| Command | Does |
|---|---|
| `smuggling status` | one line with the whole state: day and time, Oscar count/threshold, unlock, route, reputation, boat, order, hold, imports, record, whether Dafydd spawned, the market the orders draw on, how many imports are offered |
| `smuggling oscar <spend>` | as if an Oscar checkout of `<spend>` dollars completed (default 300) |
| `smuggling unlock` | brings the count to one short of the threshold and makes a qualifying checkout: Oscar's line and Dafydd's first text |
| `smuggling order` | posts an order now (boat in, none on) |
| `smuggling accept` / `decline` | answers the current order as the text buttons do |
| `smuggling load` | one delivery, as talking to Dafydd or using the boat does |
| `smuggling sail` | moves the deadline to now: the boat sails and pays |
| `smuggling return` | brings the boat back now, landing any imports |
| `smuggling collect` | collects waiting imports |
| `smuggling route [known]` | where the player is, whether that is on the route, seconds since, drove since, and whether a delivery now would count as via the route; `known` sets the route as known without the sewer spoke |
| `smuggling tanker` | starts the experimental tanker job in 5 s (needs the setting and the methylamine item) |
| `smuggling boat` | the berth, where the boat is now, whether it is shown, model or stand-in, prompt on or off |

The game's own console commands used below: `give <item> <qty>`, `settime <hhmm>`, `teleport`, `changecash`, `addxp`.

## Probe scenarios

### P1. Load and wiring
- Setup: load any save (single player).
- Log: `loaded: Oscar <n>/<threshold>, unlocked <bool>, route <bool>, reputation 50, boat Moored, order none, aboard 0, runs 0/0`.
  The threshold is 10-15 and stays the same across reloads of that save.
- Log: `listening to <Oscar's shop name> (<code>) checkouts`; record the shop code.
- Log: `boat berth (x, y, z) heading <deg>, quay top <y>, waterline from ...`, plus any `water candidate <name> at y <y>`
  lines. Record all of them: they settle where the water is (assumption A5).
- No warnings starting `Oscar's shop wasn't found`, `boat model`, `boat prompt not added`, `Dafydd's look`,
  `Dafydd's dialogue`, `import <id>: no such item`.
- `smuggling status` says `Dafydd spawned`. If it says `missing`, check S1API's log for the NPC (assumption A8).

### P2. Reset between saves
- Setup: P5 done in save A (unlocked, an order on). Quit to the menu, load save B that never had the spoke.
- Expected: `loaded: Oscar 0/<new roll>, unlocked False, ... order none`. Nothing from save A carries over.

### P3. Oscar counts big purchases only
- Setup: a save with the warehouse open, `changecash 20000`.
- Buy something under $300 from Oscar in person: `Oscar checkout $<n>: too small, <k>/<t>`.
- Buy $300 or more: `counts, <k+1>/<t>`.
- Order from Oscar through the delivery app: record whether any `Oscar checkout` line appears (assumption A2;
  expected none).
- At the threshold: Oscar says `You spend like you mean it...` in a bubble over his own thanks, the log shows
  `Dafydd's number passed on`, and a text arrives from Dafydd Seabiscuit (`Shwmae! ...`).
- Oscar's choice `Who's your supplier?`: before unlocking he says `My supplier? Keep buying...`, after it the repeat line.
- Shortcut: `smuggling unlock`.

### P4. Dafydd and the boat on the quay
- Setup: P3 done. `teleport` to the Docks quay, between the third and fourth bollards (about -70, -34).
- Expected: Dafydd stands on the quay in a black wide-brimmed hat, long black curls, twirled moustache, white shirt,
  burgundy jacket, belt, dark trousers, boots, gold chain. Record a screenshot; check nothing failed to load (bald,
  missing jacket) against the S1API log (assumption A9).
- The boat lies in the water beside the quay, parallel to it, bow towards the next bollard (south-east). Record whether
  it floats at the waterline, sits in the air or is sunk (A5), whether it is on the water side at all (A4), and whether
  the colours render (pink means the material template failed: A6).
- The prompt on the quay edge beside the boat reads `Turnip Night's boat (no order on)` (or `Somebody's speedboat`
  before unlocking). Record the reach (A7).
- After midnight and again at noon he is still there (his schedule walks him back).

### P5. An order
- Setup: P3 done. Wait for 08:00 the morning after unlocking, or `smuggling order`.
- Expected: a text `Right then. I'm after <n> units of <drug>, Standard or better, $<p> a unit. Load her at the quay
  before she sails at 02:00 (<time> from now)...` with buttons `Aye, it'll be there` / `Not this time`.
- `<drug>` is one the player has discovered; `<p>` is about 1.2x the game's market value (compare `smuggling status`).
- Press `Aye`: `order #<id> accepted`, Dafydd answers `Tidy...`.
- Save, reload, check the buttons still answer (or are gone) without errors (A10).

### P6. Loading
- Setup: P5 done. `give` packaged product of the ordered type (bricks, jars and baggies; one stack below the quality
  floor; one of another drug type). Stand at the boat.
- The prompt reads `Load <n> units (<p>% police risk)`, counting only matching product.
- Use the prompt (or talk to Dafydd, `Load the hold`): matching stacks leave the inventory, Dafydd says
  `<n> units aboard, <k> of <order> now.`; log `loaded <n> (risk <p>%), <k>/<order> aboard`. The non-matching stacks stay.
- Trunk: park the last-driven vehicle within 15 m of the boat with product in its trunk, get out, load: the trunk's
  matching stacks go too. Record whether a trunk 15 m away is reachable in practice.
- Over-delivery: load past the order; the hold stops at 1.5x and the rest stays with the player.
- Seizure: set `BasePoliceRisk` to 0.5 in MelonPreferences, reload, load: about half the time
  `delivery of <n> seized`, a notification, the product gone, `seizures` up in `smuggling status`.
- Curfew: after 21:00 the shown risk is 1.5x.

### P7. Sailing and pay
- Setup: P6 done. Wait for 02:00 the night after the deadline day, or `smuggling sail`.
- Expected: the boat runs out along the quay and away for about 25 s, then disappears; `voyage: Departed Full $<n>`
  (or Partial/Short/NoShow/Skipped); an online transaction "Turnip Night" for the payout in the banking app; a text with
  the outcome. Reputation +5 for a full hold, -5 short (under half), -10 for an accepted order with nothing aboard.
- Sleep through 02:00 instead of waiting: the boat must still sail and pay on waking (`Departed` once, not twice).
- Then at 07:00 (or `smuggling return`): the boat is back, `voyage: Returned`, a text.

### P8. Imports
- Setup: one paid run (P7). Talk to Dafydd, `Bring something back`: one choice per catalogue item (acid, phosphorus,
  high-quality pseudo by default; record their names and whether the IDs exist: A11).
- With cash: `<crate> <name> on the return leg... $<price>`; cash goes down by the reputation-discounted price.
- After the next sailing and return: the prompt reads `Collect your crates`; using it fills the pockets
  (`imports collected: <n>, <k> still waiting` when they don't all fit).
- Methylamine: set `MethylamineItemId` to an existing item ID (any, for the test), reload: it appears in the list.
  Empty: it doesn't.

### P9. The bootleggers' route
- Setup: the sewer spoke installed and its route revealed (or `smuggling route known` to test without it).
- Walk from the Sewer Office through the tunnels, out of the central canal pipe (26, 11), along the canal bed to the
  canal mouth (-34.5, -10.7), then on foot to the quay. `smuggling route` along the way: `on route True` in the tunnels,
  on the canal bed and at the mouth; `on route False` on the streets (A12).
- At the boat within 3 minutes of leaving the mouth, without getting in a vehicle: the prompt says
  `(0% police risk, the old way)` and loading logs `via the route`; Dafydd adds `Came the old way, did you?`.
- Get in any vehicle after leaving the route: the next delivery counts normal risk again.
- Record whether the canal bed is walkable all the way and whether the Docks basin is reachable on foot from the mouth
  (research open question).

### P10. Declining and the schedule
- `smuggling order`, press `Not this time`: `order #<id> declined`, no sailing that night, the next order three days later.
- Load something, then press `Not this time` on the same order: refused with `Too late for that...`.
- With no product discovered (a fresh save forced through `smuggling unlock`): `voyage: NoProduct` and one text
  `Nothing I can sell yet...`, asked again each morning without repeating the text in one session.

### P11. Co-op (host decides)
- Client: the boat and Dafydd are visible; the boat prompt and his choices answer `Talk to whoever's running things...`.
  The client's boat never sails: its state lives on the host, and the model is local to each machine (known limit).
- Client buys from Oscar: nothing is counted (the checkout event fires on the buyer's machine only: A3).
- Host's texts reach the client? Record (S1API sends networked texts by default).

### P12. The tanker (experimental, off by default)
- Setup: `TankerJob = true`, `MethylamineItemId` set to an existing item, rank 6 or more (or `smuggling tanker`).
- Expected: a tip text, and 2 minutes later (5 s with the probe) `tanker: veeper on its way from (-100,80)`.
- Follow it: it drives itself on the road towards the Docks plant (-51, -74). Block it with your car, get out, stand
  within 6 m for 3 s: `tanker: stopped by the player, <n>/<n> methylamine given`, a text.
- Leave it alone: `tanker: reached the Docks unstopped` (or `gave up` after 10 minutes), a text.
- Walk 80 m away: `tanker: cleared away`; save and reload: no stray van in the world (A14).

## Assumptions the code could not settle

- **A1** `ShopInterface.onOrderCompletedWithSpend` fires for in-person purchases from Oscar with the order total
  (`ScheduleOne.UI.Shop/Cart.cs:218-225`, decompiled Mono; the IL2CPP field exists, `get_/set_onOrderCompletedWithSpend`
  in the interop). Combining our delegate with the game's (as the sewer spoke does for the goblin) is expected to keep
  the game's own listeners (the supplier meetup uses this field too, `Supplier.cs:168`).
- **A2** Delivery-app orders from Oscar don't fire it (they go through the delivery shop). If they do, they count too,
  which is acceptable.
- **A3** The checkout runs on the buyer's machine (`Cart.Buy`), so a co-op client's purchases are not counted; only the
  host's are. By design (host decides) but worth confirming.
- **A4** The basin is on the east (larger x) side of the bollard line, from the scene dump (the Docks Warehouse and the
  fishing hut are west of it). If the boat appears on land, use `Berth` or flip the side in `Quay.Between`.
- **A5** The waterline. The code looks for a renderer named like water whose footprint covers the berth and whose top
  is 0.3-25 m below the quay; failing that it guesses 1.8 m below the quay top. The scene's `StylizedWater2_Ocean` was
  dumped at y about -16.8, which is suspiciously low for a quay at about -2.5; if the boat floats wrong, set
  `BoatWaterline` to the right height (world y) and report it so the default can change.
- **A6** A URP Lit (or Simple Lit) material is loaded somewhere to copy for the boat's colours, as the mixers spoke
  copies the Mk2's. If none, a plain grey box stands in.
- **A7** The game's interaction search mask includes the Default layer and hits trigger colliders
  (`InteractionManager` raycasts with `QueryTriggerInteraction.Collide`; the mask itself is set in the scene). If the
  prompt never shows, the collider needs another layer.
- **A8** S1API spawns Dafydd as a physical custom NPC from `ConfigurePrefab` alone, possibly after the save reports
  loaded (the spoke wires him whenever he appears). His texts before he spawns are not sent (only the first text is
  retried).
- **A9** The clothing paths (S1API's appearance constants) all exist in 0.4.7f7. The game has no eyepatch or tricorn;
  the cowboy hat in black stands in.
- **A10** S1API restores text replies on load and calls `OnResponseLoaded`, where the order buttons are re-attached by
  label (`msm_accept_<id>`, `msm_decline_<id>`); an old order's buttons do nothing.
- **A11** The default import item IDs `acid`, `phosphorus`, `highqualitypseudo` exist. An unknown ID is left out with
  a warning; prices are guesses near Oscar's and should be tuned.
- **A12** The route box (y -10..-3.5 over x -54..103, z -12..115, plus 12 m around the canal mouth and pipe) catches the
  tunnels and the canal bed and nothing at street level. The Dark Market warehouse's foundations at y -6.5 sit inside the
  box: if the player can stand that low inside Oscar's warehouse, it would count as the route.
- **A13** `ProductItemInstance.Amount` is the packaging quantity (brick 20, jar 5, baggie 1) and `ItemSlot.ChangeQuantity`
  removes items from the player's pockets and from a vehicle trunk on the host without further sync calls.
- **A14** The tanker: `VehicleManager.SpawnAndReturnVehicle(code, pos, rot, playerOwned: false)` gives a vehicle with a
  working `VehicleAgent` that drives with no driver NPC (the cartel spoke drives its existing SUV this way, not a freshly
  spawned one); not-player-owned vehicles are not saved (`VehicleManager.GetSaveString` writes only
  `PlayerOwnedVehicles`, `VehicleManager.cs:119-127`); `DestroyVehicle` despawns it. `veeper` is a valid vehicle code
  (S1API uses it). There is no tanker model.
- **A15** Payment uses the game's online transaction (`MoneyManager.CreateOnlineTransaction`) on the host; imports are
  paid in cash (`ChangeCashBalance`).
- **A16** Vanilla police never search NPC vehicles and nothing watches the quay (research 2.3), so the police risk is
  wholly the spoke's: a seized delivery is lost, but adds no crime or pursuit. Hooking it into vanilla crime (or Police
  Response Overhaul) is a later choice.
- **A17** The clock: `TimeManager.GetTotalMinSum()` is `ElapsedDays * 1440 + minutes since midnight`, and stands still at
  04:00 until the player sleeps; every step compares "has it passed", so sleeping through a departure sails it on waking.

## Not in this version

- The boat is a static model with a scripted departure, not a vehicle; no water physics, no chase at sea, no
  interception at sea (the plan's "coastguard or Benzies" roll). The Benzies' raids on runs belong to the cartel spoke
  through future hub events (`smuggling.runPlanned`, `smuggling.ambush`), which the hub doesn't offer yet.
- The hold is the save's record, not a game storage: a networked storage on a mod object isn't possible without a
  network prefab. The player loads by using the boat or talking to Dafydd.
- The tanker job is behind a setting with its seam in `Tanker.cs` (spawn, drive, stop, clean up). For v2: a real tanker
  model, a driver NPC (`NPC.EnterVehicle`), guards, and a save for a job in progress. If spawned vehicles don't drive
  (A14), fall back to the research's option 1: a methylamine container landed among the Docks shipping containers.
- The supply-run engine, runners and hijacks stay in the cartel spoke for now; moving them is that session's call.
