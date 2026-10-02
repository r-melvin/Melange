using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.IO.Compression;
using System.Text.Json.Serialization;
using MelonLoader.Utils;

namespace PaperTrail
{
    /// <summary>What kind of save a snapshot came from.</summary>
    public enum SaveKind { Manual, Auto, Sleep, BeforeRestore, Milestone, Safeguard, Imported }

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

        /// <summary>For an imported save: its campaign's creation time (ticks), to tell whether it is the slot's campaign.</summary>
        public long SaveCreatedTicks { get; set; }

        /// <summary>Why a Milestone or Safeguard save was taken: "Quest completed: ...", "Game updated 0.4.7f6 to 0.4.7f7".</summary>
        public string Reason { get; set; } = "";

        /// <summary>
        /// This save looks like it lost something: far fewer or smaller files than the save before it. It is kept but
        /// never pushes older saves out, so one bad save cannot replace the good ones behind it.
        /// </summary>
        public bool Suspect { get; set; }

        public int FileCount { get; set; }
        public long TotalBytes { get; set; }

        /// <summary>Pinned by the player, as opposed to the pin the game-kept copies carry by themselves.</summary>
        public bool PinnedByUser { get; set; }

        [JsonIgnore] public string Folder { get; set; } = "";

        /// <summary>
        /// The save, as a zip laid out like the game's own export: one folder (SaveGame_N) holding the files. The game's
        /// Import button reads it as it is, so a snapshot works without Paper Trail installed.
        /// </summary>
        [JsonIgnore] public string ZipPath => Path.Combine(Folder, "save.zip");

        /// <summary>Snapshots made by the first builds were plain folders; they are zipped the next time the game starts.</summary>
        [JsonIgnore] public string DataFolder => Path.Combine(Folder, "save");

        [JsonIgnore] public bool IsZip => File.Exists(ZipPath);
    }

    /// <summary>Per-campaign (save slot) bookkeeping kept by Paper Trail.</summary>
    public sealed class CampaignInfo
    {
        public double PlaySeconds { get; set; }
        public int NextAutoNumber { get; set; } = 1;

        /// <summary>The mods ("Name vX") installed when this campaign was last played.</summary>
        public List<string> Mods { get; set; } = new List<string>();

        /// <summary>
        /// Which campaign this is: the slot it was played in, its organisation, and the game's creation time of its
        /// save (ticks). A different creation time in the slot means a new game or an import replaced it there.
        /// </summary>
        public int Slot { get; set; }
        public string Organisation { get; set; } = "";
        public long SaveCreatedTicks { get; set; }

        /// <summary>Set when this history became one of its slot's backups, and why ("Before a new game").</summary>
        public DateTime? ArchivedUtc { get; set; }
        public string BackupReason { get; set; } = "";
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
        private const string InfoFile = "snapshot.json";
        private const string CampaignFile = "campaign.json";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Saves/&lt;steam id&gt;, set once the game knows it.</summary>
        public static string SavesRoot { get; set; } = "";

        /// <summary>
        /// The working copy is always on this computer; <see cref="Mirror"/> keeps a second one beside the game's saves
        /// for Steam Cloud to carry.
        /// </summary>
        public static string Root => LocalRoot;

        public static string CloudRoot => Path.Combine(SavesRoot, "PaperTrail");
        public static string LocalRoot => Path.Combine(MelonEnvironment.UserDataDirectory, "PaperTrail", "Snapshots");

        public static string SlotFolder(int slot) => Path.Combine(Root, "Slot_" + slot);

        /// <summary>A slot's backups, one folder each: a campaign's whole history, or one save.</summary>
        public static string BackupsFolder(int slot) => Path.Combine(Root, "Backups", "Slot_" + slot);
        private static string CloudBackupsFolder(int slot) => Path.Combine(CloudRoot, "Backups", "Slot_" + slot);
        public static string GameSlotFolder(int slot) => Path.Combine(SavesRoot, "SaveGame_" + slot);


        /// <summary>Paper Trail's early builds were called SaveKeeper: their snapshots are renamed, once, for the mirror to find.</summary>
        public static void MigrateFolders()
        {
            try
            {
                var old = Path.Combine(SavesRoot, "SaveKeeper");
                if (Directory.Exists(old) && !Directory.Exists(CloudRoot)) { Directory.Move(old, CloudRoot); Mod.Log.Msg("moved snapshots from the old SaveKeeper folder"); }
                Directory.CreateDirectory(Root);
                // Half-written copies a crash left behind.
                foreach (var root in new[] { LocalRoot, CloudRoot })
                    if (Directory.Exists(root))
                        foreach (var leftover in Directory.GetDirectories(root, "*.partial", SearchOption.AllDirectories))
                            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(leftover) > TimeSpan.FromMinutes(10))
                                try { Directory.Delete(leftover, true); } catch { }
            }
            catch (Exception e) { Mod.Log.Warning("could not prepare the snapshots folder: " + e.Message); }
        }

        // ---------------------------------------------------------------- campaigns

        public static CampaignInfo Campaign(int slot) => Campaign(SlotFolder(slot));

        public static CampaignInfo Campaign(string folder)
        {
            try
            {
                var file = Path.Combine(folder, CampaignFile);
                if (File.Exists(file)) return JsonSerializer.Deserialize<CampaignInfo>(File.ReadAllText(file), Json) ?? new CampaignInfo();
            }
            catch (Exception e) { Mod.Log.Warning($"campaign {Path.GetFileName(folder)}: could not read its bookkeeping ({e.Message}); starting fresh"); }
            return new CampaignInfo();
        }

        public static void SaveCampaign(int slot, CampaignInfo info) => SaveCampaign(SlotFolder(slot), info);

        public static void SaveCampaign(string folder, CampaignInfo info)
        {
            Directory.CreateDirectory(folder);
            WriteAtomic(Path.Combine(folder, CampaignFile), JsonSerializer.Serialize(info, Json));
        }

        /// <summary>
        /// The campaign now in the slot is <paramref name="organisation"/>, created at <paramref name="createdTicks"/>. If
        /// the slot's history belongs to another campaign (a new game or an import took the slot), that history is
        /// archived, so the two never share one list and the old one can still be loaded. Returns the campaign's
        /// bookkeeping.
        /// </summary>
        public static CampaignInfo Claim(int slot, string organisation, long createdTicks)
        {
            lock (Work.Disk)
            {
                var info = Campaign(slot);
                bool other;
                if (info.SaveCreatedTicks != 0) other = Math.Abs(info.SaveCreatedTicks - createdTicks) > TimeSpan.TicksPerSecond * 2;
                else
                {
                    // Bookkeeping from before this was recorded: judge by the organisation of the newest snapshot.
                    string newest = List(slot).FirstOrDefault()?.Organisation ?? "";
                    other = newest.Length > 0 && !string.Equals(newest, organisation, StringComparison.Ordinal);
                }
                if (other && List(slot).Count > 0)
                {
                    // Normally Paper Trail backed it up before the game replaced it (new game, import); this catches the rest.
                    string moved = MoveHistoryLocked(slot, info, "Replaced in its slot");
                    Mod.Log.Msg($"slot {slot} now holds {organisation}; the history of {Path.GetFileName(moved)} became a backup");
                    info = new CampaignInfo();
                }
                info.Slot = slot;
                info.Organisation = organisation ?? "";
                info.SaveCreatedTicks = createdTicks;
                SaveCampaign(slot, info);
                return info;
            }
        }

        /// <summary>
        /// Moves the slot's history into its backups, as one backup, and returns where it went. The synced copy moves
        /// with it, or the mirror would bring the history back into the slot.
        /// </summary>
        internal static string MoveHistoryLocked(int slot, CampaignInfo info, string reason)
        {
            string org = info.Organisation.Length > 0 ? info.Organisation : List(slot).FirstOrDefault()?.Organisation ?? "";
            string name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{SafeName(org)}";
            info.Slot = slot;
            info.Organisation = org;
            info.ArchivedUtc = DateTime.UtcNow;
            info.BackupReason = reason;
            SaveCampaign(slot, info);
            Directory.CreateDirectory(BackupsFolder(slot));
            var target = Path.Combine(BackupsFolder(slot), name);
            Directory.Move(SlotFolder(slot), target);
            var cloud = Path.Combine(CloudRoot, "Slot_" + slot);
            if (Directory.Exists(cloud))
                try { Directory.CreateDirectory(CloudBackupsFolder(slot)); Directory.Move(cloud, Path.Combine(CloudBackupsFolder(slot), name)); }
                catch (Exception e) { Mod.Log.Warning("could not move the Steam Cloud copy to the backups: " + e.Message); }
            Mirror.Request();
            return target;
        }

        /// <summary>Makes a backup the slot's history again (the slot must have none now), its synced copy too.</summary>
        internal static void MoveToSlotLocked(string backup, int slot)
        {
            string name = Path.GetFileName(backup);
            if (Directory.Exists(SlotFolder(slot))) Directory.Delete(SlotFolder(slot), true);
            Directory.Move(backup, SlotFolder(slot));
            var cloud = Path.Combine(CloudBackupsFolder(slot), name);
            var cloudSlot = Path.Combine(CloudRoot, "Slot_" + slot);
            if (Directory.Exists(cloud) && !Directory.Exists(cloudSlot))
                try { Directory.Move(cloud, cloudSlot); } catch (Exception e) { Mod.Log.Warning("could not move the Steam Cloud copy back: " + e.Message); }
            var info = Campaign(slot);
            info.Slot = slot;
            info.ArchivedUtc = null;
            info.BackupReason = "";
            SaveCampaign(slot, info);
            Mirror.Request();
        }

        /// <summary>The archive of 0.3.0 becomes the slots' backups: each archived campaign is a backup of its slot.</summary>
        public static void MigrateArchive()
        {
            foreach (var (root, cloud) in new[] { (Root, false), (CloudRoot, true) })
            {
                var archive = Path.Combine(root, "Archive");
                if (!Directory.Exists(archive)) continue;
                foreach (var dir in Directory.GetDirectories(archive))
                {
                    try
                    {
                        int slot = Campaign(dir).Slot;
                        if (slot < 1 || slot > 5) slot = 1;
                        var target = Path.Combine(cloud ? CloudBackupsFolder(slot) : BackupsFolder(slot), Path.GetFileName(dir));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        if (!Directory.Exists(target)) Directory.Move(dir, target);
                    }
                    catch (Exception e) { Mod.Log.Warning($"could not move {Path.GetFileName(dir)} to the backups: {e.Message}"); }
                }
                try { if (!Directory.EnumerateFileSystemEntries(archive).Any()) Directory.Delete(archive); } catch { }
            }
        }

        private static string SafeName(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var clean = new string((name ?? "").Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
            return clean.Length == 0 ? "campaign" : clean.Length > 40 ? clean.Substring(0, 40) : clean;
        }

        // ---------------------------------------------------------------- snapshots

        /// <summary>Every snapshot of a slot, newest first.</summary>
        public static List<SnapshotInfo> List(int slot) => List(SlotFolder(slot));

        /// <summary>Every snapshot in a campaign's folder (a slot's or an archived one), newest first.</summary>
        public static List<SnapshotInfo> List(string folder)
        {
            var list = new List<SnapshotInfo>();
            if (!Directory.Exists(folder)) return list;
            foreach (var dir in Directory.GetDirectories(folder))
            {
                var file = Path.Combine(dir, InfoFile);
                if (!File.Exists(file) || !(File.Exists(Path.Combine(dir, "save.zip")) || Directory.Exists(Path.Combine(dir, "save")))) continue;   // half-written: ignored
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

        /// <summary>
        /// Adds a save file (a zip, as the game's Export makes) to the slot's list as an imported save. Nothing in the
        /// game's slot changes until it is loaded. Returns null when the zip holds no save.
        /// </summary>
        public static SnapshotInfo ImportSave(int slot, string zipPath)
        {
            lock (Work.Disk)
            {
                string temp = Path.Combine(Root, "Import.partial");
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                try
                {
                    ZipFile.ExtractToDirectory(zipPath, temp);
                    var game = Directory.GetFiles(temp, "Game.json", SearchOption.AllDirectories).OrderBy(f => f.Length).FirstOrDefault();
                    if (game == null) return null;
                    string saveDir = Path.GetDirectoryName(game)!;
                    var info = new SnapshotInfo
                    {
                        Kind = SaveKind.Imported,
                        Location = "Imported",
                        Note = Path.GetFileNameWithoutExtension(zipPath),
                        GameVersion = ReadString(game, "GameVersion"),
                        Organisation = ReadString(game, "OrganisationName"),
                        NetWorth = (float)ReadNumber(Path.Combine(saveDir, "Money.json"), "Networth"),
                        GameDay = (int)ReadNumber(Path.Combine(saveDir, "Time.json"), "ElapsedDays") + 1,
                        GameTime = (int)ReadNumber(Path.Combine(saveDir, "Time.json"), "TimeOfDay"),
                        PlaySeconds = ReadNumber(Path.Combine(saveDir, "Time.json"), "Playtime"),
                        SaveCreatedTicks = ReadCreated(Path.Combine(saveDir, "Metadata.json")),
                        CreatedUtc = DateTime.UtcNow,
                    };
                    string name = info.CreatedUtc.ToString("yyyyMMdd-HHmmss-fff") + "-imported";
                    var final = Path.Combine(SlotFolder(slot), name);
                    var building = final + ".partial";
                    Directory.CreateDirectory(building);
                    // Repacked in the game's own layout, whatever folder (or none) the file had its save in.
                    ZipTree(saveDir, Path.Combine(building, "save.zip"), "SaveGame_" + slot);
                    var files = Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories);
                    info.FileCount = files.Length;
                    info.TotalBytes = files.Sum(f => new FileInfo(f).Length);
                    File.WriteAllText(Path.Combine(building, InfoFile), JsonSerializer.Serialize(info, Json));
                    Directory.Move(building, final);
                    info.Folder = final;
                    Mirror.Request();
                    return info;
                }
                finally { try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { } }
            }
        }

        /// <summary>Is this save from a campaign other than the one in the slot?</summary>
        public static bool IsOtherCampaign(int slot, SnapshotInfo save)
        {
            var campaign = Campaign(slot);
            if (campaign.SaveCreatedTicks != 0 && save.SaveCreatedTicks != 0)
                return Math.Abs(campaign.SaveCreatedTicks - save.SaveCreatedTicks) > TimeSpan.TicksPerSecond * 2;
            string org = campaign.Organisation.Length > 0 ? campaign.Organisation : List(slot).FirstOrDefault(s => s.Kind != SaveKind.Imported)?.Organisation ?? "";
            return org.Length > 0 && !string.Equals(org, save.Organisation, StringComparison.Ordinal);
        }

        private static JsonElement? ReadJson(string file)
        {
            try { return File.Exists(file) ? JsonDocument.Parse(File.ReadAllText(file)).RootElement : (JsonElement?)null; }
            catch { return null; }
        }

        private static string ReadString(string file, string name)
            => ReadJson(file) is JsonElement e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static double ReadNumber(string file, string name)
            => ReadJson(file) is JsonElement e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

        private static long ReadCreated(string file)
        {
            try
            {
                if (!(ReadJson(file) is JsonElement e) || !e.TryGetProperty("CreationDate", out var d)) return 0;
                int Get(string n) => d.TryGetProperty(n, out var v) ? v.GetInt32() : 0;
                return new DateTime(Get("Year"), Get("Month"), Get("Day"), Get("Hour"), Get("Minute"), Get("Second")).Ticks;
            }
            catch { return 0; }
        }

        /// <summary>Copies the slot's save as it is on disk now into a new snapshot.</summary>
        public static SnapshotInfo Take(int slot, SnapshotInfo info)
        {
            lock (Work.Disk) return TakeLocked(slot, info, SlotFolder(slot));
        }

        /// <summary>The same, into another campaign folder (a backup).</summary>
        internal static SnapshotInfo TakeInto(int slot, SnapshotInfo info, string folder)
        {
            lock (Work.Disk) return TakeLocked(slot, info, folder);
        }

        private static SnapshotInfo TakeLocked(int slot, SnapshotInfo info, string folder)
        {
            var source = GameSlotFolder(slot);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);

            info.CreatedUtc = DateTime.UtcNow;
            string name = info.CreatedUtc.ToString("yyyyMMdd-HHmmss-fff") + "-" + info.Kind.ToString().ToLowerInvariant();
            Directory.CreateDirectory(folder);
            var final = Path.Combine(folder, name);
            var temp = final + ".partial";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);

            // Written under a temporary name and renamed at the end, so a crash mid-copy never leaves a
            // snapshot that looks complete.
            Directory.CreateDirectory(temp);
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            info.FileCount = files.Length;
            info.TotalBytes = files.Sum(f => new FileInfo(f).Length);
            ZipTree(source, Path.Combine(temp, "save.zip"), "SaveGame_" + slot);
            var before = List(slot).FirstOrDefault(x => x.Kind != SaveKind.BeforeRestore && x.FileCount > 0);
            info.Suspect = before != null && (info.FileCount < before.FileCount * 0.8 || info.TotalBytes < before.TotalBytes * 0.5);
            File.WriteAllText(Path.Combine(temp, InfoFile), JsonSerializer.Serialize(info, Json));
            Directory.Move(temp, final);
            info.Folder = final;
            Mirror.Request();
            return info;
        }

        public static void Update(SnapshotInfo info)
        {
            WriteAtomic(Path.Combine(info.Folder, InfoFile), JsonSerializer.Serialize(info, Json));
            Mirror.Request();
        }

        public static void Delete(SnapshotInfo info)
        {
            lock (Work.Disk) DeleteLocked(info);
        }

        private static void DeleteLocked(SnapshotInfo info)
        {
            // Remembered first: a sync that sees the cloud copy must know it was deleted on purpose.
            var campaign = Path.GetDirectoryName(info.Folder);
            if (campaign != null) Mirror.Deleted(Path.GetRelativePath(Root, campaign).Replace('\\', '/'), Path.GetFileName(info.Folder));
            if (Directory.Exists(info.Folder)) Directory.Delete(info.Folder, true);
            Mirror.Request();
        }

        /// <summary>
        /// Keeps the newest auto-saves and key-moment saves (as many as the settings say) and deletes the rest.
        /// Pinned saves, manual saves, sleep saves and safeguards are never removed. A save that looks incomplete
        /// does not count, so it cannot push the good ones out; only the newest three of those are kept.
        /// </summary>
        public static int Prune(int slot)
        {
            lock (Work.Disk) return PruneLocked(slot);
        }

        private static int PruneLocked(int slot)
        {
            int removed = 0;
            void Trim(IEnumerable<SnapshotInfo> doomed)
            {
                foreach (var old in doomed)
                {
                    try { Delete(old); removed++; }
                    catch (Exception e) { Mod.Log.Warning($"could not remove old save {Path.GetFileName(old.Folder)}: {e.Message}"); }
                }
            }
            var all = List(slot).Where(s => !s.Pinned).ToList();
            // The copies Paper Trail keeps by itself are pinned so nothing else removes them; they are capped here.
            Trim(List(slot).Where(s => (s.Kind == SaveKind.BeforeRestore || s.Kind == SaveKind.Safeguard) && !s.PinnedByUser)
                           .Skip(Settings.KeptCopiesKept));
            Trim(all.Where(s => s.Kind == SaveKind.Auto && !s.Suspect).Skip(Settings.AutoSavesKept));
            Trim(all.Where(s => s.Kind == SaveKind.Milestone && !s.Suspect).Skip(Settings.KeyMomentSavesKept));
            Trim(all.Where(s => s.Suspect && (s.Kind == SaveKind.Auto || s.Kind == SaveKind.Milestone)).Skip(3));
            // Leftovers from a copy that never finished.
            foreach (var partial in Directory.Exists(SlotFolder(slot)) ? Directory.GetDirectories(SlotFolder(slot), "*.partial") : Array.Empty<string>())
                try { Directory.Delete(partial, true); } catch { }
            return removed;
        }

        /// <summary>
        /// Puts a snapshot back into the game's slot. Only call with that slot not loaded, and with what is in it
        /// backed up first if it matters (see <see cref="Backups"/>).
        /// </summary>
        public static void Restore(int slot, SnapshotInfo snapshot)
        {
            lock (Work.Disk) RestoreLocked(slot, snapshot);
        }

        private static void RestoreLocked(int slot, SnapshotInfo snapshot)
        {
            var target = GameSlotFolder(slot);
            if (!snapshot.IsZip && !Directory.Exists(snapshot.DataFolder)) throw new DirectoryNotFoundException(snapshot.Folder);

            // Copied beside the slot first and swapped in, so a failure never leaves the slot half-written.
            var incoming = target + ".papertrail-incoming";
            var outgoing = target + ".papertrail-outgoing";
            foreach (var d in new[] { incoming, outgoing }) if (Directory.Exists(d)) Directory.Delete(d, true);
            if (snapshot.IsZip) UnzipTo(snapshot.ZipPath, incoming);
            else CopyDirectory(snapshot.DataFolder, incoming);
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
                SaveKind.BeforeRestore => $"Kept copy - {s.Location}",
                SaveKind.Milestone => $"{s.Reason} - {s.Location}",
                SaveKind.Safeguard => $"{s.Reason} - {s.Location}",
                _ => s.Location,
            };
            string when = s.GameDay > 0 ? $" - Day {s.GameDay}, {Clock(s.GameTime)}" : "";
            return $"{head}{when} - {PlayTime(s.PlaySeconds)}";
        }

        /// <summary>
        /// A snapshot named in another snapshot's note: its kind, place and time, never its own note, so restoring a
        /// kept copy does not wrap one name in another ("Before restoring Before restore - ...").
        /// </summary>
        public static string ShortName(SnapshotInfo s)
        {
            string kind = s.Kind switch
            {
                SaveKind.Auto => $"AutoSave {s.AutoNumber}",
                SaveKind.Sleep => "a sleep save",
                SaveKind.BeforeRestore => "a kept copy",
                SaveKind.Milestone => "a key-moment save",
                SaveKind.Safeguard => "a safeguard copy",
                _ => "a manual save",
            };
            string when = s.GameDay > 0 ? $", Day {s.GameDay} {Clock(s.GameTime)}" : "";
            return $"{kind} ({s.Location}{when})";
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
        /// Is the slot as it stands identical to this snapshot? Metadata.json is left out: the game rewrites its
        /// last-played date every time it starts a save, without anything having changed.
        /// </summary>
        public static bool SlotMatches(int slot, SnapshotInfo snapshot)
        {
            var slotDir = GameSlotFolder(slot);
            if (!Directory.Exists(slotDir)) return false;
            var mine = Relative(slotDir);
            if (snapshot.IsZip)
            {
                try
                {
                    using var zip = ZipFile.OpenRead(snapshot.ZipPath);
                    var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name))
                        .Select(e => (Path: e.FullName.Substring(e.FullName.IndexOf('/') + 1).Replace('/', Path.DirectorySeparatorChar), Entry: e))
                        .Where(x => !string.Equals(x.Path, "Metadata.json", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (entries.Count != mine.Count) return false;
                    foreach (var (path, entry) in entries)
                    {
                        if (!mine.Contains(path)) return false;
                        var file = new FileInfo(Path.Combine(slotDir, path));
                        if (file.Length != entry.Length) return false;
                    }
                    foreach (var (path, entry) in entries)
                    {
                        using var a = entry.Open();
                        using var b = File.OpenRead(Path.Combine(slotDir, path));
                        if (!StreamsEqual(a, b)) return false;
                    }
                    return true;
                }
                catch (Exception e) { Mod.Log.Warning($"could not compare the slot with {Path.GetFileName(snapshot.Folder)}: {e.Message}"); return false; }
            }
            if (!Directory.Exists(snapshot.DataFolder)) return false;
            var theirs = Relative(snapshot.DataFolder);
            if (mine.Count != theirs.Count) return false;
            foreach (var file in mine)
            {
                if (!theirs.Contains(file)) return false;
                if (new FileInfo(Path.Combine(slotDir, file)).Length != new FileInfo(Path.Combine(snapshot.DataFolder, file)).Length) return false;
            }
            foreach (var file in mine)
                if (!File.ReadAllBytes(Path.Combine(slotDir, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(snapshot.DataFolder, file))))
                    return false;
            return true;
        }

        private static bool StreamsEqual(Stream a, Stream b)
        {
            var x = new byte[81920]; var y = new byte[81920];
            while (true)
            {
                int n = ReadFull(a, x), m = ReadFull(b, y);
                if (n != m) return false;
                if (n == 0) return true;
                if (!x.AsSpan(0, n).SequenceEqual(y.AsSpan(0, n))) return false;
            }
        }

        private static int ReadFull(Stream s, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length) { int n = s.Read(buffer, total, buffer.Length - total); if (n == 0) break; total += n; }
            return total;
        }

        // ---------------------------------------------------------------- zip

        /// <summary>A zip with one top-level folder, the layout the game's own export makes and its Import button reads.</summary>
        public static void ZipTree(string source, string zipPath, string rootName)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, rootName + "/" + relative, CompressionLevel.SmallestSize);
                }
            }
            // Read back before anything depends on it.
            using var check = ZipFile.OpenRead(zipPath);
            int expected = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length;
            if (check.Entries.Count(e => !string.IsNullOrEmpty(e.Name)) != expected)
                throw new IOException("the zip does not hold every file");
        }

        private static void UnzipTo(string zipPath, string target)
        {
            string extract = target + ".extract";
            if (Directory.Exists(extract)) Directory.Delete(extract, true);
            ZipFile.ExtractToDirectory(zipPath, extract);
            var root = Directory.GetDirectories(extract).FirstOrDefault() ?? extract;
            Directory.Move(root, target);
            if (Directory.Exists(extract)) Directory.Delete(extract, true);
        }

        /// <summary>Zips the plain-folder snapshots the first builds made, and removes the folder once the zip reads back whole.</summary>
        public static int CompressOldSnapshots()
        {
            lock (Work.Disk) return CompressOldLocked();
        }

        private static int CompressOldLocked()
        {
            int done = 0;
            for (int slot = 1; slot <= 5; slot++)
                foreach (var snap in List(slot).Where(x => !x.IsZip && Directory.Exists(x.DataFolder)))
                {
                    try
                    {
                        ZipTree(snap.DataFolder, snap.ZipPath + ".new", "SaveGame_" + slot);
                        File.Move(snap.ZipPath + ".new", snap.ZipPath, true);
                        Directory.Delete(snap.DataFolder, true);
                        done++;
                    }
                    catch (Exception e)
                    {
                        Mod.Log.Warning($"could not compress {Path.GetFileName(snap.Folder)}: {e.Message}");
                        try { File.Delete(snap.ZipPath + ".new"); } catch { }
                    }
                }
            return done;
        }

        private static HashSet<string> Relative(string root)
            => new HashSet<string>(Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f))
                .Where(f => !string.Equals(f, "Metadata.json", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Is what is in the slot already kept as a snapshot? False for saves made without Paper Trail and for a
        /// slot with no snapshots - the only cases where restoring one would lose something.
        /// </summary>
        public static bool SlotIsKept(int slot)
        {
            if (SlotWrittenUtc(slot) == null) return true;          // nothing there to lose
            return List(slot).Any(s => SlotMatches(slot, s));
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
