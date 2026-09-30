using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;

[assembly: MelonInfo(typeof(PaperTrail.Mod), "Paper Trail", "0.1.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace PaperTrail
{
    public sealed class Mod : MelonMod
    {
        public static MelonLogger.Instance Log { get; private set; }

        /// <summary>In-game minutes between auto-saves: 2 hours.</summary>
        private const int AutoSaveEveryMinutes = 120;

        // What the save now starting is: set just before a save by whoever asked for it.
        private static SaveKind? _pendingKind;
        private static SaveKind _currentKind = SaveKind.Manual;

        private bool _hooked;
        private int _slot;                       // 1-5 while a game is loaded, 0 otherwise
        private CampaignInfo _campaign;
        private int _lastClockMinute = -1;
        private int _minutesSinceSave;
        private float _lastBlockedLog;
        private float _lastPlayTimeStored;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(SleepController), "RpcLogic___StartSleep_2166136261"),
                    prefix: new HarmonyMethod(typeof(Mod), nameof(BeforeSleepSave)));
            }
            catch (Exception e) { Log.Warning("sleep saves will show as manual saves: " + e.Message); }
        }

        private static void BeforeSleepSave() => _pendingKind = SaveKind.Sleep;

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            HookSaveManager();
            if (sceneName == "Menu") LeaveGame();
        }

        private void HookSaveManager()
        {
            if (_hooked || !Singleton<SaveManager>.InstanceExists) return;
            var sm = Singleton<SaveManager>.Instance;
            sm.onSaveStart.AddListener((UnityAction)new Action(OnSaveStart));
            sm.onSaveComplete.AddListener((UnityAction)new Action(OnSaveComplete));
            Store.SavesRoot = sm.IndividualSavesContainerPath;
            _hooked = true;
            Store.MigrateOldFolder();
            Log.Msg($"snapshots are kept in {Store.Root}");
        }

        // ---------------------------------------------------------------- the loaded game

        private bool InGame(out LoadManager lm)
        {
            lm = Singleton<LoadManager>.InstanceExists ? Singleton<LoadManager>.Instance : null;
            return lm != null && lm.IsGameLoaded && !lm.IsLoading && lm.IsInGameScene
                && !string.IsNullOrEmpty(lm.LoadedGameFolderPath) && lm.ActiveSaveInfo != null;
        }

        private void EnterGame(LoadManager lm)
        {
            if (string.IsNullOrEmpty(Store.SavesRoot)) HookSaveManager();
            _slot = lm.ActiveSaveInfo.SaveSlotNumber;
            if (!string.Equals(Path.GetFullPath(lm.LoadedGameFolderPath).TrimEnd('\\', '/'),
                               Path.GetFullPath(Store.GameSlotFolder(_slot)), StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning($"the loaded game is not in save slot {_slot}'s folder ({lm.LoadedGameFolderPath}); Paper Trail stays off");
                _slot = -1;
                return;
            }
            _campaign = Store.Campaign(_slot);
            _minutesSinceSave = 0;
            _lastClockMinute = -1;
            Log.Msg($"campaign: slot {_slot}, {lm.ActiveSaveInfo.OrganisationName} - {Store.PlayTime(_campaign.PlaySeconds)} played so far");
        }

        private void LeaveGame()
        {
            if (_slot > 0 && _campaign != null)
                try { Store.SaveCampaign(_slot, _campaign); } catch (Exception e) { Log.Warning("could not store play time: " + e.Message); }
            _slot = 0;
            _campaign = null;
        }

        public override void OnApplicationQuit() => LeaveGame();

        public override void OnUpdate()
        {
            if (!InGame(out var lm)) { if (_slot != 0 && (lm == null || !lm.IsGameLoaded)) LeaveGame(); return; }
            if (_slot == 0) EnterGame(lm);
            if (_slot < 0) return;

            bool paused = Singleton<PauseMenu>.InstanceExists && Singleton<PauseMenu>.Instance.IsPaused;
            if (!paused) _campaign.PlaySeconds += Time.unscaledDeltaTime;
            // Stored every minute as well as with each snapshot: a game that is closed abruptly never
            // reaches OnApplicationQuit.
            if (Time.realtimeSinceStartup - _lastPlayTimeStored > 60f)
            {
                _lastPlayTimeStored = Time.realtimeSinceStartup;
                try { Store.SaveCampaign(_slot, _campaign); } catch (Exception e) { Log.Warning("could not store play time: " + e.Message); }
            }

            CountGameMinutes();
            if (_minutesSinceSave >= AutoSaveEveryMinutes) TryAutoSave();
        }

        /// <summary>In-game minutes since the last save of any kind, from the clock's hhmm.</summary>
        private void CountGameMinutes()
        {
            if (!NetworkSingleton<TimeManager>.InstanceExists) return;
            int now = NetworkSingleton<TimeManager>.Instance.CurrentTime;
            int minute = now / 100 * 60 + now % 100;
            if (_lastClockMinute >= 0)
            {
                int delta = (minute - _lastClockMinute + 1440) % 1440;
                // A jump of hours is a time skip (sleeping), and sleeping saves anyway.
                if (delta <= 30) _minutesSinceSave += delta;
            }
            _lastClockMinute = minute;
        }

        // ---------------------------------------------------------------- auto-save

        private void TryAutoSave()
        {
            string blocked = WhyNotNow();
            if (blocked != null)
            {
                if (Time.realtimeSinceStartup - _lastBlockedLog > 60f)
                {
                    _lastBlockedLog = Time.realtimeSinceStartup;
                    Log.Msg($"auto-save due, waiting: {blocked}");
                }
                return;
            }
            Log.Msg($"auto-saving (AutoSave {_campaign.NextAutoNumber})");
            _pendingKind = SaveKind.Auto;
            _minutesSinceSave = 0;
            Singleton<SaveManager>.Instance.Save();
        }

        /// <summary>Why this is not a safe moment to save, or null when it is.</summary>
        private static string WhyNotNow()
        {
            if (!InstanceFinder.IsServer) return "only the host saves";
            if (NetworkSingleton<GameManager>.InstanceExists && NetworkSingleton<GameManager>.Instance.IsTutorial) return "tutorial";
            if (Singleton<SaveManager>.Instance.IsSaving) return "a save is already running";
            var player = Player.Local;
            if (player == null) return "no player yet";
            if (player.IsSleeping) return "sleeping";
            if (player.IsArrested) return "arrested";
            if (player.IsUnconscious) return "knocked out";
            if (player.IsInVehicle) return "in a vehicle";
            if (player.CrimeData != null && player.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None) return "police pursuit";
            if (Singleton<PauseMenu>.InstanceExists && Singleton<PauseMenu>.Instance.IsPaused) return "paused";
            if (PlayerSingleton<PlayerCamera>.InstanceExists && PlayerSingleton<PlayerCamera>.Instance.ActiveUIElementCount > 0) return "a menu or dialogue is open";
            return null;
        }

        // ---------------------------------------------------------------- snapshots after every save

        private static void OnSaveStart()
        {
            _currentKind = _pendingKind ?? SaveKind.Manual;
            _pendingKind = null;
        }

        private void OnSaveComplete()
        {
            if (_slot <= 0 || _campaign == null) return;
            _minutesSinceSave = 0;
            var info = Describe(_currentKind);
            info.SaveHadErrors = SaveManager.SaveError;
            MelonCoroutines.Start(SnapshotSoon(info));
        }

        private SnapshotInfo Describe(SaveKind kind)
        {
            var info = new SnapshotInfo
            {
                Kind = kind,
                PlaySeconds = _campaign.PlaySeconds,
                GameVersion = Application.version,
            };
            try
            {
                var lm = Singleton<LoadManager>.Instance;
                info.Organisation = lm.ActiveSaveInfo?.OrganisationName ?? "";
                var player = Player.Local;
                if (player != null)
                    info.Location = player.CurrentProperty != null ? player.CurrentProperty.PropertyName : Spaced(player.CurrentRegion.ToString());
                if (NetworkSingleton<TimeManager>.InstanceExists)
                {
                    var tm = NetworkSingleton<TimeManager>.Instance;
                    info.GameDay = tm.ElapsedDays + 1;
                    info.GameTime = tm.CurrentTime;
                }
                if (NetworkSingleton<MoneyManager>.InstanceExists)
                {
                    var mm = NetworkSingleton<MoneyManager>.Instance;
                    info.Cash = mm.cashBalance;
                    info.Online = mm.onlineBalance;
                    info.NetWorth = mm.GetNetWorth();
                }
                if (NetworkSingleton<LevelManager>.InstanceExists)
                {
                    var lv = NetworkSingleton<LevelManager>.Instance;
                    info.Rank = $"{Spaced(lv.Rank.ToString())} {Roman(lv.Tier)}";
                }
            }
            catch (Exception e) { Log.Warning("some save details could not be read: " + e.Message); }
            if (string.IsNullOrEmpty(info.Location)) info.Location = "Unknown";
            return info;
        }

        /// <summary>
        /// The copy is taken a moment after the game's save completes: other mods write their own files into
        /// the slot around the same time, and the snapshot should include them.
        /// </summary>
        private IEnumerator SnapshotSoon(SnapshotInfo info)
        {
            int slot = _slot;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until || Singleton<SaveManager>.Instance.IsSaving) yield return null;
            try
            {
                if (info.Kind == SaveKind.Auto) info.AutoNumber = _campaign.NextAutoNumber++;
                Store.Take(slot, info);
                Store.SaveCampaign(slot, _campaign);
                int pruned = Store.Prune(slot);
                Log.Msg($"snapshot: {Store.Describe(info)}{(info.SaveHadErrors ? " (the game reported errors while saving)" : "")}"
                      + (pruned > 0 ? $" - removed {pruned} old auto-save(s)" : ""));
            }
            catch (Exception e) { Log.Error("snapshot failed: " + e); }
        }

        // ---------------------------------------------------------------- text

        private static string Spaced(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(name[i] == '_' ? ' ' : name[i]);
            }
            return sb.ToString();
        }

        private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString() };
    }
}
