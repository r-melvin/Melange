using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

[assembly: MelonInfo(typeof(PaperTrail.Mod), "Paper Trail", "0.1.2", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace PaperTrail
{
    public sealed class Mod : MelonMod
    {
        public static MelonLogger.Instance Log { get; private set; }
        public static Mod Instance { get; private set; }

        /// <summary>Raised on the main thread once a save's snapshot is on disk.</summary>
        public static event Action<SnapshotInfo> SnapshotTaken;

        // What the save now starting is: set just before a save by whoever asked for it.
        private static SaveKind? _pendingKind;
        private static SaveKind _currentKind = SaveKind.Manual;
        private static string _pendingNote, _currentNote;
        private static SnapshotInfo _pendingReplace, _currentReplace;

        // True from the moment a load is chosen until the next game has started: nothing is saved or snapshotted
        // in between, so the old game can never write over the save that is about to be loaded.
        private static bool _switching;

        private static bool _startupHandled;
        private static string _pendingReason, _currentReason;
        private float _enteredAt;
        private float _lastSnapshotAt;

        /// <summary>True once a loaded game has been running a few seconds: load-time events are not key moments.</summary>
        public bool PastLoad => _slot > 0 && Time.realtimeSinceStartup - _enteredAt > 25f;
        private bool _hooked;
        private int _slot;                       // 1-5 while a game is loaded, 0 otherwise
        private CampaignInfo _campaign;
        private int _lastClockMinute = -1;
        private float _nextClockCheck;
        private int _minutesSinceSave;
        private float _lastBlockedLog;
        private float _lastPlayTimeStored;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Instance = this;
            Settings.Register();
            Milestones.PatchGameEvents(HarmonyInstance);
            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(SleepController), "RpcLogic___StartSleep_2166136261"),
                    prefix: new HarmonyMethod(typeof(Mod), nameof(BeforeSleepSave)));
            }
            catch (Exception e) { Log.Warning("sleep saves will show as manual saves: " + e.Message); }
            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(SavePoint), nameof(SavePoint.Interacted)),
                    prefix: new HarmonyMethod(typeof(Mod), nameof(SavePointInteracted)));
            }
            catch (Exception e) { Log.Warning("the safehouse save button keeps saving straight away: " + e.Message); }
        }

        /// <summary>The safehouse "Save game" button opens the save screen instead of saving on the spot.</summary>
        /// <summary>How many times the safehouse save point was used this session (for the dev tests).</summary>
        public static int SavePointPresses;

        private static bool SavePointInteracted()
        {
            SavePointPresses++;
            // Inside the game's wait the intercom shows "Wait Ns" and does nothing; its own code is left to do that.
            if (!InstanceFinder.IsServer || Singleton<SaveManager>.Instance.IsSaving || Instance._slot <= 0 || CooldownLeft() > 0f) return true;
            SaveScreen.Open(SaveScreen.Mode.Save);
            return false;
        }

        /// <summary>The game's own wait between saves, from SavePoint: the intercom refuses for this long after any save or load.</summary>
        public const float SaveCooldown = 60f;

        /// <summary>
        /// Seconds left of that wait, counted by the game itself: in real time, so it runs with the pause menu open,
        /// and from zero again each time a save is made or a game finishes loading.
        /// </summary>
        public static float CooldownLeft()
        {
            if (!Singleton<SaveManager>.InstanceExists) return 0f;
            return Mathf.Max(0f, SaveCooldown - Singleton<SaveManager>.Instance.SecondsSinceLastSave);
        }

        /// <summary>The save slot of the game being played, or 0 at the menu.</summary>
        public int CurrentSlot => Math.Max(_slot, 0);

        /// <summary>
        /// A manual save from the save screen: optionally named, optionally replacing an existing save (which
        /// keeps its pin).
        /// </summary>
        public bool RequestManualSave(string note, SnapshotInfo replace)
        {
            string blocked = WhyNotNow(manual: true);
            if (blocked != null) { Log.Msg("can't save now: " + blocked); return false; }
            if (replace == null && Store.List(_slot).Count(x => x.Kind == SaveKind.Manual) >= Settings.ManualSavesKept)
            { Log.Msg($"can't make another manual save: {Settings.ManualSavesKept} is the limit"); return false; }
            if (CooldownLeft() > 0f) { Log.Msg($"can't save for another {Mathf.CeilToInt(CooldownLeft())}s (the game's wait between saves)"); return false; }
            _pendingKind = SaveKind.Manual;
            _pendingNote = note;
            _pendingReplace = replace;
            Singleton<SaveManager>.Instance.Save();
            return true;
        }

        /// <summary>
        /// Loads a campaign: the slot as last saved (snapshot null) or a snapshot of it. The game's loading screen
        /// comes up first, so nothing else is seen while the files are put in place; from inside a game it loads
        /// through the game's own exit-and-load, with the loading screen up the whole time.
        /// </summary>
        public static void LoadNow(int slot, SnapshotInfo snapshot, bool fromGame)
        {
            try
            {
                SaveScreen.CoverForLoading();
                Singleton<LoadingScreen>.Instance.Open(false);
                _switching = true;
                Instance.AbandonCampaign();
                var lm = Singleton<LoadManager>.Instance;
                if (snapshot == null) Safeguard(slot);
                if (snapshot != null)
                {
                    SnapshotInfo keep = null;
                    if (!Store.SlotIsKept(slot))
                        keep = new SnapshotInfo
                        {
                            Location = "Last save",
                            PlaySeconds = Store.Campaign(slot).PlaySeconds,
                            Organisation = snapshot.Organisation,
                            GameVersion = Application.version,
                        };
                    Store.Restore(slot, snapshot, keep);
                    var campaign = Store.Campaign(slot);
                    campaign.PlaySeconds = snapshot.PlaySeconds;      // play time goes back with the save
                    Store.SaveCampaign(slot, campaign);
                    Log.Msg($"restored slot {slot} to: {Store.Describe(snapshot)}");
                }
                lm.RefreshSaveInfo();
                var info = LoadManager.SaveGames[slot - 1];
                if (info == null) { Log.Error($"slot {slot} has no save to load"); _switching = false; Singleton<LoadingScreen>.Instance.Close(); return; }
                // From inside a game, the game's own way: its death screen reloads the last save with exactly this call.
                // The loading screen stays up throughout, and the running game is cleaned up properly first - time
                // unfrozen, the mouse freed, the network stopped. Loading straight over it left the clock frozen.
                if (fromGame) lm.ExitToMenu(info, null, false);
                else lm.StartGame(info, false, true);
            }
            catch (Exception e)
            {
                Log.Error("load failed: " + e);
                _switching = false;
                try { Singleton<LoadingScreen>.Instance.Close(); } catch { }
            }
        }

        /// <summary>The mods installed right now, as "Name vX", sorted.</summary>
        private static List<string> CurrentMods()
            => MelonBase.RegisteredMelons.Where(m => m?.Info != null && m.Info.Name != "Paper Trail")
                                         .Select(m => $"{m.Info.Name} v{m.Info.Version}").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        private static string Delta(List<string> before, List<string> now)
        {
            string Name(string s) { int i = s.LastIndexOf(" v", StringComparison.Ordinal); return i > 0 ? s.Substring(0, i) : s; }
            var was = before.ToDictionary(Name, x => x, StringComparer.OrdinalIgnoreCase);
            var has = now.ToDictionary(Name, x => x, StringComparer.OrdinalIgnoreCase);
            var parts = new List<string>();
            parts.AddRange(has.Keys.Where(k => !was.ContainsKey(k)).Take(2).Select(k => "+" + k));
            parts.AddRange(was.Keys.Where(k => !has.ContainsKey(k)).Take(2).Select(k => "-" + k));
            int updated = has.Keys.Count(k => was.ContainsKey(k) && was[k] != has[k]);
            if (updated > 0) parts.Add($"{updated} updated");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Before a campaign is loaded by a newer game or a different set of mods, keeps what is on disk - the one
        /// moment a save's contents can change under it. Nothing is kept when the slot already matches a snapshot.
        /// </summary>
        private static void Safeguard(int slot)
        {
            try
            {
                if (Store.SlotIsKept(slot)) return;
                LoadManager.SaveGames = LoadManager.SaveGames;       // (the info read at the menu)
                var info = LoadManager.SaveGames[slot - 1];
                string why = null;
                if (Settings.BeforeUpdate && info != null
                    && SaveManager.GetVersionNumber(info.SaveVersion) < SaveManager.GetVersionNumber(Application.version))
                    why = $"Game updated {info.SaveVersion} to {Application.version}";
                var campaign = Store.Campaign(slot);
                var mods = CurrentMods();
                if (why == null && Settings.BeforeModChange && campaign.Mods.Count > 0 && !campaign.Mods.SequenceEqual(mods))
                {
                    string delta = Delta(campaign.Mods, mods);
                    if (delta.Length > 0) why = "Mods changed (" + delta + ")";
                }
                if (why == null) return;
                Store.Take(slot, new SnapshotInfo
                {
                    Kind = SaveKind.Safeguard,
                    Pinned = true,
                    Reason = why,
                    Location = "Before loading",
                    PlaySeconds = campaign.PlaySeconds,
                    Organisation = info?.OrganisationName ?? "",
                    GameVersion = info?.SaveVersion ?? "",
                });
                Log.Msg($"kept a copy before loading: {why}");
            }
            catch (Exception e) { Log.Warning("could not keep a copy before loading: " + e.Message); }
        }

        /// <summary>Forgets the running campaign without storing it: a load is about to replace its files.</summary>
        private void AbandonCampaign()
        {
            _slot = 0;
            _campaign = null;
        }

        /// <summary>Loads the most recently played save once the menu is up. Holding Shift skips it.</summary>
        private IEnumerator ContinueLastGame()
        {
            float deadline = Time.realtimeSinceStartup + 40f;
            // The menu needs a moment: its save list is read, and Paper Trail's screen pieces are captured.
            float settle = Time.realtimeSinceStartup + 2.5f;
            while (Time.realtimeSinceStartup < settle) { if (SkipKeyHeld()) break; yield return null; }
            while (Time.realtimeSinceStartup < deadline
                   && (!Singleton<LoadManager>.InstanceExists || Singleton<LoadManager>.Instance.IsLoading
                       || LoadManager.LastPlayedGame == null))
                yield return null;
            if (SkipKeyHeld()) { Log.Msg("start-up continue skipped (Shift held)"); yield break; }
            var last = LoadManager.LastPlayedGame;
            if (last == null) { Log.Msg("start-up continue: no saved game to continue"); yield break; }
            Log.Msg($"start-up continue: loading {last.OrganisationName} (slot {last.SaveSlotNumber}) - hold Shift at start-up to skip");
            LoadNow(last.SaveSlotNumber, null, false);
        }

        private static bool SkipKeyHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);


        private static void BeforeSleepSave() => _pendingKind = SaveKind.Sleep;

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            HookSaveManager();
            SaveScreen.CloseIfOpen();
            if (sceneName == "Menu")
            {
                LeaveGame();
                if (!_startupHandled)
                {
                    _startupHandled = true;           // only the first menu of a session: going back to it stays there
                    if (Settings.ContinueOnStartup) MelonCoroutines.Start(ContinueLastGame());
                }
            }
            MelonCoroutines.Start(Hooks.AttachWhenReady(sceneName));
#if PT_DEV
            if (DevInspect.On) MelonCoroutines.Start(DevInspect.Run(sceneName));
#endif
        }

        private void HookSaveManager()
        {
            if (_hooked || !Singleton<SaveManager>.InstanceExists) return;
            var sm = Singleton<SaveManager>.Instance;
            sm.onSaveStart.AddListener((UnityAction)new Action(OnSaveStart));
            sm.onSaveComplete.AddListener((UnityAction)new Action(OnSaveComplete));
            Store.SavesRoot = sm.IndividualSavesContainerPath;
            _hooked = true;
            Store.MigrateFolders();
            Work.Run(() =>
            {
                try
                {
                    int n = Store.CompressOldSnapshots();
                    if (n > 0) Log.Msg($"compressed {n} older snapshot(s) to zip");
                }
                catch (Exception e) { Log.Warning("could not compress older snapshots: " + e.Message); }
                Mirror.Request();
            });
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
            _campaign.Mods = CurrentMods();
            _enteredAt = Time.realtimeSinceStartup;
            _switching = false;
            Milestones.Subscribe();
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

        public override void OnApplicationQuit()
        {
            LeaveGame();
            Mirror.Now();
        }

        public override void OnUpdate()
        {
            SaveScreen.Tick();
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

            if (Time.unscaledTime >= _nextClockCheck) { _nextClockCheck = Time.unscaledTime + 0.5f; CountGameMinutes(); }
            if (Milestones.Pending) TryKeyMomentSave();
            else if (Settings.AutoSaveEnabled && _minutesSinceSave >= Settings.AutoSaveEveryMinutes) TryAutoSave();
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
            if (_switching) return;
            string blocked = WhyNotNow(manual: false);
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

        private void TryKeyMomentSave()
        {
            if (_switching) return;
            // A little gap after the last save, so a burst of events is one save; and a limit on the wait.
            if (Time.realtimeSinceStartup - _lastSnapshotAt < 20f) return;
            string blocked = WhyNotNow(manual: false);
            if (blocked != null)
            {
                if (Milestones.WaitingFor > 600f) { Log.Msg("key moment dropped: no safe moment in ten minutes"); Milestones.Clear(); }
                return;
            }
            string reason = Milestones.Take();
            Log.Msg($"saving for a key moment: {reason}");
            _pendingKind = SaveKind.Milestone;
            _pendingReason = reason;
            _minutesSinceSave = 0;
            Singleton<SaveManager>.Instance.Save();
        }

        /// <summary>Why this is not a safe moment to save, or null when it is.</summary>
        private static string WhyNotNow(bool manual)
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
            if (manual) return null;   // saving from the save screen: the pause menu or the screen itself is open
            if (Singleton<PauseMenu>.InstanceExists && Singleton<PauseMenu>.Instance.IsPaused) return "paused";
            if (PlayerSingleton<PlayerCamera>.InstanceExists && PlayerSingleton<PlayerCamera>.Instance.ActiveUIElementCount > 0) return "a menu or dialogue is open";
            return null;
        }

        // ---------------------------------------------------------------- snapshots after every save

        private static void OnSaveStart()
        {
            _currentKind = _pendingKind ?? SaveKind.Manual;
            _currentNote = _pendingNote;
            _currentReason = _pendingReason;
            _pendingReason = null;
            _currentReplace = _pendingReplace;
            _pendingKind = null;
            _pendingNote = null;
            _pendingReplace = null;
        }

        private void OnSaveComplete()
        {
            if (_switching || _slot <= 0 || _campaign == null) return;
            _minutesSinceSave = 0;
            var info = Describe(_currentKind);
            info.SaveHadErrors = SaveManager.SaveError;
            info.Note = _currentNote ?? "";
            info.Reason = _currentReason ?? "";
            _currentReason = null;
            if (_currentReplace != null) info.Pinned = _currentReplace.Pinned;
            MelonCoroutines.Start(SnapshotSoon(info, _currentReplace));
            _currentReplace = null;
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
        private IEnumerator SnapshotSoon(SnapshotInfo info, SnapshotInfo replace)
        {
            int slot = _slot;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until || Singleton<SaveManager>.Instance.IsSaving) yield return null;
            if (_switching || _campaign == null || _slot != slot) yield break;      // a load replaced the files meanwhile
            if (info.Kind == SaveKind.Auto) info.AutoNumber = _campaign.NextAutoNumber++;
            // Copying, zipping and pruning are disk and CPU work: done off the game's thread.
            int pruned = 0;
            var done = new System.Threading.ManualResetEventSlim(false);
            Exception failure = null;
            Work.Run(() =>
            {
                try
                {
                    Store.Take(slot, info);
                    if (replace != null)
                        try { Store.Delete(replace); } catch (Exception e) { Log.Warning("could not remove the save it overwrote: " + e.Message); }
                    pruned = Store.Prune(slot);
                }
                catch (Exception e) { failure = e; }
                finally { done.Set(); }
            });
            while (!done.IsSet) yield return null;
            done.Dispose();
            if (failure != null) Log.Error("snapshot failed: " + failure);
            else
            {
                try
                {
                    if (_campaign != null && _slot == slot) Store.SaveCampaign(slot, _campaign);
                    _lastSnapshotAt = Time.realtimeSinceStartup;
                    if (info.Suspect) Log.Warning($"this save looks incomplete: {info.FileCount} files, {info.TotalBytes / 1024} KB - the save before it was larger. It is kept, and will not push older saves out.");
                    Log.Msg($"snapshot: {Store.Describe(info)}{(info.SaveHadErrors ? " (the game reported errors while saving)" : "")}"
                          + (pruned > 0 ? $" - removed {pruned} old auto-save(s)" : ""));
                }
                catch (Exception e) { Log.Error("snapshot failed: " + e); }
            }
            try { SnapshotTaken?.Invoke(info); } catch (Exception e) { Log.Warning("save screen refresh: " + e.Message); }
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
