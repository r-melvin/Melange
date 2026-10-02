# Melange Levels

Rewards for the late ranks. Schedule I unlocks nothing after Block Boss I, and Kingpin's tiers go on forever with almost
nothing to show for them. Melange Levels fills that gap with choices, not more grind.

**Needs:** MelonLoader 0.7.x, [S1API](https://github.com/ifBars/S1API) 3.2.1-beta.7 or newer, and Melange Core (in `Mods/`).
**Status:** in development; tested in game on the 0.4.7 beta (IL2CPP).

## What you get

| Rank | Rewards |
|---|---|
| Block Boss I | +1 employee at a property of your choice; bulk discount 5%; an underboss candidate |
| Underlord I | +1 employee; an underboss candidate |
| Baron I | +1 employee; bulk discount 10%; an underboss candidate |
| Kingpin I | bulk discount 15%; an underboss candidate |
| Every Kingpin tier after the first | +1 Prestige |

- **Employees:** placed from the **Connections** phone app, at any property where staff can stand (not the RV). Each
  extra employee gets their own spot to wait at. Three in all.
- **Bulk discounts:** off orders of 20+ units in shops and the delivery app, and 8+ units from suppliers (their phone
  orders are capped at 10).
- **Underbosses:** with the When Benzies Met Steroids cartel mod, each candidate is another underboss you can hire, so a
  dead one can be replaced and your crew built from the personalities you want. Without it, nothing happens.
- **Prestige:** spend it in Connections to make offers they can't refuse: a supplier's dead drop is ready now, or 25%
  off Oscar's stock for a day. Each costs 1 Prestige and has a cooldown.

A game already past these ranks when you install the mod gets them on its next load. Rewards are granted once, and are
saved with your game. In co-op the host earns them for the session.

## Removing it

Fire any employees hired into the extra slots first, then delete `MelangeLevels.dll`. Without the mod those slots, and
the spots their employees wait at, don't exist; what the game does with an employee left over is untested.
