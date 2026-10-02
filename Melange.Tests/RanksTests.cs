using System.Linq;
using Melange.Core;
using Xunit;

namespace Melange.Tests
{
    public sealed class RanksTests
    {
        [Fact]
        public void OneTierUp()
            => Assert.Equal(new[] { (7, 3) }, Ranks.TiersCrossed(7, 2, 7, 3).ToArray());

        [Fact]
        public void CrossingIntoTheNextRankStartsAtTierOne()
            => Assert.Equal(new[] { (8, 1) }, Ranks.TiersCrossed(7, 5, 8, 1).ToArray());

        [Fact]
        public void ALargeAwardReportsEveryTierInBetween()
            => Assert.Equal(new[] { (7, 5), (8, 1), (8, 2) }, Ranks.TiersCrossed(7, 4, 8, 2).ToArray());

        [Fact]
        public void KingpinTiersGoOnPastFive()
            => Assert.Equal(new[] { (10, 5), (10, 6), (10, 7) }, Ranks.TiersCrossed(10, 4, 10, 7).ToArray());

        [Fact]
        public void BaronFiveToKingpinOne()
            => Assert.Equal(new[] { (10, 1) }, Ranks.TiersCrossed(9, 5, 10, 1).ToArray());

        [Fact]
        public void NoChangeOrGoingBackReportsNothing()
        {
            Assert.Empty(Ranks.TiersCrossed(5, 3, 5, 3));
            Assert.Empty(Ranks.TiersCrossed(5, 3, 4, 1));
        }
    }
}
