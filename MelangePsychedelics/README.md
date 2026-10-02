# Melange Psychedelics

Two new products for Schedule I, sold to the customers who already buy shrooms: **Toad** and **LSD**.

Needs MelonLoader 0.7, S1API, and `MelangeCore.dll` (Melange Core 0.2 or later) in `Mods/`. The Melange Sewer spoke is
optional: with it, the sewer story changes how you get the best toads. Version 0.1.0 has not been tested in the game
yet; see `TESTING.md`.

## Toad

1. **Get toads.**
   - **The pond**, most evenings for a few hours. You need a **toad net** (Randy sells them). A **wildlife officer**, Clive
     Mossop in his ranger's khaki, walks round the pond while the toads are out: take one where he can see you (near
     him) and he fines you and takes the toad, and the third time in a night he calls the police. Wait for him to pass.
   - **The sewer**, once you can get in. If you spared the Sewer King he shows you his spot (more toads, and he teaches
     you to keep them better); otherwise there's the odd one by the mushrooms.
   - **Randy's**, round the back of Randy's Bait & Tackle at the Docks, at night for a couple of hours (10 pm to midnight
     by default). Few toads, steep prices, dearer with each one sold.
2. **Keep them** in a **terrarium** (Randy's stall, by day, with crickets and nets). Put toads and tubs of crickets in its
   tray. Each toad eats one tub a night. Two or more fed toads breed a new one every few days, up to six a tank. Toads left
   without food go hungry, then die.
3. **Milk** them: look at the tank's lid. Once a day per toad, you get a Toad venom each. Sewer toads give the best,
   then wild ones, then tank-bred, then Randy's. Well-fed toads give better; hungry ones worse; freshly caught ones are
   stressed for a couple of days.
4. Each toad can be milked **10 times** before it shrivels to dust, so keep them breeding.
5. **Cure** the venom on a drying rack for a better grade, mix it if you like, and bag or jar it.

## LSD

1. **Ergot.** Fungal Phil sells **ergot spores** once you reach Shot Caller. Spores and a grain bag at the spawn station
   make ergot spawn; grow it in a mushroom bed like shrooms (it wants the cold). Ergot itself is worthless to customers.
2. **Ana Slughin**, a chemist, sells a **reagent**, **blank blotter sheets** and the **blotter frame**. She's unlocked like
   any supplier: keep Fiona Hancock, Lily Turner or Pearl Moore happy until one of them passes your number on.
3. **Chemistry station**: 4 ergot and 1 reagent make a vial of **LSD solution** (once Ana is unlocked).
4. **Blotter frame**: put blank sheets and solution in it, pick a **design** at its base, and dose. Each vial makes a sheet
   of 20 tabs.
5. Every sheet has a small chance of being a **bad batch**, bigger the lower the quality. It looks the same; your
   customers find out. A bad trip costs you their goodwill and costs that design its name.
6. **Designs are brands.** A design that keeps giving good trips at good quality gets known, then loved: customers come
   back for it. One known for bad trips is burnt; switch to another.

## Settings

In `UserData/MelonPreferences.cfg`, section `MelangePsychedelics`: milkings before a toad turns to dust, toads per tank,
breeding and starving days, Randy's night hours and markup, the bad-batch odds, the spores' rank, and switches for the
pond, the sewer toads, Randy's stall and ergot growing (off: Phil sells ergot ready to use), and `PaintDesigns`
(experimental, off by default: spray your own design onto a frame's sheet with a spray can). In co-op the host's settings
decide.

## Co-op

The host runs the terrariums, the blotter frames and the brands; in this version only the host can milk, dose and change
designs. Catching toads and buying from Randy work for everyone.

## Known gaps

- The toad, crickets, sheets and the like borrow other items' looks for now; the terrarium, the toads in it and the
  blotter frame have their own models.
- Designs are five built-in ones. Painting your own with the spray can is built but untested in game, so it is behind
  the `PaintDesigns` setting (off by default; host only). Painted designs don't change the look of the LSD item.
- Brands follow your sheets in the order you made them, so selling an older batch after a newer one of the same quality
  can credit the wrong design.
- Removing the mod: Toad and LSD items, terrariums, frames and ergot disappear from the save.

Credits: models made with the Melange Blender scripts (`scripts/art`).
