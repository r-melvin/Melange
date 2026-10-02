namespace Melange.Smuggling
{
    /// <summary>
    /// Every tunable number in one place. The game side fills one from MelonPreferences (Settings.cs); tests build their own.
    /// The defaults are the design's: 10-15 purchases of $300 or more, an order the morning after the boat comes back, a
    /// departure at 02:00, police risk on every delivery except those made via the bootleggers' route.
    /// </summary>
    public sealed class SmugglingRules
    {
        // ---- unlock ----
        public int UnlockMin = 10;
        public int UnlockMax = 15;
        public float QualifyingSpend = 300f;

        // ---- timing (24-hour clock as the game writes it: 200 = 02:00) ----
        /// <summary>When Dafydd texts a new order.</summary>
        public int PostTime = 800;
        /// <summary>When the boat leaves. Before noon means the night after the deadline day.</summary>
        public int DepartureTime = 200;
        /// <summary>Whole days between the posting day and the deadline day: 0 = the same night (about 18 in-game hours).</summary>
        public int LeadDays = 1;
        /// <summary>When the boat is back in port with any imports.</summary>
        public int ReturnTime = 700;
        /// <summary>Days from one departure to the next order (scheduled windows, not cooldowns).</summary>
        public int OrderIntervalDays = 3;

        // ---- orders ----
        public int BaseUnits = 200;
        public int UnitsPerRank = 60;
        public int MinUnits = 100;
        public int MaxUnits = 2000;
        /// <summary>Orders come in whole bricks.</summary>
        public int UnitStep = 20;
        /// <summary>Over the game's market value per unit, for volume (vanilla bulk buyers pay less; Dafydd pays more).</summary>
        public float ExportPremium = 1.2f;
        /// <summary>Extra for an order with a Premium floor.</summary>
        public float PremiumQualityBonus = 1.15f;
        /// <summary>From this rank on, some orders ask for Premium.</summary>
        public int PremiumFromRank = 7;
        public double PremiumChance = 0.3;

        // ---- payment ----
        /// <summary>The hold takes up to this multiple of the order; the extra is bought at <see cref="OverDeliveryRate"/>.</summary>
        public float OverDeliveryCap = 1.5f;
        public float OverDeliveryRate = 0.6f;
        public int ReputationFull = 5;
        public int ReputationShort = -5;
        public int ReputationNoShow = -10;

        // ---- police risk, per delivery to the boat ----
        public float BaseRisk = 0.03f;
        public float RiskPerUnit = 0.0002f;
        public float RiskCap = 0.3f;
        /// <summary>During the curfew (21:00-05:00) the quay is watched harder.</summary>
        public float CurfewRiskMultiplier = 1.5f;
        /// <summary>Deliveries via the bootleggers' route: 0 = no police risk at all (the design), raise it for a softer route.</summary>
        public float RouteRiskMultiplier = 0f;
        /// <summary>Seconds after leaving the route (the canal mouth or the sewer) that a delivery still counts as made via it.</summary>
        public float RouteWindowSeconds = 180f;

        // ---- imports ----
        /// <summary>The best import discount, at full reputation.</summary>
        public float MaxImportDiscount = 0.2f;
        /// <summary>Imports open after this many paid runs.</summary>
        public int ImportsAfterRuns = 1;

        // ---- the tanker (experimental) ----
        public int TankerIntervalDays = 7;
        public double TankerChance = 0.5;
        public int TankerMinRank = 6;
    }
}
