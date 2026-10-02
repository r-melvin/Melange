using System;

namespace Melange.Smuggling
{
    /// <summary>
    /// Police risk on the way to the boat. Vanilla police never search an NPC's vehicle and nothing in the game watches the
    /// quay, so this is the mod's own: every delivery (one load into the hold) rolls once, bigger loads and the curfew raise
    /// it, and a seized delivery is lost (the player is told the odds before loading, never wiped silently).
    /// Deliveries made via the bootleggers' route (sewer, canal bed, canal mouth, quay) are multiplied by
    /// <see cref="SmugglingRules.RouteRiskMultiplier"/>, 0 by default: no police risk at all.
    /// </summary>
    public static class Risk
    {
        public static float PoliceRisk(int units, int hhmm, bool viaRoute, SmugglingRules r)
        {
            if (units <= 0) return 0f;
            float p = Math.Min(r.RiskCap, r.BaseRisk + r.RiskPerUnit * units);
            if (Timing.IsCurfew(hhmm)) p *= r.CurfewRiskMultiplier;
            if (viaRoute) p *= r.RouteRiskMultiplier;
            return Math.Max(0f, Math.Min(0.5f, p));
        }

        /// <summary>A roll in [0, 1) under the risk is a seizure. Zero risk never seizes.</summary>
        public static bool Seized(float risk, double roll) => risk > 0f && roll < risk;

        /// <summary>
        /// Did this delivery come via the route? Only once the route is known, only on foot (getting into any vehicle since
        /// leaving it breaks the chain: the route is tunnels and a canal bed), and only shortly after leaving it.
        /// </summary>
        public static bool ViaRoute(bool routeKnown, float secondsSinceOnRoute, bool droveSince, SmugglingRules r)
            => routeKnown && !droveSince && secondsSinceOnRoute >= 0f && secondsSinceOnRoute <= r.RouteWindowSeconds;

        public static string Percent(float risk) => $"{Math.Round(risk * 100f):0}%";
    }
}
