using System;

namespace Melange.Hydro
{
    /// <summary>
    /// Buds at harvest. The game (Plant.GrowthDone, on the host) activates round(BaseYieldQuantity x YieldMultiplier) bud
    /// sites, clamped to [1, the final stage's site count]: 16 for weed and 20 for coca, so weed is capped from a multiplier of
    /// 1.33. The aeroponic tower passes the cap by adding sites to the plant when it is set up, the same way on every peer.
    /// </summary>
    public static class Yield
    {
        /// <summary>BaseYieldQuantity for weed and coca alike.</summary>
        public const int BaseQuantity = 12;
        public const int WeedSites = 16, CocaSites = 20;
        /// <summary>The game's PGR (x1.5 yield).</summary>
        public const float Pgr = 1.5f;

        /// <summary>Buds the game activates for a yield multiplier and a site count.</summary>
        public static int Buds(int baseQuantity, float yieldMultiplier, int sites)
        {
            int n = (int)Math.Round(baseQuantity * yieldMultiplier, MidpointRounding.ToEven);   // Mathf.RoundToInt rounds half to even
            if (sites < 1) return 1;
            return n < 1 ? 1 : n > sites ? sites : n;
        }

        /// <summary>The plant's yield multiplier after additives (the pot's own, times each additive's).</summary>
        public static float Multiplier(float potMultiplier, params float[] additiveMultipliers)
        {
            float m = potMultiplier;
            foreach (var a in additiveMultipliers) if (a != 0f) m = Math.Max(0f, m * a);
            return m;
        }

        /// <summary>
        /// Where the k-th added bud site goes, relative to the site it copies: turned about the stem by the golden angle and
        /// pulled in a little, so the new buds sit between the old ones. Fixed numbers, so every peer builds the same plant and
        /// bud indexes (which the game sends over the network) agree.
        /// </summary>
        public static (float AngleDegrees, float RadiusScale, float Lift) ExtraSitePlacement(int k)
        {
            float angle = (137.5f * (k + 1)) % 360f;
            float radius = 0.85f - 0.03f * (k % 4);
            float lift = 0.02f * ((k % 3) - 1);
            return (angle, radius, lift);
        }

        /// <summary>Which existing site the k-th added site copies (round-robin over the plant's own sites).</summary>
        public static int ExtraSiteSource(int k, int vanillaSites) => vanillaSites <= 0 ? -1 : k % vanillaSites;
    }
}
