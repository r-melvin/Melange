using System;
using System.Collections.Generic;

namespace Melange.Core
{
    /// <summary>
    /// The shared manager role: an NPC who runs a site's chores and takes a cut. The cartel spoke's underbosses, the Sewer
    /// King as Underground underboss, and level-unlocked managers all implement it, so there is one system, not several.
    /// What a manager does each day, and their personality, stay with the spoke that owns them.
    /// </summary>
    public abstract class Manager
    {
        /// <summary>Stable id, unique across spokes ("cartel.underboss.tommy", "sewer.king").</summary>
        public abstract string Id { get; }
        public abstract string Name { get; }
        /// <summary>Where they work: a property code or a spoke-defined site id.</summary>
        public abstract string Site { get; }
        /// <summary>Their share of what the site earns, 0-1.</summary>
        public abstract float Cut { get; }
        /// <summary>0 (about to turn) to 1 (devoted). Owned by the spoke; reported here so other spokes can react.</summary>
        public abstract float Loyalty { get; }

        /// <summary>The day's work (tend, restock, collect, pay). Called once per in-game day, on the host only.</summary>
        public abstract void RunDailyChores(int day);
    }

    /// <summary>A manager's loyalty changed (the owning spoke reports it through <see cref="Managers.ReportLoyalty"/>).</summary>
    public sealed class ManagerLoyaltyChanged
    {
        public Manager Manager { get; }
        public float Before { get; }
        public float After { get; }
        internal ManagerLoyaltyChanged(Manager manager, float before, float after) { Manager = manager; Before = before; After = after; }
    }

    /// <summary>Every registered manager, and their daily round.</summary>
    public static class Managers
    {
        private static readonly Dictionary<string, Manager> All = new Dictionary<string, Manager>();

        public static void Register(Manager manager) => All[manager.Id] = manager;
        public static void Unregister(string id) => All.Remove(id);
        public static IReadOnlyCollection<Manager> Current => All.Values;
        public static Manager Get(string id) => All.TryGetValue(id, out var m) ? m : null;

        /// <summary>For the owning spoke to announce a loyalty change it has made.</summary>
        public static void ReportLoyalty(Manager manager, float before)
            => Events.Publish(new ManagerLoyaltyChanged(manager, before, manager.Loyalty));

        internal static void OnDayPassed(int day)
        {
            if (!Host.IsHost) return;
            foreach (var m in new List<Manager>(All.Values))
            {
                try { m.RunDailyChores(day); }
                catch (Exception e) { Core.Log?.Warning($"manager {m.Id}'s daily chores failed: {e.Message}"); }
            }
        }

        /// <summary>Leaving a save: managers belong to it.</summary>
        internal static void Reset() => All.Clear();
    }
}
