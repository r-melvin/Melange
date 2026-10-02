using System.Collections.Generic;

namespace Melange.Core
{
    /// <summary>
    /// The game's rank ladder as numbers, so spokes and tests can reason about it without game types: ranks 0 (Street Rat) to
    /// 10 (Kingpin), five tiers each, except Kingpin, whose tiers go on without end.
    /// </summary>
    public static class Ranks
    {
        public const int Kingpin = 10;
        public const int TiersPerRank = 5;

        /// <summary>
        /// Every tier reached going from <paramref name="beforeRank"/>/<paramref name="beforeTier"/> to
        /// <paramref name="afterRank"/>/<paramref name="afterTier"/>, in order, excluding the start and including the end. One
        /// rank-up event from the game can cover several tiers (a large XP award), and a milestone must not be skipped.
        /// </summary>
        public static IEnumerable<(int Rank, int Tier)> TiersCrossed(int beforeRank, int beforeTier, int afterRank, int afterTier)
        {
            int rank = beforeRank, tier = beforeTier;
            while (Compare(rank, tier, afterRank, afterTier) < 0)
            {
                if (rank < Kingpin && tier >= TiersPerRank) { rank++; tier = 1; }
                else tier++;
                yield return (rank, tier);
            }
        }

        public static int Compare(int rankA, int tierA, int rankB, int tierB)
            => rankA != rankB ? rankA.CompareTo(rankB) : tierA.CompareTo(tierB);
    }
}
