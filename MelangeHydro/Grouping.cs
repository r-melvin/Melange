using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.Building;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Persistence;
using UnityEngine;
using UnityEngine.Events;

namespace Melange.Hydro
{
    /// <summary>
    /// The seam between the two ways of placing trays and towers (Settings.Placement):
    /// <list type="bullet">
    /// <item>Individual (the default): every hole is its own item, placed on its own; a frame placed anyway (bought while
    /// grouping was on) is one hole wearing the whole frame. Nothing here runs.</item>
    /// <item>Grouped (experimental, unproven in game): placing a Hydro Tray or Aeroponic Tower makes the host create the
    /// frame's other holes as more pots on the same tiles, through the game's own BuildManager.CreateGridItem (so they are
    /// networked, saved and botanist-assignable like any pot). Each player's game works out which hole sits where from what
    /// the game already sends: pots of the same kind on the same grid and origin as a frame are its holes, numbered by GUID
    /// (Layout.Order). Picking the frame up removes its empty holes; a hole with a plant stays where it is.</item>
    /// </list>
    /// What is unproven: whether the game lets several pots share tiles when created by code, and how the overlapping pots'
    /// colliders behave for the player (Looks shrinks them to each hole). See TESTING.md.
    /// </summary>
    internal static class Grouping
    {
        private static readonly HashSet<string> _hooked = new HashSet<string>();

        public static void Reset() => _hooked.Clear();

        /// <summary>A hole has just been placed or loaded.</summary>
        public static void OnPlaced(Hole hole)
        {
            if (!hole.Unit.IsFrame || !Host.IsHost || string.IsNullOrEmpty(hole.Guid)) return;
            WatchPickup(hole);
            if (Settings.Placement != Placement.Grouped) return;
            var data = MelangeHydroData.Current;
            if (data == null || data.Frames.ContainsKey(hole.Guid) || Loading()) return;   // loaded: its holes come from the save
            data.Frames[hole.Guid] = new List<string>();
            MelonLoader.MelonCoroutines.Start(PlaceSiblings(hole));
        }

        // Next frame, so the frame has finished its own setup and spawn before more pots go on its tiles.
        private static System.Collections.IEnumerator PlaceSiblings(Hole frame)
        {
            yield return null;
            try
            {
                var gi = frame.Pot.TryCast<GridItem>();
                var section = Il2CppScheduleOne.Registry.GetItem(Units.SectionFor(frame.Kind).Id);
                var bm = NetworkSingleton<BuildManager>.Instance;
                if (gi == null || section == null || bm == null) { Mod.Log.Warning($"{frame.Unit.Name}: could not place its holes"); yield break; }
                var list = MelangeHydroData.Current.Frames[frame.Guid];
                int n = Layout.SiblingsToPlace(frame.Unit, list.Count);
                for (int i = 0; i < n; i++)
                {
                    string guid = Guid.NewGuid().ToString();
                    var placed = bm.CreateGridItem(section.GetDefaultInstance(1), gi.OwnerGrid, gi._originCoordinate, gi._rotation, guid, null);
                    if (placed == null) { Mod.Log.Warning($"{frame.Unit.Name}: hole {i + 1} was refused by the game"); continue; }
                    list.Add(guid);
                }
                Mod.Log.Msg($"{frame.Unit.Name} {frame.Guid}: placed {list.Count} more hole(s) on its tiles");
            }
            catch (Exception e) { Mod.Log.Warning($"{frame.Unit.Name}: placing its holes failed: {e.Message}"); }
        }

        /// <summary>When the frame is picked up, its empty holes go with it (the host destroys them through the game's own request).</summary>
        private static void WatchPickup(Hole frame)
        {
            if (!_hooked.Add(frame.Guid)) return;
            frame.Pot.onDestroyed.AddListener((UnityAction)new Action(() =>
            {
                try
                {
                    int removed = 0, kept = 0;
                    foreach (var h in Siblings(frame))
                    {
                        if (h.Pot.Plant != null) { kept++; continue; }
                        h.Pot.Destroy_Server();
                        removed++;
                    }
                    MelangeHydroData.Current?.Frames.Remove(frame.Guid);
                    _hooked.Remove(frame.Guid);
                    if (removed + kept > 0) Mod.Log.Msg($"{frame.Unit.Name} picked up: {removed} empty hole(s) removed, {kept} with plants left in place");
                }
                catch (Exception e) { Mod.Log.Warning("frame pickup: " + e.Message); }
            }));
        }

        /// <summary>The frame a hole sits in: a frame of its kind on the same grid and origin (itself for a frame), or null.</summary>
        public static Hole FrameOf(Hole hole)
        {
            if (hole.Unit.IsFrame) return hole;
            var gi = hole.Pot.TryCast<GridItem>();
            if (gi == null) return null;
            foreach (var h in Holes.All)
            {
                if (!h.Unit.IsFrame || h.Kind != hole.Kind || !h.Alive) continue;
                var fg = h.Pot.TryCast<GridItem>();
                if (fg != null && SameSpot(fg, gi)) return h;
            }
            return null;
        }

        /// <summary>A frame's other holes: sections of its kind on the same grid and origin.</summary>
        public static List<Hole> Siblings(Hole frame)
        {
            var list = new List<Hole>();
            var fg = frame.Pot.TryCast<GridItem>();
            if (fg == null) return list;
            foreach (var h in Holes.All)
            {
                if (h == frame || h.Unit.IsFrame || h.Kind != frame.Kind || !h.Alive) continue;
                var g = h.Pot.TryCast<GridItem>();
                if (g != null && SameSpot(fg, g)) list.Add(h);
            }
            return list;
        }

        /// <summary>The hole's place in its frame (0 for the frame), the same on every player's game; -1 if it has no frame.</summary>
        public static int IndexInFrame(Hole hole, out Hole frame)
        {
            frame = FrameOf(hole);
            if (frame == null) return -1;
            var guids = new List<string>();
            foreach (var s in Siblings(frame)) guids.Add(s.Guid);
            return Layout.IndexOf(frame.Guid, guids, hole.Guid);
        }

        private static bool SameSpot(GridItem a, GridItem b)
            => a.OwnerGrid != null && b.OwnerGrid != null && a.OwnerGrid.Pointer == b.OwnerGrid.Pointer
               && (a._originCoordinate - b._originCoordinate).sqrMagnitude < 0.01f;

        /// <summary>A grouped tray shares one reservoir: topping up one hole raises the others to the same level (host).</summary>
        public static void ShareTopUp(Hole topped)
        {
            var frame = FrameOf(topped);
            if (frame == null) return;
            float fraction = topped.Pot.NormalizedMoistureAmount;
            var all = Siblings(frame);
            all.Add(frame);
            foreach (var h in all)
            {
                if (h == topped) continue;
                float level = Reservoir.SharedLevel(h.Pot._currentMoistureAmount, h.Pot.MoistureCapacity, fraction);
                if (level <= h.Pot._currentMoistureAmount) continue;
                h.Pot.SetMoistureAmount(level);
                h.Pot.SyncMoistureData();
                h.LastSynced = level;
            }
        }

        private static bool Loading()
        {
            try { var lm = Singleton<LoadManager>.Instance; return lm == null || lm.IsLoading || !lm.IsGameLoaded; } catch { return true; }
        }
    }
}
