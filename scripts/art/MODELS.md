# Melange models (scripts/art)

All scripts follow `build_gunrack.py`: Blender primitives and bmesh, one flat Principled material per colour, joined into one
object, exported as `<name>.glb` plus `<name>.mesh.json` (one part per colour, Blender (x, y, z) -> Unity (x, z, y), each
triangle's winding reversed) and a `<name>.png` preview with `--render` (view transform "Standard"). Front is Blender +Y
(Unity +Z). Grid cells are 0.5 m. Colours are deliberately lighter than they look in the previews.

Run any of them headless:

    blender -b --python scripts/art/build_X.py -- --out ~/src/Melange/scripts/art/out --render [--only NAME]

| Model | Script | Spoke | Footprint (cells) | Size W x H x D (m) | Triangles | Origin |
|---|---|---|---|---|---|---|
| mixer2 | build_mixers.py | mixer | 2 x 2 | 0.95 x 1.65 x 0.85 | 1184 | bottom centre, floor |
| mixer3 | build_mixers.py | mixer | 3 x 2 | 1.40 x 1.73 x 0.90 | 1508 | bottom centre, floor |
| mixer4 | build_mixers.py | mixer | 4 x 2 | 1.90 x 1.81 x 0.95 | 1788 | bottom centre, floor |
| hydro_tray | build_hydro.py | hydroponics | 3 x 1 | 1.45 x 0.61 x 0.40 | 1200 | bottom centre, floor |
| hydro_hole | build_hydro.py | hydroponics | < 1 (0.15 m flange) | 0.15 x 0.12 x 0.15 | 408 | centre of the flange underside (see below) |
| aero_tower | build_aero.py | hydroponics | 2 x 2 | 0.79 x 2.11 x 0.79 | 1896 | bottom centre, floor |
| pump | build_aero.py | hydroponics (tap-to-pump hose) | 1 x 1 | 0.36 x 0.32 x 0.29 | 668 | bottom centre, floor |
| terrarium | build_terrarium.py | psychedelics | 2 x 1 | 0.96 x 1.14 x 0.50 | 916 | bottom centre, floor |
| toad | build_toad.py | psychedelics | n/a (terrarium item) | 0.19 x 0.08 x 0.17 | 1784 | under the toad, on the surface its feet touch |
| ergot_colony | build_ergot.py | psychedelics | n/a (sits in a mushroom bed) | 0.65 x 0.51 x 0.42 | 1644 | bottom centre, on the bed's soil |
| blotter_frame | build_ergot.py | psychedelics | 2 x 1 | 0.66 x 1.54 x 0.49 | 352 | bottom centre, floor |
| tricorn | build_pirate.py | smuggling | n/a (NPC head) | 0.41 x 0.15 x 0.40 | 1468 | centre of the crown opening (the hat's inner rim) |
| eyepatch | build_pirate.py | smuggling | n/a (NPC head) | 0.20 x 0.07 x 0.22 | 512 | bridge of the nose at eye height, on the face surface |
| speedboat | build_speedboat.py | smuggling | n/a (moored at the Docks) | 2.30 x 3.06 x 7.55 | 1474 | on the waterline, midships, on the centre line |
| pier | build_pier.py | smuggling | n/a (at the Docks quay) | 3.48 x 8.08 x 10.23 | 2448 | the quay's wall face at quay-top height, abreast the pontoon's middle (see below) |

## Attachment points (Unity axes, metres from the model's origin)

- **hydro_tray holes** (place one `hydro_hole` at each): x = -0.56, -0.28, 0, 0.28, 0.56; y = 0.60; z = 0. The hole's origin
  is the underside of its flange, so a hole placed at these points seats on the channel; its cup hangs 0.08 m below.
- **aero_tower ports** (12, four levels of three, each level turned 60 degrees; openings face out and 35 degrees up):
  (0, 0.713, 0.19), (-0.165, 0.713, -0.095), (0.165, 0.713, -0.095),
  (-0.165, 1.013, 0.095), (0, 1.013, -0.19), (0.165, 1.013, 0.095),
  (0, 1.313, 0.19), (-0.165, 1.313, -0.095), (0.165, 1.313, -0.095),
  (-0.165, 1.613, 0.095), (0, 1.613, -0.19), (0.165, 1.613, 0.095).
- **pump hose spigot** tip: about (0.10, 0.16, 0.19), pointing +Z (front).
- **blotter_frame sheet** (for the SpraySurface): centre (0, 0.956, 0.105), 0.60 x 0.40 m (3:2, like the 450 x 300
  canvas), normal (0, 0.139, 0.990) (leaning back 8 degrees).
- **tricorn / eyepatch** are drawn for eyes 0.066 m apart (the eyepatch's patch is centred at x = -0.033, the wearer's
  left eye, front face at z = +0.009); the mod scales both by the wearer's real eye spacing. The eyepatch strap is
  snapped to a 0.19 x 0.23 x 0.21 m ellipsoid head centred 0.095 m behind the origin, so on a differently shaped head it
  may float or clip. The tricorn's gold braid is `metal`.
- **pier** (all numbers are `MelangeSmuggling/Logic/PierLayout.cs`'s, which the game's colliders use; change both together):
  +X out into the basin from the wall face, +Z along the quay, the stair's head at -Z. Pontoon x 0.1..2.6, z -4.5..4.5, deck
  top y -3.65 (0.35 m above water at y -4.0, i.e. a 4 m quay). Stair x 0.1..1.1 against the wall, 20 risers of 0.1825 m on
  0.28 m treads (33 degrees), head at z -4.2, foot on the deck at z 1.4. Landing on the quay top x -0.6..1.1, z -5.2..-4.2,
  top y 0.03. Handrails 1 m. Cleats at (2.4, -3.65, +-2.8), timber bollards at (2.3, -3.65, +-3.9); the boat's centre line
  is x 4.0 (0.25 m of fenders off the pontoon). Guide piles stand 0.6 m above the quay top just off the pontoon's ends.
  `out/pier.png` shows it from the basin with the speedboat alongside, `out/pier_stair.png` from the pontoon's far end; the
  quay, water and boat in the renders are not exported.
- **terrarium**: the sand top (where toads sit) is 0.83 m up; interior about 0.86 x 0.41 m.

## Notes

- mixer4 is 4 x 2 cells, bigger than the 2x2 to 3x2 first estimate, so each tier is visibly larger; hoppers are numbered
  1..n from the player's left; product input (green chute) on the player's left, output tray (orange) on the right.
- The terrarium glass is opaque (its own part, colour `glass`), so the inside shows only from above through the barred lid.
  The mod can make that part translucent at runtime if wanted.
- The ergot colony's sclerotia are oversized so they read at game scale; scale the colony to the mushroom bed's soil area
  (the bed's size wasn't measured).
