using System;
using System.Collections.Generic;
using HarmonyLib;
using Melange.Core;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Management;
using UnityEngine;

namespace Melange.Hydro
{
    /// <summary>
    /// Clipboard bulk assignment. Every hole is a pot, so a botanist's pot list is picked hole by hole in the game's object
    /// selector (the clipboard's "Assigns" field opens ObjectSelector with the botanist's pot limit as its maximum). With this:
    /// <list type="bullet">
    /// <item>Clicking a hole selects its whole tray (a run of touching holes of one kind: a grouped frame's holes, or sections
    /// placed side by side), nearest first, up to the limit; clicking a selected hole deselects the whole tray. Holding the
    /// game's Crouch button clicks one hole only.</item>
    /// <item>The game's Reload button adds every tray in the property, whole trays first, up to the limit.</item>
    /// <item>Holes the botanist isn't trained for are never added in bulk (host; a client doesn't know the training).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// One seam: a prefix on ObjectSelector.Update (a Unity message, so the engine calls it through the patched entry and it
    /// can't be inlined). On the frame of a click it changes only the clicked hole's tray-mates in the selector's own list,
    /// then lets the game's Update handle the clicked hole as always (its toggle, outline, and closing when the list is full),
    /// which is why adding keeps one slot for it. Nothing is sent or saved here: the game applies the list, networked, when the
    /// selector is submitted. ObjectSelector.ObjectClicked is not patched, since the call from Update could be inlined.
    /// </remarks>
    internal static class Clipboard
    {
        private static ManagementInterface _ui;
        private static float _nextHintCheck;

        public static void Reset() { _ui = null; _nextHintCheck = 0f; }

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(ObjectSelector), "Update") ?? throw new MissingMethodException(nameof(ObjectSelector), "Update");
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Clipboard), nameof(BeforeUpdate)));
            }
            catch (Exception e) { Mod.Log.Error($"clipboard bulk assign: patch failed ({e.Message}); holes are assigned one by one"); }
        }

        private static void BeforeUpdate(ObjectSelector __instance)
        {
            try
            {
                if (!Settings.ClipboardBulk || __instance == null || !__instance.IsOpen || __instance.maxSelectedObjects < 2) return;
                AddHint(__instance);
                bool click = GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick);
                bool all = GameInput.GetButtonDown(GameInput.ButtonCode.Reload);
                if (click && !GameInput.GetButton(GameInput.ButtonCode.Crouch)) ClickTray(__instance);
                else if (all) AssignAll(__instance);
            }
            catch (Exception e) { Mod.Log.Warning("clipboard bulk assign: " + e.Message); }
        }

        // ------------------------------------------------------------------ a click on a hole

        private static void ClickTray(ObjectSelector sel)
        {
            var hovered = sel.GetHoveredObject();           // the same look raycast the game's Update makes this frame
            var clicked = Holes.Get(hovered?.TryCast<Pot>());
            if (clicked == null || !sel.IsObjectTypeValid(hovered, out _)) return;
            var holes = new List<Hole> { clicked };
            foreach (var h in Holes.All)
                if (h != clicked && h.Alive && h.Kind == clicked.Kind && SameProperty(h, clicked)) holes.Add(h);
            var spots = Spots(holes);
            var run = Bulk.Run(spots, 0);
            if (run.Count < 2) return;

            var list = sel.selectedObjects;
            var chosen = SelectedPointers(list);
            var level = TrainingFor(sel.maxSelectedObjects);
            var change = Bulk.Click(run, i => chosen.Contains(holes[i].Pot.Pointer), i => Eligible(sel, holes[i], level), list.Count, sel.maxSelectedObjects);
            if (change.Others.Count == 0) return;
            foreach (int i in change.Others) Toggle(sel, holes[i].Pot, change.Add);
            if (Settings.Verbose) Mod.Log.Msg($"clipboard: {(change.Add ? "added" : "removed")} {change.Others.Count} more hole(s) of a {run.Count}-hole {clicked.Kind} tray");
        }

        // ------------------------------------------------------------------ every tray in the property

        private static void AssignAll(ObjectSelector sel)
        {
            int added = FillAll(sel, TrainingFor(sel.maxSelectedObjects));
            if (added > 0 && sel.selectedObjects.Count >= sel.maxSelectedObjects) sel.CloseAndSubmit();   // as the game closes a full list after a click
        }

        /// <summary>
        /// The Reload key's work on the selector's own list: every tray in its property, whole trays nearest the player first,
        /// within its limit, holes the botanist isn't trained for (<paramref name="level"/>, null = unknown) left out. Returns
        /// how many were added; -1 when the property has no holes. The probe runs it on the closed selector set up as the
        /// botanist's list would open it.
        /// </summary>
        internal static int FillAll(ObjectSelector sel, TrainingLevel? level)
        {
            var target = sel.targetProperty;
            var holes = new List<Hole>();
            foreach (var h in Holes.All)
                if (h.Alive && (target == null || (h.Pot.ParentProperty != null && h.Pot.ParentProperty.Pointer == target.Pointer))) holes.Add(h);
            if (holes.Count == 0) return -1;

            var list = sel.selectedObjects;
            int max = sel.maxSelectedObjects;
            var chosen = SelectedPointers(list);
            var eye = PlayerPosition();
            var trays = new List<(float Distance, IReadOnlyList<int> Free)>();
            foreach (var run in Bulk.Runs(Spots(holes)))
            {
                var free = new List<int>();
                float nearest = float.MaxValue;
                foreach (int i in run)
                {
                    var pot = holes[i].Pot;
                    nearest = Mathf.Min(nearest, (pot.transform.position - eye).sqrMagnitude);
                    if (!chosen.Contains(pot.Pointer) && Eligible(sel, holes[i], level)) free.Add(i);
                }
                if (free.Count > 0) trays.Add((nearest, free));
            }
            trays.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var ordered = new List<IReadOnlyList<int>>();
            foreach (var t in trays) ordered.Add(t.Free);
            var add = Bulk.FillAll(ordered, max - list.Count);
            foreach (int i in add) Toggle(sel, holes[i].Pot, true);
            Mod.Log.Msg($"clipboard: every tray: {add.Count} hole(s) added ({list.Count}/{max})");
            return add.Count;
        }

        // ------------------------------------------------------------------ helpers

        private static void Toggle(ObjectSelector sel, Pot pot, bool on)
        {
            var list = sel.selectedObjects;
            if (on) list.Add(pot); else list.Remove(pot);
            if (sel.IsOpen) sel.SetSelectionOutline(pot, on);
        }

        /// <summary>Whether the selector would take the hole (type, property, not another botanist's) and the botanist is trained for it.</summary>
        private static bool Eligible(ObjectSelector sel, Hole h, TrainingLevel? level)
            => sel.IsObjectTypeValid(h.Pot, out _) && (level == null || Training.CanOperate(level.Value, h.Kind));

        /// <summary>
        /// The training of the botanist whose pot list is open, or null when it isn't known (a co-op client, which has no
        /// training data; or a selector that isn't a botanist's pot list): then training doesn't filter, and the host's check
        /// (Courses) still relieves an untrained botanist afterwards.
        /// </summary>
        private static TrainingLevel? TrainingFor(int max)
        {
            if (!Host.IsHost) return null;
            var b = OpenBotanist(max);
            return b == null ? (TrainingLevel?)null : Courses.Level(b);
        }

        /// <summary>The botanist being edited on the clipboard, if the open selector is his pot list (its limit is his).</summary>
        private static Botanist OpenBotanist(int max)
        {
            var ui = Ui();
            var cfgs = ui?.Configurables;
            if (cfgs == null || cfgs.Count != 1) return null;
            var b = cfgs[0]?.TryCast<Botanist>();
            var assigns = b?.configuration?.Assigns;
            return assigns != null && assigns.MaxItems == max ? b : null;
        }

        // The cartel spoke found ManagementInterface's singleton null while the clipboard was equipped; look it up instead.
        internal static ManagementInterface Ui()
        {
            if (_ui != null && _ui.Pointer != IntPtr.Zero) return _ui;
            _ui = null;
            try { _ui = Singleton<ManagementInterface>.Instance; } catch { }
            if (_ui == null)
                foreach (var x in Resources.FindObjectsOfTypeAll<ManagementInterface>())
                    if (x != null && x.gameObject.scene.IsValid()) { _ui = x; break; }
            return _ui;
        }

        /// <summary>Tells the player about the tools in the selector's title, when a botanist's pot list is open in a property with holes.</summary>
        private static void AddHint(ObjectSelector sel)
        {
            string title = sel.selectionTitle ?? "";
            if (title.Contains("[hole:") || Time.unscaledTime < _nextHintCheck) return;
            _nextHintCheck = Time.unscaledTime + 0.5f;
            if (OpenBotanist(sel.maxSelectedObjects) == null) return;
            var target = sel.targetProperty;
            foreach (var h in Holes.All)
            {
                if (!h.Alive || (target != null && (h.Pot.ParentProperty == null || h.Pot.ParentProperty.Pointer != target.Pointer))) continue;
                sel.selectionTitle = Bulk.Hint(title);
                return;
            }
        }

        private static HashSet<IntPtr> SelectedPointers(Il2CppSystem.Collections.Generic.List<BuildableItem> list)
        {
            var set = new HashSet<IntPtr>();
            for (int i = 0; list != null && i < list.Count; i++) if (list[i] != null) set.Add(list[i].Pointer);
            return set;
        }

        private static bool SameProperty(Hole a, Hole b)
        {
            var pa = a.Pot.ParentProperty; var pb = b.Pot.ParentProperty;
            return pa != null && pb != null && pa.Pointer == pb.Pointer;
        }

        /// <summary>Each hole's grid and the grid tiles it covers (GridItem.CoordinatePairs: footprint tile to grid tile).</summary>
        private static List<SiteSpot> Spots(List<Hole> holes)
        {
            var spots = new List<SiteSpot>(holes.Count);
            foreach (var h in holes)
            {
                var gi = h.Pot.TryCast<GridItem>();
                var grid = gi?.OwnerGrid;
                var tiles = new List<(int X, int Y)>();
                var pairs = gi?.CoordinatePairs;
                for (int i = 0; pairs != null && i < pairs.Count; i++)
                {
                    var c = pairs[i]?.coord2;
                    if (c != null) tiles.Add((c.x, c.y));
                }
                spots.Add(new SiteSpot(grid == null ? "" : grid.Pointer.ToInt64().ToString(), h.Kind, tiles));
            }
            return spots;
        }

        private static Vector3 PlayerPosition()
        {
            try { var cam = PlayerSingleton<PlayerCamera>.Instance; if (cam != null) return cam.transform.position; } catch { }
            return Vector3.zero;
        }
    }
}
