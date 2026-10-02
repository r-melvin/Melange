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
- **Prestige:** spend it in Connections to make offers they can't refuse: a supplier's dead drop is ready now, 25%
  off Oscar's stock for a day, or the police look away until 6 AM (no body searches, no curfew stops, and if they're
  only searching for you, the search is called off; a chase with cuffs or guns out goes on). Each costs 1 Prestige and
  has a cooldown (the police one: 3 days). In co-op the police offer covers the host only.

A game already past these ranks when you install the mod gets them on its next load. Rewards are granted once, and are
saved with your game. In co-op the host earns them for the session.

## Removing it

Fire any employees hired into the extra slots first, then delete `MelangeLevels.dll`. Without the mod those slots, and
the spots their employees wait at, don't exist; what the game does with an employee left over is untested.

## Testing

Checks to run in game (host or single player) for the police offer; the rules (cost, cooldown, the 6 AM end, which
pursuits are called off) are covered by `Melange.Tests/OffersTests.cs`. How it works and why: `POLICE-SPIKE.md`.

1. Give yourself Prestige (a Kingpin tier after the first), open Connections: "Lean on the precinct..." is offered for
   1 Prestige. Buy it: Prestige drops by 1, the line reads "The police are looking away until 6 AM", and the log says
   `offer: the police look away until 06:00`.
2. Body searches: carry something illegal, equip a weapon or crouch-run past foot patrols and sentries for a few minutes.
   No "Being searched..." bar, no "comply" stop. Walk through a foot checkpoint before 21:00: no search.
3. Curfew: stay out past 21:15 in sight of police. Nobody arrests you for curfew and the HUD doesn't go wanted. (Another
   crime in sight of them still counts.)
4. Calling off a search: get to "Investigating" (e.g. hit pedestrians three times with a car, or be reported), then buy
   the offer: the wanted HUD clears and the officers give up. At "Arresting" or higher, buying it leaves the chase on.
5. The end: at 06:00 (or after sleeping) the line in Connections goes back to the cooldown note and searches happen
   again. The offer can be bought again 3 in-game days after the day it was bought.
6. Save while it's on, quit to the menu, load: it's still on until 6 AM. Load a different save: it's off.
7. Vehicle checkpoints still search your car: the offer doesn't cover them (see `POLICE-SPIKE.md`).

Results (2026-10-02, slot 1, IL2CPP, scripted with the `levels` probe command): bought at 21:37 in the hard curfew, the
player's `DisobeyingCurfew` state is removed and stays gone, the time since the last body search is held at 0, a second
purchase is refused while it is on, and it is still on at 05:59 the next morning and off at 06:17 (cooldown shown).
Bought in the same frame as an Investigating pursuit, the search is called off (pursuit None) and stays off. An
Arresting chase is left on. (On the test save the player holds a shotgun, and an officer seeing it raises a pursuit to
NonLethal within a second or two: the game's `NoticePlayerBrandishingWeapon`, not the offer.) Not run yet: an actual
patrol walking past without searching, the Connections app's buttons, and save/reload while it is on.
