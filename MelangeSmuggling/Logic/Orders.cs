using System;
using System.Collections.Generic;

namespace Melange.Smuggling
{
    /// <summary>One drug type the player can make, with the game's average market value per unit of their products of it.</summary>
    public sealed class MarketEntry
    {
        public string DrugType;
        public float UnitValue;
        public MarketEntry(string drugType, float unitValue) { DrugType = drugType; UnitValue = unitValue; }
    }

    /// <summary>The game's quality names, by EQuality number.</summary>
    public static class Quality
    {
        public const int Trash = 0, Poor = 1, Standard = 2, Premium = 3, Heavenly = 4;
        private static readonly string[] Names = { "Trash", "Poor", "Standard", "Premium", "Heavenly" };
        public static string Name(int q) => q >= 0 && q < Names.Length ? Names[q] : "Unknown";
    }

    /// <summary>
    /// Dafydd's orders: huge, for one drug type the player already makes, at a quality floor, paid above the market for
    /// volume. Sizes grow with rank and with how reliably the player has filled the boat before.
    /// </summary>
    public static class Orders
    {
        /// <summary>Order size before rounding: the base plus a step per rank, scaled by reputation (0.6x at 0, 1.4x at 100) and +/-20% jitter.</summary>
        public static int Size(int rank, int reputation, double jitter, SmugglingRules r)
        {
            if (rank < 0) rank = 0;
            reputation = Math.Max(0, Math.Min(100, reputation));
            jitter = Math.Max(0.0, Math.Min(1.0, jitter));
            double raw = (r.BaseUnits + r.UnitsPerRank * rank) * (0.6 + 0.8 * reputation / 100.0) * (0.8 + 0.4 * jitter);
            int step = Math.Max(1, r.UnitStep);
            int units = (int)Math.Round(raw / step) * step;
            int min = (int)Math.Ceiling((double)r.MinUnits / step) * step;
            int max = Math.Max(min, (r.MaxUnits / step) * step);
            return Math.Max(min, Math.Min(max, units));
        }

        public static float UnitPrice(float marketValue, int minQuality, SmugglingRules r)
        {
            float p = Math.Max(0f, marketValue) * r.ExportPremium;
            if (minQuality >= Quality.Premium) p *= r.PremiumQualityBonus;
            return (float)Math.Round(p, 2);
        }

        /// <summary>
        /// A new order, or null when the player makes nothing yet (Dafydd waits a day and asks again). The drug type is
        /// picked evenly among what they make.
        /// </summary>
        public static Order Generate(Random rng, int id, int now, int rank, int reputation, IReadOnlyList<MarketEntry> market, SmugglingRules r)
        {
            var usable = new List<MarketEntry>();
            if (market != null)
                foreach (var m in market)
                    if (m != null && !string.IsNullOrEmpty(m.DrugType) && m.UnitValue > 0f) usable.Add(m);
            if (usable.Count == 0) return null;

            var pick = usable[rng.Next(usable.Count)];
            int quality = rank >= r.PremiumFromRank && rng.NextDouble() < r.PremiumChance ? Quality.Premium : Quality.Standard;
            int units = Size(rank, reputation, rng.NextDouble(), r);
            int day = Timing.DayOf(now);
            return new Order
            {
                Id = id,
                DrugType = pick.DrugType,
                MinQuality = quality,
                Units = units,
                UnitPrice = UnitPrice(pick.UnitValue, quality, r),
                PostedAt = now,
                DepartsAt = Timing.Departure(day, r.LeadDays, r.DepartureTime),
                Answer = OrderAnswer.None,
            };
        }

        public static bool Matches(Order o, string drugType, int quality)
            => o != null && string.Equals(o.DrugType, drugType, StringComparison.OrdinalIgnoreCase) && quality >= o.MinQuality;

        /// <summary>The most the hold takes for this order (the order plus the over-delivery allowance).</summary>
        public static int Capacity(Order o, SmugglingRules r)
            => o == null ? 0 : (int)Math.Floor(o.Units * Math.Max(1f, r.OverDeliveryCap));

        /// <summary>
        /// How many whole items of a stack the hold takes: matching product only, while the boat is in and the order open,
        /// up to its capacity. <paramref name="unitsPerItem"/> is the packaging's quantity (a brick is 20 units, a jar 5).
        /// <paramref name="pendingUnits"/> are units already picked for the same delivery from other stacks, not yet aboard.
        /// </summary>
        public static int ItemsTaken(Order o, SmugglingState s, int now, string drugType, int quality, int unitsPerItem, int itemsOffered, SmugglingRules r, int pendingUnits = 0)
        {
            if (o == null || s.Boat != BoatPhase.Moored || o.Answer == OrderAnswer.Declined || now >= o.DepartsAt) return 0;
            if (!Matches(o, drugType, quality) || unitsPerItem <= 0 || itemsOffered <= 0) return 0;
            int room = Capacity(o, r) - s.UnitsAboard - Math.Max(0, pendingUnits);
            if (room <= 0) return 0;
            return Math.Min(itemsOffered, room / unitsPerItem);
        }

        public static string Describe(Order o)
            => o == null ? "no order" : $"{o.Units} units of {Lines.DrugName(o.DrugType)}, {Quality.Name(o.MinQuality)} or better, at ${o.UnitPrice:0.00} a unit";
    }
}
