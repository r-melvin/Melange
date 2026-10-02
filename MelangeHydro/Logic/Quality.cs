using System;

namespace Melange.Hydro
{
    /// <summary>
    /// The game's quality ladder as numbers. A plant's quality is a float; its tier is read with strict "greater than" lines
    /// (ItemQuality.GetQuality): above 0.9 Heavenly, above 0.75 Premium, above 0.4 Standard, above 0.25 Poor, else Trash. A
    /// plant starts at 0.5 and only additives move it.
    /// </summary>
    public static class Quality
    {
        public const int Trash = 0, Poor = 1, Standard = 2, Premium = 3, Heavenly = 4;
        /// <summary>A plant's quality before additives (Plant.BaseQualityLevel).</summary>
        public const float Base = 0.5f;
        /// <summary>The game's fertiliser.</summary>
        public const float Fertiliser = 0.3f;

        /// <summary>The bottom (exclusive, except for Trash) and top (inclusive) of each tier's band.</summary>
        private static readonly float[] Floor = { 0f, 0.25f, 0.4f, 0.75f, 0.9f };
        private static readonly float[] Ceiling = { 0.25f, 0.4f, 0.75f, 0.9f, 1f };
        private static readonly string[] Names = { "Trash", "Poor", "Standard", "Premium", "Heavenly" };

        public static int TierOf(float q)
        {
            if (q > 0.9f) return Heavenly;
            if (q > 0.75f) return Premium;
            if (q > 0.4f) return Standard;
            if (q > 0.25f) return Poor;
            return Trash;
        }

        public static string Name(int tier) => Names[Math.Max(0, Math.Min(Heavenly, tier))];

        /// <summary>Quality at harvest from the additives' changes (the base plus each additive's change, as the game adds them).</summary>
        public static float AtHarvest(params float[] additiveChanges)
        {
            float q = Base;
            foreach (var c in additiveChanges) q += c;
            return q;
        }

        /// <summary>
        /// The same place one tier higher: as far through the next band as <paramref name="q"/> is through its own, and always
        /// clear of the next band's line, so the tier really changes. Heavenly has nowhere higher to go and stays put.
        /// </summary>
        public static float OneTierUp(float q)
        {
            q = Clamp01(q);
            int t = TierOf(q);
            if (t >= Heavenly) return q;
            float frac = (q - Floor[t]) / (Ceiling[t] - Floor[t]);
            float up = Floor[t + 1] + frac * (Ceiling[t + 1] - Floor[t + 1]);
            return Math.Min(Ceiling[t + 1], Math.Max(up, Floor[t + 1] + 0.005f));
        }

        public static float Clamp01(float q) => q < 0f ? 0f : q > 1f ? 1f : q;
    }
}
