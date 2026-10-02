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

        [Fact]
        public void PoliceLookAwayCostsOneWithAThreeDayCooldown()
        {
            Assert.Equal(1, Offers.Cost(OfferKind.PoliceLookAway));
            Assert.Equal(3, Offers.CooldownDays(OfferKind.PoliceLookAway));
            Assert.False(Offers.Available(OfferKind.PoliceLookAway, 3, 4, 6, out string why));
            Assert.Equal("again tomorrow", why);
            Assert.True(Offers.Available(OfferKind.PoliceLookAway, 3, 4, 7, out _));
        }

        [Fact]
        public void ThePoliceLookAwayUntilTheNextSixAm()
        {
            // bought in the evening: until 06:00 the next morning
            int until = Offers.LenientUntil(5, 2130);
            Assert.Equal(Offers.AbsoluteMinute(6, 600), until);
            Assert.True(Offers.LenientActive(until, 5, 2130));
            Assert.True(Offers.LenientActive(until, 6, 0));          // past midnight the game is on the next day
            Assert.True(Offers.LenientActive(until, 6, 559));
            Assert.False(Offers.LenientActive(until, 6, 600));
            Assert.False(Offers.LenientActive(until, 6, 1200));
            // bought after midnight, before 06:00: until 06:00 that morning
            Assert.Equal(Offers.AbsoluteMinute(6, 600), Offers.LenientUntil(6, 130));
            // bought at 06:00 exactly: the next morning, not zero minutes
            Assert.Equal(Offers.AbsoluteMinute(7, 600), Offers.LenientUntil(6, 600));
            Assert.False(Offers.LenientActive(-1, 6, 1200));
        }

        [Fact]
        public void TheLeniencyCoversTheEndOfCurfew()
        {
            // the hard curfew ends at 05:00 (CurfewManager.CURFEW_END_TIME = 500): the cover must outlast it
            int until = Offers.LenientUntil(2, 2115);
            Assert.True(Offers.LenientActive(until, 3, 459));
            Assert.Equal(Leniency.BodySearches | Leniency.Curfew | Leniency.DropPursuit, Offers.PoliceCovers);
        }

        [Fact]
        public void OnlyASearchForYouIsCalledOff()
        {
            Assert.False(Offers.DropsPursuit(0));   // None
            Assert.True(Offers.DropsPursuit(1));    // Investigating
            Assert.False(Offers.DropsPursuit(2));   // Arresting
            Assert.False(Offers.DropsPursuit(3));   // NonLethal
            Assert.False(Offers.DropsPursuit(4));   // Lethal
        }
    }
}
