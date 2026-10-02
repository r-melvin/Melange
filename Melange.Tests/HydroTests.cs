using System;
using System.Linq;
using Melange.Hydro;
using Xunit;

namespace Melange.Tests
{
    public sealed class HydroQualityTests
    {
        [Theory]
        [InlineData(0.0f, Quality.Trash)]
        [InlineData(0.25f, Quality.Trash)]
        [InlineData(0.26f, Quality.Poor)]
        [InlineData(0.4f, Quality.Poor)]
        [InlineData(0.5f, Quality.Standard)]
        [InlineData(0.75f, Quality.Standard)]
        [InlineData(0.8f, Quality.Premium)]
        [InlineData(0.9f, Quality.Premium)]
        [InlineData(0.95f, Quality.Heavenly)]
        public void TiersUseTheGamesStrictLines(float q, int tier) => Assert.Equal(tier, Quality.TierOf(q));

        [Fact]
        public void FertiliserAloneIsPremiumAtBest()
            => Assert.Equal(Quality.Premium, Quality.TierOf(Quality.AtHarvest(Quality.Fertiliser)));

        [Fact]
        public void FertiliserAndGrowNJuicerReachHeavenlyAtHarvest()
        {
            float q = Quality.AtHarvest(Quality.Fertiliser, Units.JuicerQuality);
            Assert.Equal(0.95f, q, 3);
            Assert.Equal(Quality.Heavenly, Quality.TierOf(q));
        }

        [Fact]
        public void GrowNJuicerAloneIsStandard()
            => Assert.Equal(Quality.Standard, Quality.TierOf(Quality.AtHarvest(Units.JuicerQuality)));

        [Theory]
        [InlineData(0.1f)]
        [InlineData(0.25f)]
        [InlineData(0.3f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        [InlineData(0.8f)]
        [InlineData(0.9f)]
        public void OneTierUpRaisesExactlyOneTier(float q)
            => Assert.Equal(Quality.TierOf(q) + 1, Quality.TierOf(Quality.OneTierUp(q)));

        [Fact]
        public void OneTierUpKeepsThePlaceInTheBand()
        {
            // 0.8 is a third of the way through Premium (0.75-0.9), so a third of the way through Heavenly (0.9-1.0)
            Assert.Equal(0.9333f, Quality.OneTierUp(0.8f), 3);
            // the base 0.5 is 2/7 through Standard (0.4-0.75), so 2/7 through Premium
            Assert.Equal(0.75f + 0.15f * (0.1f / 0.35f), Quality.OneTierUp(0.5f), 3);
        }

        [Fact]
        public void HeavenlyHasNowhereHigher()
        {
            Assert.Equal(0.95f, Quality.OneTierUp(0.95f));
            Assert.Equal(1f, Quality.OneTierUp(1.3f));
        }
    }

    public sealed class HydroCuringTests
    {
        [Theory]
        [InlineData(-1, CureStage.Growing)]
        [InlineData(0, CureStage.Rising)]
        [InlineData(719, CureStage.Rising)]
        [InlineData(720, CureStage.Peak)]
        [InlineData(1439, CureStage.Peak)]
        [InlineData(1440, CureStage.Falling)]
        [InlineData(2159, CureStage.Falling)]
        [InlineData(2160, CureStage.Settled)]
        [InlineData(100000, CureStage.Settled)]
        public void StagesAreTwelveHoursEach(int minutes, CureStage stage) => Assert.Equal(stage, Curing.StageAt(minutes));

        [Fact]
        public void PremiumPlantPeaksHeavenlyThenSettlesBackToPremium()
        {
            float grown = 0.8f;
            Assert.Equal(grown, Curing.QualityAt(grown, 0));
            Assert.Equal(Quality.Premium, Quality.TierOf(Curing.QualityAt(grown, 300)));
            Assert.Equal(Quality.Heavenly, Quality.TierOf(Curing.QualityAt(grown, 720)));
            Assert.Equal(Quality.Heavenly, Quality.TierOf(Curing.QualityAt(grown, 1439)));
            Assert.Equal(Quality.Premium, Quality.TierOf(Curing.QualityAt(grown, 2000)));
            Assert.Equal(grown, Curing.QualityAt(grown, 2160));
            Assert.Equal(grown, Curing.QualityAt(grown, 50000));
        }

        [Fact]
        public void CuringNeverGoesBelowTheGrownQuality()
        {
            foreach (float grown in new[] { 0.1f, 0.3f, 0.5f, 0.8f, 0.95f })
                for (int m = 0; m < 4000; m += 37)
                    Assert.True(Curing.QualityAt(grown, m) >= grown - 1e-6f, $"{grown} at {m}");
        }

        [Fact]
        public void CuringNeverRisesMoreThanOneTier()
        {
            foreach (float grown in new[] { 0.1f, 0.3f, 0.5f, 0.8f })
                for (int m = 0; m < 4000; m += 37)
                    Assert.True(Quality.TierOf(Curing.QualityAt(grown, m)) <= Quality.TierOf(grown) + 1);
        }

        [Fact]
        public void RiseIsSteadyAndFallMirrorsIt()
        {
            Assert.Equal(0.5f, Curing.Progress(360), 3);
            Assert.Equal(0.5f, Curing.Progress(1440 + 360), 3);
            Assert.True(Curing.Progress(100) < Curing.Progress(200));
            Assert.True(Curing.Progress(1500) > Curing.Progress(1600));
        }

        [Fact]
        public void HeavenlyAtHarvestStaysHeavenly()
        {
            for (int m = 0; m < 3000; m += 60) Assert.Equal(0.95f, Curing.QualityAt(0.95f, m), 4);
        }

        [Fact]
        public void MinutesSinceComesFromTheSharedClock()
        {
            Assert.Equal(90, Curing.MinutesSince(1440, 1530));
            Assert.Equal(0, Curing.MinutesSince(2000, 1530));   // a clock read before the record: not negative
        }
    }

    public sealed class HydroTrainingTests
    {
        [Theory]
        [InlineData(0, 0f)]
        [InlineData(1, 100f)]
        [InlineData(4, 400f)]
        [InlineData(5, 500f)]
        [InlineData(6, 500f)]
        [InlineData(20, 500f)]
        public void ExtraSigningFeeMatchesTheFixer(int recruited, float fee) => Assert.Equal(fee, Training.AdditionalSigningFee(recruited));

        [Fact]
        public void PricesAreTwoAndFiveTimesTheHirePrice()
        {
            float hire = Training.HirePrice(1000f, 500f);
            Assert.Equal(1500f, hire);
            Assert.Equal(3000f, Training.Price(TrainingLevel.Hydroponics, hire));
            Assert.Equal(7500f, Training.Price(TrainingLevel.Aeroponics, hire));
            Assert.Equal(0f, Training.Price(TrainingLevel.None, hire));
        }

        [Fact]
        public void LimitsAreSixteenAndTwentyFour()
        {
            Assert.Equal(8, Training.PotLimit(TrainingLevel.None, 8));
            Assert.Equal(16, Training.PotLimit(TrainingLevel.Hydroponics, 8));
            Assert.Equal(24, Training.PotLimit(TrainingLevel.Aeroponics, 8));
        }

        [Fact]
        public void TrainingNeverLowersAnotherModsLimit()
        {
            Assert.Equal(20, Training.PotLimit(TrainingLevel.Hydroponics, 20));
            Assert.Equal(30, Training.PotLimit(TrainingLevel.Aeroponics, 30));
        }

        [Fact]
        public void OnlyTrainedBotanistsOperateHoles()
        {
            Assert.True(Training.CanOperate(TrainingLevel.None, HoleKind.None));
            Assert.False(Training.CanOperate(TrainingLevel.None, HoleKind.Hydro));
            Assert.False(Training.CanOperate(TrainingLevel.None, HoleKind.Aero));
            Assert.True(Training.CanOperate(TrainingLevel.Hydroponics, HoleKind.Hydro));
            Assert.False(Training.CanOperate(TrainingLevel.Hydroponics, HoleKind.Aero));
            Assert.True(Training.CanOperate(TrainingLevel.Aeroponics, HoleKind.Hydro));
            Assert.True(Training.CanOperate(TrainingLevel.Aeroponics, HoleKind.Aero));
        }

        [Fact]
        public void AeroponicsNeedsHydroponicsAndBaronThree()
        {
            Assert.False(Training.CanOffer(TrainingLevel.None, TrainingLevel.Aeroponics, true, true, out var r1));
            Assert.Equal("needs hydroponics first", r1);
            Assert.False(Training.CanOffer(TrainingLevel.Hydroponics, TrainingLevel.Aeroponics, true, false, out var r2));
            Assert.Equal("unlocks at Baron III", r2);
            Assert.True(Training.CanOffer(TrainingLevel.Hydroponics, TrainingLevel.Aeroponics, true, true, out _));
        }

        [Fact]
        public void HydroponicsNeedsUnderlordThreeAndIsOfferedOnce()
        {
            Assert.False(Training.CanOffer(TrainingLevel.None, TrainingLevel.Hydroponics, false, false, out _));
            Assert.True(Training.CanOffer(TrainingLevel.None, TrainingLevel.Hydroponics, true, false, out _));
            Assert.False(Training.CanOffer(TrainingLevel.Hydroponics, TrainingLevel.Hydroponics, true, true, out var r));
            Assert.Equal("already trained", r);
            Assert.False(Training.CanOffer(TrainingLevel.Aeroponics, TrainingLevel.Hydroponics, true, true, out _));
        }

        [Fact]
        public void CoursesComeInOrder()
        {
            Assert.Equal(TrainingLevel.Hydroponics, Training.Next(TrainingLevel.None));
            Assert.Equal(TrainingLevel.Aeroponics, Training.Next(TrainingLevel.Hydroponics));
            Assert.Equal(TrainingLevel.None, Training.Next(TrainingLevel.Aeroponics));
        }

        [Fact]
        public void EachKindNeedsItsCourse()
        {
            Assert.Equal(TrainingLevel.Hydroponics, Units.HydroKind.Training);
            Assert.Equal(TrainingLevel.Aeroponics, Units.AeroKind.Training);
        }
    }

    public sealed class HydroReservoirTests
    {
        [Fact]
        public void DrainEmptiesTheReservoirInTheSetHours()
        {
            Assert.Equal(5f / 36f, Reservoir.DrainPerHour(5f, Units.HydroKind.ReservoirHours), 5);
            Assert.Equal(36f, Reservoir.HoursToDry(5f, Reservoir.DrainPerHour(5f, 36f)), 3);
            Assert.Equal(24f, Reservoir.HoursToDry(5f, Reservoir.DrainPerHour(5f, Units.AeroKind.ReservoirHours)), 3);
        }

        [Fact]
        public void ReservoirsOutlastVanillaPots()
        {
            // the slowest vanilla pot (moisture-preserving) lasts 11.3 hours
            Assert.True(Units.HydroKind.ReservoirHours > 11.3f * 2);
            Assert.True(Units.AeroKind.ReservoirHours > 11.3f * 2);
        }

        [Fact]
        public void NonsenseHoursFallBackSafely()
        {
            Assert.Equal(5f, Reservoir.DrainPerHour(5f, 0f));
            Assert.Equal(5f, Reservoir.DrainPerHour(5f, float.NaN));
            Assert.Equal(0f, Reservoir.DrainPerHour(0f, 10f));
            Assert.True(float.IsPositiveInfinity(Reservoir.HoursToDry(5f, 0f)));
        }

        [Fact]
        public void PumpTakesATrickleOfTheTap()
        {
            Assert.Equal(0.5f, Reservoir.PumpLitresPerMinute(), 4);
            Assert.True(Reservoir.PumpLitresPerMinute() < Reservoir.TapFullFlow);
            Assert.Equal(0f, Reservoir.PumpLitresPerMinute(-3f));
        }

        [Fact]
        public void PumpFillsTheEmptiestFirst()
        {
            var levels = new[] { 4f, 1f, 2.5f };
            var caps = new[] { 5f, 5f, 5f };
            var after = Reservoir.Distribute(levels, caps, 3f, out float used);
            Assert.Equal(3f, used, 4);
            Assert.Equal(4f, after[0]);       // 80%: below the start line, but last in the queue
            Assert.Equal(4f, after[1], 4);
            Assert.Equal(2.5f, after[2], 4);
        }

        [Fact]
        public void PumpNeverOverfillsAndSkipsFullReservoirs()
        {
            var after = Reservoir.Distribute(new[] { 4.6f, 4.9f, 0f }, new[] { 5f, 5f, 5f }, 100f, out float used);
            Assert.Equal(4.6f, after[0]);      // above 90%: left alone
            Assert.Equal(4.9f, after[1]);
            Assert.Equal(5f, after[2]);
            Assert.Equal(5f, used, 4);
        }

        [Fact]
        public void PumpWithNoWaterChangesNothing()
        {
            var after = Reservoir.Distribute(new[] { 1f }, new[] { 5f }, 0f, out float used);
            Assert.Equal(1f, after[0]);
            Assert.Equal(0f, used);
        }

        [Fact]
        public void SyncOnlyOnRealChanges()
        {
            Assert.False(Reservoir.ShouldSync(2f, 2.2f, 5f));
            Assert.True(Reservoir.ShouldSync(2f, 2.5f, 5f));
            Assert.True(Reservoir.ShouldSync(4.8f, 5f, 5f));  // reaching full always syncs
            Assert.False(Reservoir.ShouldSync(5f, 5f, 5f));
        }

        [Fact]
        public void SharedReservoirRaisesButNeverLowers()
        {
            Assert.Equal(4.75f, Reservoir.SharedLevel(1f, 5f, 0.95f), 4);
            Assert.Equal(5f, Reservoir.SharedLevel(5f, 5f, 0.9f));
        }
    }

    public sealed class HydroUnlockTests
    {
        [Fact]
        public void StaggeredAtTierThree()
        {
            Assert.Equal((8, 3), (Unlocks.Hydro.Rank, Unlocks.Hydro.Tier));
            Assert.Equal((9, 3), (Unlocks.Aero.Rank, Unlocks.Aero.Tier));
            Assert.Equal((10, 3), (Unlocks.Juicer.Rank, Unlocks.Juicer.Tier));
        }

        [Fact]
        public void ItemsCarryTheirUnlocksRank()
        {
            foreach (var u in Unlocks.All)
                foreach (var id in u.ItemIds)
                {
                    var item = Units.ById(id);
                    Assert.NotNull(item);
                    Assert.Equal((u.Rank, u.Tier), (item.Rank, item.RankTier));
                }
        }

        [Fact]
        public void EveryItemIsUnlockedSomewhere()
        {
            var unlocked = Unlocks.All.SelectMany(u => u.ItemIds).ToHashSet();
            foreach (var item in Units.All) Assert.Contains(item.Id, unlocked);
        }

        [Fact]
        public void ReachedFollowsTheRankLadder()
        {
            Assert.False(Unlocks.Reached(Unlocks.Hydro, 8, 2));
            Assert.True(Unlocks.Reached(Unlocks.Hydro, 8, 3));
            Assert.True(Unlocks.Reached(Unlocks.Hydro, 9, 1));
            Assert.False(Unlocks.Reached(Unlocks.Aero, 9, 2));
            Assert.True(Unlocks.Reached(Unlocks.Juicer, 10, 7));
            Assert.Equal(2, Unlocks.ReachedBy(9, 3).Count());
        }

        [Fact]
        public void CoursesUnlockWithTheirUnits()
        {
            Assert.Same(Unlocks.Hydro, Unlocks.ForCourse(TrainingLevel.Hydroponics));
            Assert.Same(Unlocks.Aero, Unlocks.ForCourse(TrainingLevel.Aeroponics));
            Assert.Null(Unlocks.ForCourse(TrainingLevel.None));
        }
    }

    public sealed class HydroYieldTests
    {
        [Fact]
        public void WeedIsCappedAtSixteenInAVanillaPot()
        {
            Assert.Equal(12, Yield.Buds(Yield.BaseQuantity, 1f, Yield.WeedSites));
            Assert.Equal(16, Yield.Buds(Yield.BaseQuantity, Yield.Pgr, Yield.WeedSites));
        }

        [Fact]
        public void GrowTentPenaltyIsGoneInATray()
        {
            Assert.Equal(8, Yield.Buds(Yield.BaseQuantity, 0.667f, Yield.WeedSites));
            Assert.Equal(12, Yield.Buds(Yield.BaseQuantity, Units.HydroKind.YieldMultiplier, Yield.WeedSites));
        }

        [Fact]
        public void AeroPassesTheCap()
        {
            int sites = Yield.WeedSites + Units.AeroKind.ExtraBudSites;
            Assert.Equal(18, Yield.Buds(Yield.BaseQuantity, Units.AeroKind.YieldMultiplier, sites));
            Assert.Equal(24, Yield.Buds(Yield.BaseQuantity, Yield.Multiplier(Units.AeroKind.YieldMultiplier, Yield.Pgr), sites));
            Assert.Equal(18, Yield.Buds(Yield.BaseQuantity, Units.AeroKind.YieldMultiplier, Yield.CocaSites + Units.AeroKind.ExtraBudSites));
        }

        [Fact]
        public void RoundingIsTheGamesHalfToEven()
        {
            Assert.Equal(2, Yield.Buds(1, 2.5f, 16));
            Assert.Equal(4, Yield.Buds(1, 3.5f, 16));
        }

        [Fact]
        public void AtLeastOneBud()
        {
            Assert.Equal(1, Yield.Buds(12, 0f, 16));
            Assert.Equal(1, Yield.Buds(12, 1f, 0));
        }

        [Fact]
        public void AdditivesMultiplyAndZeroMeansNoChange()
        {
            Assert.Equal(2.25f, Yield.Multiplier(1.5f, 1.5f), 4);
            Assert.Equal(1.5f, Yield.Multiplier(1.5f, 0f), 4);
            Assert.Equal(1.5f, Yield.Multiplier(1.5f, 1f), 4);   // Grow N Juicer leaves yield alone
        }

        [Fact]
        public void ExtraSitesAreDeterministicAndSpread()
        {
            var a = Enumerable.Range(0, 8).Select(Yield.ExtraSitePlacement).ToArray();
            var b = Enumerable.Range(0, 8).Select(Yield.ExtraSitePlacement).ToArray();
            Assert.Equal(a, b);
            Assert.Equal(8, a.Select(p => Math.Round(p.AngleDegrees, 1)).Distinct().Count());
            Assert.All(a, p => Assert.InRange(p.RadiusScale, 0.7f, 1f));
            Assert.Equal(new[] { 0, 1, 2, 0 }, Enumerable.Range(0, 4).Select(k => Yield.ExtraSiteSource(k, 3)));
            Assert.Equal(-1, Yield.ExtraSiteSource(0, 0));
        }
    }

    public sealed class HydroLayoutTests
    {
        [Fact]
        public void FramesHaveAHolePerSite()
        {
            Assert.Equal(Units.HydroTray.Sites, Layout.TrayHoles.Length);
            Assert.Equal(Units.AeroTower.Sites, Layout.TowerPorts.Length);
            Assert.Same(Layout.TowerPorts, Layout.HolesFor(HoleKind.Aero));
            Assert.Same(Layout.TrayHoles, Layout.HolesFor(HoleKind.Hydro));
        }

        [Fact]
        public void FrameIsHoleZeroAndSiblingsAreOrderedTheSameEverywhere()
        {
            var one = Layout.Order("m", new[] { "c", "a", "b" });
            var two = Layout.Order("m", new[] { "b", "c", "a", "m" });
            Assert.Equal(new[] { "m", "a", "b", "c" }, one);
            Assert.Equal(one, two);
            Assert.Equal(0, Layout.IndexOf("m", new[] { "b", "a" }, "m"));
            Assert.Equal(2, Layout.IndexOf("m", new[] { "b", "a" }, "b"));
            Assert.Equal(-1, Layout.IndexOf("m", new[] { "b" }, "z"));
        }

        [Fact]
        public void SiblingsToPlace()
        {
            Assert.Equal(4, Layout.SiblingsToPlace(Units.HydroTray, 0));
            Assert.Equal(11, Layout.SiblingsToPlace(Units.AeroTower, 0));
            Assert.Equal(0, Layout.SiblingsToPlace(Units.AeroTower, 11));
            Assert.Equal(0, Layout.SiblingsToPlace(Units.HydroSection, 0));
        }

        [Fact]
        public void FramesAreOnlySoldWhenGrouped()
        {
            Assert.DoesNotContain(Layout.ForSale(Placement.Individual), u => u.IsFrame);
            Assert.Contains(Layout.ForSale(Placement.Grouped), u => u == Units.HydroTray);
            Assert.Contains(Layout.ForSale(Placement.Individual), u => u == Units.HydroSection);
            Assert.Contains(Layout.ForSale(Placement.Individual), u => u == Units.Juicer);
        }
    }

    public sealed class HydroUnitTests
    {
        [Fact]
        public void IdsArePrefixedAndUnique()
        {
            Assert.All(Units.All, u => Assert.StartsWith("melange_", u.Id));
            Assert.Equal(Units.All.Count, Units.All.Select(u => u.Id).Distinct().Count());
        }

        [Fact]
        public void KindsByItemId()
        {
            Assert.Equal(HoleKind.Hydro, Units.KindOf(Units.HydroSection.Id));
            Assert.Equal(HoleKind.Hydro, Units.KindOf(Units.HydroTray.Id));
            Assert.Equal(HoleKind.Aero, Units.KindOf(Units.AeroTower.Id));
            Assert.Equal(HoleKind.None, Units.KindOf(Units.Pump.Id));
            Assert.Equal(HoleKind.None, Units.KindOf("growtent"));
            Assert.Equal(HoleKind.None, Units.KindOf(null));
        }

        [Fact]
        public void AeroIsFasterThanHydroWhichIsFasterThanATent()
        {
            Assert.True(Units.HydroKind.SpeedFactor > 1f);
            Assert.True(Units.AeroKind.SpeedFactor > Units.HydroKind.SpeedFactor);
        }

        [Fact]
        public void OnlyAeroCures()
        {
            Assert.True(Units.AeroKind.Cures);
            Assert.False(Units.HydroKind.Cures);
        }

        [Fact]
        public void SectionsForFrames()
        {
            Assert.Same(Units.HydroSection, Units.SectionFor(Units.HydroTray.Kind));
            Assert.Same(Units.AeroSection, Units.SectionFor(Units.AeroTower.Kind));
            Assert.Null(Units.SectionFor(HoleKind.None));
        }

        [Fact]
        public void AFrameCostsLessThanItsSectionsBoughtSingly()
        {
            Assert.True(Units.HydroTray.Price < Units.HydroSection.Price * Units.HydroTray.Sites);
            Assert.True(Units.AeroTower.Price < Units.AeroSection.Price * Units.AeroTower.Sites);
        }
    }
}
