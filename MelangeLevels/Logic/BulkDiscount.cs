using Melange.Core;

namespace Melange.Levels
{
    /// <summary>
    /// The levels reward "bulk-order discounts": a step (1-3, earned at Block Boss, Baron and Kingpin) gives 5%, 10% or 15%
    /// off an order big enough to count as bulk. Supplier phone orders are capped at 10 units by the game, so bulk starts
    /// lower there.
    /// </summary>
    public static class BulkDiscount
    {
        public static float PercentFor(int step) => step <= 0 ? 0f : step >= 3 ? 15f : step * 5f;

        public static int BulkFrom(OrderChannel channel) => channel == OrderChannel.Supplier ? 8 : 20;

        /// <summary>The multiplier for an order: 1 below the bulk size or with no step earned.</summary>
        public static float Multiplier(int step, OrderChannel channel, int units)
            => units >= BulkFrom(channel) ? 1f - PercentFor(step) / 100f : 1f;
    }
}
