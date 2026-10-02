using System.Collections.Generic;

namespace Melange.Levels
{
    public enum RewardKind
    {
        /// <summary>One more employee at a property of the player's choice (three in all).</summary>
        EmployeeSlot,
        /// <summary>A step up in bulk-order discounts with suppliers and shopkeepers.</summary>
        BulkDiscount,
        /// <summary>Another underboss candidate to hire from (the cartel spoke owns underbosses; this only grows its pool).</summary>
        UnderbossCandidate,
        /// <summary>One point of Prestige, spent on offers they can't refuse (every Kingpin tier after the first).</summary>
        Prestige,
    }

    /// <summary>
    /// What each rank milestone gives. The game unlocks nothing from Underlord upward and its last unlock is Block Boss I, so
    /// the rewards start there. Ranks are numbers: 7 Block Boss, 8 Underlord, 9 Baron, 10 Kingpin. Other spokes add their
    /// own unlocks (the mixers, hydroponics) at tiers of the same ranks.
    /// </summary>
    public static class Schedule
    {
        public const int BlockBoss = 7, Underlord = 8, Baron = 9, Kingpin = 10;
        public const int EmployeeSlotsInAll = 3;

        private static readonly Dictionary<(int, int), RewardKind[]> Milestones = new Dictionary<(int, int), RewardKind[]>
        {
            [(BlockBoss, 1)] = new[] { RewardKind.EmployeeSlot, RewardKind.BulkDiscount, RewardKind.UnderbossCandidate },
            [(Underlord, 1)] = new[] { RewardKind.EmployeeSlot, RewardKind.UnderbossCandidate },
            [(Baron, 1)] = new[] { RewardKind.EmployeeSlot, RewardKind.BulkDiscount, RewardKind.UnderbossCandidate },
            [(Kingpin, 1)] = new[] { RewardKind.BulkDiscount, RewardKind.UnderbossCandidate },
        };

        /// <summary>The rewards for reaching exactly this rank and tier.</summary>
        public static IReadOnlyList<RewardKind> At(int rank, int tier)
        {
            if (Milestones.TryGetValue((rank, tier), out var kinds)) return kinds;
            return rank == Kingpin && tier >= 2 ? new[] { RewardKind.Prestige } : System.Array.Empty<RewardKind>();
        }

        /// <summary>Every milestone with rewards, in order (for the rank-up screen); Kingpin's endless tiers are left out.</summary>
        public static IEnumerable<(int Rank, int Tier, RewardKind[] Kinds)> AllMilestones()
        {
            foreach (var kv in Milestones) yield return (kv.Key.Item1, kv.Key.Item2, kv.Value);
        }

        /// <summary>The bulk-discount step held at a rank and tier: 0 before Block Boss, then 1, 2, 3.</summary>
        public static int DiscountStep(int rank, int tier)
        {
            int step = 0;
            foreach (var kv in Milestones)
                if (System.Array.IndexOf(kv.Value, RewardKind.BulkDiscount) >= 0 && Melange.Core.Ranks.Compare(rank, tier, kv.Key.Item1, kv.Key.Item2) >= 0)
                    step++;
            return step;
        }

        public static string Describe(RewardKind kind) => kind switch
        {
            RewardKind.EmployeeSlot => "+1 employee at a property",
            RewardKind.BulkDiscount => "Bulk-order discounts",
            RewardKind.UnderbossCandidate => "New underboss candidate",
            RewardKind.Prestige => "+1 Prestige",
            _ => kind.ToString(),
        };
    }
}
