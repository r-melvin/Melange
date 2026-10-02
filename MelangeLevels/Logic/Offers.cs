namespace Melange.Levels
{
    /// <summary>Offers they can't refuse: what Prestige buys. Each costs Prestige and has a cooldown in in-game days.</summary>
    public enum OfferKind
    {
        /// <summary>A supplier's pending dead drop is ready now.</summary>
        RushOrder,
        /// <summary>Oscar's warehouse sells at a discount for a day.</summary>
        WarehouseDiscount,
        /// <summary>The police look away until 06:00: no body searches, no curfew stops, and a search for you is called off.</summary>
        PoliceLookAway,
    }

    /// <summary>What the police overlook while they look away.</summary>
    [System.Flags]
    public enum Leniency
    {
        None = 0,
        /// <summary>No body searches: officers on foot, the suspicious-player stop, and walking through a checkpoint.</summary>
        BodySearches = 1,
        /// <summary>Being out in the hard curfew (21:15 to 05:00) isn't noticed.</summary>
        Curfew = 2,
        /// <summary>On buying it, a pursuit no higher than <see cref="Offers.MaxDroppedPursuit"/> is called off.</summary>
        DropPursuit = 4,
    }

    public static class Offers
    {
        public const float WarehouseDiscountPercent = 25f;
        public const int WarehouseDiscountDays = 1;

        /// <summary>The police look away until this time (24-hour hhmm) comes round next.</summary>
        public const int LenientUntilTime = 600;
        /// <summary>What they overlook.</summary>
        public const Leniency PoliceCovers = Leniency.BodySearches | Leniency.Curfew | Leniency.DropPursuit;
        /// <summary>
        /// The highest pursuit level called off on buying it, as the game's <c>PlayerCrimeData.EPursuitLevel</c> number
        /// (None 0, Investigating 1, Arresting 2, NonLethal 3, Lethal 4): a search for you, not a chase with guns out.
        /// </summary>
        public const int MaxDroppedPursuit = 1;

        public static int Cost(OfferKind kind) => 1;

        public static int CooldownDays(OfferKind kind) => kind switch
        {
            OfferKind.RushOrder => 2,
            OfferKind.WarehouseDiscount => 3,
            OfferKind.PoliceLookAway => 3,
            _ => 3,
        };

        public static string Title(OfferKind kind) => kind switch
        {
            OfferKind.RushOrder => "Lean on a supplier: their dead drop is ready now",
            OfferKind.WarehouseDiscount => $"Lean on the warehouse: {WarehouseDiscountPercent:0}% off Oscar's stock for a day",
            OfferKind.PoliceLookAway => "Lean on the precinct: the police look away until 6 AM (no searches or curfew stops; a search for you is called off)",
            _ => kind.ToString(),
        };

        /// <summary>Can it be used today? <paramref name="lastUsedDay"/> is -1 if never used.</summary>
        public static bool Available(OfferKind kind, int prestige, int lastUsedDay, int today, out string why)
        {
            why = null;
            if (prestige < Cost(kind)) { why = $"needs {Cost(kind)} Prestige"; return false; }
            if (lastUsedDay >= 0 && today - lastUsedDay < CooldownDays(kind))
            {
                int left = CooldownDays(kind) - (today - lastUsedDay);
                why = left == 1 ? "again tomorrow" : $"again in {left} days";
                return false;
            }
            return true;
        }

        /// <summary>Is the warehouse discount on, for a discount bought on <paramref name="boughtDay"/> (-1: never)?</summary>
        public static bool WarehouseDiscountActive(int boughtDay, int today)
            => boughtDay >= 0 && today >= boughtDay && today < boughtDay + WarehouseDiscountDays;

        /// <summary>An in-game moment as minutes since day 0 began: <paramref name="hhmm"/> is the game's 24-hour time (e.g. 2130).</summary>
        public static int AbsoluteMinute(int day, int hhmm) => day * 1440 + hhmm / 100 * 60 + hhmm % 100;

        /// <summary>When the police stop looking away, bought at <paramref name="hhmm"/> on <paramref name="day"/>: the next 06:00.</summary>
        public static int LenientUntil(int day, int hhmm)
        {
            int now = AbsoluteMinute(day, hhmm), sixToday = AbsoluteMinute(day, LenientUntilTime);
            return now < sixToday ? sixToday : sixToday + 1440;
        }

        /// <summary>Are the police looking away now? <paramref name="until"/> is from <see cref="LenientUntil"/>, or -1 if never bought.</summary>
        public static bool LenientActive(int until, int day, int hhmm) => until >= 0 && AbsoluteMinute(day, hhmm) < until;

        /// <summary>Is a pursuit at this level (the game's EPursuitLevel number) called off on buying it?</summary>
        public static bool DropsPursuit(int pursuitLevel)
            => (PoliceCovers & Leniency.DropPursuit) != 0 && pursuitLevel > 0 && pursuitLevel <= MaxDroppedPursuit;
    }
}
