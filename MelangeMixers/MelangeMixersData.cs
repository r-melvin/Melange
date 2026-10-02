using System.Collections.Generic;
using S1API.Saveables;
using Melange.Core;
using S1API.Internal.Abstraction;

namespace Melange.Mixers
{
    /// <summary>
    /// What the mixer spoke remembers for one saved game: each machine's extra mixer slots and the order of its running mix, by
    /// the machine's GUID. An S1API saveable, so the game's own save system writes it into the slot. Its type name is prefixed
    /// so it can't collide with another mod's save data (S1API names saves by short type name). Nothing here goes into the
    /// game's own data: without the mod the machines are gone anyway (their item ID is unknown), and nothing else changes.
    /// </summary>
    public sealed class MelangeMixersData : Saveable, IResettableSaveData
    {
        public static MelangeMixersData Current { get; private set; }

        public const int CurrentVersion = 1;
        [SaveableField("version")] public int Version = CurrentVersion;
        /// <summary>Machine GUID -> its extra slots and running chain.</summary>
        [SaveableField("stations")] public Dictionary<string, StationRecord> Stations = new Dictionary<string, StationRecord>();

        /// <summary>Back to a new game's values (the hub calls it on returning to the menu, before the next save loads).</summary>
        public void ResetToDefaults()
        {
            Version = CurrentVersion;
            Stations = new Dictionary<string, StationRecord>();
        }

        public MelangeMixersData() { Current = this; SaveData.Track(this); }

        public StationRecord For(string guid)
        {
            if (!Stations.TryGetValue(guid, out var r) || r == null) Stations[guid] = r = new StationRecord();
            if (r.Chain == null) r.Chain = new List<string>();
            return r;
        }
    }
}
