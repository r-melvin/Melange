using System;

namespace Melange.Smuggling
{
    /// <summary>
    /// The methylamine tanker (experimental, off by default): now and then Dafydd tips the player off that a tanker is
    /// leaving Billy's chemical plant, and whoever stops it on the road gets its methylamine. The rules for when the tip
    /// comes and what it's worth live here; the driving and the stop are game code (Tanker.cs), which is unproven in game.
    /// </summary>
    public static class TankerJob
    {
        /// <summary>
        /// Is a tip due today? Only with the setting on, a methylamine item to give, the number passed on, from a rank,
        /// at most once per interval, and then on a chance roll (so it isn't the same day every week).
        /// </summary>
        public static bool TipDue(bool enabled, string methylamineId, SmugglingState s, int rank, int today, double roll, SmugglingRules r)
        {
            if (!enabled || string.IsNullOrWhiteSpace(methylamineId) || !s.Unlocked) return false;
            if (rank < r.TankerMinRank) return false;
            if (s.LastTankerTipDay >= 0 && today - s.LastTankerTipDay < Math.Max(1, r.TankerIntervalDays)) return false;
            return roll < r.TankerChance;
        }

        /// <summary>What a stopped tanker yields: the configured load, more if the route is known (the old bootleggers knew where to stash it).</summary>
        public static int Yield(int baseUnits, bool routeKnown) => Math.Max(0, routeKnown ? baseUnits + baseUnits / 4 : baseUnits);

        /// <summary>
        /// The tanker counts as stopped by the player when it has stood still for a few seconds with the player close by on
        /// foot, after it had started moving (so it isn't "stopped" at the gate before it leaves).
        /// </summary>
        public static bool Hijacked(bool startedMoving, float stillSeconds, float playerDistance, bool playerOnFoot, float needStill = 3f, float needWithin = 6f)
            => startedMoving && playerOnFoot && stillSeconds >= needStill && playerDistance <= needWithin;
    }
}
