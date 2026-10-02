using System;
using Melange.Core;
using Xunit;

namespace Melange.Tests
{
    public sealed class PriceMathTests
    {
        private static PriceModifier Mod(string id, int order, float f, string onlyItem = null)
            => new PriceModifier { Id = id, Order = order, Multiplier = (shop, item) => onlyItem == null || item == onlyItem ? f : 1f };

        [Fact]
        public void ModifiersMultiply()
            => Assert.Equal(1.5f * 0.9f, PriceMath.Combined(new[] { Mod("cartel.monopoly", 0, 1.5f), Mod("levels.bulk", 10, 0.9f) }, "s", "x"), 4);

        [Fact]
        public void AModifierOnlyTouchesItsListings()
        {
            var mods = new[] { Mod("m", 0, 1.5f, onlyItem: "seed") };
            Assert.Equal(1.5f, PriceMath.Combined(mods, "s", "seed"), 4);
            Assert.Equal(1f, PriceMath.Combined(mods, "s", "jar"), 4);
        }

        [Fact]
        public void AThrowingOrNonsenseModifierCountsAsNoChange()
        {
            var errors = 0;
            var mods = new[]
            {
                new PriceModifier { Id = "bad", Multiplier = (s, i) => throw new InvalidOperationException() },
                new PriceModifier { Id = "nan", Multiplier = (s, i) => float.NaN },
                new PriceModifier { Id = "neg", Multiplier = (s, i) => -2f },
                Mod("ok", 0, 2f),
            };
            Assert.Equal(2f, PriceMath.Combined(mods, "s", "x", _ => errors++), 4);
            Assert.Equal(1, errors);
        }

        [Fact]
        public void DiscountsNeverGoBelowTheFloor()
            => Assert.Equal(PriceMath.Floor, PriceMath.Combined(new[] { Mod("a", 0, 0.1f), Mod("b", 0, 0.1f) }, "s", "x"), 4);

        [Fact]
        public void PricesRoundToTheCent()
            => Assert.Equal(67.5f, PriceMath.Apply(45f, 1.5f));

        [Fact]
        public void OrderIsByOrderThenId()
        {
            var ordered = PriceMath.Ordered(new[] { Mod("b", 5, 1f), Mod("a", 5, 1f), Mod("z", 0, 1f) });
            Assert.Equal(new[] { "z", "a", "b" }, ordered.ConvertAll(m => m.Id));
        }
    }
}
