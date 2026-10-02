using System;
using System.Collections.Generic;

namespace Melange.Psychedelics
{
    /// <summary>
    /// The bad-batch roll made when a sheet is dosed: lower quality, worse odds. A bad batch looks like any other; it only shows
    /// when customers take it.
    /// </summary>
    public static class BadBatch
    {
        private static readonly double[] ChanceByTier = { 0.45, 0.25, 0.10, 0.04, 0.01 };

        public static double Chance(int tier, double multiplier = 1.0)
            => Math.Max(0.0, Math.Min(1.0, ChanceByTier[Tier.Clamp(tier)] * Math.Max(0.0, multiplier)));

        /// <summary>Bad when the roll (0..1) falls under the chance.</summary>
        public static bool IsBad(int tier, double roll, double multiplier = 1.0) => roll < Chance(tier, multiplier);
    }

    /// <summary>How customers know a design.</summary>
    public enum Standing
    {
        /// <summary>Known for bad trips: customers are wary of it.</summary>
        Burnt = -1,
        Unknown = 0,
        Known = 1,
        /// <summary>Customers ask for it by name: loyalty.</summary>
        Loved = 2,
    }

    /// <summary>A blotter design: the art on a sheet, and the brand it becomes.</summary>
    public sealed class Design
    {
        public string Id;
        public string Name;
        /// <summary>True for the built-in designs; painted ones carry a drawing (see <see cref="Drawing"/>).</summary>
        public bool Preset;
        /// <summary>A painted design's strokes, in whatever text form the game layer stores them (empty for presets).</summary>
        public string Drawing = "";
        public bool Retired;
        public float Reputation;
        public int GoodTrips, BadTrips, SheetsPrinted;
    }

    /// <summary>
    /// The designs in a save and their reputations. Customers who liked a batch come back for "the one with the ..." (a loyalty
    /// bonus for a design that sold well at good quality); a design known for bad batches puts them off.
    /// </summary>
    public sealed class DesignBook
    {
        public const float MinRep = -50f, MaxRep = 50f, BadTripCost = 8f, BurntBelow = -15f, KnownFrom = 5f, LovedFrom = 20f;

        /// <summary>The built-in designs (id, name). IDs are save data.</summary>
        public static readonly (string Id, string Name)[] Presets =
        {
            ("preset:plain", "Plain"),
            ("preset:sunburst", "Sunburst"),
            ("preset:third-eye", "Third Eye"),
            ("preset:checkers", "Checkers"),
            ("preset:toadstool", "Toadstool"),
        };

        public List<Design> Designs = new List<Design>();
        public int NextPainted = 1;

        /// <summary>Adds any missing built-in design (a new version may add more); never removes one.</summary>
        public void EnsurePresets()
        {
            foreach (var (id, name) in Presets)
                if (Find(id) == null) Designs.Add(new Design { Id = id, Name = name, Preset = true });
        }

        public Design Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in Designs) if (d.Id == id) return d;
            return null;
        }

        /// <summary>A newly painted design, kept for reuse on any number of sheets.</summary>
        public Design AddPainted(string name, string drawing)
        {
            var d = new Design { Id = "painted:" + NextPainted++, Name = string.IsNullOrWhiteSpace(name) ? "Design " + (NextPainted - 1) : name, Drawing = drawing ?? "" };
            Designs.Add(d);
            return d;
        }

        /// <summary>The design after this one in the book, skipping retired ones; the first when none is current.</summary>
        public Design Next(string currentId)
        {
            var live = Designs.FindAll(d => !d.Retired);
            if (live.Count == 0) return null;
            int i = live.FindIndex(d => d.Id == currentId);
            return live[(i + 1) % live.Count];
        }

        public static Standing StandingOf(float reputation)
        {
            if (reputation <= BurntBelow) return Standing.Burnt;
            if (reputation >= LovedFrom) return Standing.Loved;
            if (reputation >= KnownFrom) return Standing.Known;
            return Standing.Unknown;
        }

        /// <summary>A customer took a tab of this design: a good trip earns more the better the batch; a bad one costs a lot.</summary>
        public static void RecordTrip(Design d, bool good, int tier)
        {
            if (d == null) return;
            if (good) { d.GoodTrips++; d.Reputation += 1f + Math.Max(0, Tier.Clamp(tier) - Tier.Standard); }
            else { d.BadTrips++; d.Reputation -= BadTripCost; }
            d.Reputation = Math.Max(MinRep, Math.Min(MaxRep, d.Reputation));
        }

        /// <summary>
        /// How much a trip moves the customer's relationship with the player (the game's relationship delta): loyalty for a
        /// good trip on a known or loved design, a sour note for a burnt one even when the trip was fine, and a real knock for a
        /// bad trip.
        /// </summary>
        public static float RelationshipChange(Standing standing, bool good)
        {
            if (!good) return -0.3f;
            switch (standing)
            {
                case Standing.Loved: return 0.15f;
                case Standing.Known: return 0.05f;
                case Standing.Burnt: return -0.05f;
                default: return 0f;
            }
        }
    }

    /// <summary>One dosing of sheets: its design, quality, whether it went bad, and the tabs not yet accounted for.</summary>
    public sealed class Batch
    {
        public int Id;
        public string DesignId;
        public int Tier;
        public bool Bad;
        public int Tabs;
    }

    /// <summary>
    /// Which batch a customer's tab came from. The game's product items carry no per-item data, so a sheet can't say which
    /// batch it is; instead, tabs are matched first in, first out against the batches of the same quality. Exact while the
    /// player sells batches in the order they were made; otherwise the blame or credit can land on a neighbouring batch of the
    /// same quality. (Per-item data would make it exact: see TESTING.md.)
    /// </summary>
    public sealed class BatchLedger
    {
        public const int MaxBatches = 200;

        public List<Batch> Batches = new List<Batch>();
        public int NextId = 1;

        public Batch Add(string designId, int tier, bool bad, int tabs)
        {
            var b = new Batch { Id = NextId++, DesignId = designId, Tier = Tier.Clamp(tier), Bad = bad, Tabs = Math.Max(0, tabs) };
            Batches.Add(b);
            Prune();
            return b;
        }

        /// <summary>
        /// The batch a consumed tab of this quality is put down to, and one tab off it: the oldest with tabs left; when every
        /// batch of that quality is accounted for, the newest of them (tabs can outnumber the ledger after a mix or a reload);
        /// null when no batch of that quality was ever made.
        /// </summary>
        public Batch Attribute(int tier)
        {
            Batch newest = null;
            foreach (var b in Batches)
            {
                if (b.Tier != tier) continue;
                if (b.Tabs > 0) { b.Tabs--; return b; }
                newest = b;
            }
            return newest;
        }

        /// <summary>Keeps the ledger bounded: the oldest used-up batches go first, then the oldest of all.</summary>
        private void Prune()
        {
            while (Batches.Count > MaxBatches)
            {
                int i = Batches.FindIndex(b => b.Tabs == 0);
                Batches.RemoveAt(i >= 0 ? i : 0);
            }
        }
    }
}
