using S1API.Saveables;
using Melange.Core;
using S1API.Internal.Abstraction;

namespace Melange.Sewer
{
    /// <summary>
    /// What the sewer spoke remembers for one saved game: the quest's facts. An S1API saveable, so the game's own save
    /// writes it into the slot; the type name is prefixed so it can't collide with another mod's (S1API names saves by
    /// short type name). The game's own sewer flags (Sewer/Sewer) are never touched.
    /// </summary>
    public sealed class MelangeSewerData : Saveable, IResettableSaveData
    {
        public static MelangeSewerData Current { get; private set; }

        [SaveableField("state")] public SewerState State = new SewerState();

        public MelangeSewerData() { Current = this; SaveData.Track(this); }

        /// <summary>
        /// S1API keeps one instance for the whole session and skips fields with no file, so a save without sewer data would
        /// otherwise inherit the last save's quest. Called on returning to the menu, before the next save loads.
        /// </summary>
        public void ResetToDefaults() => State = new SewerState();

        protected override void OnLoaded()
        {
            if (State == null) State = new SewerState();
        }
    }
}
