# Melange Smuggling

Turnip Night's speedboat. Spend big at Oscar's and he puts you in touch with his supplier: Dafydd "Turnip Night"
Seabiscuit, a Welshman in a pirate's get-up (tricorn, eyepatch, curls and a burgundy coat) who runs a speedboat out of the Docks. He takes huge bulk orders, sails at
night whether you've delivered or not, and brings goods back on the return leg.

**Needs:** MelonLoader 0.7.x, [S1API](https://github.com/ifBars/S1API), and Melange Core 0.2 or newer (in `Mods/`).
**Status:** in development; not yet tested in game (see `TESTING.md`).

## How it works

- **The way in.** Buy from Oscar at the warehouse. Somewhere between 10 and 15 purchases (it's different every game,
  and only purchases of **$300 or more** count) he decides you're serious and passes on Dafydd's number. Ask Oscar
  "Who's your supplier?" any time.
- **Orders.** The morning after, Dafydd texts an order: a lot of one drug you already make, at a quality floor
  (Standard, later sometimes Premium), at about 20% over market value per unit. Orders grow with your rank and with how
  reliably you've filled his boat. Answer **Aye** or **Not this time**.
- **Delivery.** The boat is moored on the east side of the Docks quay, Dafydd beside it. Bring the product and use the
  boat (or ask Dafydd to load the hold): matching product comes out of your pockets, and out of the trunk of the car you
  drove there if it's parked close by. Every delivery has a **police risk**, shown before you load: bigger loads and
  the curfew raise it, and a seized delivery is lost.
- **Sailing.** At **02:00** after the deadline day the boat sails, with or without your product. You're paid by bank
  transfer for what was aboard: the full price up to the order, a cheaper rate for up to half as much again. Fill it and
  your reputation goes up; say aye and leave it empty and it goes down. She's back at 07:00, and the next order comes a
  few days later.
- **Imports.** Once you've been paid for a run, ask Dafydd to bring something back (cash up front; better reputation,
  better prices). Collect your crates at the boat after it returns.
- **The old bootleggers' route.** With Melange Sewer, once you've learned the Sewer King's secret route (the sewer, the
  canal bed, out at the canal mouth by the Docks), deliveries carried that way on foot carry **no police risk**.
- **Methylamine.** If another mod provides a methylamine item (the cartel spoke), set its item ID in the settings and
  Dafydd will bring it in. Without one, it simply isn't offered.

## Settings

In `UserData/MelonPreferences.cfg`, section `MelangeSmuggling`: the purchase range and the $300 floor, order, departure
and return times, days of grace before the deadline, days between orders, the price premium, police risk (and the
route's multiplier), the import catalogue (`id:crate:price`), the methylamine item, which gap between the quay's
bollards the boat uses, and the boat's waterline if it sits wrong. `PirateLook` (off: Dafydd wears a black cowboy hat
instead of the tricorn and eyepatch), and `PirateHatScale`, `PirateHatOffset`, `PirateEyepatchOffset` to nudge them if
they sit wrong on his head. Changes apply from the next load. In co-op the
host's settings and the host's game decide everything; other players see the boat and Dafydd and get his texts.

**Experimental (off by default):** `TankerJob` adds a methylamine tanker that leaves Billy's chemical plant now and
then; stop it on the road and its load is yours. It needs `MethylamineItemId` and is untested.

## Removing it

Delete `MelangeSmuggling.dll`. The boat, Dafydd, any order and anything in the hold go with it (cargo aboard is lost,
so wait for a sailing first). Nothing is written into the game's own save data.
