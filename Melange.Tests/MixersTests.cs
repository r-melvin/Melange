using System.Collections.Generic;
using System.Linq;
using Melange.Mixers;
using Xunit;

namespace Melange.Tests
{
    public sealed class MixersTests
    {
        // ---- tiers

        [Fact]
        public void ThreeMachinesTakingTwoThreeAndFourMixers()
        {
            Assert.Equal(new[] { 2, 3, 4 }, Tiers.All.Select(t => t.Mixers));
            Assert.Equal(new[] { 1, 2, 3 }, Tiers.All.Select(t => t.ExtraSlots));
        }

        [Fact]
        public void UnlockedAtUnderlordBaronKingpinTierOne()
        {
            Assert.Equal(new[] { (8, 1), (9, 1), (10, 1) }, Tiers.All.Select(t => (t.Rank, t.RankTier)));
        }

        [Fact]
        public void TimeMultipliersAsDecided()
        {
            Assert.Equal(new[] { 1.0f, 1.25f, 1.5f }, Tiers.All.Select(t => t.DefaultTimeMultiplier));
        }

        [Fact]
        public void EveryMachineBeatsChainingTheSameMixes()
        {
            foreach (var t in Tiers.All)
                Assert.True(Timing.SpeedUpOverChaining(t.Mixers, t.DefaultTimeMultiplier) > 1.9f, t.Name);
            Assert.Equal(2.7f, Timing.SpeedUpOverChaining(4, 1.5f), 1);
        }

        [Fact]
        public void IdsArePrefixedAndOnlyMachinesAreFound()
        {
            foreach (var t in Tiers.All)
            {
                Assert.StartsWith("melange_", t.Id);
                Assert.Same(t, Tiers.ById(t.Id));
            }
            Assert.Null(Tiers.ById(Tiers.Mk2Id));
            Assert.Null(Tiers.ById("mixingstation"));
            Assert.Null(Tiers.ById(null));
            Assert.Null(Tiers.ById(""));
        }

        // ---- timing

        [Theory]
        [InlineData(15, 1.0f, 15)]
        [InlineData(15, 1.25f, 19)]      // 18.75 rounds up: whole minutes only
        [InlineData(15, 1.5f, 23)]       // 22.5 rounds away from zero
        [InlineData(1, 0.1f, 1)]         // never below a minute
        [InlineData(0, 1.0f, 1)]
        public void MinutesPerItem(int mk2, float multiplier, int expected)
            => Assert.Equal(expected, Timing.MixTimePerItem(mk2, multiplier));

        [Theory]
        [InlineData(float.NaN, 1.25f)]
        [InlineData(float.PositiveInfinity, 1.25f)]
        [InlineData(-1f, 1.25f)]
        [InlineData(0f, 1.25f)]
        [InlineData(0.01f, 0.1f)]
        [InlineData(50f, 10f)]
        [InlineData(2f, 2f)]
        public void SettingsAreSanitised(float value, float expected)
            => Assert.Equal(expected, Timing.Sanitise(value, 1.25f));

        // ---- the fold: in series, in slot order

        // a stand-in for the game's single mix: records each step, so order is visible
        private static List<string> Mix(List<string> effects, string mixer) => effects.Concat(new[] { mixer }).ToList();

        [Fact]
        public void FoldMixesOncePerMixerInSlotOrder()
        {
            var result = MixChain.Fold(new List<string> { "P" }, new[] { "A", "B", "C" }, Mix);
            Assert.Equal(new[] { "P", "A", "B", "C" }, result);
        }

        [Fact]
        public void FoldEqualsChainedSingleMixes()
        {
            // what three Mk2 passes give, one after another
            var chained = Mix(Mix(Mix(new List<string> { "P" }, "A"), "B"), "C");
            Assert.Equal(chained, MixChain.Fold(new List<string> { "P" }, new[] { "A", "B", "C" }, Mix));
        }

        [Fact]
        public void OrderMattersWhenTheStepDoes()
        {
            // a rule like the game's: a mixer transforms what is already there
            string Step(string s, string m) => m == "Double" ? s + s : s + m;
            Assert.NotEqual(MixChain.Fold("x", new[] { "a", "Double" }, Step), MixChain.Fold("x", new[] { "Double", "a" }, Step));
        }

        [Fact]
        public void NoMixersLeavesTheProductAsItIs()
            => Assert.Equal(new[] { "P" }, MixChain.Fold(new List<string> { "P" }, new string[0], Mix));

        // ---- which ingredients a machine accepts

        private static bool Valid(string id) => id != "cash";

        [Fact]
        public void ReadyOnlyWithEveryMixerSlotFilled()
        {
            Assert.True(MixChain.Check("ogkush", new[] { "banana", "cuke" }, 2, Valid).Ready);
            var missing = MixChain.Check("ogkush", new[] { "banana", null }, 2, Valid);
            Assert.Equal(ChainState.MissingMixer, missing.State);
            Assert.Equal(2, missing.Slot);
            Assert.Equal(ChainState.MissingMixer, MixChain.Check("ogkush", new[] { "banana", "cuke" }, 3, Valid).State);
        }

        [Fact]
        public void TheFirstFaultySlotIsReported()
        {
            var c = MixChain.Check("ogkush", new[] { "banana", "cash", null }, 3, Valid);
            Assert.Equal(ChainState.InvalidMixer, c.State);
            Assert.Equal(2, c.Slot);
        }

        [Fact]
        public void ModdedProductsAndMixersAreRefusedInV1()
        {
            Assert.Equal(ChainState.ProductNotVanilla, MixChain.Check("ifbars.moredrugs:products/mdma", new[] { "banana", "cuke" }, 2, Valid).State);
            var c = MixChain.Check("ogkush", new[] { "banana", "somemod:mixers/glitter" }, 2, Valid);
            Assert.Equal(ChainState.MixerNotVanilla, c.State);
            Assert.Equal(2, c.Slot);
        }

        [Fact]
        public void NoProductNoStart()
            => Assert.Equal(ChainState.NoProduct, MixChain.Check(null, new[] { "banana", "cuke" }, 2, Valid).State);

        [Fact]
        public void ExtraMixerSlotsAreOnlyThoseBeyondTheGamesOwn()
        {
            foreach (var t in Tiers.All) Assert.Equal(t.Mixers - 1, t.ExtraSlots);
        }

        [Fact]
        public void EveryFaultHasAnExplanation()
        {
            foreach (ChainState s in System.Enum.GetValues(typeof(ChainState)))
                if (s != ChainState.Ready) Assert.False(string.IsNullOrEmpty(MixChain.Explain(new ChainCheck(s, 2), 3)), s.ToString());
            Assert.Equal(string.Empty, MixChain.Explain(new ChainCheck(ChainState.Ready), 3));
        }

        // ---- batch size

        [Fact]
        public void BatchCapIsTheSmallestExtraStack()
        {
            Assert.Equal(7, MixChain.BatchCap(20, new[] { 12, 7, 30 }, true));
            Assert.Equal(20, MixChain.BatchCap(20, new[] { 25, 40 }, true));
        }

        [Fact]
        public void BatchCapIsZeroWhenNotReady()
        {
            Assert.Equal(0, MixChain.BatchCap(20, new[] { 12, 7 }, false));
            Assert.Equal(0, MixChain.BatchCap(20, new[] { 12, 0 }, true));
        }

        [Fact]
        public void StartQuantityShrinksToTheExtrasAndRefusesAtZero()
        {
            Assert.Equal(20, MixChain.StartQuantity(20, new[] { 20, 25 }));
            Assert.Equal(5, MixChain.StartQuantity(20, new[] { 5, 25 }));
            Assert.Equal(0, MixChain.StartQuantity(20, new[] { 0, 25 }));
            Assert.Equal(0, MixChain.StartQuantity(-3, new[] { 10 }));
        }

        // ---- save rules

        [Fact]
        public void OnlyPlainSingleMixesGoIntoTheGamesRecipes()
        {
            Assert.True(SaveRules.MayRecordGameRecipe(1));
            foreach (var t in Tiers.All) Assert.False(SaveRules.MayRecordGameRecipe(t.Mixers));
            Assert.False(SaveRules.MayRecordGameRecipe(0));
        }

        [Fact]
        public void TheGamesOperationNamesOnlyTheFirstMixer()
        {
            Assert.Equal("banana", SaveRules.OperationIngredientId(new[] { "banana", "cuke", "paracetamol" }));
            Assert.Throws<System.ArgumentException>(() => SaveRules.OperationIngredientId(new string[0]));
        }

        [Theory]
        [InlineData("ogkush", true)]
        [InlineData("banana", true)]
        [InlineData("ifbars.moredrugs:products/mdma", false)]
        [InlineData("mod/thing", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void VanillaIds(string id, bool vanilla) => Assert.Equal(vanilla, SaveRules.IsVanillaId(id));

        [Fact]
        public void ChainFitsOnlyAtTheMachinesLength()
        {
            Assert.True(SaveRules.ChainFits(new[] { "a", "b", "c" }, 3));
            Assert.False(SaveRules.ChainFits(new[] { "a", "b" }, 3));
            Assert.False(SaveRules.ChainFits(new[] { "a", null, "c" }, 3));
            Assert.False(SaveRules.ChainFits(null, 2));
        }

        [Fact]
        public void PruneDropsRecordsOfMachinesThatAreGone()
        {
            var records = new Dictionary<string, StationRecord> { ["a"] = new StationRecord(), ["b"] = new StationRecord(), ["c"] = null };
            var kept = SaveRules.Prune(records, new HashSet<string> { "a", "c" });
            Assert.Equal(new[] { "a" }, kept.Keys);
            Assert.Empty(SaveRules.Prune(null, new HashSet<string> { "a" }));
        }

        [Theory]
        [InlineData("Lemon Haze", "lemonhaze")]
        [InlineData("Dr. Feel-Good!", "drfeel-good")]
        [InlineData("It's (very) \"nice\": yes; ok, sure?", "itsveryniceyesoksure")]
        public void IdsAreMadeAsTheGameMakesThem(string name, string id) => Assert.Equal(id, SaveRules.MixId(name));

        [Fact]
        public void IdsAreMadeUnique()
        {
            var taken = new HashSet<string> { "lemonhaze", "lemonhaze2" };
            Assert.Equal("lemonhaze3", SaveRules.UniqueId("lemonhaze", taken.Contains));
            Assert.Equal("blue", SaveRules.UniqueId("blue", taken.Contains));
        }

        // ---- prices

        [Fact]
        public void PricesRiseWithTheTierAndRoundToHundreds()
        {
            var prices = Tiers.All.Select(t => Pricing.Price(2000f, t)).ToArray();
            Assert.Equal(new[] { 6000f, 12000f, 20000f }, prices);
            Assert.Equal(Pricing.Price(Pricing.AssumedMk2Price, Tiers.Two), Pricing.Price(0f, Tiers.Two));
            Assert.Equal(0f, Pricing.Price(1234f, Tiers.Two) % 100f);
        }
    }
}
