using System.Collections.Generic;
using System.Linq;
using Melange.Psychedelics;
using Xunit;

namespace Melange.Tests
{
    public sealed class PsychedelicsTests
    {
        private static TerrariumRules Rules(bool mentor = false, int capacity = 6, int dust = 10, int breed = 3, int starve = 4)
            => TerrariumRules.Sanitised(capacity, dust, 1, breed, starve, mentor);

        private static Terrarium With(TerrariumRules rules, params ToadOrigin[] origins)
        {
            var t = new Terrarium();
            foreach (var o in origins) Assert.True(t.Release(o, rules));
            return t;
        }

        // ---- terrarium: capacity and release ----

        [Fact]
        public void ReleaseStopsAtCapacity()
        {
            var r = Rules(capacity: 3);
            var t = new Terrarium();
            Assert.True(t.Release(ToadOrigin.Wild, r));
            Assert.True(t.Release(ToadOrigin.Wild, r));
            Assert.True(t.Release(ToadOrigin.Bred, r));
            Assert.False(t.Release(ToadOrigin.Sewer, r));
            Assert.Equal(3, t.Count);
            Assert.Equal(new[] { 1, 2, 3 }, t.Toads.Select(x => x.Id));
        }

        [Theory]
        [InlineData(0, 10, 1, 0, 0, 2, 10, 1, 1, 1)]          // floors
        [InlineData(100, 1000, 50, 99, 99, 24, 100, 10, 30, 30)] // ceilings
        public void RulesAreSanitised(int cap, int dust, int feed, int breed, int starve, int eCap, int eDust, int eFeed, int eBreed, int eStarve)
        {
            var r = TerrariumRules.Sanitised(cap, dust, feed, breed, starve, false);
            Assert.Equal(new[] { eCap, eDust, eFeed, eBreed, eStarve }, new[] { r.Capacity, r.MilkingsBeforeDust, r.FeedPerToad, r.BreedDays, r.StarveDays });
        }

        // ---- feeding ----

        [Fact]
        public void FeedingEatsOnePortionEachAndBuildsCare()
        {
            var r = Rules();
            var t = With(r, ToadOrigin.Wild, ToadOrigin.Wild);
            var rep = Husbandry.Feed(t, 10, 1, r);
            Assert.Equal(2, rep.FeedEaten);
            Assert.Equal(2, rep.Fed);
            Assert.All(t.Toads, x => { Assert.True(x.FedToday); Assert.Equal(1, x.CareStreak); });
        }

        [Fact]
        public void FeedingTwiceTheSameDayIsIgnored()
        {
            var r = Rules();
            var t = With(r, ToadOrigin.Wild);
            Husbandry.Feed(t, 10, 5, r);
            var again = Husbandry.Feed(t, 10, 5, r);
            Assert.True(again.Skipped);
            Assert.Equal(0, again.FeedEaten);
            Assert.Equal(1, t.Toads[0].CareStreak);
        }

        [Fact]
        public void ShortFeedLeavesTheLastToadsHungry()
        {
            var r = Rules();
            var t = With(r, ToadOrigin.Wild, ToadOrigin.Bred, ToadOrigin.Sewer);
            t.Toads[2].CareStreak = 7;
            var rep = Husbandry.Feed(t, 2, 1, r);
            Assert.Equal(2, rep.Fed);
            Assert.Equal(1, rep.Hungry);
            Assert.False(t.Toads[2].FedToday);
            Assert.Equal(0, t.Toads[2].CareStreak);                 // a missed meal breaks the streak
            Assert.Equal(1, t.Toads[2].HungryDays);
        }

        [Fact]
        public void UnfedToadsStarveAfterTheSetDays()
        {
            var r = Rules(starve: 3);
            var t = With(r, ToadOrigin.Wild);
            Assert.Equal(0, Husbandry.Feed(t, 0, 1, r).Starved);
            Assert.Equal(0, Husbandry.Feed(t, 0, 2, r).Starved);
            Assert.Equal(1, Husbandry.Feed(t, 0, 3, r).Starved);
            Assert.Equal(0, t.Count);
        }

        [Fact]
        public void AMealResetsHunger()
        {
            var r = Rules(starve: 2);
            var t = With(r, ToadOrigin.Wild);
            Husbandry.Feed(t, 0, 1, r);
            Husbandry.Feed(t, 1, 2, r);
            Husbandry.Feed(t, 0, 3, r);
            Assert.Equal(1, t.Count);
        }

        [Fact]
        public void CareStreakIsCapped()
        {
            var r = Rules();
            var t = With(r, ToadOrigin.Bred);
            for (int d = 1; d <= 40; d++) Husbandry.Feed(t, 1, d, r);
            Assert.Equal(Husbandry.MaxCareStreak, t.Toads[0].CareStreak);
        }

        // ---- breeding ----

        [Fact]
        public void AFedPairBreedsOnTheTimer()
        {
            var r = Rules(breed: 3);
            var t = With(r, ToadOrigin.Wild, ToadOrigin.Wild);
            Assert.Equal(0, Husbandry.Feed(t, 10, 1, r).Born);
            Assert.Equal(0, Husbandry.Feed(t, 10, 2, r).Born);
            Assert.Equal(1, Husbandry.Feed(t, 10, 3, r).Born);
            Assert.Equal(3, t.Count);
            Assert.Equal(ToadOrigin.Bred, t.Toads[2].Origin);
            Assert.Equal(0, t.BreedProgress);
        }

        [Fact]
        public void ALoneToadNeverBreeds()
        {
            var r = Rules(breed: 1);
            var t = With(r, ToadOrigin.Wild);
            for (int d = 1; d <= 10; d++) Husbandry.Feed(t, 10, d, r);
            Assert.Equal(1, t.Count);
        }

        [Fact]
        public void APairWithOnlyOneFedStartsOver()
        {
            var r = Rules(breed: 3);
            var t = With(r, ToadOrigin.Wild, ToadOrigin.Wild);
            Husbandry.Feed(t, 2, 1, r);
            Husbandry.Feed(t, 2, 2, r);
            Assert.Equal(2, t.BreedProgress);
            Husbandry.Feed(t, 1, 3, r);                              // one went hungry
            Assert.Equal(0, t.BreedProgress);
            Assert.Equal(2, t.Count);
        }

        [Fact]
        public void BreedingStopsAtCapacityAndWaits()
        {
            var r = Rules(capacity: 3, breed: 2);
            var t = With(r, ToadOrigin.Wild, ToadOrigin.Wild);
            for (int d = 1; d <= 20; d++) Husbandry.Feed(t, 10, d, r);
            Assert.Equal(3, t.Count);
            Assert.True(t.BreedProgress < r.EffectiveBreedDays);   // waiting, not about to overfill
        }

        [Fact]
        public void TheMentorBreedsADayQuicker()
        {
            Assert.Equal(2, Rules(mentor: true, breed: 3).EffectiveBreedDays);
            Assert.Equal(1, Rules(mentor: true, breed: 1).EffectiveBreedDays);
            var r = Rules(mentor: true, breed: 3);
            var t = With(r, ToadOrigin.Wild, ToadOrigin.Wild);
            Husbandry.Feed(t, 10, 1, r);
            Assert.Equal(1, Husbandry.Feed(t, 10, 2, r).Born);
        }

        [Fact]
        public void FarmGrowsFromAPairToFull()
        {
            var r = Rules(capacity: 6, breed: 3);
            var t = With(r, ToadOrigin.Wild, ToadOrigin.Bred);
            int born = 0;
            for (int d = 1; d <= 12; d++) born += Husbandry.Feed(t, 20, d, r).Born;
            Assert.Equal(4, born);
            Assert.Equal(6, t.Count);
        }

        // ---- milking and lifespan ----

        [Fact]
        public void MilkingIsOncePerToadPerDay()
        {
            var r = Rules();
            var t = With(r, ToadOrigin.Wild, ToadOrigin.Wild);
            var first = Husbandry.Milk(t, 4, r);
            Assert.Equal(2, first.Tiers.Count);
            Assert.Equal(0, Husbandry.Ready(t, 4));
            var second = Husbandry.Milk(t, 4, r);
            Assert.Empty(second.Tiers);
            Assert.Equal(2, second.AlreadyMilked);
            Assert.Equal(2, Husbandry.Ready(t, 5));
        }

        [Fact]
        public void AToadShrivelsToDustAfterTheSetMilkings()
        {
            var r = Rules(dust: 10);
            var t = With(r, ToadOrigin.Bred);
            int venom = 0;
            for (int d = 1; d <= 9; d++) { var m = Husbandry.Milk(t, d, r); venom += m.Tiers.Count; Assert.Equal(0, m.Dust); }
            var last = Husbandry.Milk(t, 10, r);
            Assert.Single(last.Tiers);                              // the tenth milking still gives venom
            Assert.Equal(1, last.Dust);
            Assert.Equal(0, t.Count);
            Assert.Equal(10, venom + last.Tiers.Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(25)]
        public void LifespanFollowsTheSetting(int limit)
        {
            var r = Rules(dust: limit);
            var t = With(r, ToadOrigin.Wild);
            int total = 0;
            for (int d = 1; d <= 100 && t.Count > 0; d++) total += Husbandry.Milk(t, d, r).Tiers.Count;
            Assert.Equal(limit, total);
        }

        // ---- venom quality ----

        [Theory]
        // origin, care streak, fed today, mentor, expected tier
        [InlineData(ToadOrigin.Wild, 0, true, false, Tier.Standard)]       // just caught: stressed
        [InlineData(ToadOrigin.Wild, 2, true, false, Tier.Premium)]        // settled
        [InlineData(ToadOrigin.Wild, 8, true, false, Tier.Premium)]        // wild ceiling without the mentor
        [InlineData(ToadOrigin.Wild, 8, true, true, Tier.Heavenly)]
        [InlineData(ToadOrigin.Wild, 8, false, false, Tier.Standard)]      // hungry today
        [InlineData(ToadOrigin.Bred, 0, true, false, Tier.Standard)]       // bred: no capture stress
        [InlineData(ToadOrigin.Bred, 5, true, false, Tier.Premium)]
        [InlineData(ToadOrigin.Bred, 3, true, true, Tier.Premium)]         // the mentor's care tip
        [InlineData(ToadOrigin.Bred, 0, false, false, Tier.Poor)]
        [InlineData(ToadOrigin.Sewer, 5, true, false, Tier.Heavenly)]
        [InlineData(ToadOrigin.Sewer, 0, true, false, Tier.Standard)]
        [InlineData(ToadOrigin.BlackMarket, 0, true, false, Tier.Trash)]
        [InlineData(ToadOrigin.BlackMarket, 9, true, false, Tier.Standard)]
        [InlineData(ToadOrigin.BlackMarket, 0, false, false, Tier.Trash)]  // never below Trash
        public void VenomTier(ToadOrigin origin, int care, bool fed, bool mentor, int expected)
            => Assert.Equal(expected, Venom.TierOf(origin, care, fed, mentor));

        [Fact]
        public void MilkingUsesEachToadsCare()
        {
            var r = Rules(capacity: 2);                             // no room to breed: just the two
            var t = With(r, ToadOrigin.Sewer, ToadOrigin.BlackMarket);
            for (int d = 1; d <= 5; d++) Husbandry.Feed(t, 10, d, r);
            var m = Husbandry.Milk(t, 5, r);
            Assert.Equal(new[] { Tier.Heavenly, Tier.Standard }, m.Tiers);
        }

        [Theory]
        [InlineData(ToadOrigin.BlackMarket)]
        [InlineData(ToadOrigin.Bred)]
        [InlineData(ToadOrigin.Wild)]
        [InlineData(ToadOrigin.Sewer)]
        public void ALiveToadItemKeepsItsOrigin(ToadOrigin o) => Assert.Equal(o, LiveToads.OriginOf(LiveToads.ItemTier(o)));

        [Fact]
        public void TrashLiveToadsCountAsBlackMarket() => Assert.Equal(ToadOrigin.BlackMarket, LiveToads.OriginOf(Tier.Trash));

        // ---- dice ----

        [Fact]
        public void DiceAreDeterministicAndInRange()
        {
            for (int day = 0; day < 500; day++)
            {
                double u = Dice.Unit(42, day, 7);
                Assert.InRange(u, 0.0, 0.9999999999);
                Assert.Equal(u, Dice.Unit(42, day, 7));
                Assert.InRange(Dice.Range(42, day, 8, 3, 6), 3, 6);
            }
            Assert.NotEqual(Dice.Unit(42, 1, 7), Dice.Unit(43, 1, 7));
        }

        [Fact]
        public void DiceRangeHitsBothEnds()
        {
            var seen = new HashSet<int>();
            for (int day = 0; day < 400; day++) seen.Add(Dice.Range(9, day, 1, 2, 4));
            Assert.Equal(new HashSet<int> { 2, 3, 4 }, seen);
        }

        // ---- clock and windows ----

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1930, 1170)]
        [InlineData(2359, 1439)]
        [InlineData(600, 360)]
        public void ClockConverts(int hhmm, int minutes)
        {
            Assert.Equal(minutes, Clock.ToMinutes(hhmm));
            Assert.Equal(hhmm, Clock.ToHhmm(minutes));
        }

        [Fact]
        public void AWindowPastMidnightBelongsToTheEveningBefore()
        {
            var w = new Window(23 * 60, 120);
            Assert.True(w.OpenSameDay(23 * 60 + 30));
            Assert.False(w.OpenSameDay(30));
            Assert.True(w.OpenNextDay(30));
            Assert.False(w.OpenNextDay(60 + 5));
            Assert.Equal(90, w.MinutesInto(30, true));
        }

        // ---- pond ----

        [Fact]
        public void PondWindowsStayInTheirBounds()
        {
            for (int day = 0; day < 300; day++)
            {
                var d = PondSchedule.For(1234, day, 8);
                Assert.InRange(d.Window.OpenAt, PondSchedule.EarliestOpen, PondSchedule.LatestOpen);
                Assert.InRange(d.Window.Minutes, PondSchedule.MinMinutes, PondSchedule.MaxMinutes);
                Assert.InRange(d.Spots.Count, PondSchedule.MinToads, PondSchedule.MaxToads);
                Assert.Equal(d.Spots.Count, d.Spots.Distinct().Count());
                Assert.All(d.Spots, s => Assert.InRange(s, 0, 7));
            }
        }

        [Fact]
        public void PondIsTheSameForEveryPlayer()
        {
            var a = PondSchedule.For(99, 12, 8);
            var b = PondSchedule.For(99, 12, 8);
            Assert.Equal(a.Window.OpenAt, b.Window.OpenAt);
            Assert.Equal(a.Spots, b.Spots);
        }

        [Fact]
        public void PondDaysVary()
        {
            var opens = new HashSet<int>();
            for (int day = 0; day < 60; day++) opens.Add(PondSchedule.For(7, day, 8).Window.OpenAt);
            Assert.True(opens.Count >= 5);
        }

        [Fact]
        public void FewerSpotsThanToadsCapsTheCount()
        {
            var d = PondSchedule.For(5, 3, 2);
            Assert.True(d.Spots.Count <= 2);
            Assert.Empty(PondSchedule.For(5, 3, 0).Spots);
        }

        [Fact]
        public void OpenNowFindsTodayAndLastNight()
        {
            const long seed = 77;
            int found = 0, lateFound = 0;
            for (int day = 1; day < 200; day++)
            {
                var today = PondSchedule.For(seed, day, 6);
                int mid = today.Window.OpenAt + today.Window.Minutes / 2;
                if (mid < Clock.MinutesPerDay)
                {
                    var open = PondSchedule.OpenNow(seed, day, mid, 6, out int into);
                    Assert.NotNull(open);
                    Assert.Equal(day, open.Day);
                    Assert.Equal(today.Window.Minutes / 2, into);
                    found++;
                }
                var y = PondSchedule.For(seed, day - 1, 6);
                int end = y.Window.OpenAt + y.Window.Minutes;
                if (end > Clock.MinutesPerDay)
                {
                    var late = PondSchedule.OpenNow(seed, day, end - Clock.MinutesPerDay - 1, 6, out _);
                    Assert.NotNull(late);
                    Assert.Equal(day - 1, late.Day);
                    lateFound++;
                }
                Assert.Null(PondSchedule.OpenNow(seed, day, 12 * 60, 6, out _));   // never at noon
            }
            Assert.True(found > 50);
            Assert.True(lateFound > 0);
        }

        // ---- wildlife officer and penalties ----

        [Fact]
        public void TheOfficerWalksTheWholeRing()
        {
            var at = new HashSet<int>();
            for (int m = 0; m < WildlifeOfficer.LapMinutes; m++) at.Add(WildlifeOfficer.At(m, 8));
            Assert.Equal(Enumerable.Range(0, 8).ToHashSet(), at);
            Assert.Equal(WildlifeOfficer.At(5, 8), WildlifeOfficer.At(5 + WildlifeOfficer.LapMinutes, 8));
        }

        [Fact]
        public void TheOfficerSeesHisSpotAndNeighboursRoundTheRing()
        {
            Assert.Equal(0, WildlifeOfficer.At(0, 8));
            Assert.True(WildlifeOfficer.Sees(0, 0, 8));
            Assert.True(WildlifeOfficer.Sees(1, 0, 8));
            Assert.True(WildlifeOfficer.Sees(7, 0, 8));            // the ring wraps
            Assert.False(WildlifeOfficer.Sees(4, 0, 8));
            Assert.False(WildlifeOfficer.Sees(0, 0, 0));
        }

        [Theory]
        [InlineData(10.0, 0.0, 0)]
        [InlineData(0.0, 10.0, 2)]
        [InlineData(-10.0, 0.0, 4)]
        [InlineData(0.0, -10.0, 6)]
        [InlineData(7.0, 7.2, 1)]
        [InlineData(10.0, -1.0, 0)]                                  // just below +x wraps to spot 0, not 8
        [InlineData(7.0, -7.2, 7)]
        public void TheWalkingOfficerIsAtTheSpotNearestHim(double dx, double dz, int spot)
        {
            Assert.Equal(spot, WildlifeOfficer.SpotNearest(dx, dz, 8));
        }

        [Fact]
        public void TheWalkingOfficerNeedsSpotsAndAnOffset()
        {
            Assert.Equal(-1, WildlifeOfficer.SpotNearest(1, 1, 0));
            Assert.Equal(-1, WildlifeOfficer.SpotNearest(0, 0, 8));
        }

        [Fact]
        public void SeeingFromASpotMatchesTheClock()
        {
            for (int m = 0; m < WildlifeOfficer.LapMinutes; m++)
                for (int s = 0; s < 8; s++)
                    Assert.Equal(WildlifeOfficer.Sees(s, m, 8), WildlifeOfficer.SeesFrom(s, WildlifeOfficer.At(m, 8), 8));
            Assert.False(WildlifeOfficer.SeesFrom(0, -1, 8));
            Assert.False(WildlifeOfficer.SeesFrom(0, 8, 8));
            Assert.False(WildlifeOfficer.SeesFrom(9, 0, 8));
        }

        [Fact]
        public void TheWalkingOfficerHeadsForTheNextSpot()
        {
            Assert.Equal(1, WildlifeOfficer.Heading(0, 8));
            Assert.Equal(0, WildlifeOfficer.Heading(WildlifeOfficer.LapMinutes - 1, 8));   // round the ring
            Assert.Equal(-1, WildlifeOfficer.Heading(0, 0));
        }

        [Theory]
        [InlineData(20.0, 10.0, true)]
        [InlineData(25.0, 10.0, true)]
        [InlineData(25.1, 10.0, false)]
        [InlineData(200.0, 10.0, false)]
        public void TheOfficerIsOnHisRoundNearThePond(double distance, double radius, bool on)
        {
            Assert.Equal(on, WildlifeOfficer.OnRound(distance, radius));
        }

        [Fact]
        public void ThereIsAlwaysASafeSpotOnABigEnoughPond()
        {
            for (int m = 0; m < WildlifeOfficer.LapMinutes; m++)
                Assert.Contains(Enumerable.Range(0, 8), s => !WildlifeOfficer.Sees(s, m, 8));
        }

        [Theory]
        [InlineData(1, 250f, false)]
        [InlineData(2, 500f, false)]
        [InlineData(3, 750f, true)]
        [InlineData(9, 1000f, true)]
        public void PenaltiesGrow(int offence, float fine, bool pursuit)
        {
            Assert.Equal(fine, Penalty.Fine(offence));
            Assert.Equal(pursuit, Penalty.Pursuit(offence));
        }

        // ---- sewer ----

        [Theory]
        [InlineData(true, false, false, false, SewerRoute.Mentor)]
        [InlineData(true, true, true, true, SewerRoute.Mentor)]
        [InlineData(false, true, false, false, SewerRoute.Hard)]
        [InlineData(false, false, true, false, SewerRoute.Hard)]
        [InlineData(false, false, false, true, SewerRoute.Hard)]   // no sewer spoke: Randy's tip once the sewer is open
        [InlineData(false, false, false, false, SewerRoute.Closed)]
        public void SewerRouteFromStory(bool spared, bool defeated, bool calmed, bool unlocked, SewerRoute expected)
            => Assert.Equal(expected, SewerToads.Route(spared, defeated, calmed, unlocked));

        [Fact]
        public void SewerToadCountsByRoute()
        {
            int hardDays = 0;
            for (int day = 0; day < 200; day++)
            {
                Assert.Equal(0, SewerToads.Count(SewerRoute.Closed, 3, day));
                Assert.InRange(SewerToads.Count(SewerRoute.Mentor, 3, day), 2, 3);
                int h = SewerToads.Count(SewerRoute.Hard, 3, day);
                Assert.InRange(h, 0, 1);
                hardDays += h;
            }
            Assert.InRange(hardDays, 60, 140);                      // about every other day
        }

        // ---- Randy's stall ----

        [Theory]
        [InlineData(6 * 60 + 59, false)]
        [InlineData(7 * 60, true)]
        [InlineData(18 * 60 + 59, true)]
        [InlineData(19 * 60, false)]
        public void DayStallHours(int minute, bool open) => Assert.Equal(open, RandysStall.DayOpenAt(minute));

        [Fact]
        public void TheBlackMarketNightRunsPastMidnight()
        {
            var night = RandysStall.Night(23 * 60, 2);
            Assert.Equal(10, RandysStall.NightOpen(10, 23 * 60 + 10, night));
            Assert.Equal(9, RandysStall.NightOpen(10, 30, night));          // after midnight: last night's
            Assert.Equal(-1, RandysStall.NightOpen(10, 2 * 60, night));
            Assert.Equal(-1, RandysStall.NightOpen(10, 12 * 60, night));
            Assert.Equal(-1, RandysStall.NightOpen(0, 30, night));          // no night before the first day
        }

        [Fact]
        public void BlackMarketNightLengthIsBounded()
        {
            Assert.Equal(60, RandysStall.Night(22 * 60, 0).Minutes);
            Assert.Equal(480, RandysStall.Night(22 * 60, 99).Minutes);
            Assert.Equal(22 * 60, RandysStall.Night(22 * 60 + Clock.MinutesPerDay, 2).OpenAt);
        }

        [Theory]
        [InlineData(100f, 4f, 0, 400f)]
        [InlineData(100f, 4f, 1, 460f)]
        [InlineData(100f, 4f, 3, 580f)]
        [InlineData(100f, 0.5f, 0, 100f)]     // never below the day price
        [InlineData(-5f, 4f, 0, 0f)]
        public void ToadPriceClimbsThroughTheNight(float basePrice, float markup, int sold, float expected)
            => Assert.Equal(expected, RandysStall.ToadPrice(basePrice, markup, sold));

        [Fact]
        public void BlackMarketStockIsSmall()
        {
            for (int n = 0; n < 100; n++) Assert.InRange(RandysStall.Stock(5, n), RandysStall.MinStock, RandysStall.MaxStock);
        }

        // ---- bad batches ----

        [Fact]
        public void BadBatchOddsFallWithQuality()
        {
            for (int t = Tier.Trash; t < Tier.Heavenly; t++) Assert.True(BadBatch.Chance(t) > BadBatch.Chance(t + 1));
            Assert.Equal(0.10, BadBatch.Chance(Tier.Standard), 6);
            Assert.Equal(0.20, BadBatch.Chance(Tier.Standard, 2.0), 6);
            Assert.Equal(1.0, BadBatch.Chance(Tier.Trash, 10.0), 6);
            Assert.Equal(0.0, BadBatch.Chance(Tier.Trash, 0.0), 6);
            Assert.Equal(BadBatch.Chance(Tier.Heavenly), BadBatch.Chance(99));
        }

        [Theory]
        [InlineData(Tier.Standard, 0.05, true)]
        [InlineData(Tier.Standard, 0.10, false)]
        [InlineData(Tier.Heavenly, 0.005, true)]
        [InlineData(Tier.Heavenly, 0.02, false)]
        public void BadBatchRoll(int tier, double roll, bool bad) => Assert.Equal(bad, BadBatch.IsBad(tier, roll));

        [Fact]
        public void BadBatchRatesMatchTheOddsOverManyRolls()
        {
            int bad = 0;
            for (int i = 0; i < 20000; i++) if (BadBatch.IsBad(Tier.Poor, Dice.Unit(1, i, 3))) bad++;
            Assert.InRange(bad / 20000.0, 0.23, 0.27);
        }

        // ---- designs and reputation ----

        [Fact]
        public void PresetsAreAddedOnceAndKept()
        {
            var book = new DesignBook();
            book.EnsurePresets();
            book.EnsurePresets();
            Assert.Equal(DesignBook.Presets.Length, book.Designs.Count);
            Assert.All(book.Designs, d => Assert.True(d.Preset));
        }

        [Fact]
        public void PaintedDesignsGetFreshIds()
        {
            var book = new DesignBook();
            var a = book.AddPainted("Frog Prince", "strokes-a");
            var b = book.AddPainted("", null);
            Assert.Equal("painted:1", a.Id);
            Assert.Equal("painted:2", b.Id);
            Assert.Equal("Design 2", b.Name);
            Assert.Equal("", b.Drawing);
            Assert.Same(a, book.Find("painted:1"));
            Assert.Null(book.Find("nope"));
        }

        [Fact]
        public void NextCyclesAndSkipsRetired()
        {
            var book = new DesignBook();
            book.EnsurePresets();
            book.Designs[1].Retired = true;
            Assert.Equal(book.Designs[0].Id, book.Next(null).Id);
            Assert.Equal(book.Designs[2].Id, book.Next(book.Designs[0].Id).Id);
            Assert.Equal(book.Designs[0].Id, book.Next(book.Designs[^1].Id).Id);   // wraps
            foreach (var d in book.Designs) d.Retired = true;
            Assert.Null(book.Next(null));
        }

        [Fact]
        public void GoodTripsAtHighQualityBuildABrand()
        {
            var d = new Design { Id = "x" };
            for (int i = 0; i < 7; i++) DesignBook.RecordTrip(d, true, Tier.Heavenly);
            Assert.Equal(21f, d.Reputation);
            Assert.Equal(Standing.Loved, DesignBook.StandingOf(d.Reputation));
            Assert.Equal(7, d.GoodTrips);
        }

        [Fact]
        public void BadTripsBurnADesign()
        {
            var d = new Design { Id = "x" };
            DesignBook.RecordTrip(d, true, Tier.Standard);
            DesignBook.RecordTrip(d, false, Tier.Standard);
            DesignBook.RecordTrip(d, false, Tier.Standard);
            Assert.Equal(-15f, d.Reputation);
            Assert.Equal(Standing.Burnt, DesignBook.StandingOf(d.Reputation));
            Assert.Equal(2, d.BadTrips);
        }

        [Fact]
        public void ReputationIsBounded()
        {
            var d = new Design { Id = "x" };
            for (int i = 0; i < 100; i++) DesignBook.RecordTrip(d, false, Tier.Trash);
            Assert.Equal(DesignBook.MinRep, d.Reputation);
            for (int i = 0; i < 100; i++) DesignBook.RecordTrip(d, true, Tier.Heavenly);
            Assert.Equal(DesignBook.MaxRep, d.Reputation);
            DesignBook.RecordTrip(null, true, Tier.Heavenly);           // no design: nothing happens
        }

        [Theory]
        [InlineData(-15f, Standing.Burnt)]
        [InlineData(-14.9f, Standing.Unknown)]
        [InlineData(4.9f, Standing.Unknown)]
        [InlineData(5f, Standing.Known)]
        [InlineData(20f, Standing.Loved)]
        public void Standings(float rep, Standing s) => Assert.Equal(s, DesignBook.StandingOf(rep));

        [Fact]
        public void LoyaltyAndBadTripsMoveRelationships()
        {
            Assert.True(DesignBook.RelationshipChange(Standing.Loved, true) > DesignBook.RelationshipChange(Standing.Known, true));
            Assert.Equal(0f, DesignBook.RelationshipChange(Standing.Unknown, true));
            Assert.True(DesignBook.RelationshipChange(Standing.Burnt, true) < 0f);
            foreach (Standing s in System.Enum.GetValues(typeof(Standing)))
                Assert.True(DesignBook.RelationshipChange(s, false) < DesignBook.RelationshipChange(Standing.Burnt, true));
        }

        // ---- batch ledger ----

        [Fact]
        public void TabsAreMatchedFirstInFirstOutByQuality()
        {
            var l = new BatchLedger();
            var a = l.Add("preset:plain", Tier.Premium, false, 2);
            var b = l.Add("preset:sunburst", Tier.Standard, true, 1);
            var c = l.Add("preset:third-eye", Tier.Premium, true, 1);
            Assert.Same(a, l.Attribute(Tier.Premium));
            Assert.Same(a, l.Attribute(Tier.Premium));
            Assert.Same(c, l.Attribute(Tier.Premium));
            Assert.Same(b, l.Attribute(Tier.Standard));
            Assert.Equal(0, a.Tabs + b.Tabs + c.Tabs);
        }

        [Fact]
        public void ExtraTabsFallOnTheNewestBatchOfThatQuality()
        {
            var l = new BatchLedger();
            l.Add("a", Tier.Premium, false, 1);
            var newest = l.Add("b", Tier.Premium, true, 0);
            l.Attribute(Tier.Premium);
            Assert.Same(newest, l.Attribute(Tier.Premium));
            Assert.Null(l.Attribute(Tier.Poor));
        }

        [Fact]
        public void PeekNamesTheBatchWithoutTakingATab()
        {
            var l = new BatchLedger();
            var a = l.Add("preset:plain", Tier.Premium, false, 1);
            Assert.Same(a, l.Peek(Tier.Premium));
            Assert.Equal(1, a.Tabs);
            Assert.Same(a, l.Attribute(Tier.Premium));
            Assert.Equal(0, a.Tabs);
            Assert.Same(a, l.Peek(Tier.Premium));                            // used up: still the newest of that quality
            Assert.Null(l.Peek(Tier.Poor));
        }

        [Fact]
        public void TierForADesignIsOneWhereItsBatchComesFirst()
        {
            var l = new BatchLedger();
            l.Add("preset:plain", Tier.Premium, false, 2);
            l.Add("preset:sunburst", Tier.Premium, false, 3);                 // behind plain at Premium
            l.Add("preset:sunburst", Tier.Standard, false, 1);                // first at Standard
            Assert.Equal(Tier.Premium, l.TierFor("preset:plain"));
            Assert.Equal(Tier.Standard, l.TierFor("preset:sunburst"));
            Assert.Equal(-1, l.TierFor("preset:third-eye"));
            var only = new BatchLedger();
            only.Add("preset:plain", Tier.Premium, false, 1);
            only.Add("preset:sunburst", Tier.Premium, false, 1);
            Assert.Equal(-1, only.TierFor("preset:sunburst"));                // plain's tab is taken first
        }

        [Fact]
        public void TheLedgerStaysBounded()
        {
            var l = new BatchLedger();
            var keep = l.Add("keep", Tier.Premium, false, 20);
            for (int i = 0; i < BatchLedger.MaxBatches + 50; i++) l.Add("used", Tier.Standard, false, 0);
            Assert.Equal(BatchLedger.MaxBatches, l.Batches.Count);
            Assert.Contains(keep, l.Batches);                       // batches with tabs left outlive used-up ones
            Assert.Equal(BatchLedger.MaxBatches + 52, l.NextId);       // IDs are never reused
        }

        [Fact]
        public void LedgerClampsInput()
        {
            var l = new BatchLedger();
            var b = l.Add("x", 99, false, -3);
            Assert.Equal(Tier.Heavenly, b.Tier);
            Assert.Equal(0, b.Tabs);
        }

        // ---- painted designs: strokes as text ----

        [Fact]
        public void StrokesRoundTrip()
        {
            var strokes = new List<Stroke> { new Stroke(20, 30, 200, 150, 3, 24), new Stroke(100, 100, 100, 100, 1, 10), new Stroke(16, 16, 434, 284, 8, 32) };
            string text = StrokeCodec.Encode(strokes);
            Assert.Equal("s1:20,30,200,150,3,24;100,100,100,100,1,10;16,16,434,284,8,32", text);
            Assert.Equal(strokes, StrokeCodec.Decode(text));
            Assert.True(StrokeCodec.HasStrokes(text));
        }

        [Fact]
        public void BrushSizeIsKept()
        {
            // the game's own SprayStroke.Serialize drops the size; ours must not
            var back = StrokeCodec.Decode(StrokeCodec.Encode(new[] { new Stroke(50, 50, 60, 60, 2, 32) }));
            Assert.Equal(32, back.Single().Size);
        }

        [Theory]
        [InlineData(0, 24)]      // no colour
        [InlineData(9, 24)]      // past Brown
        [InlineData(1, 9)]       // brush too small
        [InlineData(1, 33)]      // brush too big
        public void BadColourOrBrushIsDropped(int color, int size)
        {
            Assert.Empty(StrokeCodec.Decode(StrokeCodec.Encode(new[] { new Stroke(100, 100, 120, 120, color, size) })));
        }

        [Theory]
        [InlineData(4, 100, 10)]     // brush half-width 5 would leave the left edge
        [InlineData(5, 100, 10)]     // just fits
        [InlineData(446, 100, 10)]   // leaves the right edge (450 - 5 = 445)
        [InlineData(445, 100, 10)]
        [InlineData(100, 285, 32)]   // top: 300 - 16 = 284
        public void BrushMustStayOnTheCanvas(int x, int y, int size)
        {
            bool fits = x >= (size + 1) / 2 && x <= StrokeCodec.CanvasWidth - (size + 1) / 2 && y >= (size + 1) / 2 && y <= StrokeCodec.CanvasHeight - (size + 1) / 2;
            Assert.Equal(fits, StrokeCodec.Valid(new Stroke(x, y, x, y, 1, size), StrokeCodec.CanvasWidth, StrokeCodec.CanvasHeight));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("strokes-a")]                                     // no prefix: not ours
        [InlineData("s1:")]
        [InlineData("s1:1,2,3")]
        [InlineData("s1:a,b,c,d,e,f")]
        [InlineData("s1:-20,30,200,150,3,24")]
        [InlineData("s1:70000,30,200,150,3,24")]
        [InlineData("s1:20,30,200,150,300,24")]
        public void DamagedTextDecodesToNothing(string text)
        {
            Assert.Empty(StrokeCodec.Decode(text));
            Assert.False(StrokeCodec.HasStrokes(text));
        }

        [Fact]
        public void DamagedStrokesAreSkippedNotFatal()
        {
            var back = StrokeCodec.Decode("s1:20,30,200,150,3,24;junk;;1,1,1,1,1,1;40,40,50,50,5,16");
            Assert.Equal(2, back.Count);
            Assert.Equal(5, back[1].Color);
        }

        [Fact]
        public void StrokeCountIsBounded()
        {
            var many = Enumerable.Range(0, StrokeCodec.MaxStrokes + 50).Select(i => new Stroke(100, 100, 101, 101, 1, 10));
            Assert.Equal(StrokeCodec.MaxStrokes, StrokeCodec.Decode(StrokeCodec.Encode(many)).Count);
        }

        [Fact]
        public void EncodeSkipsUndrawableStrokes()
        {
            Assert.Equal("s1:", StrokeCodec.Encode(new[] { new Stroke(0, 0, 0, 0, 1, 10) }));
            Assert.Equal("s1:", StrokeCodec.Encode(null));
        }

        [Fact]
        public void DesignIsNamedAfterItsMainColour()
        {
            var strokes = new[]
            {
                new Stroke(20, 20, 400, 20, 3, 32),    // a long, fat red line
                new Stroke(20, 50, 60, 50, 5, 32),     // a short blue one
                new Stroke(20, 80, 200, 80, 1, 10),    // a thin black one
            };
            Assert.Equal(3, DesignNamer.MainColor(strokes));
            Assert.Equal("Red design 4", DesignNamer.Name(strokes, 4));
            Assert.Equal("Plain design 1", DesignNamer.Name(new Stroke[0], 1));
        }

        [Fact]
        public void PaintedDesignJoinsTheCycleAndKeepsItsStrokes()
        {
            var book = new DesignBook();
            book.EnsurePresets();
            var strokes = new[] { new Stroke(100, 100, 300, 200, 7, 24) };
            var d = book.AddPainted(DesignNamer.Name(strokes, book.NextPainted), StrokeCodec.Encode(strokes));
            Assert.Equal("Pink design 1", d.Name);
            Assert.False(d.Preset);
            Assert.Equal(strokes, StrokeCodec.Decode(book.Find(d.Id).Drawing));
            Assert.Same(d, book.Next(DesignBook.Presets[^1].Id));          // after the last built-in
            Assert.Equal(DesignBook.Presets[0].Id, book.Next(d.Id).Id);    // and round again
            DesignBook.RecordTrip(d, true, Tier.Heavenly);
            Assert.True(d.Reputation > 0f);
        }

        // ---- ids ----

        [Fact]
        public void ProductIdsAreNamespaced()
        {
            foreach (var id in new[] { Ids.ToadKind, Ids.LsdKind, Ids.ToadProduct, Ids.LsdProduct, Ids.SolutionRecipe })
                Assert.Contains(":", id);
            Assert.Equal(20, Ids.TabsPerSheet);
        }
    }
}
