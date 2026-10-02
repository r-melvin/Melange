using MelonLoader;

namespace Melange.Mixers
{
    /// <summary>
    /// The machines' speed, as multiples of the Mixing Station Mk2's time per mix (decided with the user: a setting to tune if
    /// the advantage breaks the game). Read live, so a change applies to every machine at once, a running mix included. In
    /// co-op every player should use the same values: the host decides when a mix finishes, each player's screen counts down
    /// with its own.
    /// </summary>
    internal static class Settings
    {
        private static MelonPreferences_Entry<float> _two, _three, _four;

        public static void Create()
        {
            var c = MelonPreferences.CreateCategory("MelangeMixers", "Melange Mixers");
            _two = c.CreateEntry("TwoIngredientTime", Tiers.Two.DefaultTimeMultiplier, "Two-ingredient mixer: time per mix",
                "Time per mix as a multiple of the Mixing Station Mk2's. 1 = the same as the Mk2 (two Mk2 mixes take 2).");
            _three = c.CreateEntry("ThreeIngredientTime", Tiers.Three.DefaultTimeMultiplier, "Three-ingredient mixer: time per mix",
                "Time per mix as a multiple of the Mixing Station Mk2's. 1.25 = 25% slower than the Mk2 (three Mk2 mixes take 3).");
            _four = c.CreateEntry("FourIngredientTime", Tiers.Four.DefaultTimeMultiplier, "Four-ingredient mixer: time per mix",
                "Time per mix as a multiple of the Mixing Station Mk2's. 1.5 = 50% slower than the Mk2 (four Mk2 mixes take 4).");
        }

        public static float TimeMultiplier(MixerTier tier)
        {
            var entry = tier == Tiers.Two ? _two : tier == Tiers.Three ? _three : _four;
            return Timing.Sanitise(entry?.Value ?? tier.DefaultTimeMultiplier, tier.DefaultTimeMultiplier);
        }
    }
}
