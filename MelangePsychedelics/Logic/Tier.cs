using System;

namespace Melange.Psychedelics
{
    /// <summary>
    /// The game's quality ladder as numbers, in the order of its EQuality enum (Trash 0 .. Heavenly 4), so a tier converts to
    /// the game's value with a plain cast.
    /// </summary>
    public static class Tier
    {
        public const int Trash = 0, Poor = 1, Standard = 2, Premium = 3, Heavenly = 4;
        private static readonly string[] Names = { "Trash", "Poor", "Standard", "Premium", "Heavenly" };

        public static int Clamp(int tier) => Math.Max(Trash, Math.Min(Heavenly, tier));
        public static string Name(int tier) => Names[Clamp(tier)];
    }

    /// <summary>
    /// Deterministic dice: the same seed, day and salt always give the same number on every machine, so a day's pond, Randy's
    /// stock and the like come out the same on the host and on clients without being sent over the network. SplitMix64, not
    /// System.Random, whose sequence .NET does not promise to keep across versions.
    /// </summary>
    public static class Dice
    {
        public static ulong Hash(long seed, long day, long salt)
        {
            ulong z = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL ^ (ulong)day * 0xBF58476D1CE4E5B9UL ^ (ulong)salt * 0x94D049BB133111EBUL);
            z = unchecked(z + 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        /// <summary>A number in [0, 1).</summary>
        public static double Unit(long seed, long day, long salt) => (Hash(seed, day, salt) >> 11) * (1.0 / (1UL << 53));

        /// <summary>A whole number from min to max, both included.</summary>
        public static int Range(long seed, long day, long salt, int min, int max)
        {
            if (max <= min) return min;
            return min + (int)(Unit(seed, day, salt) * (max - min + 1));
        }
    }
}
