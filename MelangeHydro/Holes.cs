using System;
using System.Collections.Generic;
using HarmonyLib;
using Melange.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence;
using UnityEngine;
using PropertyType = Il2CppScheduleOne.Property.Property;

namespace Melange.Hydro
{
    /// <summary>One placed growing site: the game's pot, what it really is, and its bookkeeping.</summary>
    internal sealed class Hole
    {
        public Pot Pot;
        public UnitSpec Unit;
        public HoleKind Kind => Unit.Kind;
        public string Guid;
        /// <summary>The pot's real item while, during a save, it wears the Grow Tent's.</summary>
        public ItemInstance RealWhileSaving;
        /// <summary>The reservoir level the other players last heard about.</summary>
        public float LastSynced = -1f;
        public bool Alive => Pot != null && Pot.Pointer != IntPtr.Zero && Pot.gameObject != null;
    }

    /// <summary>
    /// The holes: every placed section or frame is a cloned Grow Tent, a real Pot, recognised by its item ID on every player's
    /// game. Four seams, all on virtual or multi-line methods (IL2CPP inlines one-liners and a patch on them silently never
    /// runs):
    /// <list type="bullet">
    /// <item>Placement (Pot.InitializeGridItem, postfix, last): the pot learns it is a hole and gets the hole's numbers (a slow
    /// reservoir, no Grow Tent yield penalty, the nutrient allowed). It runs after Production Expansion Reborn's postfix,
    /// which scales the drain, so ours is the value that holds.</item>
    /// <item>Speed (Pot.GetTemperatureGrowthMultiplier, postfix): the hole's factor on top. The pot's speed field is left
    /// alone, because Production Expansion Reborn overwrites it.</item>
    /// <item>A botanist's top-up (WaterPotBehaviour.OnActionSuccess, postfix, host): the game forgets to send the new level
    /// to the other players after a botanist waters; for holes we send it, and share it across a grouped tray.</item>
    /// <item>Saving: holes are saved under the Grow Tent's item ID, so without the mod they load as Grow Tents with their
    /// plants; the spoke's own save turns them back into holes by GUID.</item>
    /// </list>
    /// </summary>
    internal static class Holes
    {
        private static readonly Dictionary<IntPtr, Hole> _holes = new Dictionary<IntPtr, Hole>();
        private static bool _wearingTents;
        private static float _nextTick;

        public static IEnumerable<Hole> All => _holes.Values;

        public static void Reset() { _holes.Clear(); _wearingTents = false; _nextTick = 0f; }

        public static Hole Get(Pot pot)
        {
            if (pot == null) return null;
            return _holes.TryGetValue(pot.Pointer, out var h) && h.Alive ? h : null;
        }

        public static Hole Get(Il2CppScheduleOne.Growing.GrowContainer container) => container == null ? null : Get(container.TryCast<Pot>());

        // ------------------------------------------------------------------ patches

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            TryPatch(harmony, "hole placement", typeof(Pot), nameof(Pot.InitializeGridItem), nameof(AfterInitialize), Priority.Last);
            TryPatch(harmony, "hole speed", typeof(Pot), nameof(Pot.GetTemperatureGrowthMultiplier), nameof(AfterTemperature), Priority.Normal);
            TryPatch(harmony, "botanist top-up", typeof(WaterPotBehaviour), nameof(WaterPotBehaviour.OnActionSuccess), nameof(AfterWatered), Priority.Normal);
        }

        internal static void TryPatch(HarmonyLib.Harmony harmony, string what, Type type, string method, string postfix, int priority, Type patches = null)
        {
            try
            {
                var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method);
                harmony.Patch(target, postfix: new HarmonyMethod(patches ?? typeof(Holes), postfix) { priority = priority });
            }
            catch (Exception e) { Mod.Log.Error($"{what}: patch failed ({e.Message}); that part of hydro will not work"); }
        }

        // A placed or loaded pot learns its item here, on every player's game. A hole loaded from a save arrives as a Grow
        // Tent (that is how it was saved); on the host, the spoke's save says which GUIDs are really holes, and the pot gets
        // its real item back before anything else asks.
        private static void AfterInitialize(Pot __instance, string GUID)
        {
            try
            {
                var unit = Units.ById(__instance.ItemInstance?.ID);
                if (unit == null && __instance.ItemInstance?.ID == Units.DonorId) unit = Restore(__instance, GUID);
                if (unit == null || unit.Kind == HoleKind.None) return;
                var hole = new Hole { Pot = __instance, Unit = unit, Guid = GUID };
                _holes[__instance.Pointer] = hole;
                Configure(hole);
                if (Host.IsHost && MelangeHydroData.Current != null && !string.IsNullOrEmpty(GUID)) MelangeHydroData.Current.Holes[GUID] = unit.Id;
                Grouping.OnPlaced(hole);
                if (Settings.Verbose) Mod.Log.Msg($"{unit.Name} {GUID}: drain {__instance._moistureDrainPerHour:0.###}/h of {__instance.MoistureCapacity}, yield x{__instance.YieldMultiplier}");
            }
            catch (Exception e) { Mod.Log.Warning("hole setup: " + e.Message); }
        }

        /// <summary>A Grow Tent the save says is a hole: give it its real item (host only; clients get the real item from the host).</summary>
        private static UnitSpec Restore(Pot pot, string guid)
        {
            var data = MelangeHydroData.Current;
            if (!Host.IsHost || data == null || string.IsNullOrEmpty(guid) || !data.Holes.TryGetValue(guid, out var realId)) return null;
            var unit = Units.ById(realId);
            var def = Il2CppScheduleOne.Registry.GetItem(realId);
            if (unit == null || def == null) return null;
            pot.ItemInstance = def.GetDefaultInstance(1);
            return unit;
        }

        private static void Configure(Hole hole)
        {
            var pot = hole.Pot;
            var spec = Units.Spec(hole.Kind);
            pot.YieldMultiplier = spec.YieldMultiplier;
            pot._moistureDrainPerHour = Reservoir.DrainPerHour(pot.MoistureCapacity, Settings.ReservoirHours(hole.Kind));
            AllowJuicer(pot);
        }

        /// <summary>
        /// Lets the hole take Grow N Juicer: a new array (the prefab's may be shared, so never changed in place), and the
        /// pot's configuration offers it in its additive slots (their options were copied from the array when it was built).
        /// Vanilla pots never get it, so the game itself refuses the nutrient anywhere else.
        /// </summary>
        private static void AllowJuicer(Pot pot)
        {
            var juicer = Items.Juicer();
            if (juicer == null) return;
            var allowed = pot.AllowedAdditives;
            int n = allowed?.Length ?? 0;
            for (int i = 0; i < n; i++) if (allowed[i]?.ID == juicer.ID) return;
            var more = new Il2CppReferenceArray<AdditiveDefinition>(n + 1);
            for (int i = 0; i < n; i++) more[i] = allowed[i];
            more[n] = juicer;
            pot.AllowedAdditives = more;
            var cfg = pot.potConfiguration;
            if (cfg == null) return;
            foreach (var field in new[] { cfg.Additive1, cfg.Additive2, cfg.Additive3 })
            {
                var options = field?.Options;
                if (options == null) continue;
                bool has = false;
                for (int i = 0; i < options.Count; i++) if (options[i]?.ID == juicer.ID) { has = true; break; }
                if (!has) options.Add(juicer);
            }
        }

        private static void AfterTemperature(Pot __instance, ref float __result)
        {
            if (_holes.TryGetValue(__instance.Pointer, out var h)) __result *= Settings.Speed(h.Kind);
        }

        // The host's botanist has just set the reservoir to 90-100%: tell the other players, and fill the rest of a grouped tray.
        private static void AfterWatered(WaterPotBehaviour __instance)
        {
            try
            {
                if (!Host.IsHost) return;
                var hole = Get(__instance._growContainer);
                if (hole == null) return;
                hole.Pot.SyncMoistureData();
                hole.LastSynced = hole.Pot._currentMoistureAmount;
                Grouping.ShareTopUp(hole);
                if (Settings.Verbose) Mod.Log.Msg($"{hole.Unit.Name} {hole.Guid}: topped up by a botanist to {hole.Pot.NormalizedMoistureAmount:P0}");
            }
            catch (Exception e) { Mod.Log.Warning("botanist top-up: " + e.Message); }
        }

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// Before the game writes its save, every hole wears a Grow Tent item, so the save holds only vanilla IDs: without the
        /// mod each hole loads as a Grow Tent with its plant and soil kept. The real item comes back when the save is done.
        /// </summary>
        public static void BeforeSave()
        {
            try
            {
                var tent = Il2CppScheduleOne.Registry.GetItem(Units.DonorId);
                if (tent == null) return;
                foreach (var h in All)
                {
                    if (!h.Alive || h.RealWhileSaving != null) continue;
                    h.RealWhileSaving = h.Pot.ItemInstance;
                    h.Pot.ItemInstance = tent.GetDefaultInstance(1);
                }
                _wearingTents = true;
            }
            catch (Exception e) { Mod.Log.Warning("could not save the holes as Grow Tents: " + e.Message); }
        }

        public static void AfterSave()
        {
            foreach (var h in All)
            {
                if (h.RealWhileSaving == null) continue;
                try { if (h.Alive) h.Pot.ItemInstance = h.RealWhileSaving; } catch (Exception e) { Mod.Log.Warning("hole item not restored: " + e.Message); }
                h.RealWhileSaving = null;
            }
            _wearingTents = false;
        }

        /// <summary>
        /// After the save loads: any Grow Tent the spoke's save names as a hole and that was set up before the save data was
        /// read (the load order is unproven) is turned back into a hole now.
        /// </summary>
        public static void AfterLoad()
        {
            var data = MelangeHydroData.Current;
            if (data == null || !Host.IsHost) return;
            int fixedUp = 0;
            foreach (var pot in PotsAtOwnedProperties())
            {
                if (_holes.ContainsKey(pot.Pointer) || pot.ItemInstance?.ID != Units.DonorId) continue;
                string guid = pot.GUID.ToString();
                if (!data.Holes.ContainsKey(guid)) continue;
                AfterInitialize(pot, guid);
                fixedUp++;
            }
            if (fixedUp > 0) Mod.Log.Warning($"{fixedUp} hole(s) were set up late (after their plants); an aero plant among them has lost its extra bud sites until replanted");
            Mod.Log.Msg($"{_holes.Count} hole(s) in this save");
        }

        public static IEnumerable<Pot> PotsAtOwnedProperties()
        {
            var props = PropertyType.OwnedProperties;
            for (int p = 0; props != null && p < props.Count; p++)
            {
                var bi = props[p]?.BuildableItems;
                for (int i = 0; bi != null && i < bi.Count; i++)
                {
                    var pot = bi[i]?.TryCast<Pot>();
                    if (pot != null) yield return pot;
                }
            }
        }

        // ------------------------------------------------------------------ the tick

        public static void Tick()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            try
            {
                if (_wearingTents && !SavingNow()) AfterSave();      // the save ended without telling us
                var dead = new List<IntPtr>();
                foreach (var kv in _holes)
                {
                    var h = kv.Value;
                    if (!h.Alive) { dead.Add(kv.Key); continue; }
                    Looks.DressHole(h);
                    if (Host.IsHost && !_wearingTents) FillMedium(h);
                }
                foreach (var k in dead) _holes.Remove(k);
            }
            catch (Exception e) { Mod.Log.Warning("holes: " + e.Message); _nextTick = Time.unscaledTime + 30f; }
        }

        private static bool SavingNow()
        {
            try { return Singleton<SaveManager>.Instance != null && Singleton<SaveManager>.Instance.IsSaving; } catch { return false; }
        }

        private static bool Loading()
        {
            try { var lm = Singleton<LoadManager>.Instance; return lm == null || lm.IsLoading || !lm.IsGameLoaded; } catch { return true; }
        }

        /// <summary>
        /// A newly placed hole gets its grow medium (the game's Extra Long-Life Soil with many uses) poured in for the player,
        /// once. A hole loaded with soil already counts as filled. When the medium's uses run out, any soil will do.
        /// </summary>
        private static void FillMedium(Hole h)
        {
            var data = MelangeHydroData.Current;
            if (data == null || string.IsNullOrEmpty(h.Guid) || data.Filled.Contains(h.Guid) || Loading()) return;
            var pot = h.Pot;
            if (pot.CurrentSoil == null)
            {
                var medium = Il2CppScheduleOne.Registry.GetItem(Units.MediumSoilId)?.TryCast<SoilDefinition>();
                if (medium == null) return;
                pot.SetSoil(medium);
                pot.SetSoilAmount(pot.SoilCapacity);
                pot.SetRemainingSoilUses(Units.MediumUses);
                pot.SyncSoilData();
                pot.SetMoistureAmount(pot.MoistureCapacity);
                pot.SyncMoistureData();
                Mod.Log.Msg($"{h.Unit.Name} {h.Guid}: grow medium in ({Units.MediumUses} harvests), reservoir full");
            }
            data.Filled.Add(h.Guid);
        }

        /// <summary>The game's total minutes (days x 1440 + minutes today): the clock every player shares.</summary>
        public static int Now()
        {
            var tm = NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance;
            if (tm == null) return 0;
            try { return tm.GetTotalMinSum(); }
            catch { return tm.ElapsedDays * 1440 + Il2CppScheduleOne.GameTime.TimeManager.GetMinSumFrom24HourTime(tm.CurrentTime); }
        }
    }
}
