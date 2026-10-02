using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;
using UnityEngine;

namespace Melange.Core
{
    /// <summary>
    /// Entries spokes add to the game's rank-up screen ("Unlocked: ..."). The game keeps that list only at runtime, per client,
    /// so the hub re-adds every registered entry on each load; the game ignores an entry it already has (same title and icon).
    /// </summary>
    public static class RankUpScreen
    {
        private sealed class Entry { public int Rank, Tier; public string Title; public Func<Sprite> Icon; }
        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>Shows <paramref name="title"/> as unlocked at that rank and tier. <paramref name="icon"/> is asked for the sprite at each load.</summary>
        public static void Register(int rank, int tier, string title, Func<Sprite> icon)
        {
            Entries.Add(new Entry { Rank = rank, Tier = tier, Title = title, Icon = icon });
            Apply();                                             // already in a game: show it now
        }

        internal static void Apply()
        {
            try
            {
                var lm = NetworkSingleton<LevelManager>.Instance;
                if (lm == null) return;
                foreach (var e in Entries)
                {
                    Sprite icon = null;
                    try { icon = e.Icon?.Invoke(); } catch { }
                    lm.AddUnlockable(new Unlockable(new FullRank((ERank)e.Rank, e.Tier), e.Title, icon));
                }
            }
            catch (Exception e) { Core.Log?.Warning("rank-up screen entries not added: " + e.Message); }
        }
    }
}
