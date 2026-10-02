using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Growing;
using UnityEngine;

namespace Melange.Hydro
{
    /// <summary>
    /// What the aeroponic tower does to its plants, on every player's game (plants are simulated separately on each, so
    /// every change here must come out the same everywhere):
    /// <list type="bullet">
    /// <item>Extra bud sites (Plant.Initialize, virtual, postfix): copies of the plant's own sites at fixed offsets, so the yield
    /// passes the game's cap. Fixed numbers, so every game builds the same plant and the bud indexes the host sends agree.</item>
    /// <item>Curing (Plant.MinPass, virtual, postfix): once fully grown, the plant's quality follows the curing curve, from the
    /// game time it was first seen fully grown (the host saves it).</item>
    /// </list>
    /// The harvest itself is untouched: the game reads the plant's quality and active buds as always (the botanist's harvest
    /// loop indexes the active buds, so the product's quantity is never inflated).
    /// </summary>
    internal static class Plants
    {
        private sealed class Cure { public int GrownAt; public float Offset; public int LastTier = -1; }
        private static readonly Dictionary<IntPtr, Cure> _cures = new Dictionary<IntPtr, Cure>();
        private static readonly HashSet<IntPtr> _extended = new HashSet<IntPtr>();

        public static void Reset() { _cures.Clear(); _extended.Clear(); }

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            Holes.TryPatch(harmony, "aero bud sites", typeof(Plant), nameof(Plant.Initialize), nameof(AfterPlantInitialize), HarmonyLib.Priority.Normal, typeof(Plants));
            Holes.TryPatch(harmony, "aero curing", typeof(Plant), nameof(Plant.MinPass), nameof(AfterMinPass), HarmonyLib.Priority.Normal, typeof(Plants));
        }

        // The plant has just been set up in its pot (sown, or re-sown from a save), its sites all off, before any bud is set.
        private static void AfterPlantInitialize(Plant __instance)
        {
            try
            {
                if (!Settings.ExtraSites) return;
                var hole = Holes.Get(__instance.Pot);
                var spec = hole == null ? null : Units.Spec(hole.Kind);
                if (spec == null || spec.ExtraBudSites <= 0 || !_extended.Add(__instance.Pointer)) return;
                int n = AddSites(__instance, spec.ExtraBudSites);
                if (Settings.Verbose) Mod.Log.Msg($"{hole.Unit.Name} {hole.Guid}: plant has {n} bud sites");
            }
            catch (Exception e) { Mod.Log.Warning("extra bud sites: " + e.Message); }
        }

        /// <summary>Adds <paramref name="extra"/> sites to the final stage (inactive, as the game leaves its own); returns the new count.</summary>
        private static int AddSites(Plant plant, int extra)
        {
            var stage = plant.FinalGrowthStage;
            var sites = stage?.GrowthSites;
            if (sites == null || sites.Length == 0) return 0;
            int n0 = sites.Length;
            var more = new Il2CppReferenceArray<Transform>(n0 + extra);
            for (int i = 0; i < n0; i++) more[i] = sites[i];
            for (int k = 0; k < extra; k++)
            {
                var src = sites[Yield.ExtraSiteSource(k, n0)];
                var (angle, radius, lift) = Yield.ExtraSitePlacement(k);
                var copy = UnityEngine.Object.Instantiate(src.gameObject, src.parent);
                copy.name = src.gameObject.name + "_melange" + k;
                var turn = Quaternion.Euler(0f, angle, 0f);
                var p = src.localPosition;
                copy.transform.localPosition = turn * new Vector3(p.x * radius, 0f, p.z * radius) + new Vector3(0f, p.y + lift, 0f);
                copy.transform.localRotation = turn * src.localRotation;
                copy.SetActive(false);
                more[n0 + k] = copy.transform;
            }
            stage.GrowthSites = more;
            return n0 + extra;
        }

        // Every in-game minute, for every plant (growing or not).
        private static void AfterMinPass(Plant __instance)
        {
            try
            {
                var hole = Holes.Get(__instance.Pot);
                if (hole == null || !Units.Spec(hole.Kind).Cures) return;
                var data = MelangeHydroData.Current;
                if (__instance.NormalizedGrowthProgress < 1f)
                {
                    // still growing: whatever was recorded belonged to a plant harvested before this one
                    _cures.Remove(__instance.Pointer);
                    if (Host.IsHost && data != null && hole.Guid != null) data.GrownAt.Remove(hole.Guid);
                    return;
                }
                if (!Settings.Curing) return;
                int now = Holes.Now();
                if (!_cures.TryGetValue(__instance.Pointer, out var cure))
                {
                    int grownAt = now;
                    if (Host.IsHost && data != null && hole.Guid != null)
                    {
                        if (data.GrownAt.TryGetValue(hole.Guid, out int saved) && saved <= now) grownAt = saved;
                        else data.GrownAt[hole.Guid] = now;
                    }
                    _cures[__instance.Pointer] = cure = new Cure { GrownAt = grownAt };
                }
                // anything else that moved the quality since (an additive) stays; only our own offset is replaced
                float grown = __instance.QualityLevel - cure.Offset;
                float target = Curing.QualityAt(grown, Curing.MinutesSince(cure.GrownAt, now));
                cure.Offset = target - grown;
                __instance._QualityLevel_k__BackingField = target;
                int tier = Quality.TierOf(target);
                if (tier != cure.LastTier)
                {
                    if (cure.LastTier >= 0 || Settings.Verbose)
                        Mod.Log.Msg($"{hole.Unit.Name} {hole.Guid}: curing {Curing.StageAt(Curing.MinutesSince(cure.GrownAt, now))}, {Quality.Name(tier)} ({target:0.###}, grown {grown:0.###})");
                    cure.LastTier = tier;
                }
            }
            catch (Exception e) { Mod.Log.Warning("curing: " + e.Message); }
        }

        /// <summary>
        /// The probe's curing fast-forward: the curve is computed from when the plant was first seen grown, so moving that
        /// time back by <paramref name="minutes"/> (here and in the host's save) is the same as that time passing; then one
        /// Plant.MinPass runs the curing step as the clock would. Returns null when the plant isn't curing.
        /// </summary>
        internal static string AgeCure(Plant plant, Hole hole, int minutes)
        {
            if (plant == null) return null;
            if (!_cures.ContainsKey(plant.Pointer)) plant.MinPass(1);       // grown since the last minute: let the curing step see it
            if (!_cures.TryGetValue(plant.Pointer, out var cure)) return null;
            cure.GrownAt -= minutes;
            var data = MelangeHydroData.Current;
            if (Host.IsHost && data != null && hole?.Guid != null) data.GrownAt[hole.Guid] = cure.GrownAt;
            plant.MinPass(1);
            return Describe(plant);
        }

        /// <summary>For the log: a cured plant's state (null if not curing).</summary>
        public static string Describe(Plant plant)
        {
            if (plant == null || !_cures.TryGetValue(plant.Pointer, out var c)) return null;
            int m = Curing.MinutesSince(c.GrownAt, Holes.Now());
            return $"{Curing.StageAt(m)} {m} min, quality {plant.QualityLevel:0.###}";
        }
    }
}
