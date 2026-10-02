using System.Collections.Generic;
using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace Melange.Psychedelics
{
    /// <summary>
    /// What the psychedelics spoke remembers for one saved game. An S1API saveable, so the game's own save system writes it
    /// into the slot; its type name is prefixed so it can't collide with another mod's (S1API names saves by short type name).
    /// Only the host saves, so only the host has it; that is why the terrariums, the dosing and the brands run on the host.
    /// </summary>
    /// <remarks>
    /// The game saves the placed terrariums and frames (they are clones of a storage rack) and what's in their slots; this
    /// holds what the game can't: the toads themselves, the designs and their reputations, and the batches.
    /// </remarks>
    public sealed class MelangePsychedelicsData : Saveable
    {
        public static MelangePsychedelicsData Current { get; private set; }

        public const int CurrentVersion = 1;
        [SaveableField("version")] public int Version = CurrentVersion;
        /// <summary>Terrarium GUID -> its toads.</summary>
        [SaveableField("terrariums")] public Dictionary<string, Terrarium> Terrariums = new Dictionary<string, Terrarium>();
        /// <summary>Blotter frame GUID -> the design it prints.</summary>
        [SaveableField("frames")] public Dictionary<string, string> FrameDesigns = new Dictionary<string, string>();
        [SaveableField("designs")] public DesignBook Designs = new DesignBook();
        [SaveableField("batches")] public BatchLedger Batches = new BatchLedger();
        // the sewer story, from the sewer spoke's events (re-published after every load, so these follow it)
        [SaveableField("kingSpared")] public bool KingSpared;
        [SaveableField("kingDefeated")] public bool KingDefeated;
        [SaveableField("goblinCalmed")] public bool GoblinCalmed;
        /// <summary>The pond window (by its day) whose caught spots are listed, so a reload can't refill a night's pond.</summary>
        [SaveableField("pondDay")] public int PondDay = -1;
        [SaveableField("pondCaught")] public List<int> PondCaught = new List<int>();
        [SaveableField("pondOffences")] public int PondOffences;
        [SaveableField("sewerDay")] public int SewerDay = -1;
        [SaveableField("sewerCaught")] public int SewerCaught;

        public MelangePsychedelicsData() { Current = this; }

        /// <summary>
        /// Back to a fresh game's values. S1API keeps one instance for the whole session and, loading a save, only sets the
        /// fields that save has files for: without this, a save with no psychedelics data loaded after one with it would
        /// inherit its toads and designs. Called on returning to the menu, before the next save loads.
        /// </summary>
        public void ResetToDefaults()
        {
            Version = CurrentVersion;
            Terrariums = new Dictionary<string, Terrarium>();
            FrameDesigns = new Dictionary<string, string>();
            Designs = new DesignBook();
            Batches = new BatchLedger();
            KingSpared = KingDefeated = GoblinCalmed = false;
            PondDay = -1;
            PondCaught = new List<int>();
            PondOffences = 0;
            SewerDay = -1;
            SewerCaught = 0;
        }

        /// <summary>Fills in anything an older or partial save left null.</summary>
        public void Normalise()
        {
            Terrariums ??= new Dictionary<string, Terrarium>();
            FrameDesigns ??= new Dictionary<string, string>();
            Designs ??= new DesignBook();
            Designs.Designs ??= new List<Design>();
            Designs.EnsurePresets();
            Batches ??= new BatchLedger();
            Batches.Batches ??= new List<Batch>();
            PondCaught ??= new List<int>();
            foreach (var t in Terrariums.Values) if (t != null) t.Toads ??= new List<Toad>();
        }

        public Terrarium TerrariumFor(string guid)
        {
            if (!Terrariums.TryGetValue(guid, out var t) || t == null) Terrariums[guid] = t = new Terrarium();
            t.Toads ??= new List<Toad>();
            return t;
        }

        public SewerRoute Route(bool sewerUnlocked) => SewerToads.Route(KingSpared, KingDefeated, GoblinCalmed, sewerUnlocked);
    }
}
