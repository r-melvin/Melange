using System;
using System.Collections.Generic;

namespace Melange.Core
{
    /// <summary>Where an order is placed: a shop's checkout, a supplier's phone order, or the delivery app.</summary>
    public enum OrderChannel { Shop, Supplier, Delivery }

    /// <summary>A change one spoke makes to whole-order totals: a multiplier for an order, or 1 for no change.</summary>
    public sealed class OrderModifier
    {
        /// <summary>Unique per modifier ("levels.bulk"); registering the same id again replaces it.</summary>
        public string Id;
        public int Order;
        /// <summary>(channel, shop or supplier name, units in the order) -> multiplier.</summary>
        public Func<OrderChannel, string, int, float> Multiplier;
    }

    /// <summary>The pure part of the order-total pipeline.</summary>
    public static class OrderMath
    {
        public static float Combined(IEnumerable<OrderModifier> modifiers, OrderChannel channel, string shop, int units, Action<string> onError = null)
        {
            float m = 1f;
            foreach (var mod in modifiers)
            {
                float f;
                try { f = mod.Multiplier?.Invoke(channel, shop, units) ?? 1f; }
                catch (Exception e) { onError?.Invoke($"order modifier {mod.Id} threw: {e.Message}"); continue; }
                if (float.IsNaN(f) || float.IsInfinity(f) || f <= 0f) continue;
                m *= f;
            }
            return Math.Max(m, PriceMath.Floor);
        }
    }
}
