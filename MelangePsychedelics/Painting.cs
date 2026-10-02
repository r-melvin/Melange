using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.Vehicles;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Painted blotter designs (experimental, setting "PaintDesigns", off by default): each blotter frame gets a copy of the
    /// game's own spray canvas (a vehicle's SpraySurface with its SpraySurfaceInteraction, copied from the vehicle prefab) over
    /// its sheet. With a spray can in hand the player gets the game's "Use spray can", paints in the game's graffiti screen, and
    /// on closing it the strokes become a new design in the save, reusable on any number of sheets like a built-in one. A frame
    /// showing a built-in design shows a blank canvas, ready to paint; one showing a painted design shows the painting.
    /// Host only (designs are host save data). See SPRAY-SPIKE.md for the game code this leans on.
    /// </summary>
    /// <remarks>
    /// The copy is never spawned on the network. Its RPCs that run locally (adding strokes, undo, history, Set) still work; the
    /// rest (editor lock, the menu's Clear) only log FishNet's "not active" warning, so Clear is redone here. The copy's network
    /// object reference must be the prefab's (never initialised): one pointing at a live object would send our strokes to
    /// that object's peers, so such a template is refused.
    /// </remarks>
    internal static class Painting
    {
        internal sealed class Canvas
        {
            public GameObject Root;
            public SpraySurface Surface;
            public SpraySurfaceInteraction Ui;
            /// <summary>The design the canvas shows (null until first shown).</summary>
            public string Shown;
            public bool Failed;
            public bool ClearHooked;
        }

        private const string SprayCanId = "spraypaint";
        // just in front of the sheet's face (model units), inside the frame's dose prompt box (0.04 deep each way), so that
        // prompt is hit first unless it is hidden for painting
        private const float FaceOffset = 0.01f;

        private static GameObject _template;
        private static bool _searched;
        private static Il2CppSystem.Action _onClear;
        private static Canvas _open;

        public static void Reset() { _template = null; _searched = false; _onClear = null; _open = null; }

        /// <summary>A canvas over a frame's sheet (centre and tilt in model units), or a failed one (logged once) when the game's canvas can't be copied safely.</summary>
        public static Canvas Create(GameObject model, Vector3 sheetCentre, Quaternion sheetTilt, float sheetWidth)
        {
            var c = new Canvas();
            GameObject holder = null;
            try
            {
                var template = Template();
                if (template == null) { c.Failed = true; return c; }
                holder = new GameObject("Melange canvas");
                holder.SetActive(false);                 // so the copy's Awake waits until its size is set
                holder.transform.SetParent(model.transform, false);
                holder.transform.localPosition = sheetCentre + sheetTilt * new Vector3(0f, 0f, FaceOffset);
                holder.transform.localRotation = sheetTilt;
                var copy = UnityEngine.Object.Instantiate(template, holder.transform);
                copy.name = "Melange spray canvas";
                c.Root = holder;
                c.Surface = copy.GetComponent<SpraySurface>();
                c.Ui = copy.GetComponent<SpraySurfaceInteraction>();
                if (!Safe(copy, c)) { UnityEngine.Object.Destroy(holder); c.Root = null; c.Failed = true; return c; }

                c.Surface.Width = StrokeCodec.CanvasWidth;
                c.Surface.Height = StrokeCodec.CanvasHeight;
                c.Surface.Editable = true;
                c.Surface.IsVandalismSurface = false;    // painting your own sheet isn't vandalism (no police visual state)
                if (c.Surface.Projector != null) c.Surface.Projector.scaleMode = DecalScaleMode.InheritFromHierarchy;
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one * (sheetWidth / (StrokeCodec.CanvasWidth * SpraySurface.PIXEL_SIZE));
                holder.SetActive(true);
                Align(copy.transform, c.Surface, c.Ui, holder.transform);
                Mod.Log.Msg($"spray canvas on a frame: scale {copy.transform.localScale.x:F3}, centre {c.Surface.CenterPoint}, camera {(c.Ui.CameraPosition != null ? c.Ui.CameraPosition.position.ToString() : "none")}");
            }
            catch (Exception e)
            {
                Mod.Log.Warning("spray canvas: " + e.Message);
                if (holder != null) UnityEngine.Object.Destroy(holder);
                c.Root = null;
                c.Failed = true;
            }
            return c;
        }

        /// <summary>
        /// The game's canvas to copy: the first vehicle prefab's SpraySurface that has its own SpraySurfaceInteraction. Prefabs,
        /// not the vehicles in the world, because a copy keeps its original's network object, and a prefab's is never live.
        /// </summary>
        private static GameObject Template()
        {
            if (_template != null || _searched) return _template;
            _searched = true;
            var vm = NetworkSingleton<VehicleManager>.Instance;
            var prefabs = vm != null ? vm.VehiclePrefabs : null;
            for (int i = 0; prefabs != null && i < prefabs.Count; i++)
            {
                var v = prefabs[i];
                var surfaces = v != null ? v._spraySurfaces : null;
                for (int j = 0; surfaces != null && j < surfaces.Length; j++)
                {
                    var s = surfaces[j];
                    if (s == null || s.GetComponent<SpraySurfaceInteraction>() == null) continue;
                    _template = s.gameObject;
                    Mod.Log.Msg($"spray canvas template: {v.VehicleCode} / {s.gameObject.name} ({s.Width} x {s.Height}), components: {Components(s.gameObject)}");
                    return _template;
                }
            }
            Mod.Log.Warning($"no vehicle prefab with a spray surface ({(prefabs == null ? "no VehicleManager" : prefabs.Count + " prefab(s)")}): painted designs are off");
            return null;
        }

        private static string Components(GameObject go)
        {
            var names = new List<string>();
            foreach (var comp in go.GetComponents<Component>()) if (comp != null) names.Add(comp.GetIl2CppType().Name);
            return string.Join(", ", names) + $"; {go.transform.childCount} child(ren)";
        }

        /// <summary>
        /// Refuses a copy that would touch anything outside itself: every part the canvas drives must be inside the copy, and
        /// its network object must not be a live one.
        /// </summary>
        private static bool Safe(GameObject copy, Canvas c)
        {
            if (c.Surface == null || c.Ui == null) { Mod.Log.Warning("spray canvas: the copy lost its surface or interaction"); return false; }
            var root = copy.transform;
            var outside = new List<string>();
            void Check(string name, Component part) { if (part == null) outside.Add(name + " (missing)"); else if (!part.transform.IsChildOf(root)) outside.Add(name); }
            Check("BottomLeftPoint", c.Surface.BottomLeftPoint);
            Check("Projector", c.Surface.Projector);
            Check("IntObj", c.Ui.IntObj);
            Check("CameraPosition", c.Ui.CameraPosition);
            Check("Canvas", c.Ui.Canvas);
            Check("SprayImg", c.Ui.SprayImg);
            Check("SpraySound", c.Ui.SpraySound);
            Check("CleanSound", c.Ui.CleanSound);
            Check("State", c.Ui.State);
            if (outside.Count > 0) { Mod.Log.Warning("spray canvas: parts outside the copy: " + string.Join(", ", outside) + "; painted designs are off"); return false; }
            var nob = c.Surface._networkObjectCache;
            if (nob != null && (nob.IsClientInitialized || nob.IsServerInitialized))
            {
                Mod.Log.Warning($"spray canvas: the copy points at a live network object ({nob.gameObject.name}); painted designs are off");
                return false;
            }
            int nested = copy.GetComponentsInChildren<Il2CppFishNet.Object.NetworkObject>(true).Length;
            Mod.Log.Msg($"spray canvas copy: network object {(nob == null ? "none" : nob.gameObject.name)}, {nested} network object(s) inside");
            return true;
        }

        /// <summary>
        /// Puts the drawing's centre on the sheet and faces it out of the frame. The game paints along its BottomLeftPoint's
        /// plane with the camera on the canvas's +Z side; if the camera ends up behind the sheet, the copy is turned round.
        /// </summary>
        private static void Align(Transform copy, SpraySurface s, SpraySurfaceInteraction ui, Transform sheet)
        {
            var blp = s.BottomLeftPoint;
            copy.rotation = sheet.rotation * Quaternion.Inverse(Quaternion.Inverse(copy.rotation) * blp.rotation);
            copy.position += sheet.position - s.CenterPoint;
            if (ui.CameraPosition != null && Vector3.Dot(ui.CameraPosition.position - s.CenterPoint, sheet.forward) < 0f)
            {
                copy.RotateAround(s.CenterPoint, sheet.up, 180f);
                copy.position += sheet.position - s.CenterPoint;
                Mod.Log.Msg("spray canvas: turned round (its camera was behind the sheet)");
            }
        }

        public static bool SprayCanInHand()
        {
            var inv = PlayerSingleton<PlayerInventory>.Instance;
            return inv != null && inv.isAnythingEquipped && inv.equippedSlot?.ItemInstance?.ID == SprayCanId;
        }

        /// <summary>Blank and free: the game's "Use spray can" is on offer.</summary>
        public static bool Paintable(Canvas c) => c != null && !c.Failed && c.Surface != null && c.Surface.CanBeEdited(true);

        public static bool IsOpen(Canvas c) => c != null && !c.Failed && c.Ui != null && c.Ui.IsOpen;

        /// <summary>
        /// Keeps a frame's canvas in step with its design, and turns a finished painting into a new design. Returns the new
        /// design when one was just painted (the caller makes it the frame's), else null.
        /// </summary>
        public static Design Sync(Canvas c, Design selected, DesignBook book)
        {
            if (c == null || c.Failed || c.Surface == null) return null;
            if (IsOpen(c)) { HookClear(c); return null; }
            if (c.ClearHooked) UnhookClear(c);

            var shown = c.Shown == null ? null : book.Find(c.Shown);
            if (c.Shown != null && c.Surface.DrawingStrokeCount > 0 && (shown == null || shown.Preset || !StrokeCodec.HasStrokes(shown.Drawing)))
                return Capture(c, book);

            if (selected != null && selected.Id != c.Shown) Show(c, selected);
            // a painted design isn't to be cleaned or painted over: its canvas takes no interaction (and doesn't hide the frame's prompts)
            var intObj = c.Ui.IntObj;
            if (intObj != null && intObj.gameObject != c.Surface.gameObject)
            {
                bool blank = c.Surface.DrawingStrokeCount == 0;
                if (intObj.gameObject.activeSelf != blank) intObj.gameObject.SetActive(blank);
            }
            return null;
        }

        private static Design Capture(Canvas c, DesignBook book)
        {
            var strokes = Read(c.Surface);
            if (strokes.Count == 0)
            {
                Mod.Log.Warning($"spray canvas: {c.Surface.DrawingStrokeCount} stroke(s) painted, none usable; clearing");
                Show(c, null);
                return null;
            }
            var d = book.AddPainted(DesignNamer.Name(strokes, book.NextPainted), StrokeCodec.Encode(strokes));
            c.Shown = d.Id;
            Mod.Log.Msg($"painted design {d.Id} ({d.Name}): {strokes.Count} stroke(s), {d.Drawing.Length} chars");
            return d;
        }

        private static List<Stroke> Read(SpraySurface s)
        {
            var list = new List<Stroke>();
            var data = s.GetSaveData();
            var strokes = data != null ? data.Strokes : null;
            for (int i = 0; strokes != null && i < strokes.Count; i++)
            {
                var st = strokes[i];
                if (st == null) continue;
                var stroke = new Stroke(st.Start.X, st.Start.Y, st.End.X, st.End.Y, (int)st.Color, st.StrokeSize);
                if (StrokeCodec.Valid(stroke, StrokeCodec.CanvasWidth, StrokeCodec.CanvasHeight)) list.Add(stroke);
            }
            return list;
        }

        /// <summary>Shows a design on the canvas: a painted one's strokes, or blank for a built-in (or none).</summary>
        private static void Show(Canvas c, Design d)
        {
            var strokes = d != null && !d.Preset ? StrokeCodec.Decode(d.Drawing) : new List<Stroke>();
            var arr = new Il2CppReferenceArray<SprayStroke>(strokes.Count);
            for (int i = 0; i < strokes.Count; i++)
            {
                var s = strokes[i];
                arr[i] = new SprayStroke(new UShort2(s.X0, s.Y0), new UShort2(s.X1, s.Y1), (ESprayColor)s.Color, s.Size);
            }
            // Set is an observers RPC that runs locally: on this unspawned copy it logs FishNet's warning and redraws here only
            c.Surface.Set(null, arr, false);
            c.Shown = d?.Id;
        }

        /// <summary>
        /// The graffiti menu's Clear is a server RPC that doesn't run locally, so on our copy it does nothing; while our canvas
        /// is open, a handler of ours clears it the local way.
        /// </summary>
        private static void HookClear(Canvas c)
        {
            if (c.ClearHooked) return;
            var menu = Singleton<GraffitiMenu>.Instance;
            if (menu == null) return;
            _onClear ??= new Action(ClearOpen);
            menu.onClearClicked = menu.onClearClicked == null ? _onClear : Il2CppSystem.Delegate.Combine(menu.onClearClicked, _onClear).Cast<Il2CppSystem.Action>();
            c.ClearHooked = true;
            _open = c;
        }

        private static void UnhookClear(Canvas c)
        {
            c.ClearHooked = false;
            if (_open == c) _open = null;
            var menu = Singleton<GraffitiMenu>.Instance;
            if (menu == null || _onClear == null || menu.onClearClicked == null) return;
            var rest = Il2CppSystem.Delegate.Remove(menu.onClearClicked, _onClear);
            menu.onClearClicked = rest == null ? null : rest.Cast<Il2CppSystem.Action>();
        }

        private static void ClearOpen()
        {
            try
            {
                var c = _open;
                if (c == null || !IsOpen(c) || c.Ui.isPaintingStroke) return;
                var empty = new Il2CppReferenceArray<SprayStroke>(0);
                c.Surface.Set(null, empty, false);
            }
            catch (Exception e) { Mod.Log.Warning("spray canvas clear: " + e.Message); }
        }
    }
}
