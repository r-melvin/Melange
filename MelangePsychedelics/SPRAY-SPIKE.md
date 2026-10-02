# Spray paint spike: the game's graffiti system and blotter designs

Game version 0.4.7 (IL2CPP). Sources, and how far each claim goes:

- **[I]** = signature checked in the IL2CPP interop assembly `MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll` (with
  `scripts/dev/cecilq`; the Polyfill-patched copy and `Assembly-CSharp.dll.polyfill-orig` give the same graffiti and vehicle
  signatures). Interop assemblies hold signatures only, no method bodies.
- **[M]** = behaviour read from the decompiled **Mono** build of the same version (`~/src/decomp/s1-mono-0.4.7f7`, file
  `ScheduleOne.Graffiti/*`). The IL2CPP build is compiled from the same C#, including FishNet's generated RPC code, so it
  should behave the same, but that is an inference, not something checked in IL2CPP.
- **[F]** = FishNet runtime decompiled from the Mono build's `Managed/FishNet.Runtime.dll`; field names checked in
  `Il2CppFishNet.Runtime.dll` [I].

Nothing here was run in the game.

## 1. Types (namespace `Il2CppScheduleOne.Graffiti`)

| Type | What it is |
|---|---|
| `SpraySurface : NetworkBehaviour` | A paintable canvas. Base class of the world walls and used as is on vehicles. |
| `WorldSpraySurface : SpraySurface, IGUIDRegisterable` | A wall in the map: GUID, region, XP and cartel influence, saved by `GraffitiManager`. |
| `SpraySurfaceInteraction : MonoBehaviour` | `[RequireComponent(typeof(SpraySurface))]` [M]: the prompt, camera, cursor and painting input for one surface. |
| `Drawing` | Plain class (not a Unity object): the stroke list plus the output texture and undo history. |
| `SprayStroke` | `[Serializable]` class: one straight stroke. |
| `UShort2` | Struct `{ ushort X, Y }`, pixel coordinates from the canvas's bottom-left. |
| `ESprayColor : byte` | `None, Black, White, Red, Green, Blue, Yellow, Pink, Brown` (0..8) [I]. |
| `SprayDisplay : MonoBehaviour` | Draws a surface's texture through its URP `DecalProjector` [I][M]. |
| `SerializedGraffitiDrawing : ScriptableObject` | An authored drawing asset (used by NPC/cartel graffiti) [I]. |
| `GraffitiManager : NetworkSingleton<GraffitiManager>` | Saves the world walls; the brush falloff table [I][M]. |
| `Il2CppScheduleOne.UI.GraffitiMenu : Singleton<GraffitiMenu>` | The graffiti screen (colours, brush sizes, undo, clear, done) [I][M]. |
| `Il2CppScheduleOne.Persistence.Datas.SpraySurfaceData` | `List<SprayStroke> Strokes; bool ContainsCartelGraffiti` [I]. `WorldSpraySurfaceData` adds `GUID`, `HasDrawingBeenFinalized` [I]. |

## 2. How a drawing is stored

**As a stroke list; the texture is derived.**

- `SprayStroke` fields [I]: `UShort2 Start`, `UShort2 End`, `ESprayColor Color`, `byte StrokeSize`; constructor
  `SprayStroke(UShort2 start, UShort2 end, ESprayColor color, byte strokeSize)`. Brush constants [I]: `StrokeSize_Min` 10,
  `_Small` 10, `_Medium` 16, `_Large` 24, `_ExtraLarge` 32, `_Max` 32; `StrokeSizePresets` = {10, 16, 24, 32} [M].
- `SpraySurface` [I]: `int Width` (default 450 [M]), `int Height` (default 300 [M]), `const float PIXEL_SIZE` (0.006666671 m
  [M], so 450 x 300 is 3 x 2 m), `bool Editable`, `bool IsVandalismSurface`, `Transform BottomLeftPoint`,
  `DecalProjector Projector`, `protected Drawing drawing`, `Il2CppSystem.Action onDrawingChanged`,
  `int DrawingStrokeCount`, `Texture DrawingOutputTexture`, `int DrawingPaintedPixelCount`, `Vector3 CenterPoint`,
  `Vector3 TopRightPoint`, `NetworkObject CurrentEditor`.
- `Drawing` [I]: `Drawing(int width, int height, bool initPixels)`, `List<SprayStroke> GetStrokes()`,
  `void AddStrokes(List<SprayStroke>)`, `Texture2D OutputTexture`, `int StrokeCount`, `int PaintedPixelCount`. [M]: the texture
  is RGBA32 at the next power of two (512 x 512 for 450 x 300), cleared to transparent; each stroke is rasterised pixel by
  pixel with the brush falloff from `NetworkSingleton<GraffitiManager>.Instance.GetPixelStrength(byte, int)` (so drawing
  needs the manager to exist). Undo keeps 10 texture snapshots in a `Texture2DArray`.
- Reading strokes out [I]: `SpraySurface.GetSaveData() : SpraySurfaceData` (its `Strokes` is the live list [M]);
  `GetSerializedDrawing() : SerializedGraffitiDrawing` (strokes shifted to the drawing's bounds [M]; throws on a surface whose
  `drawing` is null [M]).
- Putting strokes back [I]: `SpraySurface.Set(NetworkConnection conn, Il2CppReferenceArray<SprayStroke> strokes, bool
  isCartelGraffiti)` (`[ObserversRpc(RunLocally = true)][TargetRpc]` [M]: with `conn` null it sends to observers and runs
  `RpcLogic___Set_4105842735` locally, which makes a new `Drawing` and adds the strokes). Also
  `LoadSerializedDrawing(SerializedGraffitiDrawing, bool)`, which centres an asset drawing and calls `Set` [M].
- **The game's own binary form loses the brush:** `SprayStroke.Serialize(BinaryWriter)` writes `Start.X, Start.Y, End.X,
  End.Y, (byte)Color` and `Deserialize` reads the same, never `StrokeSize` [M] (the members exist [I]). So the mod keeps its
  own text form.
- How the game saves them [M]: world walls through `GraffitiManager.GetSaveString()` (folder/file "Graffiti",
  `WorldSpraySurfaceData` per surface that `ShouldSave()`), loaded by `GraffitiLoader.LoadSpraySurface(WorldSpraySurfaceData)`
  [I]. Vehicles through `LandVehicle.GetSpraySurfaceData() : List<SpraySurfaceData>` and `LandVehicle.Load(VehicleData,
  string)` [I], which calls `_spraySurfaces[i].Set(null, strokes, cartel)` [M]; `VehicleData.SpraySurfaces` [I]. Both are
  Newtonsoft JSON of the stroke objects; neither is open to a mod's own objects without patching the save path.

## 3. How the spray UI opens [M]

1. `SpraySurfaceInteraction.Awake()` hooks its `InteractableObject IntObj` (`onHovered`, `onInteractStart`), registers an
   exit listener, hides its world-space `Canvas`, and sizes the canvas, `IntObj` and `CameraPosition` from the surface's
   `Width`/`Height` (`ResizeCanvas`).
2. `Hovered()`: with the item `"spraypaint"` equipped and `SpraySurface.CanBeEdited(true)` (no `CurrentEditor`, **no strokes
   yet**, `Editable`), the prompt reads "Use spray can". With `"graffiticleaner"` equipped and strokes present: "Clean
   graffiti". Otherwise the prompt is disabled.
3. `Interacted()` -> `Open()` (private): pushes its `MonoState State`, `SetCurrentEditor_Server(Player.Local.NetworkObject)`,
   `EnsureDrawingExists()`, moves the player camera to `CameraPosition` (`PlayerCamera.OverrideTransform`, FOV 70), applies
   the player visual state `"graffiti"` = `EVisualState.Vandalizing` **if `IsVandalismSurface`**, opens
   `Singleton<GraffitiMenu>.Instance` and subscribes to its `onColorSelected`, `onWeightSelected`, `onClearClicked`,
   `onDone`, `onUndoClicked`, `onConfirmClicked`, then `GraffitiMenu.SetActiveSurface(surface)`.
4. Painting (`FixedUpdate`): ray from the pointer onto the `BottomLeftPoint` plane, pixel = position / (Width, Height),
   kept a brush half-width from the edges; pixels drawn locally while the button is held; on release `EndStroke` turns them
   into straight strokes (`SprayStroke.GetStrokesFromPixels`) and calls `AddStrokes_Server(List<SprayStroke>, int)`. Paint
   runs out at 25,000 painted pixels times `PaintedPixelLimitMultiplier`.
5. Closing (Done, or Esc then confirm): `OnClose()` calls `SpraySurface.OnEditingFinished()` (virtual: the world wall gives
   50 XP once and marks itself finalised), **removes one `"spraypaint"` from the inventory if anything was painted**,
   restores the camera and closes the menu.
6. `SpraySurfaceInteraction.IsOpen` [I] is public (get); `Open`, `Close`, `Interacted` are private [I] (callable through
   interop, but the input path above needs nothing from us).

## 4. Networking of a surface [M][F]

Each mutation is a FishNet RPC: `AddStrokes_Server` (ServerRpc, RunLocally) -> `AddStrokes_Client` (ObserversRpc,
RunLocally); `AddTextureToHistory_*` and `Undo_*` the same; `Set` (ObserversRpc/TargetRpc, RunLocally);
`SetCurrentEditor_Server` and `ClearDrawing` are ServerRpcs **without** RunLocally. A late joiner gets the strokes from
`OnSpawnServer` -> `GraffitiManager.QueueSurfaceToReplicate` -> `ReplicateTo`.

The generated writers start with `if (!base.IsClientInitialized)` (or `IsServerInitialized`) `{ log a warning; }` and only
otherwise send; the RunLocally ones then run their logic anyway. `NetworkBehaviour.IsClientInitialized` is
`_networkObjectCache.IsClientInitialized` [F]; `_networkObjectCache` is a `[SerializeField]` field [F], exposed in interop as
`NetworkBehaviour._networkObjectCache` [I]. So:

- On a surface that is **not spawned** but whose `_networkObjectCache` is a never-initialised NetworkObject, painting,
  undo and `Set` work locally, each logging FishNet's "Cannot complete action because client/server is not active"
  warning; the editor lock and the menu's **Clear** do nothing.
- With `_networkObjectCache` null, the same calls throw (null dereference) before the local logic.
- A copy made with `Object.Instantiate` keeps references to objects outside the copied hierarchy, so a copy of a **live**
  vehicle's surface would keep that vehicle's NetworkObject and send our strokes to that vehicle's surface on other peers.

## 5. Can it be pointed at a custom surface?

Not by configuration: `SpraySurfaceInteraction` has no API to target a different surface, and both components carry many
serialised references (`SpraySurface`, `IntObj`, `CameraPosition`, `Canvas`, `SprayImg`, `SpraySound`, `CleanSound`,
`State`, `BottomLeftPoint`, `Projector`), so building one from scratch with `AddComponent` is not practical. What is
practical is **copying an existing surface-with-interaction** and placing the copy on the frame:

- Template: vehicles. `NetworkSingleton<VehicleManager>.Instance.VehiclePrefabs : List<LandVehicle>` [I] and
  `LandVehicle._spraySurfaces : Il2CppReferenceArray<SpraySurface>` [I] (a plain `SpraySurface`, not a world wall:
  `LandVehicle.Load` sets them and `SetIsPlayerOwned` makes them `Editable` [M]). Prefab copies are the safe choice because a
  prefab's NetworkObject is never initialised (section 4). World walls are the wrong template: a copy would join
  `GraffitiManager.WorldSpraySurfaces` in `Start()`, register a duplicate `BakedGUID`, be saved into the game's graffiti
  file, give XP and be eligible for NPC/cartel graffiti [M].
- Which vehicle prefabs carry a surface, and whether its interaction parts sit inside the surface's own GameObject, is
  prefab data, not code: unknown until run.
- Size: set `Width`/`Height` before `Awake` (instantiate under an inactive parent), then scale the copy. 450 x 300 px at
  0.2 scale fits the frame's 0.6 x 0.4 m sheet; the camera distance and canvas are derived from the same numbers.
  `DecalProjector.scaleMode` [I] (URP, `Unity.RenderPipelines.Universal.Runtime`) must be `InheritFromHierarchy` or the decal
  ignores the scale (URP's default mode is `ScaleInvariant`: Unity documentation, not checked in this game).
- Drawing a stroke list into a texture without any surface is possible: `new Drawing(w, h, true)` + `AddStrokes` +
  `OutputTexture` [I], given the GraffitiManager exists [M]. Not used yet (it is the route to sheet textures and icons).

## 6. What was built (behind `PaintDesigns`, off by default)

`Painting.cs` (game layer) and `Logic/Strokes.cs` (pure, tested):

- Each blotter frame (host only, model present) gets a copy of the first vehicle prefab's `SpraySurface` +
  `SpraySurfaceInteraction`, 450 x 300, scaled onto the sheet, `Editable`, `IsVandalismSurface = false`, decal scale
  inherited. The copy is refused (logged, feature off for that frame) if any of the parts listed above lies outside it, or if
  its `_networkObjectCache` is a live object.
- A frame showing a built-in design shows a blank canvas: with a spray can in hand the frame's own dose prompt steps aside
  and the game's "Use spray can" opens the graffiti screen. Closing it with strokes makes a new design (`painted:N`, named
  after its main colour, e.g. "Red design 1"), selected on that frame and saved in `MelangePsychedelicsData` as text
  (`Design.Drawing` = `s1:x0,y0,x1,y1,colour,size;...`). Painted designs join the cycle on the frame's base with the five
  built-ins and earn reputation the same way.
- A frame showing a painted design shows its strokes (`Set`) and its canvas takes no interaction (so no cleaning or painting
  over it); cycle to a built-in to get a blank canvas again.
- The menu's Clear is a no-op on the copy (section 4), so while the copy is open the mod adds its own handler to
  `GraffitiMenu.onClearClicked` that clears with `Set(null, [], false)`.
- The game takes one spray can per finished painting (its own `OnClose`).

Not built: per-sheet textures or icons for the LSD item (no per-item data, see TESTING.md assumption 8), naming a design,
retiring one, co-op clients painting.
