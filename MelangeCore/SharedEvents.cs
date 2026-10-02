namespace Melange.Core
{
    // Events one spoke publishes and others may act on. They live in the hub because spokes never reference each other;
    // a spoke that doesn't care simply doesn't subscribe.

    /// <summary>The levels spoke made another underboss candidate available to hire. The cartel spoke owns underbosses and grows its pool on this.</summary>
    public sealed class UnderbossCandidateUnlocked
    {
        public int Total { get; }
        public UnderbossCandidateUnlocked(int total) => Total = total;
    }

    /// <summary>The bulk-order discount step changed (0 none, then 1-3), from the levels spoke.</summary>
    public sealed class BulkDiscountStepChanged
    {
        public int Step { get; }
        public BulkDiscountStepChanged(int step) => Step = step;
    }

    /// <summary>The player's Prestige changed (earned at Kingpin tiers, spent on offers), from the levels spoke.</summary>
    public sealed class PrestigeChanged
    {
        public int Prestige { get; }
        public PrestigeChanged(int prestige) => Prestige = prestige;
    }
}
