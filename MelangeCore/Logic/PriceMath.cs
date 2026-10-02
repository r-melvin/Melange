using System;
using System.Collections.Generic;

namespace Melange.Core
{
    /// <summary>A price change one spoke contributes: a multiplier for a shop's item, or 1 for no change.</summary>
    public sealed class PriceModifier
    {
        /// <summary>Unique per modifier ("cartel.monopoly", "levels.bulk"); registering the same id again replaces it.</summary>
        public string Id;
        /// <summary>Lower runs first. Markups and discounts multiply, so order only matters for the rounding and the floor.</summary>
        public int Order;
        /// <summary>(shop code, item id) -> multiplier. Return 1 for listings this modifier leaves alone.</summary>
        public Func<string, string, float> Multiplier;
    }

    /// <summary>The pure part of the price pipeline: composing modifiers into one price.</summary>
    public static class PriceMath
    {
        /// <summary>No modifier may take a price below this fraction of the original.</summary>
        public const float Floor = 0.05f;

        /// <summary>The combined multiplier for a listing; a modifier that throws or returns nonsense counts as 1.</summary>
        public static float Combined(IEnumerable<PriceModifier> modifiers, string shopCode, string itemId, Action<string> onError = null)
        {
            float m = 1f;
            foreach (var mod in modifiers)
            {
                float f;
                try { f = mod.Multiplier?.Invoke(shopCode, itemId) ?? 1f; }
                catch (Exception e) { onError?.Invoke($"price modifier {mod.Id} threw: {e.Message}"); continue; }
                if (float.IsNaN(f) || float.IsInfinity(f) || f <= 0f) continue;
                m *= f;
            }
            return Math.Max(m, Floor);
        }

        /// <summary>The price to show: the original times the combined multiplier, to the cent.</summary>
        public static float Apply(float original, float multiplier) => (float)Math.Round(original * multiplier, 2);

        /// <summary>Sorted by <see cref="PriceModifier.Order"/>, then id, so every peer composes in the same order.</summary>
        public static List<PriceModifier> Ordered(IEnumerable<PriceModifier> modifiers)
        {
            var list = new List<PriceModifier>(modifiers);
            list.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
            return list;
        }
    }
}
