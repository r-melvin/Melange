using System.Collections.Generic;

namespace Melange.Hydro
{
    /// <summary>One rank milestone: what it unlocks (items and a training course), and the line for the rank-up screen.</summary>
    public sealed class Unlock
    {
        public int Rank { get; }
        public int Tier { get; }
        public string Title { get; }
        public IReadOnlyList<string> ItemIds { get; }
        public TrainingLevel Course { get; }

        internal Unlock(int rank, int tier, string title, TrainingLevel course, params string[] itemIds)
        {
            Rank = rank; Tier = tier; Title = title; Course = course; ItemIds = itemIds;
        }
    }

    /// <summary>
    /// When things unlock, staggered with the mixers (decided with the user): the mixers take tier I of Underlord, Baron and
    /// Kingpin, hydroponics takes tier III. Items are gated by the game's own rank lock on their definitions; the training
    /// courses are gated here.
    /// </summary>
    public static class Unlocks
    {
        public static readonly Unlock Hydro = new Unlock(Units.Underlord, 3, "Hydroponics (trays, pump, botanist training)",
            TrainingLevel.Hydroponics, Units.HydroSection.Id, Units.HydroTray.Id, Units.Pump.Id);
        public static readonly Unlock Aero = new Unlock(Units.Baron, 3, "Aeroponics (towers, botanist training)",
            TrainingLevel.Aeroponics, Units.AeroSection.Id, Units.AeroTower.Id);
        public static readonly Unlock Juicer = new Unlock(Units.Kingpin, 3, "Grow N Juicer (premium nutrient)",
            TrainingLevel.None, Units.Juicer.Id);

        public static readonly IReadOnlyList<Unlock> All = new[] { Hydro, Aero, Juicer };

        /// <summary>Compares two rank/tier pairs as the game orders them.</summary>
        public static int Compare(int rankA, int tierA, int rankB, int tierB)
            => rankA != rankB ? rankA.CompareTo(rankB) : tierA.CompareTo(tierB);

        public static bool Reached(Unlock unlock, int rank, int tier) => Compare(rank, tier, unlock.Rank, unlock.Tier) >= 0;

        /// <summary>The unlock that brings a training course.</summary>
        public static Unlock ForCourse(TrainingLevel course)
            => course == TrainingLevel.Hydroponics ? Hydro : course == TrainingLevel.Aeroponics ? Aero : null;

        /// <summary>Every unlock at or below a rank and tier.</summary>
        public static IEnumerable<Unlock> ReachedBy(int rank, int tier)
        {
            foreach (var u in All) if (Reached(u, rank, tier)) yield return u;
        }
    }
}
