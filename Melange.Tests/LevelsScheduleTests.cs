using System.Linq;
using Melange.Core;
using Melange.Levels;
using Xunit;

namespace Melange.Tests
{
    public sealed class LevelsScheduleTests
    {
        [Fact]
        public void NothingBeforeBlockBoss()
        {
            for (int rank = 0; rank < Schedule.BlockBoss; rank++)
                for (int tier = 1; tier <= 5; tier++)
                    Assert.Empty(Schedule.At(rank, tier));
        }

        [Fact]
        public void ThreeEmployeeSlotsInAll()
        {
            int slots = Schedule.AllMilestones().Sum(m => m.Kinds.Count(k => k == RewardKind.EmployeeSlot));
            Assert.Equal(Schedule.EmployeeSlotsInAll, slots);
        }

        [Fact]
        public void AnUnderbossCandidateAtEachLateRank()
        {
            foreach (int rank in new[] { Schedule.BlockBoss, Schedule.Underlord, Schedule.Baron, Schedule.Kingpin })
                Assert.Contains(RewardKind.UnderbossCandidate, Schedule.At(rank, 1));
        }

        [Fact]
        public void PrestigeFromTheSecondKingpinTierOn()
        {
            Assert.DoesNotContain(RewardKind.Prestige, Schedule.At(Schedule.Kingpin, 1));
            Assert.Equal(new[] { RewardKind.Prestige }, Schedule.At(Schedule.Kingpin, 2));
            Assert.Equal(new[] { RewardKind.Prestige }, Schedule.At(Schedule.Kingpin, 40));
        }

        [Theory]
        [InlineData(6, 5, 0)]
        [InlineData(7, 1, 1)]
        [InlineData(8, 3, 1)]
        [InlineData(9, 1, 2)]
        [InlineData(10, 1, 3)]
        [InlineData(10, 9, 3)]
        public void DiscountStepsBuildUp(int rank, int tier, int step)
            => Assert.Equal(step, Schedule.DiscountStep(rank, tier));

        [Fact]
        public void AMilestoneInsideALargeAwardIsStillGranted()
        {
            var granted = Ranks.TiersCrossed(Schedule.BlockBoss, 5, Schedule.Underlord, 2).SelectMany(t => Schedule.At(t.Rank, t.Tier)).ToList();
            Assert.Contains(RewardKind.EmployeeSlot, granted);
            Assert.Contains(RewardKind.UnderbossCandidate, granted);
        }
    }
}
