using System.Collections.Generic;

namespace Melange.Mixers
{
    /// <summary>One of the three machines: how many mixers it takes, how slow it is, when it unlocks and what it costs.</summary>
    public sealed class MixerTier
    {
        /// <summary>The item ID, prefixed so it can't collide with the game's or another mod's.</summary>
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        /// <summary>Mixer ingredients per mix, applied one after another in slot order.</summary>
        public int Mixers { get; }
        /// <summary>Time per mix as a multiple of the Mixing Station Mk2's (the settings can change it).</summary>
        public float DefaultTimeMultiplier { get; }
        /// <summary>The rank (as the game numbers them: 8 Underlord, 9 Baron, 10 Kingpin) and tier that unlock it.</summary>
        public int Rank { get; }
        public int RankTier { get; }
        /// <summary>Shop price as a multiple of the Mk2's own price.</summary>
        public float PriceMultiplier { get; }
        /// <summary>The colour the stand-in look is tinted with until the machine has its own model.</summary>
        public float[] Tint { get; }
        /// <summary>Extra slots the station needs beyond the game's own mixer slot.</summary>
        public int ExtraSlots => Mixers - 1;

        internal MixerTier(string id, string name, string description, int mixers, float time, int rank, int rankTier, float price, float[] tint)
        {
            Id = id; Name = name; Description = description; Mixers = mixers; DefaultTimeMultiplier = time;
            Rank = rank; RankTier = rankTier; PriceMultiplier = price; Tint = tint;
        }
    }

    /// <summary>
    /// The machines, decided with the user: two, three and four mixers, at the Mk2's time, 25% slower and 50% slower, so each
    /// stays faster than chaining the same mixes through one Mk2 (2x, 2.4x, 2.7x). They fill the late ranks, where the game
    /// unlocks nothing. Prices are a proposal: 3x, 6x and 10x the Mk2, since by Underlord a day's sales dwarf the Mk2's price.
    /// </summary>
    public static class Tiers
    {
        public const int Underlord = 8, Baron = 9, Kingpin = 10;
        /// <summary>The game's own Mixing Station Mk2, which every machine is cloned from.</summary>
        public const string Mk2Id = "mixingstationmk2";

        public static readonly MixerTier Two = new MixerTier("melange_mixer_2", "Two-Ingredient Mixer",
            "Mixes a product with two ingredients in one go, in slot order: the same result as two passes through a Mixing Station Mk2.",
            2, 1.0f, Underlord, 1, 3f, new[] { 0.35f, 0.75f, 0.70f });
        public static readonly MixerTier Three = new MixerTier("melange_mixer_3", "Three-Ingredient Mixer",
            "Mixes a product with three ingredients in one go, in slot order: the same result as three passes through a Mixing Station Mk2.",
            3, 1.25f, Baron, 1, 6f, new[] { 0.62f, 0.42f, 0.80f });
        public static readonly MixerTier Four = new MixerTier("melange_mixer_4", "Four-Ingredient Mixer",
            "Mixes a product with four ingredients in one go, in slot order: the same result as four passes through a Mixing Station Mk2.",
            4, 1.5f, Kingpin, 1, 10f, new[] { 0.85f, 0.68f, 0.30f });

        public static readonly IReadOnlyList<MixerTier> All = new[] { Two, Three, Four };

        /// <summary>The machine with this item ID, or null for anything else (the vanilla stations included).</summary>
        public static MixerTier ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }
    }
}
