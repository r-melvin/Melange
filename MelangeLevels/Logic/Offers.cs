namespace Melange.Levels
{
    /// <summary>Offers they can't refuse: what Prestige buys. Each costs Prestige and has a cooldown in in-game days.</summary>
    public enum OfferKind
    {
        /// <summary>A supplier's pending dead drop is ready now.</summary>
        RushOrder,
        /// <summary>Oscar's warehouse sells at a discount for a day.</summary>
        WarehouseDiscount,
    }

    public static class Offers
    {
        public const float WarehouseDiscountPercent = 25f;
        public const int WarehouseDiscountDays = 1;

        public static int Cost(OfferKind kind) => 1;

        public static int CooldownDays(OfferKind kind) => kind switch
        {
            OfferKind.RushOrder => 2,
            OfferKind.WarehouseDiscount => 3,
            _ => 3,
        };

        public static string Title(OfferKind kind) => kind switch
        {
            OfferKind.RushOrder => "Lean on a supplier: their dead drop is ready now",
            OfferKind.WarehouseDiscount => $"Lean on the warehouse: {WarehouseDiscountPercent:0}% off Oscar's stock for a day",
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
    }
}
