using System;
using System.Collections.Generic;

namespace Melange.Hydro
{
    /// <summary>
    /// The reservoir is the game's own moisture, drained much more slowly: a pot's moisture is litres up to its capacity,
    /// drained by a per-hour rate on every player's game, and growth stops at zero. So botanists, the watering can, the pot's
    /// UI and saving all keep working; only the numbers change.
    /// </summary>
    public static class Reservoir
    {
        /// <summary>
        /// Top up when below this fraction full. Above the game's botanist thresholds (0.2 urgent, 0.3 routine), so a pump
        /// keeps reservoirs out of the botanists' way.
        /// </summary>
        public const float PumpStartFraction = 0.9f;
        /// <summary>
        /// Share of the tap's flow the pump takes. The tap's flow is 6 per minute when fully open (Tap.ActualFlowRate = 6 x how far
        /// it is open); the pump takes a trickle so it never blocks the tap for the player's watering can.
        /// </summary>
        public const float PumpShareOfTap = 1f / 12f;
        public const float TapFullFlow = 6f;
        /// <summary>Only send a moisture change over the network when it is at least this fraction of capacity, or fills it.</summary>
        public const float SyncStepFraction = 0.1f;

        /// <summary>The drain per in-game hour that empties <paramref name="capacity"/> in <paramref name="hours"/>.</summary>
        public static float DrainPerHour(float capacity, float hours)
        {
            if (capacity <= 0f) return 0f;
            if (hours <= 0f || float.IsNaN(hours) || float.IsInfinity(hours)) return capacity;   // nonsense setting: a vanilla-ish hour
            return capacity / hours;
        }

        /// <summary>Hours from full to dry at a drain per hour (infinite with no drain).</summary>
        public static float HoursToDry(float capacity, float drainPerHour)
            => drainPerHour <= 0f ? float.PositiveInfinity : capacity / drainPerHour;

        /// <summary>Litres a pump adds per in-game minute, from the tap's full flow.</summary>
        public static float PumpLitresPerMinute(float tapFullFlow = TapFullFlow) => Math.Max(0f, tapFullFlow) * PumpShareOfTap;

        /// <summary>
        /// Shares <paramref name="litres"/> between reservoirs, the emptiest (by fraction) first, each up to its capacity, only
        /// those below the pump's start line taking any. Returns the new levels (same order) and how many litres were used.
        /// </summary>
        public static float[] Distribute(IReadOnlyList<float> levels, IReadOnlyList<float> capacities, float litres, out float used)
        {
            int n = levels.Count;
            var result = new float[n];
            for (int i = 0; i < n; i++) result[i] = levels[i];
            used = 0f;
            if (litres <= 0f || n == 0) return result;
            var order = new List<int>();
            for (int i = 0; i < n; i++)
                if (capacities[i] > 0f && levels[i] < capacities[i] * PumpStartFraction) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = (levels[a] / capacities[a]).CompareTo(levels[b] / capacities[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            float left = litres;
            foreach (int i in order)
            {
                if (left <= 0f) break;
                float room = capacities[i] - result[i];
                float add = Math.Min(room, left);
                result[i] += add;
                left -= add;
            }
            used = litres - left;
            return result;
        }

        /// <summary>Whether a reservoir changed enough since the last sync to send it to the other players.</summary>
        public static bool ShouldSync(float lastSynced, float now, float capacity)
        {
            if (capacity <= 0f) return false;
            if (now >= capacity - 0.0001f && lastSynced < capacity - 0.0001f) return true;
            return Math.Abs(now - lastSynced) >= capacity * SyncStepFraction;
        }

        /// <summary>
        /// A shared reservoir (one tray): when one hole is topped up to <paramref name="toppedTo"/> of its capacity, every
        /// other hole of the same frame is raised to the same fraction (never lowered).
        /// </summary>
        public static float SharedLevel(float siblingLevel, float siblingCapacity, float toppedFraction)
        {
            float wanted = siblingCapacity * Quality.Clamp01(toppedFraction);
            return siblingLevel > wanted ? siblingLevel : wanted;
        }
    }
}
