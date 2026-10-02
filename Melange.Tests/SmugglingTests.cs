using System;
using System.Collections.Generic;
using System.Linq;
using Melange.Smuggling;
using Xunit;

namespace Melange.Tests
{
    public sealed class SmugglingTests
    {
        private static readonly SmugglingRules R = new SmugglingRules();
        private static readonly IReadOnlyList<MarketEntry> Weed = new[] { new MarketEntry("Marijuana", 40f) };

        private static SmugglingState Unlocked() => new SmugglingState { Unlocked = true, UnlockThreshold = 10, QualifyingPurchases = 10 };

        /// <summary>Unlocked, with the first order due on day 5.</summary>
        private static SmugglingState Ready() { var s = Unlocked(); s.NextOrderDay = 5; return s; }

        private static Order OrderOf(int units, float price = 10f, OrderAnswer answer = OrderAnswer.None, int departs = 10000)
            => new Order { Id = 1, DrugType = "Marijuana", MinQuality = Quality.Standard, Units = units, UnitPrice = price, PostedAt = 0, DepartsAt = departs, Answer = answer };

        // ================================================================ unlock

        [Fact]
        public void TheThresholdIsRolledBetweenTenAndFifteenInclusive()
        {
            var seen = new HashSet<int>();
            var rng = new Random(1);
            for (int i = 0; i < 2000; i++) seen.Add(Unlock.RollThreshold(rng, 10, 15));
            Assert.Equal(new[] { 10, 11, 12, 13, 14, 15 }, seen.OrderBy(x => x));
        }

        [Theory]
        [InlineData(5, 3, 5)]     // max below min: min
        [InlineData(0, 0, 1)]     // never zero: zero means "not rolled"
        public void TheThresholdRollIsSane(int min, int max, int expected)
            => Assert.Equal(expected, Unlock.RollThreshold(new Random(3), min, max));

        [Theory]
        [InlineData(299.99f, false)]
        [InlineData(300f, true)]
        [InlineData(1200f, true)]
        [InlineData(0f, false)]
        public void OnlyPurchasesOfThreeHundredOrMoreCount(float spend, bool counts)
            => Assert.Equal(counts, Unlock.Counts(spend, 300f));

        [Fact]
        public void CheapPurchasesNeverUnlock()
        {
            var s = new SmugglingState();
            var rng = new Random(2);
            for (int i = 0; i < 50; i++) Assert.False(Unlock.RecordPurchase(s, 120f, R, rng));
            Assert.Equal(0, s.QualifyingPurchases);
            Assert.False(s.Unlocked);
            Assert.InRange(s.UnlockThreshold, 10, 15);      // rolled on the first purchase, cheap or not
        }

        [Fact]
        public void TheNumberIsPassedOnExactlyOnceAtTheThreshold()
        {
            var s = new SmugglingState { UnlockThreshold = 12 };
            var rng = new Random(4);
            for (int i = 1; i <= 11; i++) Assert.False(Unlock.RecordPurchase(s, 300f, R, rng));
            Assert.False(s.Unlocked);
            Assert.False(Unlock.RecordPurchase(s, 50f, R, rng));     // a cheap one in between changes nothing
            Assert.True(Unlock.RecordPurchase(s, 450f, R, rng));
            Assert.True(s.Unlocked);
            Assert.False(Unlock.RecordPurchase(s, 900f, R, rng));    // later purchases still count but never re-unlock
            Assert.Equal(13, s.QualifyingPurchases);
            Assert.Equal(12, s.UnlockThreshold);                      // never re-rolled
        }

        [Fact]
        public void APassedThresholdCatchesUpOnLoad()
        {
            var s = new SmugglingState { UnlockThreshold = 10, QualifyingPurchases = 11 };
            Assert.True(Unlock.CatchUp(s));
            Assert.True(s.Unlocked);
            Assert.False(Unlock.CatchUp(s));
            Assert.False(Unlock.CatchUp(new SmugglingState { UnlockThreshold = 0, QualifyingPurchases = 30 }));
        }

        // ================================================================ timing

        [Theory]
        [InlineData(200, 120)]
        [InlineData(0, 0)]
        [InlineData(2359, 1439)]
        [InlineData(1830, 1110)]
        public void MinutesOfDay(int hhmm, int minutes) => Assert.Equal(minutes, Timing.MinuteOfDay(hhmm));

        [Fact]
        public void AbsoluteMinutesRoundTrip()
        {
            int t = Timing.At(9, 1745);
            Assert.Equal(9, Timing.DayOf(t));
            Assert.Equal(1745, Timing.HhmmOf(t));
            Assert.Equal(-1, Timing.DayOf(-5));
        }

        [Fact]
        public void AnEarlyMorningDepartureIsTheNightAfterTheDeadlineDay()
        {
            Assert.Equal(Timing.At(6, 200), Timing.Departure(5, 0, 200));   // same night
            Assert.Equal(Timing.At(7, 200), Timing.Departure(5, 1, 200));   // a day's grace
            Assert.Equal(Timing.At(5, 2300), Timing.Departure(5, 0, 2300)); // a late-evening departure is that day's
            Assert.Equal(Timing.At(6, 200), Timing.Departure(5, -3, 200));  // negative lead is treated as 0
        }

        [Fact]
        public void TheBoatReturnsAtTheFirstReturnTimeAfterAProperVoyage()
        {
            int departs = Timing.At(7, 200);
            Assert.Equal(Timing.At(7, 700), Timing.ReturnAfter(departs, 700));
            Assert.Equal(Timing.At(8, 300), Timing.ReturnAfter(departs, 300));   // 02:00 to 03:00 is too short: the next day's
        }

        [Theory]
        [InlineData(2100, true)]
        [InlineData(2359, true)]
        [InlineData(200, true)]
        [InlineData(459, true)]
        [InlineData(500, false)]
        [InlineData(1200, false)]
        [InlineData(2059, false)]
        public void TheCurfewRunsNineToFive(int hhmm, bool curfew) => Assert.Equal(curfew, Timing.IsCurfew(hhmm));

        [Theory]
        [InlineData(0, "now")]
        [InlineData(-10, "now")]
        [InlineData(40, "40m")]
        [InlineData(120, "2h")]
        [InlineData(325, "5h 25m")]
        public void TimeLeftReadsNaturally(int minutes, string text) => Assert.Equal(text, Timing.Left(minutes));

        [Fact]
        public void TheNextOrderDayIsAtLeastTomorrow()
        {
            Assert.Equal(10, Timing.NextOrderDay(Timing.At(7, 200), 3));
            Assert.Equal(8, Timing.NextOrderDay(Timing.At(7, 200), 0));
        }

        // ================================================================ orders

        [Fact]
        public void OrdersGrowWithRankAndReputation()
        {
            int low = Orders.Size(0, 50, 0.5, R);
            int high = Orders.Size(10, 50, 0.5, R);
            Assert.True(high > low);
            Assert.True(Orders.Size(5, 100, 0.5, R) > Orders.Size(5, 0, 0.5, R));
        }

        [Fact]
        public void OrderSizesAreWholeBricksWithinBounds()
        {
            var rng = new Random(9);
            for (int i = 0; i < 500; i++)
            {
                int units = Orders.Size(rng.Next(0, 11), rng.Next(0, 101), rng.NextDouble(), R);
                Assert.Equal(0, units % R.UnitStep);
                Assert.InRange(units, R.MinUnits, R.MaxUnits);
            }
        }

        [Fact]
        public void OrderSizeAtTheMiddleIsTheBasePlusRank()
        {
            // rank 5, reputation 50 (factor 1.0), jitter 0.5 (factor 1.0): 200 + 5*60 = 500
            Assert.Equal(500, Orders.Size(5, 50, 0.5, R));
        }

        [Fact]
        public void HugeOrdersAreCapped()
        {
            var r = new SmugglingRules { MaxUnits = 600 };
            Assert.Equal(600, Orders.Size(10, 100, 1.0, r));
        }

        [Fact]
        public void PricesArePremiumOverMarket()
        {
            Assert.Equal(48f, Orders.UnitPrice(40f, Quality.Standard, R), 2);
            Assert.Equal(55.2f, Orders.UnitPrice(40f, Quality.Premium, R), 2);
            Assert.Equal(0f, Orders.UnitPrice(-5f, Quality.Standard, R));
        }

        [Fact]
        public void NoProductMeansNoOrder()
        {
            Assert.Null(Orders.Generate(new Random(1), 1, 0, 5, 50, new MarketEntry[0], R));
            Assert.Null(Orders.Generate(new Random(1), 1, 0, 5, 50, null, R));
            Assert.Null(Orders.Generate(new Random(1), 1, 0, 5, 50, new[] { new MarketEntry("Cocaine", 0f) }, R));
        }

        [Fact]
        public void AGeneratedOrderIsForSomethingThePlayerMakes()
        {
            var market = new[] { new MarketEntry("Marijuana", 40f), new MarketEntry("Methamphetamine", 70f) };
            var rng = new Random(5);
            var types = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                var o = Orders.Generate(rng, i, Timing.At(3, 800), 5, 50, market, R);
                types.Add(o.DrugType);
                Assert.Equal(Timing.At(5, 200), o.DepartsAt);      // posted day 3, a day's grace, sails 02:00 after day 4
                Assert.Equal(Quality.Standard, o.MinQuality);       // rank 5 is below the Premium rank
                Assert.Equal(o.DrugType == "Marijuana" ? 48f : 84f, o.UnitPrice, 2);
            }
            Assert.Equal(2, types.Count);
        }

        [Fact]
        public void HighRanksSometimesAskForPremium()
        {
            var rng = new Random(6);
            var qualities = Enumerable.Range(0, 200).Select(i => Orders.Generate(rng, i, 0, 9, 50, Weed, R).MinQuality).ToList();
            Assert.Contains(Quality.Premium, qualities);
            Assert.Contains(Quality.Standard, qualities);
        }

        [Fact]
        public void TheHoldTakesOnlyMatchingProduct()
        {
            var s = Unlocked();
            var o = OrderOf(100);
            s.Order = o;
            Assert.Equal(3, Orders.ItemsTaken(o, s, 0, "Marijuana", Quality.Standard, 20, 3, R));
            Assert.Equal(3, Orders.ItemsTaken(o, s, 0, "marijuana", Quality.Heavenly, 20, 3, R));   // better is fine
            Assert.Equal(0, Orders.ItemsTaken(o, s, 0, "Cocaine", Quality.Standard, 20, 3, R));
            Assert.Equal(0, Orders.ItemsTaken(o, s, 0, "Marijuana", Quality.Poor, 20, 3, R));
        }

        [Fact]
        public void TheHoldStopsAtItsCapacityInWholeItems()
        {
            var s = Unlocked();
            var o = OrderOf(100);                         // capacity 150
            s.Order = o;
            Voyage.Load(s, "og", "OG Kush", 2, 140);
            Assert.Equal(0, Orders.ItemsTaken(o, s, 0, "Marijuana", 2, 20, 5, R));   // 10 units of room: no whole brick fits
            Assert.Equal(2, Orders.ItemsTaken(o, s, 0, "Marijuana", 2, 5, 5, R));    // but two jars do
            Assert.Equal(5, Orders.ItemsTaken(o, s, 0, "Marijuana", 2, 1, 5, R));
        }

        [Fact]
        public void TheHoldIsShutWhenTheBoatIsOutOrTheOrderIsOff()
        {
            var s = Unlocked();
            var o = OrderOf(100, departs: 500);
            s.Order = o;
            Assert.Equal(0, Orders.ItemsTaken(o, s, 500, "Marijuana", 2, 1, 5, R));   // deadline passed
            s.Boat = BoatPhase.Away;
            Assert.Equal(0, Orders.ItemsTaken(o, s, 0, "Marijuana", 2, 1, 5, R));
            s.Boat = BoatPhase.Moored;
            o.Answer = OrderAnswer.Declined;
            Assert.Equal(0, Orders.ItemsTaken(o, s, 0, "Marijuana", 2, 1, 5, R));
            Assert.Equal(0, Orders.ItemsTaken(null, s, 0, "Marijuana", 2, 1, 5, R));
        }

        [Fact]
        public void StacksPickedForTheSameDeliveryShareTheRoom()
        {
            var s = Unlocked();
            var o = OrderOf(100);                         // capacity 150
            s.Order = o;
            Assert.Equal(5, Orders.ItemsTaken(o, s, 0, "Marijuana", 2, 20, 9, R, pendingUnits: 40));    // 110 left: five bricks
            Assert.Equal(0, Orders.ItemsTaken(o, s, 0, "Marijuana", 2, 20, 9, R, pendingUnits: 150));
            Assert.Equal(7, Orders.ItemsTaken(o, s, 0, "Marijuana", 2, 20, 9, R, pendingUnits: -40));   // a negative pending is ignored
        }

        // ================================================================ payment

        [Fact]
        public void AFullHoldPaysTheOrderAndGainsReputation()
        {
            var st = Payment.Settle(OrderOf(100, 10f), 100, R);
            Assert.Equal(RunOutcome.Full, st.Outcome);
            Assert.Equal(1000f, st.Payout);
            Assert.Equal(100, st.PaidUnits);
            Assert.Equal(0, st.ExcessUnits);
            Assert.Equal(R.ReputationFull, st.ReputationChange);
        }

        [Fact]
        public void OverDeliveryIsBoughtCheaperUpToTheCap()
        {
            var st = Payment.Settle(OrderOf(100, 10f), 140, R);
            Assert.Equal(40, st.ExcessUnits);
            Assert.Equal(1000f + 40 * 10f * 0.6f, st.Payout);
            var capped = Payment.Settle(OrderOf(100, 10f), 400, R);   // more than the hold takes: only the capacity counts
            Assert.Equal(50, capped.ExcessUnits);
        }

        [Fact]
        public void AShortHoldPaysProRataAndCostsReputation()
        {
            var partial = Payment.Settle(OrderOf(100, 10f), 60, R);
            Assert.Equal(RunOutcome.Partial, partial.Outcome);
            Assert.Equal(600f, partial.Payout);
            Assert.Equal(0, partial.ReputationChange);

            var shortRun = Payment.Settle(OrderOf(100, 10f), 20, R);
            Assert.Equal(RunOutcome.Short, shortRun.Outcome);
            Assert.Equal(200f, shortRun.Payout);
            Assert.Equal(R.ReputationShort, shortRun.ReputationChange);
        }

        [Fact]
        public void AnEmptyHoldOnlyHurtsIfThePlayerSaidYes()
        {
            var noShow = Payment.Settle(OrderOf(100, 10f, OrderAnswer.Accepted), 0, R);
            Assert.Equal(RunOutcome.NoShow, noShow.Outcome);
            Assert.Equal(0f, noShow.Payout);
            Assert.Equal(R.ReputationNoShow, noShow.ReputationChange);

            var skipped = Payment.Settle(OrderOf(100, 10f, OrderAnswer.None), 0, R);
            Assert.Equal(RunOutcome.Skipped, skipped.Outcome);
            Assert.Equal(0, skipped.ReputationChange);
        }

        [Fact]
        public void PayoutsAreWholeDollars()
            => Assert.Equal(333f, Payment.Settle(OrderOf(100, 3.333f), 100, R).Payout);

        [Theory]
        [InlineData(50, 5, 55)]
        [InlineData(98, 5, 100)]
        [InlineData(4, -10, 0)]
        public void ReputationStaysInRange(int rep, int change, int expected) => Assert.Equal(expected, Payment.ApplyReputation(rep, change));

        // ================================================================ risk

        [Fact]
        public void RiskGrowsWithTheLoadAndIsCapped()
        {
            float small = Risk.PoliceRisk(20, 1200, false, R);
            float big = Risk.PoliceRisk(1000, 1200, false, R);
            Assert.Equal(0.034f, small, 4);
            Assert.Equal(0.23f, big, 4);
            Assert.Equal(R.RiskCap, Risk.PoliceRisk(100000, 1200, false, R), 4);
            Assert.Equal(0f, Risk.PoliceRisk(0, 1200, false, R));
        }

        [Fact]
        public void TheCurfewRaisesRisk()
            => Assert.Equal(Risk.PoliceRisk(500, 1200, false, R) * R.CurfewRiskMultiplier, Risk.PoliceRisk(500, 2300, false, R), 4);

        [Fact]
        public void TheRouteCarriesNoPoliceRiskByDefault()
        {
            Assert.Equal(0f, Risk.PoliceRisk(2000, 2300, true, R));
            Assert.False(Risk.Seized(0f, 0.0));                               // zero risk never seizes, even on a zero roll
        }

        [Fact]
        public void ASofterRouteSettingStillLowersRisk()
        {
            var r = new SmugglingRules { RouteRiskMultiplier = 0.25f };
            float street = Risk.PoliceRisk(500, 1200, false, r);
            float route = Risk.PoliceRisk(500, 1200, true, r);
            Assert.Equal(street * 0.25f, route, 4);
            Assert.True(route < street);
        }

        [Fact]
        public void RiskNeverPassesHalf()
        {
            var r = new SmugglingRules { BaseRisk = 0.9f, RiskCap = 1f, CurfewRiskMultiplier = 3f };
            Assert.Equal(0.5f, Risk.PoliceRisk(10, 2300, false, r));
        }

        [Theory]
        [InlineData(0.05f, 0.04, true)]
        [InlineData(0.05f, 0.051, false)]
        [InlineData(0.05f, 0.9, false)]
        public void SeizureIsARollUnderTheRisk(float risk, double roll, bool seized) => Assert.Equal(seized, Risk.Seized(risk, roll));

        [Theory]
        [InlineData(true, 30f, false, true)]
        [InlineData(true, 0f, false, true)]
        [InlineData(false, 30f, false, false)]      // the route isn't known yet
        [InlineData(true, 30f, true, false)]        // drove since: not the route
        [InlineData(true, 400f, false, false)]      // too long ago
        [InlineData(true, -1f, false, false)]       // never been on it
        public void ViaTheRoute(bool known, float since, bool drove, bool via) => Assert.Equal(via, Risk.ViaRoute(known, since, drove, R));

        // ================================================================ the quay and the route

        [Fact]
        public void TheBoatMoorsOnTheBasinSideParallelToTheQuay()
        {
            var m = Quay.Default;                                   // between (-73,-29) and (-67,-39)
            Assert.True(m.BoatX > m.StandX);                        // the basin is east of the quay
            Assert.Equal(-70f, (m.BoatX + m.StandX * 2f) / 3f, 1);  // both on the line through the midpoint's normal
            float bow = (float)(Math.Atan2(6, -10) * 180 / Math.PI);
            Assert.Equal(bow, m.Yaw, 2);
            Assert.Equal(4.5f, Quay.Distance(m.BoatX, 0, m.BoatZ, m.StandX, 0, m.StandZ), 3);
        }

        [Fact]
        public void EveryBerthIsOnTheEastSide()
        {
            for (int i = 0; i < Quay.Bollards.Length - 1; i++)
            {
                var m = Quay.Between(i);
                var p = Quay.Bollards[i]; var q = Quay.Bollards[i + 1];
                Assert.True(m.BoatX > (p.X + q.X) / 2f);
            }
            Assert.Equal(Quay.Between(3).BoatX, Quay.Between(99).BoatX);   // clamped to the last pair
        }

        [Theory]
        [InlineData(-34.5f, -5f, -10.7f, true)]     // the canal mouth
        [InlineData(26f, -4f, 11f, true)]           // the canal pipe
        [InlineData(53f, -9f, 66f, true)]           // the Sewer Office, down in the tunnels
        [InlineData(-11f, -4.5f, -2f, true)]        // the canal bed's deal spot
        [InlineData(-70f, -2.5f, -34f, false)]      // the quay itself
        [InlineData(-50f, -2.6f, 31.5f, false)]     // Oscar's warehouse, at street level
        [InlineData(-45f, -2.5f, -61f, false)]      // the Docks checkpoint
        public void OnTheRoute(float x, float y, float z, bool on) => Assert.Equal(on, Quay.OnRoute(x, y, z));

        // ================================================================ imports

        [Fact]
        public void TheCatalogueParsesAndSkipsJunk()
        {
            var list = Imports.Parse("acid:20:35, phosphorus:10:40.5;bad;also:bad:x;neg:5:-1;zero:0:5;acid:1:1; :2:2");
            Assert.Equal(2, list.Count);
            Assert.Equal("acid", list[0].ItemId);
            Assert.Equal(20, list[0].Crate);
            Assert.Equal(40.5f, list[1].UnitPrice);
            Assert.Empty(Imports.Parse(""));
            Assert.Empty(Imports.Parse(null));
            Assert.Equal(3, Imports.Parse(Imports.DefaultCatalogue).Count);
        }

        [Fact]
        public void MethylamineIsOfferedOnlyWhenConfigured()
        {
            Assert.DoesNotContain(Imports.Catalogue(Imports.DefaultCatalogue, "", 20, 50f), o => o.ItemId == "methylamine");
            Assert.DoesNotContain(Imports.Catalogue(Imports.DefaultCatalogue, null, 20, 50f), o => o.ItemId == "methylamine");
            var with = Imports.Catalogue(Imports.DefaultCatalogue, " methylamine ", 25, 50f);
            var m = Assert.Single(with, o => o.ItemId == "methylamine");
            Assert.Equal(25, m.Crate);
            // configured over a catalogue entry with the same ID: the methylamine settings win
            var replaced = Imports.Catalogue("methylamine:5:1", "methylamine", 25, 50f);
            Assert.Equal(25, Assert.Single(replaced).Crate);
        }

        [Fact]
        public void ReputationDiscountsImports()
        {
            var crate = new ImportOffer("acid", 20, 35f);
            Assert.Equal(700f, Imports.CratePrice(crate, 0, R));
            Assert.Equal(630f, Imports.CratePrice(crate, 50, R));
            Assert.Equal(560f, Imports.CratePrice(crate, 100, R));
            Assert.Equal(560f, Imports.CratePrice(crate, 250, R));
        }

        [Fact]
        public void ImportsOpenAfterAPaidRunWhileTheBoatIsIn()
        {
            var s = Unlocked();
            Assert.False(Imports.Open(s, R));
            s.RunsPaid = 1;
            Assert.True(Imports.Open(s, R));
            s.Boat = BoatPhase.Away;
            Assert.False(Imports.Open(s, R));
        }

        [Fact]
        public void ImportsLandOnReturnAndWaitUntilCollected()
        {
            var s = Unlocked();
            var acid = new ImportOffer("acid", 20, 35f);
            Imports.AddOrdered(s, acid, "Acid", 630f);
            Imports.AddOrdered(s, acid, "Acid", 630f);
            Assert.Equal(40, Assert.Single(s.ImportsOrdered).Quantity);
            Assert.Equal(40, Imports.Land(s));
            Assert.Empty(s.ImportsOrdered);
            Imports.Collected(s, "acid", 15);
            Assert.Equal(25, Assert.Single(s.ImportsWaiting).Quantity);
            Imports.Collected(s, "ACID", 25);
            Assert.Empty(s.ImportsWaiting);
        }

        // ================================================================ the voyage

        private static List<VoyageEvent> Tick(SmugglingState s, int now, int rank = 5, IReadOnlyList<MarketEntry> market = null)
            => Voyage.Tick(s, now, rank, () => market ?? Weed, R, new Random(11));

        [Fact]
        public void NothingHappensBeforeTheNumberIsPassedOn()
        {
            var s = new SmugglingState();
            Assert.Empty(Tick(s, Timing.At(30, 1200)));
            Assert.Null(s.Order);
        }

        [Fact]
        public void TheFirstOrderComesTheMorningAfterUnlocking()
        {
            var s = Unlocked();
            Assert.Empty(Tick(s, Timing.At(4, 1500)));          // unlocked mid-afternoon on day 4
            Assert.Equal(5, s.NextOrderDay);
            Assert.Empty(Tick(s, Timing.At(5, 759)));
            var e = Assert.Single(Tick(s, Timing.At(5, 800)));
            Assert.Equal(VoyageEventKind.OrderPosted, e.Kind);
            Assert.Equal(Timing.At(7, 200), s.Order.DepartsAt);
            Assert.Equal(1, s.Order.Id);
            Assert.Equal(2, s.NextOrderId);
        }

        [Fact]
        public void UnlockingEarlyInTheMorningPostsTheSameDay()
        {
            var s = Unlocked();
            Tick(s, Timing.At(4, 700));
            Assert.Equal(4, s.NextOrderDay);
        }

        [Fact]
        public void UnlockingRightAtTheOrderTimeWaitsForTomorrow()
        {
            Assert.Equal(6, Voyage.FirstOrderDay(Timing.At(5, 800), R));
            Assert.Equal(5, Voyage.FirstOrderDay(Timing.At(5, 759), R));
        }

        [Fact]
        public void TheBoatSailsAtTheDeadlineWithOrWithoutTheProduct()
        {
            var s = Unlocked();
            Tick(s, Timing.At(4, 1500));
            Tick(s, Timing.At(5, 800));
            var order = s.Order;
            Voyage.Load(s, "og", "OG Kush", 2, order.Units);
            Assert.Empty(Tick(s, order.DepartsAt - 1));
            var e = Assert.Single(Tick(s, order.DepartsAt));
            Assert.Equal(VoyageEventKind.Departed, e.Kind);
            Assert.Equal(RunOutcome.Full, e.Settlement.Outcome);
            Assert.Equal(order.Units * order.UnitPrice, e.Settlement.Payout, 0);
            Assert.Equal(BoatPhase.Away, s.Boat);
            Assert.Empty(s.Hold);
            Assert.Null(s.Order);
            Assert.Equal(55, s.Reputation);
            Assert.Equal(1, s.RunsPaid);
            Assert.Equal(1, s.RunsSailed);
            Assert.Equal(Timing.At(7, 700), s.BoatBackAt);
            Assert.Equal(10, s.NextOrderDay);                  // three days after sailing
        }

        [Fact]
        public void AnEmptyAcceptedRunSailsAndCostsReputation()
        {
            var s = Ready();
            Tick(s, Timing.At(5, 800));
            Assert.True(Voyage.Answer(s, s.Order.Id, true, Timing.At(5, 900), R));
            var e = Tick(s, Timing.At(7, 200)).Single();
            Assert.Equal(RunOutcome.NoShow, e.Settlement.Outcome);
            Assert.Equal(40, s.Reputation);
            Assert.Equal(0, s.RunsPaid);
            Assert.Equal(1, s.RunsSailed);
        }

        [Fact]
        public void TheBoatComesBackWithTheImports()
        {
            var s = Ready();
            Tick(s, Timing.At(5, 800));
            Imports.AddOrdered(s, new ImportOffer("acid", 20, 35f), "Acid", 700f);
            Tick(s, Timing.At(7, 200));
            Assert.Empty(Tick(s, Timing.At(7, 659)));
            var e = Assert.Single(Tick(s, Timing.At(7, 700)));
            Assert.Equal(VoyageEventKind.Returned, e.Kind);
            Assert.Equal(20, e.ImportsLanded);
            Assert.Equal(BoatPhase.Moored, s.Boat);
            Assert.Equal(20, Assert.Single(s.ImportsWaiting).Quantity);
        }

        [Fact]
        public void ALongSleepCatchesUpInOrder()
        {
            var s = Ready();
            Tick(s, Timing.At(5, 800));
            var order = s.Order;
            Voyage.Load(s, "og", "OG Kush", 2, 60);
            // slept from before the departure to well after the next order day
            var events = Tick(s, Timing.At(11, 900));
            Assert.Equal(new[] { VoyageEventKind.Departed, VoyageEventKind.Returned, VoyageEventKind.OrderPosted }, events.Select(e => e.Kind));
            Assert.True(s.Order.DepartsAt > Timing.At(11, 900));
            Assert.Equal(2, s.Order.Id);
            Assert.NotSame(order, s.Order);
        }

        [Fact]
        public void APostingAfterTheDeadlineGetsAFreshOne()
        {
            var s = Unlocked();
            s.NextOrderDay = 5;
            var r = new SmugglingRules { LeadDays = 0 };
            int now = Timing.At(6, 300);                       // asleep through day 5's whole window
            var e = Voyage.Tick(s, now, 5, () => Weed, r, new Random(1)).Single();
            Assert.Equal(VoyageEventKind.OrderPosted, e.Kind);
            Assert.Equal(Timing.At(7, 200), s.Order.DepartsAt);
        }

        [Fact]
        public void WithNothingToSellDafyddAsksAgainTomorrow()
        {
            var s = Ready();
            var e = Assert.Single(Voyage.Tick(s, Timing.At(5, 800), 5, () => new MarketEntry[0], R, new Random(1)));
            Assert.Equal(VoyageEventKind.NoProduct, e.Kind);
            Assert.Equal(6, s.NextOrderDay);
            Assert.Null(s.Order);
        }

        [Fact]
        public void DecliningCancelsTheOrderAndPushesTheNextOne()
        {
            var s = Ready();
            Tick(s, Timing.At(5, 800));
            int id = s.Order.Id;
            Assert.False(Voyage.Answer(s, id + 1, false, Timing.At(5, 900), R));   // an old text's buttons do nothing
            Assert.True(Voyage.Answer(s, id, false, Timing.At(5, 900), R));
            Assert.Null(s.Order);
            Assert.Equal(8, s.NextOrderDay);
            Assert.Equal(50, s.Reputation);
            Assert.Empty(Tick(s, Timing.At(7, 200)));                              // the boat doesn't sail
            Assert.Equal(BoatPhase.Moored, s.Boat);
        }

        [Fact]
        public void YouCantBackOutWithProductAboard()
        {
            var s = Ready();
            Tick(s, Timing.At(5, 800));
            Voyage.Load(s, "og", "OG Kush", 2, 20);
            Assert.False(Voyage.Answer(s, s.Order.Id, false, Timing.At(5, 900), R));
            Assert.NotNull(s.Order);
        }

        [Fact]
        public void AnAnswerIsTakenOnceAndNotAfterSailing()
        {
            var s = Ready();
            Tick(s, Timing.At(5, 800));
            var o = s.Order;
            Assert.False(Voyage.Answer(s, o.Id, true, o.DepartsAt, R));
            Assert.True(Voyage.Answer(s, o.Id, true, o.DepartsAt - 1, R));
            Assert.False(Voyage.Answer(s, o.Id, false, o.DepartsAt - 1, R));
            Assert.Equal(OrderAnswer.Accepted, o.Answer);
        }

        [Fact]
        public void LoadingMergesLots()
        {
            var s = Unlocked();
            Voyage.Load(s, "og", "OG Kush", 2, 20);
            Voyage.Load(s, "og", "OG Kush", 2, 40);
            Voyage.Load(s, "og", "OG Kush", 3, 20);
            Voyage.Load(s, "og", "OG Kush", 3, 0);
            Assert.Equal(2, s.Hold.Count);
            Assert.Equal(80, s.UnitsAboard);
        }

        [Fact]
        public void RepairMendsNullsAndRanges()
        {
            var s = new SmugglingState { Hold = null, ImportsOrdered = null, ImportsWaiting = null, Reputation = 140, NextOrderId = 0 };
            s.Repair();
            Assert.NotNull(s.Hold);
            Assert.NotNull(s.ImportsOrdered);
            Assert.NotNull(s.ImportsWaiting);
            Assert.Equal(100, s.Reputation);
            Assert.Equal(1, s.NextOrderId);
        }

        // ================================================================ the tanker

        [Fact]
        public void TheTankerTipNeedsTheSettingAnItemAndARank()
        {
            var s = Unlocked();
            Assert.False(TankerJob.TipDue(false, "methylamine", s, 8, 10, 0.0, R));
            Assert.False(TankerJob.TipDue(true, "", s, 8, 10, 0.0, R));
            Assert.False(TankerJob.TipDue(true, "methylamine", s, 3, 10, 0.0, R));
            Assert.False(TankerJob.TipDue(true, "methylamine", new SmugglingState(), 8, 10, 0.0, R));
            Assert.True(TankerJob.TipDue(true, "methylamine", s, 8, 10, 0.0, R));
            Assert.False(TankerJob.TipDue(true, "methylamine", s, 8, 10, 0.9, R));      // the roll
        }

        [Fact]
        public void TheTankerTipComesAtMostOncePerInterval()
        {
            var s = Unlocked();
            s.LastTankerTipDay = 10;
            Assert.False(TankerJob.TipDue(true, "methylamine", s, 8, 16, 0.0, R));
            Assert.True(TankerJob.TipDue(true, "methylamine", s, 8, 17, 0.0, R));
        }

        [Fact]
        public void TheRouteSweetensTheTanker()
        {
            Assert.Equal(40, TankerJob.Yield(40, false));
            Assert.Equal(50, TankerJob.Yield(40, true));
        }

        [Theory]
        [InlineData(true, 4f, 5f, true, true)]
        [InlineData(false, 4f, 5f, true, false)]     // never left the plant
        [InlineData(true, 1f, 5f, true, false)]      // a moment's pause
        [InlineData(true, 4f, 9f, true, false)]      // nobody near
        [InlineData(true, 4f, 5f, false, false)]     // the player is still in their car
        public void TheTankerIsHijackedWhenStoppedWithThePlayerAlongside(bool moved, float still, float dist, bool onFoot, bool taken)
            => Assert.Equal(taken, TankerJob.Hijacked(moved, still, dist, onFoot));

        // ================================================================ lines

        [Fact]
        public void TheOrderTextSaysWhatWhenAndHowMuch()
        {
            var o = OrderOf(400, 48f, departs: Timing.At(7, 200));
            o.DrugType = "Methamphetamine";
            string text = Lines.OrderText(o, Timing.At(5, 800));
            Assert.Contains("400 units of meth", text);
            Assert.Contains("$48.00", text);
            Assert.Contains("02:00", text);
            Assert.Contains("42h", text);
            Assert.Contains("with or without", text);
        }

        [Fact]
        public void EveryOutcomeHasALine()
        {
            foreach (RunOutcome outcome in Enum.GetValues(typeof(RunOutcome)))
                Assert.False(string.IsNullOrWhiteSpace(Lines.Departed(new Settlement { Outcome = outcome, Ordered = 100, Loaded = 60, Payout = 600f })));
            Assert.Contains("60 of 100", Lines.Departed(new Settlement { Outcome = RunOutcome.Partial, Ordered = 100, Loaded = 60, Payout = 600f }));
        }

        [Fact]
        public void StatusReadsTheState()
        {
            var s = Unlocked();
            Assert.Contains("No order", Lines.Status(s, 0));
            s.Order = OrderOf(200, departs: 600);
            Voyage.Load(s, "og", "OG Kush", 2, 40);
            Assert.Contains("40 aboard", Lines.Status(s, 0));
            Assert.Contains("10h", Lines.Status(s, 0));
            s.Boat = BoatPhase.Away;
            Assert.Contains("at sea", Lines.Status(s, 0));
        }

        // ---- Dafydd's tricorn and eyepatch ----

        [Theory]
        [InlineData(0.066, 1.0)]
        [InlineData(0.099, 1.5)]
        [InlineData(0.01, PirateFit.MinScale)]
        [InlineData(1.0, PirateFit.MaxScale)]
        [InlineData(0.0, 1.0)]
        [InlineData(-0.05, 1.0)]
        [InlineData(double.NaN, 1.0)]
        public void PirateGearScalesWithHisEyes(double spacing, double scale)
        {
            Assert.Equal(scale, PirateFit.Scale(spacing), 6);
        }

        [Theory]
        [InlineData(1.0, 1.0)]
        [InlineData(1.2, 1.2)]
        [InlineData(0.0, 1.0)]
        [InlineData(-2.0, 1.0)]
        [InlineData(9.0, 3.0)]
        public void TheHatScaleSettingIsSane(double setting, double scale)
        {
            Assert.Equal(scale, PirateFit.UserScale(setting), 6);
        }

        [Fact]
        public void PirateOffsetsParseOrFallBackToNone()
        {
            Assert.Equal((0.01, -0.02, 0.005), PirateFit.ParseOffset("0.01,-0.02, 0.005"));
            Assert.Equal((0.0, 0.0, 0.0), PirateFit.ParseOffset(""));
            Assert.Equal((0.0, 0.0, 0.0), PirateFit.ParseOffset(null));
            Assert.Equal((0.0, 0.0, 0.0), PirateFit.ParseOffset("0,1"));
            Assert.Equal((0.0, 0.0, 0.0), PirateFit.ParseOffset("0,x,0"));
            Assert.Equal((0.0, 0.0, 0.0), PirateFit.ParseOffset("0,5,0"));          // more than a metre is a typo
            Assert.Equal((0.0, 0.0, 0.0), PirateFit.ParseOffset("0,0.1,NaN"));
        }
    }
}
