using System.Collections.Generic;
using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace Melange.Hydro
{
    /// <summary>
    /// What the hydro spoke remembers for one saved game. An S1API saveable, so the game's own save system writes it into the
    /// slot; its type name is prefixed so it can't collide with another mod's save data (S1API names saves by short type
    /// name). Only the host saves, so only the host has it: co-op clients learn what they need from the game itself (item
    /// IDs, GUIDs and the shared clock).
    /// </summary>
    /// <remarks>
    /// Holes are saved by the game under the Grow Tent's item ID (so without the mod they load as Grow Tents with their
    /// plants). This record is what turns them back into holes: GUID -> the item they really are.
    /// </remarks>
    public sealed class MelangeHydroData : Saveable
    {
        public static MelangeHydroData Current { get; private set; }

        public const int CurrentVersion = 1;
        [SaveableField("version")] public int Version = CurrentVersion;
        /// <summary>Hole GUID -> its real item ID (a section or a frame).</summary>
        [SaveableField("holes")] public Dictionary<string, string> Holes = new Dictionary<string, string>();
        /// <summary>Frame GUID -> its other holes' GUIDs (grouped placement only).</summary>
        [SaveableField("frames")] public Dictionary<string, List<string>> Frames = new Dictionary<string, List<string>>();
        /// <summary>Aero hole GUID -> the game's total minutes when its plant was first seen fully grown (for curing).</summary>
        [SaveableField("grownAt")] public Dictionary<string, int> GrownAt = new Dictionary<string, int>();
        /// <summary>Botanist GUID -> his training (TrainingLevel as a number).</summary>
        [SaveableField("training")] public Dictionary<string, int> Trained = new Dictionary<string, int>();
        /// <summary>Pump GUID -> the position of the tap it is hosed to ("x,y,z"), so it keeps the same tap.</summary>
        [SaveableField("pumps")] public Dictionary<string, string> PumpTaps = new Dictionary<string, string>();
        /// <summary>Holes that have had their grow medium poured in (once per placement; after its uses run out, any soil will do).</summary>
        [SaveableField("filled")] public List<string> Filled = new List<string>();

        public MelangeHydroData() { Current = this; }

        /// <summary>
        /// Loads before the game's buildings, so a hole saved under the Grow Tent's ID can be told apart while it is being
        /// created, before its plant is set up (the aero plant's extra bud sites must exist before its saved buds are restored).
        /// </summary>
        public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.BeforeBaseGame;

        /// <summary>
        /// S1API keeps one instance for the whole session and, loading a save, only sets the fields that save has files for: so
        /// back at the menu everything goes back to its default, or a save without hydro data would inherit the last one's.
        /// </summary>
        public void ResetToDefaults()
        {
            Version = CurrentVersion;
            Holes = new Dictionary<string, string>();
            Frames = new Dictionary<string, List<string>>();
            GrownAt = new Dictionary<string, int>();
            Trained = new Dictionary<string, int>();
            PumpTaps = new Dictionary<string, string>();
            Filled = new List<string>();
        }

        public TrainingLevel TrainingOf(string botanistGuid)
            => botanistGuid != null && Trained.TryGetValue(botanistGuid, out int l) ? (TrainingLevel)l : TrainingLevel.None;

        /// <summary>The frame a hole belongs to (itself for a frame), or null.</summary>
        public string FrameOf(string guid)
        {
            if (guid == null) return null;
            if (Frames.ContainsKey(guid)) return guid;
            foreach (var kv in Frames) if (kv.Value != null && kv.Value.Contains(guid)) return kv.Key;
            return null;
        }
    }
}
