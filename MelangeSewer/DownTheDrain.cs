using System;
using System.Collections.Generic;
using S1API.Quests;
using S1API.Quests.Constants;
using UnityEngine;

namespace Melange.Sewer
{
    /// <summary>
    /// The journal view of "Down the Drain". It holds no state of its own: <see cref="MelangeSewerData"/> does, and
    /// <see cref="Sync"/> sets every entry from it. S1API saves and recreates the quest while it is active; whatever it
    /// restores is overwritten by the next sync, so the two can't disagree for long.
    /// </summary>
    public sealed class DownTheDrain : Quest
    {
        /// <summary>The live quest, if one exists in this save (set by the constructor, which S1API also calls on load).</summary>
        public static DownTheDrain Current { get; private set; }

        protected override string Title => SewerLines.QuestTitle;
        protected override string Description => SewerLines.QuestDescription;

        private readonly Dictionary<SewerEntry, QuestEntry> _entries = new Dictionary<SewerEntry, QuestEntry>();

        public DownTheDrain()
        {
            foreach (SewerEntry e in Enum.GetValues(typeof(SewerEntry)))
                _entries[e] = AddEntry(SewerLines.EntryTitle(e));
            Current = this;
        }

        public static void Forget() => Current = null;

        /// <summary>Sets each entry's state from the logic, and points it at where to go when known.</summary>
        public void Sync(SewerQuest quest, Func<SewerEntry, Vector3?> where)
        {
            foreach (var kv in _entries)
            {
                try
                {
                    var wanted = quest.Entry(kv.Key) switch
                    {
                        EntryStatus.Active => QuestState.Active,
                        EntryStatus.Completed => QuestState.Completed,
                        _ => QuestState.Inactive,
                    };
                    if (kv.Value.State != wanted) kv.Value.SetState(wanted);
                    if (wanted == QuestState.Active)
                    {
                        var pos = where?.Invoke(kv.Key);
                        // Sync runs every second; only move the marker when the place actually changed
                        if (pos.HasValue && (kv.Value.POIPosition - pos.Value).sqrMagnitude > 0.25f) kv.Value.POIPosition = pos.Value;
                    }
                }
                catch (Exception e) { Mod.Log.Warning($"quest entry {kv.Key}: {e.Message}"); }
            }
        }
    }
}
