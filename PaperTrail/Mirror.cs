using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PaperTrail
{
    /// <summary>
    /// Keeps a second copy of the snapshots beside the game's saves, where Steam Cloud carries it to other computers.
    /// </summary>
    /// <remarks>
    /// The working copy is always the one on this computer (<see cref="Store.LocalRoot"/>): reads and writes never
    /// touch the synced folder, so a Steam Cloud conflict cannot damage a snapshot Paper Trail is using. The
    /// mirror only adds what is missing on either side, takes the newer of a snapshot's details (its pin and
    /// name), and carries deletions across - a deleted snapshot is remembered, so a cloud copy that turns up
    /// again is removed rather than brought back.
    /// </remarks>
    internal static class Mirror
    {
        private static readonly object Gate = new object();
        private static bool _running, _again;

        private static string Tombstones => Path.Combine(Store.LocalRoot, "deleted.txt");

        /// <summary>Forgets a deleted snapshot, so the mirror removes it from the synced folder and does not restore it.</summary>
        /// <param name="campaign">The campaign's folder, relative to the snapshots root: "Slot_2" or "Backups/Slot_2/...".</param>
        public static void Deleted(string campaign, string name)
        {
            try
            {
                Directory.CreateDirectory(Store.LocalRoot);
                File.AppendAllLines(Tombstones, new[] { $"{campaign}/{name}" });
            }
            catch (Exception e) { Mod.Log.Warning("could not remember a deleted snapshot: " + e.Message); }
        }

        /// <summary>Brings the two copies in line, in the background. Requests close together run once more, not many times.</summary>
        public static void Request()
        {
            if (!Settings.SyncToSteamCloud || string.IsNullOrEmpty(Store.SavesRoot)) return;
            lock (Gate)
            {
                if (_running) { _again = true; return; }
                _running = true;
            }
            Work.Run(() =>
            {
                try { do { lock (Gate) _again = false; Run(); } while (Again()); }
                catch (Exception e) { Mod.Log.Warning("could not sync snapshots with Steam Cloud: " + e.Message); }
                finally { lock (Gate) _running = false; }
            });
        }

        private static bool Again() { lock (Gate) return _again; }

        /// <summary>The same, but waiting for it: used when the game is closing.</summary>
        public static void Now()
        {
            if (!Settings.SyncToSteamCloud || string.IsNullOrEmpty(Store.SavesRoot)) return;
            for (int i = 0; i < 100 && Running(); i++) System.Threading.Thread.Sleep(50);
            try { Run(); } catch (Exception e) { Mod.Log.Warning("could not sync snapshots with Steam Cloud: " + e.Message); }
        }

        private static bool Running() { lock (Gate) return _running; }

        private static void Run()
        {
            // Not while a snapshot is being written, deleted or restored: the pass would see it half done.
            lock (Work.Disk) RunLocked();
        }

        private static void RunLocked()
        {
            var deleted = new HashSet<string>(File.Exists(Tombstones) ? File.ReadAllLines(Tombstones) : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            int toCloud = 0, toLocal = 0, removed = 0;
            foreach (var campaign in CampaignFolders())
            {
                string local = Path.Combine(Store.LocalRoot, campaign);
                string cloud = Path.Combine(Store.CloudRoot, campaign);
                if (!Directory.Exists(local) && !Directory.Exists(cloud)) continue;
                Directory.CreateDirectory(local);
                Directory.CreateDirectory(cloud);

                var here = Names(local);
                var there = Names(cloud);
                foreach (var name in there)
                {
                    if (deleted.Contains($"{campaign}/{name}"))
                    {
                        Try(() => Directory.Delete(Path.Combine(cloud, name), true));
                        removed++;
                    }
                    else if (!here.Contains(name))
                    {
                        if (Try(() => CopyWhole(Path.Combine(cloud, name), Path.Combine(local, name)))) toLocal++;
                    }
                    else SyncDetails(Path.Combine(local, name), Path.Combine(cloud, name));
                }
                foreach (var name in here)
                    if (!there.Contains(name) && !deleted.Contains($"{campaign}/{name}"))
                        if (Try(() => CopyWhole(Path.Combine(local, name), Path.Combine(cloud, name)))) toCloud++;

                // The campaign's play time and auto-save number: whichever was written last.
                SyncDetails(local, cloud, "campaign.json");
            }
            if (toCloud + toLocal + removed > 0)
                Mod.Log.Msg($"snapshots synced with Steam Cloud: {toCloud} sent, {toLocal} brought back, {removed} removed");
        }

        /// <summary>The five slots' histories and every backup, on either side ("Slot_1", "Backups/Slot_1/...").</summary>
        private static IEnumerable<string> CampaignFolders()
        {
            for (int slot = 1; slot <= 5; slot++) yield return "Slot_" + slot;
            for (int slot = 1; slot <= 5; slot++)
            {
                var backups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var root in new[] { Store.LocalRoot, Store.CloudRoot })
                {
                    var dir = Path.Combine(root, "Backups", "Slot_" + slot);
                    if (Directory.Exists(dir)) foreach (var d in Directory.GetDirectories(dir)) backups.Add(Path.GetFileName(d));
                }
                foreach (var name in backups) yield return $"Backups/Slot_{slot}/{name}";
            }
        }

        private static HashSet<string> Names(string folder)
            => new HashSet<string>(Directory.GetDirectories(folder)
                .Where(d => !d.EndsWith(".partial", StringComparison.Ordinal)
                            && File.Exists(Path.Combine(d, "snapshot.json"))
                            && (File.Exists(Path.Combine(d, "save.zip")) || Directory.Exists(Path.Combine(d, "save"))))
                .Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);

        /// <summary>Copies to a temporary name and renames, so a half-copied snapshot never looks complete.</summary>
        private static void CopyWhole(string from, string to)
        {
            string temp = to + ".partial";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            CopyTree(from, temp);
            Directory.Move(temp, to);
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(from)) CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        private static void SyncDetails(string a, string b, string file = "snapshot.json")
        {
            Try(() =>
            {
                string fa = Path.Combine(a, file), fb = Path.Combine(b, file);
                if (!File.Exists(fa) && !File.Exists(fb)) return;
                if (!File.Exists(fb)) File.Copy(fa, fb);
                else if (!File.Exists(fa)) File.Copy(fb, fa);
                else
                {
                    var ta = File.GetLastWriteTimeUtc(fa);
                    var tb = File.GetLastWriteTimeUtc(fb);
                    if (Math.Abs((ta - tb).TotalSeconds) < 2) return;
                    if (ta > tb) File.Copy(fa, fb, true); else File.Copy(fb, fa, true);
                }
            });
        }

        private static bool Try(Action action)
        {
            try { action(); return true; }
            catch (Exception e) { Mod.Log.Warning("snapshot sync: " + e.Message); return false; }
        }
    }
}
