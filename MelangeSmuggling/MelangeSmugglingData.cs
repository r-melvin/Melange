using S1API.Saveables;
using Melange.Core;
using S1API.Internal.Abstraction;

namespace Melange.Smuggling
{
    /// <summary>
    /// What the smuggling spoke remembers for one saved game (Logic/SmugglingState.cs). An S1API saveable, so the game's
    /// own save writes it into the slot; the type name is prefixed so it can't collide with another mod's (S1API names
    /// saves by short type name). Nothing goes into the game's own data: without the mod the boat and Dafydd are simply gone.
    /// </summary>
    public sealed class MelangeSmugglingData : Saveable, IResettableSaveData
    {
        public static MelangeSmugglingData Current { get; private set; }

        [SaveableField("state")] public SmugglingState State = new SmugglingState();

        public MelangeSmugglingData() { Current = this; SaveData.Track(this); }

        /// <summary>
        /// Back to a fresh game's values. S1API keeps one instance for the whole session and, loading a save, only sets the
        /// fields that save has files for: without this, a save with no smuggling data loaded after one with it would
        /// inherit that save's unlock, order and hold. Called on returning to the menu, before the next save loads.
        /// </summary>
        public void ResetToDefaults() => State = new SmugglingState();

        protected override void OnLoaded()
        {
            if (State == null) State = new SmugglingState();
            State.Repair();
        }
    }
}
