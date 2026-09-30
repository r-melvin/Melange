using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaperTrail
{
    /// <summary>What kind of save a snapshot came from.</summary>
    public enum SaveKind { Manual, Auto, Sleep, BeforeRestore }

    /// <summary>One snapshot's details, stored next to its copy of the save as snapshot.json.</summary>
    public sealed class SnapshotInfo
    {
        public SaveKind Kind { get; set; }
        public DateTime CreatedUtc { get; set; }
        public int AutoNumber { get; set; }            // "AutoSave 16"; 0 for other kinds
        public string Location { get; set; } = "";
        public int GameDay { get; set; }               // 1-based in-game day
        public int GameTime { get; set; }             // hhmm, as the game keeps it
        public double PlaySeconds { get; set; }
        public string Organisation { get; set; } = "";
        public float NetWorth { get; set; }
        public float Cash { get; set; }
        public float Online { get; set; }
        public string Rank { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string Note { get; set; } = "";
        public bool Pinned { get; set; }
        public bool SaveHadErrors { get; set; }

        [JsonIgnore] public string Folder { get; set; } = "";
        [JsonIgnore] public string DataFolder => Path.Combine(Folder, "save");
    }

    /// <summary>Per-campaign (save slot) bookkeeping kept by Paper Trail.</summary>
    public sealed class CampaignInfo
    {
        public double PlaySeconds { get; set; }
        public int NextAutoNumber { get; set; } = 1;
    }

    /// <summary>
    /// Snapshots of the game's save slots, kept beside them in Saves/&lt;steam id&gt;/PaperTrail/Slot_N/.
    /// </summary>
    /// <remarks>
    /// Never inside SaveGame_N: the game deletes any file in a slot it did not write itself
    /// (SaveManager.ClearBaseLevelOutdatedSaves). Inside the Saves folder, so Steam Cloud carries them.
    /// </remarks>
    public static class Store
    {
        public const int AutoSavesKept = 10;
        private const string InfoFile = "snapshot.json";
        private const string CampaignFile = "campaign.json";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Saves/&lt;steam id&gt;, set once the game knows it.</summary>
        public static string SavesRoot { get; set; } = "";

        public static string Root => Path.Combine(SavesRoot, "PaperTrail");

        public static string SlotFolder(int slot) => Path.Combine(Root, "Slot_" + slot);

        public static string GameSlotFolder(int slot) => Path.Combine(SavesRoot, "SaveGame_" + slot);

        /// <summary>Paper Trail's early builds were called SaveKeeper; their snapshots move across once.</summary>
        public static void MigrateOldFolder()
        {
            var old = Path.Combine(SavesRoot, "SaveKeeper");
            try
            {
                if (Directory.Exists(old) && !Directory.Exists(Root)) { Directory.Move(old, Root); Mod.Log.Msg("moved snapshots from the old SaveKeeper folder"); }
            }
            catch (Exception e) { Mod.Log.Warning("could not move the old SaveKeeper folder: " + e.Message); }
        }

        // ---------------------------------------------------------------- campaigns

        public static CampaignInfo Campaign(int slot)
        {
            try
            {
                var file = Path.Combine(SlotFolder(slot), CampaignFile);
                if (File.Exists(file)) return JsonSerializer.Deserialize<CampaignInfo>(File.ReadAllText(file), Json) ?? new CampaignInfo();
            }
            catch (Exception e) { Mod.Log.Warning($"campaign {slot}: could not read its bookkeeping ({e.Message}); starting fresh"); }
            return new CampaignInfo();
        }

        public static void SaveCampaign(int slot, CampaignInfo info)
        {
            Directory.CreateDirectory(SlotFolder(slot));
            WriteAtomic(Path.Combine(SlotFolder(slot), CampaignFile), JsonSerializer.Serialize(info, Json));
        }

        // ---------------------------------------------------------------- snapshots

        /// <summary>Every snapshot of a slot, newest first.</summary>
        public static List<SnapshotInfo> List(int slot)
        {
            var list = new List<SnapshotInfo>();
            var folder = SlotFolder(slot);
            if (!Directory.Exists(folder)) return list;
            foreach (var dir in Directory.GetDirectories(folder))
            {
                var file = Path.Combine(dir, InfoFile);
                if (!File.Exists(file) || !Directory.Exists(Path.Combine(dir, "save"))) continue;   // half-written: ignored
                try
                {
                    var info = JsonSerializer.Deserialize<SnapshotInfo>(File.ReadAllText(file), Json);
                    if (info == null) continue;
                    info.Folder = dir;
                    list.Add(info);
                }
                catch (Exception e) { Mod.Log.Warning($"snapshot {Path.GetFileName(dir)}: unreadable ({e.Message}), skipped"); }
            }
            return list.OrderByDescending(s => s.CreatedUtc).ToList();
        }

        /// <summary>Copies the slot's save as it is on disk now into a new snapshot.</summary>
        public static SnapshotInfo Take(int slot, SnapshotInfo info)
        {
            var source = GameSlotFolder(slot);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);

            info.CreatedUtc = DateTime.UtcNow;
            string name = info.CreatedUtc.ToString("yyyyMMdd-HHmmss-fff") + "-" + info.Kind.ToString().ToLowerInvariant();
            var final = Path.Combine(SlotFolder(slot), name);
            var temp = final + ".partial";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);

            // Written under a temporary name and renamed at the end, so a crash mid-copy never leaves a
            // snapshot that looks complete.
            CopyDirectory(source, Path.Combine(temp, "save"));
            File.WriteAllText(Path.Combine(temp, InfoFile), JsonSerializer.Serialize(info, Json));
            Directory.Move(temp, final);
            info.Folder = final;
            return info;
        }

        public static void Update(SnapshotInfo info)
            => WriteAtomic(Path.Combine(info.Folder, InfoFile), JsonSerializer.Serialize(info, Json));

        public static void Delete(SnapshotInfo info)
        {
            if (Directory.Exists(info.Folder)) Directory.Delete(info.Folder, true);
        }

        /// <summary>Keeps the newest <see cref="AutoSavesKept"/> unpinned auto-saves of a slot and deletes the rest.</summary>
        public static int Prune(int slot)
        {
            int removed = 0;
            foreach (var old in List(slot).Where(s => s.Kind == SaveKind.Auto && !s.Pinned).Skip(AutoSavesKept))
            {
                try { Delete(old); removed++; }
                catch (Exception e) { Mod.Log.Warning($"could not remove old auto-save {Path.GetFileName(old.Folder)}: {e.Message}"); }
            }
            // Leftovers from a copy that never finished.
            foreach (var partial in Directory.Exists(SlotFolder(slot)) ? Directory.GetDirectories(SlotFolder(slot), "*.partial") : Array.Empty<string>())
                try { Directory.Delete(partial, true); } catch { }
            return removed;
        }

        /// <summary>
        /// Puts a snapshot back into the game's slot. The slot's current state is snapshotted first, so a
        /// restore can itself be undone. Only call with that slot not loaded.
        /// </summary>
        public static void Restore(int slot, SnapshotInfo snapshot, SnapshotInfo current)
        {
            var target = GameSlotFolder(slot);
            if (!Directory.Exists(snapshot.DataFolder)) throw new DirectoryNotFoundException(snapshot.DataFolder);
            if (Directory.Exists(target) && current != null)
            {
                current.Kind = SaveKind.BeforeRestore;
                current.Pinned = true;
                current.Note = "Before restoring " + Describe(snapshot);
                Take(slot, current);
            }

            // Copied beside the slot first and swapped in, so a failure never leaves the slot half-written.
            var incoming = target + ".papertrail-incoming";
            var outgoing = target + ".papertrail-outgoing";
            foreach (var d in new[] { incoming, outgoing }) if (Directory.Exists(d)) Directory.Delete(d, true);
            CopyDirectory(snapshot.DataFolder, incoming);
            if (Directory.Exists(target)) Directory.Move(target, outgoing);
            Directory.Move(incoming, target);
            if (Directory.Exists(outgoing)) Directory.Delete(outgoing, true);
        }

        public static string Describe(SnapshotInfo s)
        {
            string head = s.Kind switch
            {
                SaveKind.Auto => $"AutoSave {s.AutoNumber} - {s.Location}",
                SaveKind.Sleep => $"Sleep - {s.Location}",
                SaveKind.BeforeRestore => $"Before restore - {s.Location}",
                _ => s.Location,
            };
            string when = s.GameDay > 0 ? $" - Day {s.GameDay}, {Clock(s.GameTime)}" : "";
            return $"{head}{when} - {PlayTime(s.PlaySeconds)}";
        }

        public static string Clock(int hhmm)
        {
            int h = hhmm / 100, m = hhmm % 100;
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00}{(h < 12 ? "am" : "pm")}";
        }

        public static string PlayTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        }

        /// <summary>When the game last wrote to the slot, or null if it is empty.</summary>
        public static DateTime? SlotWrittenUtc(int slot)
        {
            var dir = GameSlotFolder(slot);
            if (!Directory.Exists(dir)) return null;
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            return files.Length == 0 ? null : files.Max(f => File.GetLastWriteTimeUtc(f));
        }

        /// <summary>
        /// Has the slot been saved since its newest snapshot? True for saves made without Paper Trail, and for a
        /// slot with no snapshots at all - the only cases where a restore would lose something.
        /// </summary>
        public static bool SlotNewerThanSnapshots(int slot)
        {
            var written = SlotWrittenUtc(slot);
            if (written == null) return false;
            var newest = List(slot).FirstOrDefault();
            return newest == null || written.Value > newest.CreatedUtc.AddSeconds(5);
        }

        // ---------------------------------------------------------------- files

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(from))
                CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        private static void WriteAtomic(string path, string text)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, path, true);
        }
    }
}
