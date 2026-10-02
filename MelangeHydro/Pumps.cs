using System;
using System.Collections.Generic;
using System.Globalization;
using Melange.Core;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Property;
using UnityEngine;
using PropertyType = Il2CppScheduleOne.Property.Property;

namespace Melange.Hydro
{
    /// <summary>
    /// The reservoir pump: placed near the property's tap, it is hosed to it (the nearest tap within reach, kept in the save)
    /// and tops up every hole at the same property within range, a trickle at a time, from the game's shared clock. It never
    /// uses the tap's own "in use" slot, so the player can still fill a watering can there. Without a pump, trained botanists
    /// top reservoirs up by hand (free water, as the game's botanist watering already is).
    /// </summary>
    /// <remarks>
    /// v1 connects by distance rather than a hose-laying interaction: the hose is drawn from the tap to the pump's spigot.
    /// Levels change on the host and are sent to the other players only in steps of a tenth of capacity, so the network is
    /// not flooded every minute.
    /// </remarks>
    internal static class Pumps
    {
        /// <summary>How far a pump may stand from its tap, and how far from it the holes it serves may be (metres).</summary>
        public const float HoseReach = 10f, ServeRange = 15f;

        private sealed class PumpState { public BuildableItem Item; public Tap Tap; public string Guid; public float NextSearch; public bool ToldNoTap; }
        private static readonly Dictionary<IntPtr, PumpState> _pumps = new Dictionary<IntPtr, PumpState>();
        private static float _nextTick;
        private static int _lastMinute = -1;

        public static void Reset() { _pumps.Clear(); _nextTick = 0f; _lastMinute = -1; }

        public static void Tick()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 2f;
            try
            {
                Scan();
                if (!Host.IsHost || !Settings.Pumps) { _lastMinute = -1; return; }
                int now = Holes.Now();
                int minutes = _lastMinute < 0 ? 0 : Math.Min(now - _lastMinute, 24 * 60);   // a sleep counts, at most a day
                _lastMinute = now;
                if (minutes <= 0) return;
                foreach (var p in _pumps.Values) Pump(p, minutes);
            }
            catch (Exception e) { Mod.Log.Warning("pumps: " + e.Message); _nextTick = Time.unscaledTime + 30f; }
        }

        /// <summary>For the probe: each pump, its tap and the holes in its range.</summary>
        internal static string Describe()
        {
            var parts = new List<string>();
            foreach (var p in _pumps.Values)
            {
                if (p.Item == null || p.Item.Pointer == IntPtr.Zero) continue;
                var at = p.Item.transform.position;
                int served = 0;
                foreach (var h in Holes.All)
                    if (h.Alive && h.Pot.ParentProperty != null && p.Item.ParentProperty != null && h.Pot.ParentProperty.Pointer == p.Item.ParentProperty.Pointer
                        && Vector3.Distance(h.Pot.transform.position, at) <= ServeRange) served++;
                string tap = p.Tap == null || p.Tap.Pointer == IntPtr.Zero ? "no tap" : $"tap {Vector3.Distance(p.Tap.transform.position, at):0.0} m away";
                parts.Add($"pump {p.Guid} at {p.Item.ParentProperty?.PropertyCode}: {tap}, {served} hole(s) in range");
            }
            return parts.Count == 0 ? "no pumps" : string.Join("; ", parts);
        }

        /// <summary>Finds placed pumps (on every player's game, for the look), hoses each to its tap.</summary>
        private static void Scan()
        {
            var props = PropertyType.OwnedProperties;
            var seen = new HashSet<IntPtr>();
            for (int p = 0; props != null && p < props.Count; p++)
            {
                var bi = props[p]?.BuildableItems;
                for (int i = 0; bi != null && i < bi.Count; i++)
                {
                    var item = bi[i];
                    if (item == null || item.ItemInstance?.ID != Units.Pump.Id) continue;
                    seen.Add(item.Pointer);
                    if (!_pumps.TryGetValue(item.Pointer, out var state))
                        _pumps[item.Pointer] = state = new PumpState { Item = item, Guid = item.GUID.ToString() };
                    // taps are scene fixtures: look again now and then, in case the pump was moved nearer one
                    if ((state.Tap == null || state.Tap.Pointer == IntPtr.Zero) && Time.unscaledTime >= state.NextSearch)
                    {
                        state.NextSearch = Time.unscaledTime + 15f;
                        state.Tap = FindTap(state);
                    }
                    Looks.DressPump(item, state.Tap);
                }
            }
            var gone = new List<IntPtr>();
            foreach (var k in _pumps.Keys) if (!seen.Contains(k)) gone.Add(k);
            foreach (var k in gone) _pumps.Remove(k);
        }

        /// <summary>The tap this pump is hosed to: the one the save names, else the nearest within reach (then saved, on the host).</summary>
        private static Tap FindTap(PumpState state)
        {
            var taps = UnityEngine.Object.FindObjectsOfType<Tap>();
            if (taps == null || taps.Length == 0) return null;
            var data = MelangeHydroData.Current;
            Vector3 at = state.Item.transform.position;
            if (data != null && data.PumpTaps.TryGetValue(state.Guid, out var saved) && TryParse(saved, out var savedPos))
                foreach (var t in taps)
                    if (t != null && (t.transform.position - savedPos).sqrMagnitude < 0.04f) return t;
            Tap best = null; float bestD = HoseReach;
            foreach (var t in taps)
            {
                if (t == null) continue;
                float d = Vector3.Distance(t.transform.position, at);
                if (d <= bestD) { best = t; bestD = d; }
            }
            if (best != null && data != null && Host.IsHost) data.PumpTaps[state.Guid] = Format(best.transform.position);
            if (best == null)
            {
                if (!state.ToldNoTap) Mod.Log.Msg($"pump {state.Guid}: no tap within {HoseReach} m; it does nothing until moved nearer one");
                state.ToldNoTap = true;
            }
            else Mod.Log.Msg($"pump {state.Guid}: hosed to the tap {bestD:0.0} m away");
            return best;
        }

        /// <summary>Tops up the holes this pump serves with what it pumped over these minutes.</summary>
        private static void Pump(PumpState p, int minutes)
        {
            if (p.Tap == null || p.Item == null || p.Item.Pointer == IntPtr.Zero) return;
            var at = p.Item.transform.position;
            var prop = p.Item.ParentProperty;
            var served = new List<Hole>();
            foreach (var h in Holes.All)
            {
                if (!h.Alive || h.Pot.ParentProperty == null || prop == null || h.Pot.ParentProperty.Pointer != prop.Pointer) continue;
                if (Vector3.Distance(h.Pot.transform.position, at) > ServeRange) continue;
                served.Add(h);
            }
            if (served.Count == 0) return;
            var levels = new float[served.Count];
            var caps = new float[served.Count];
            for (int i = 0; i < served.Count; i++) { levels[i] = served[i].Pot._currentMoistureAmount; caps[i] = served[i].Pot.MoistureCapacity; }
            var after = Reservoir.Distribute(levels, caps, Reservoir.PumpLitresPerMinute() * minutes, out float used);
            if (used <= 0f) return;
            for (int i = 0; i < served.Count; i++)
            {
                if (after[i] <= levels[i]) continue;
                var h = served[i];
                h.Pot.SetMoistureAmount(after[i]);
                if (h.LastSynced < 0f) h.LastSynced = levels[i];
                if (Reservoir.ShouldSync(h.LastSynced, after[i], caps[i]))
                {
                    h.Pot.SyncMoistureData();
                    h.LastSynced = after[i];
                }
            }
            if (Settings.Verbose) Mod.Log.Msg($"pump {p.Guid}: {used:0.##} L over {minutes} min into {served.Count} reservoir(s)");
        }

        private static string Format(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###}", v.x, v.y, v.z);

        private static bool TryParse(string s, out Vector3 v)
        {
            v = default;
            var parts = (s ?? "").Split(',');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }
    }
}
