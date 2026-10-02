using System;

namespace Melange.Smuggling
{
    /// <summary>
    /// The way in. Oscar passes on Dafydd's number after a number of purchases from him, rolled per save between
    /// <see cref="SmugglingRules.UnlockMin"/> and <see cref="SmugglingRules.UnlockMax"/> so it can't be looked up, and only
    /// purchases of at least <see cref="SmugglingRules.QualifyingSpend"/> count, so ten cheap visits don't do it.
    /// </summary>
    public static class Unlock
    {
        /// <summary>A number from min to max, both included. A max below min is treated as min.</summary>
        public static int RollThreshold(Random rng, int min, int max)
        {
            if (min < 1) min = 1;
            if (max < min) max = min;
            return rng.Next(min, max + 1);
        }

        public static bool Counts(float spend, float qualifyingSpend) => spend >= qualifyingSpend && spend > 0f;

        /// <summary>
        /// One completed purchase from Oscar. Rolls the threshold the first time it is needed. Returns true only on the
        /// purchase that reaches it, so the number is passed on once.
        /// </summary>
        public static bool RecordPurchase(SmugglingState s, float spend, SmugglingRules rules, Random rng)
        {
            EnsureThreshold(s, rules, rng);
            if (!Counts(spend, rules.QualifyingSpend)) return false;
            s.QualifyingPurchases++;
            if (s.Unlocked || s.QualifyingPurchases < s.UnlockThreshold) return false;
            s.Unlocked = true;
            return true;
        }

        /// <summary>Rolls the threshold if this save has none yet (a new save, or one from before the spoke).</summary>
        public static void EnsureThreshold(SmugglingState s, SmugglingRules rules, Random rng)
        {
            if (s.UnlockThreshold <= 0) s.UnlockThreshold = RollThreshold(rng, rules.UnlockMin, rules.UnlockMax);
        }

        /// <summary>
        /// A save whose count already passed its threshold (the settings were lowered, or a purchase landed while the mod
        /// wasn't listening) unlocks on load rather than waiting for one more purchase.
        /// </summary>
        public static bool CatchUp(SmugglingState s)
        {
            if (s.Unlocked || s.UnlockThreshold <= 0 || s.QualifyingPurchases < s.UnlockThreshold) return false;
            s.Unlocked = true;
            return true;
        }
    }
}
