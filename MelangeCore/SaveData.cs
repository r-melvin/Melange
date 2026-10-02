using System;
using System.Collections.Generic;

namespace Melange.Core
{
    /// <summary>A spoke's save data that can go back to a fresh game's values.</summary>
    public interface IResettableSaveData
    {
        /// <summary>Puts every saved field back to the value a new game starts with.</summary>
        void ResetToDefaults();
    }

    /// <summary>
    /// Resets spokes' save data on returning to the menu. S1API keeps one instance per saveable type for the whole session
    /// and, loading a save, only sets the fields that save has files for, so without a reset a save with no data for a spoke
    /// inherits the previous save's. A spoke's S1API <c>Saveable</c> (which must inherit <c>Saveable</c> directly: S1API
    /// only registers direct subclasses) implements <see cref="IResettableSaveData"/> and calls <see cref="Track"/> from its
    /// constructor; the hub resets every tracked instance before <see cref="MenuLoaded"/> is published.
    /// </summary>
    public static class SaveData
    {
        private static readonly List<WeakReference<IResettableSaveData>> Tracked = new List<WeakReference<IResettableSaveData>>();

        public static void Track(IResettableSaveData data)
        {
            if (data == null) return;
            lock (Tracked) Tracked.Add(new WeakReference<IResettableSaveData>(data));
        }

        internal static void ResetAll()
        {
            var live = new List<IResettableSaveData>();
            lock (Tracked)
            {
                Tracked.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in Tracked) if (w.TryGetTarget(out var d)) live.Add(d);
            }
            foreach (var d in live)
            {
                try { d.ResetToDefaults(); }
                catch (Exception e) { Core.Log.Warning($"{d.GetType().Name}.ResetToDefaults threw: {e.Message}"); }
            }
        }
    }
}
