using System;
using System.Threading;

namespace PaperTrail
{
    /// <summary>Disk and compression work runs on a low-priority thread of its own, so a slow computer never stutters the game.</summary>
    internal static class Work
    {
        /// <summary>Held while a snapshot is written, pruned or restored, so a load never reads one half-made.</summary>
        public static readonly object Disk = new object();

        public static void Run(Action action, Action<Exception> failed = null)
        {
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception e) { try { failed?.Invoke(e); } catch { } }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "PaperTrail" };
            thread.Start();
        }
    }
}
