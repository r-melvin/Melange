using Melange.Levels;
using Xunit;

namespace Melange.Tests
{
    public sealed class OffersTests
    {
        [Fact]
        public void NeedsPrestige()
        {
            Assert.False(Offers.Available(OfferKind.RushOrder, 0, -1, 10, out string why));
            Assert.Contains("Prestige", why);
            Assert.True(Offers.Available(OfferKind.RushOrder, 1, -1, 10, out _));
        }

        [Fact]
        public void CooldownCountsDays()
        {
            Assert.False(Offers.Available(OfferKind.WarehouseDiscount, 5, 10, 11, out string why));
            Assert.Equal("again in 2 days", why);
            Assert.False(Offers.Available(OfferKind.WarehouseDiscount, 5, 10, 12, out why));
            Assert.Equal("again tomorrow", why);
            Assert.True(Offers.Available(OfferKind.WarehouseDiscount, 5, 10, 13, out _));
        }

        [Fact]
        public void TheWarehouseDiscountLastsADay()
        {
            Assert.False(Offers.WarehouseDiscountActive(-1, 5));
            Assert.True(Offers.WarehouseDiscountActive(5, 5));
            Assert.False(Offers.WarehouseDiscountActive(5, 6));
        }
    }
}
