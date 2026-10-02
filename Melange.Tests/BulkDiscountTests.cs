using Melange.Core;
using Melange.Levels;
using Xunit;

namespace Melange.Tests
{
    public sealed class BulkDiscountTests
    {
        [Theory]
        [InlineData(0, 0f)]
        [InlineData(1, 5f)]
        [InlineData(2, 10f)]
        [InlineData(3, 15f)]
        [InlineData(7, 15f)]
        public void StepsAreFiveTenFifteen(int step, float percent) => Assert.Equal(percent, BulkDiscount.PercentFor(step));

        [Fact]
        public void BelowBulkSizeNothingChanges()
        {
            Assert.Equal(1f, BulkDiscount.Multiplier(3, OrderChannel.Shop, 19));
            Assert.Equal(1f, BulkDiscount.Multiplier(3, OrderChannel.Supplier, 7));
        }

        [Fact]
        public void AtBulkSizeTheStepApplies()
        {
            Assert.Equal(0.95f, BulkDiscount.Multiplier(1, OrderChannel.Shop, 20), 4);
            Assert.Equal(0.85f, BulkDiscount.Multiplier(3, OrderChannel.Supplier, 8), 4);
            Assert.Equal(0.9f, BulkDiscount.Multiplier(2, OrderChannel.Delivery, 50), 4);
        }

        [Fact]
        public void NoStepNoDiscount() => Assert.Equal(1f, BulkDiscount.Multiplier(0, OrderChannel.Shop, 500));
    }
}
