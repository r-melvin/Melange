using System;

namespace Melange.Mixers
{
    /// <summary>
    /// Mix time. The game times a mix as MixTimePerItem x quantity (whole in-game minutes per item, set on each station), so
    /// a machine's speed is its own MixTimePerItem: the Mk2's, times the tier's multiplier. Whole minutes mean rounding: with
    /// the Mk2's code default of 15 a 1.25x machine takes 19 minutes an item, not 18.75.
    /// </summary>
    public static class Timing
    {
        public const float MinMultiplier = 0.1f, MaxMultiplier = 10f;

        /// <summary>A usable multiplier from a settings value: the default for nonsense, otherwise clamped to a sane range.</summary>
        public static float Sanitise(float multiplier, float fallback)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f) return fallback;
            return Math.Min(MaxMultiplier, Math.Max(MinMultiplier, multiplier));
        }

        /// <summary>Minutes per item for a machine whose base (the Mk2's) is <paramref name="mk2PerItem"/>; never below one.</summary>
        public static int MixTimePerItem(int mk2PerItem, float multiplier)
        {
            if (mk2PerItem <= 0) mk2PerItem = 1;
            return Math.Max(1, (int)Math.Round(mk2PerItem * (double)multiplier, MidpointRounding.AwayFromZero));
        }

        /// <summary>How many times faster one mix on the machine is than the same mixes chained through a Mk2.</summary>
        public static float SpeedUpOverChaining(int mixers, float multiplier) => multiplier <= 0f ? 0f : mixers / multiplier;
    }
}
