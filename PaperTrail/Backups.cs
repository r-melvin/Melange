using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Il2CppScheduleOne.Persistence;

namespace PaperTrail
{
    /// <summary>One backup of a slot: a campaign's whole history, or a single save of it.</summary>
    internal sealed class Backup
    {
        public string Folder = "";
        public CampaignInfo Info = new CampaignInfo();
        public List<SnapshotInfo> Saves = new List<SnapshotInfo>();
        public SnapshotInfo Newest => Saves.FirstOrDefault();
        public DateTime MadeUtc => Info.ArchivedUtc ?? Directory.GetCreationTimeUtc(Folder);
    }

    /// <summary>
    /// Each slot keeps a few backups (three by default): made before anything replaces what is in the slot - a new
    /// game, the game's import, loading an older or imported save - and on request. They live beside the slot's
    /// history, not in it, so the list of saves only ever holds the campaign's own saves.
    /// </summary>
    /// <remarks>
    /// A backup made when another campaign takes the slot is that campaign's whole history, so it can be brought
    /// back with every save. Restoring a backup swaps it with what the slot holds, so the count does not grow.
    /// </remarks>
    internal static class Backups
    {
        public static int Limit => Settings.BackupsPerSlot;

        /// <summary>The slot's backups, newest first.</summary>
        public static List<Backup> Of(int slot)
        {
            var list = new List<Backup>();
            var dir = Store.BackupsFolder(slot);
            if (!Directory.Exists(dir)) return list;
            foreach (var folder in Directory.GetDirectories(dir))
            {
                if (folder.EndsWith(".partial", StringComparison.Ordinal)) continue;
                var saves = Store.List(folder);
                if (saves.Count == 0) continue;
                list.Add(new Backup { Folder = folder, Info = Store.Campaign(folder), Saves = saves });
            }
            return list.OrderByDescending(b => b.MadeUtc).ToList();
        }

        public static bool Full(int slot) => Of(slot).Count >= Limit;

        /// <summary>
        /// Does replacing what is in the slot need a backup first? Only if there is something Paper Trail does not
        /// already hold: a history, or a save in the slot that is none of its snapshots.
        /// </summary>
        public static bool NeededBeforeReplacing(int slot) => Store.List(slot).Count > 0 || !Store.SlotIsKept(slot);

        /// <summary>
        /// The campaign in the slot, as one backup: its history, plus its current save when that is not already one
        /// of the snapshots. Used before another campaign takes the slot.
        /// </summary>
        public static Backup BackUpCampaign(int slot, string reason)
        {
            lock (Work.Disk)
            {
                var info = Store.Campaign(slot);
                var game = GameSave(slot);
                if (game != null && !Store.SlotIsKept(slot))
                    Store.Take(slot, LastSave(slot, game, info));
                if (Store.List(slot).Count == 0) return null;
                if (info.Organisation.Length == 0 && game != null) info.Organisation = game.OrganisationName ?? "";
                var folder = Store.MoveHistoryLocked(slot, info, reason);
                Mod.Log.Msg($"slot {slot}: backed up {info.Organisation} ({reason})");
                return Read(folder);
            }
        }

        /// <summary>The slot's current save alone, as one backup. Used before an older save of the same campaign replaces it.</summary>
        public static Backup BackUpSave(int slot, string reason)
        {
            lock (Work.Disk)
            {
                var game = GameSave(slot);
                if (game == null) return null;
                var info = Store.Campaign(slot);
                string folder = Path.Combine(Store.BackupsFolder(slot), $"{DateTime.Now:yyyyMMdd-HHmmss}-save");
                Store.TakeInto(slot, LastSave(slot, game, info), folder);
                var copy = new CampaignInfo
                {
                    Slot = slot,
                    Organisation = game.OrganisationName ?? info.Organisation,
                    SaveCreatedTicks = info.SaveCreatedTicks,
                    PlaySeconds = info.PlaySeconds,
                    ArchivedUtc = DateTime.UtcNow,
                    BackupReason = reason,
                };
                Store.SaveCampaign(folder, copy);
                Mod.Log.Msg($"slot {slot}: backed up its current save ({reason})");
                return Read(folder);
            }
        }

        /// <summary>
        /// Puts a backup back in its slot. What the slot holds becomes a backup in its place: the whole campaign when
        /// the backup is another campaign, or just the current save when it is the same one. The game's save is then
        /// the backup's newest save.
        /// </summary>
        public static void Restore(Backup backup, int slot)
        {
            lock (Work.Disk)
            {
                var current = Store.Campaign(slot);
                bool sameCampaign = current.SaveCreatedTicks != 0 && current.SaveCreatedTicks == backup.Info.SaveCreatedTicks;
                SnapshotInfo restore;
                if (sameCampaign)
                {
                    if (!Store.SlotIsKept(slot)) BackUpSave(slot, "Before restoring a backup");
                    // The restored save joins the history again; the rest of the backup has served its purpose.
                    var save = backup.Newest;
                    var target = Path.Combine(Store.SlotFolder(slot), Path.GetFileName(save.Folder));
                    if (!Directory.Exists(target)) Directory.Move(save.Folder, target);
                    save.Folder = target;
                    Delete(backup);
                    restore = save;
                }
                else
                {
                    if (NeededBeforeReplacing(slot)) BackUpCampaign(slot, $"Before restoring {backup.Info.Organisation}");
                    Store.MoveToSlotLocked(backup.Folder, slot);
                    restore = Store.List(slot).First();
                }
                Store.Restore(slot, restore);
                var campaign = Store.Campaign(slot);
                campaign.PlaySeconds = restore.PlaySeconds;
                Store.SaveCampaign(slot, campaign);
            }
        }

        /// <summary>Removes a backup, both copies.</summary>
        public static void Delete(Backup backup)
        {
            lock (Work.Disk)
            {
                foreach (var save in Store.List(backup.Folder)) Store.Delete(save);
                if (Directory.Exists(backup.Folder)) Directory.Delete(backup.Folder, true);
            }
        }

        /// <summary>A backup as one zip, for keeping somewhere else.</summary>
        public static void Export(Backup backup, string zipPath)
        {
            lock (Work.Disk)
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(backup.Folder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            }
        }

        private static Backup Read(string folder)
            => new Backup { Folder = folder, Info = Store.Campaign(folder), Saves = Store.List(folder) };

        private static SaveInfo GameSave(int slot)
        {
            try
            {
                var games = LoadManager.SaveGames;
                var info = games != null && slot - 1 < games.Length ? games[slot - 1] : null;
                return info != null && Directory.Exists(Store.GameSlotFolder(slot)) ? info : null;
            }
            catch { return null; }
        }

        /// <summary>The details of the save in the slot now, from the snapshot it matches or the game's own save info.</summary>
        private static SnapshotInfo LastSave(int slot, SaveInfo game, CampaignInfo campaign)
        {
            var save = new SnapshotInfo
            {
                Kind = SaveKind.BeforeRestore,
                Pinned = true,
                Location = "Last save",
                Organisation = game.OrganisationName ?? "",
                NetWorth = game.Networth,
                PlaySeconds = campaign.PlaySeconds,
                GameVersion = UnityEngine.Application.version,
            };
            var same = Store.List(slot).Take(5).FirstOrDefault(s => Store.SlotMatches(slot, s));
            if (same != null)
            {
                save.Location = same.Location;
                save.GameDay = same.GameDay;
                save.GameTime = same.GameTime;
                save.NetWorth = same.NetWorth;
                save.Cash = same.Cash;
                save.Rank = same.Rank;
                save.PlaySeconds = same.PlaySeconds;
            }
            return save;
        }
    }
}
