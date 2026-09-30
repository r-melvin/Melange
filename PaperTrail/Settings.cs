using MelonLoader;
using MelonLoader.Preferences;

namespace PaperTrail
{
    /// <summary>
    /// Paper Trail's settings, as MelonPreferences: they show in the Mod Manager phone app (which lists a mod's
    /// categories by its name without spaces) and can be edited in UserData/MelonPreferences.cfg without it.
    /// </summary>
    internal static class Settings
    {
        private static MelonPreferences_Entry<bool> _continueOnStartup, _autoSaveEnabled, _syncCloud;
        private static MelonPreferences_Entry<bool> _quest, _dealer, _property, _rank, _cartel, _customer, _region, _supplier;
        private static MelonPreferences_Entry<bool> _beforeUpdate, _beforeModChange;
        private static MelonPreferences_Entry<int> _everyHours, _kept, _keptMoments, _manualKept, _copiesKept;

        public static void Register()
        {
            var startup = MelonPreferences.CreateCategory("PaperTrail_01_Startup", "Paper Trail - Start-up");
            _continueOnStartup = startup.CreateEntry("ContinueLastGameOnStartup", false, "Continue last game on start-up",
                "Load straight into the most recently played save when the game starts, instead of waiting at the main menu. "
                + "Hold Shift while the game starts to skip it once.");

            var auto = MelonPreferences.CreateCategory("PaperTrail_02_AutoSave", "Paper Trail - Auto-save");
            _autoSaveEnabled = auto.CreateEntry("AutoSaveEnabled", true, "Auto-save",
                "Save automatically while you play, at a safe moment: not in a menu, dialogue, vehicle, police pursuit or while sleeping.");
            _everyHours = auto.CreateEntry("AutoSaveEveryGameHours", 2, "Auto-save every (in-game hours)",
                "How many in-game hours pass between auto-saves.", false, false, new ValueRange<int>(1, 12));
            _kept = auto.CreateEntry("AutoSavesKept", 10, "Auto-saves kept per campaign",
                "The newest this many auto-saves are kept; older ones are removed. Manual saves, sleep saves and pinned saves are never removed.",
                false, false, new ValueRange<int>(3, 50));

            var moments = MelonPreferences.CreateCategory("PaperTrail_03_KeyMoments", "Paper Trail - Key moments");
            _quest = moments.CreateEntry("SaveOnQuestCompleted", true, "Save when a quest is completed", "Saves at the next safe moment after a quest is completed.");
            _dealer = moments.CreateEntry("SaveOnDealerRecruited", true, "Save when a dealer is recruited", "Saves at the next safe moment after you recruit a dealer.");
            _property = moments.CreateEntry("SaveOnPropertyAcquired", true, "Save when a property or business is bought", "Saves at the next safe moment after you buy a property or business.");
            _region = moments.CreateEntry("SaveOnAreaUnlocked", true, "Save when a new area is unlocked", "Saves at the next safe moment after you unlock a new area of the map.");
            _supplier = moments.CreateEntry("SaveOnSupplierUnlocked", true, "Save when a supplier is unlocked", "Saves at the next safe moment after you unlock a supplier.");
            _rank = moments.CreateEntry("SaveOnRankUp", true, "Save when you rank up", "Saves at the next safe moment after you reach a new rank.");
            _cartel = moments.CreateEntry("SaveOnCartelChange", true, "Save when the cartel situation changes", "Saves when the cartel's status towards you changes - a truce, a war, a new phase.");
            _customer = moments.CreateEntry("SaveOnCustomerUnlocked", false, "Save when a customer is unlocked", "Off by default: unlocking customers happens often.");
            _keptMoments = moments.CreateEntry("KeyMomentSavesKept", 30, "Key-moment saves kept per campaign",
                "The newest this many key-moment saves are kept; older ones are removed. Pinned saves are never removed.",
                false, false, new ValueRange<int>(5, 100));

            var storage = MelonPreferences.CreateCategory("PaperTrail_05_Storage", "Paper Trail - Storage");
            _manualKept = storage.CreateEntry("ManualSavesKept", 30, "Manual saves kept per campaign",
                "When a campaign has this many manual saves, a new one is refused until you delete one or overwrite an existing save. Each save is a small zip (about 150 KB).",
                false, false, new ValueRange<int>(5, 200));
            _copiesKept = storage.CreateEntry("SafeguardCopiesKept", 10, "Safeguard copies kept per campaign",
                "The copies kept before a restore, a game update or a mod change. The newest this many are kept; pin one to keep it for good.",
                false, false, new ValueRange<int>(3, 50));

            var safe = MelonPreferences.CreateCategory("PaperTrail_04_Safeguards", "Paper Trail - Safeguards");
            _beforeUpdate = safe.CreateEntry("KeepCopyBeforeGameUpdate", true, "Keep a copy before a save moves to a newer game version",
                "When a save made on an older game version is about to be loaded by a newer one, its current state is kept first.");
            _beforeModChange = safe.CreateEntry("KeepCopyBeforeModChange", true, "Keep a copy when your mods have changed",
                "When you load a campaign after adding, removing or updating mods, its last state is kept first - mods change what a save contains.");
            _syncCloud = safe.CreateEntry("SyncSnapshotsToSteamCloud", true, "Sync snapshots with Steam Cloud",
                "Snapshots are always kept on this computer, in UserData/PaperTrail. On: a copy is also kept beside your saves, so Steam Cloud carries it "
                + "to your other computers, and a snapshot missing here is brought back from there. Off: this computer only. "
                + "Deleting a snapshot removes both copies.");
        }

        public static bool ContinueOnStartup => _continueOnStartup?.Value ?? false;
        public static bool AutoSaveEnabled => _autoSaveEnabled?.Value ?? true;
        public static int AutoSaveEveryMinutes => (_everyHours?.Value ?? 2) * 60;
        public static int AutoSavesKept => _kept?.Value ?? 10;
        public static int ManualSavesKept => _manualKept?.Value ?? 30;
        public static int KeptCopiesKept => _copiesKept?.Value ?? 10;
        public static int KeyMomentSavesKept => _keptMoments?.Value ?? 30;
        public static bool OnQuest => _quest?.Value ?? true;
        public static bool OnDealer => _dealer?.Value ?? true;
        public static bool OnProperty => _property?.Value ?? true;
        public static bool OnRegion => _region?.Value ?? true;
        public static bool OnSupplier => _supplier?.Value ?? true;
        public static bool OnRank => _rank?.Value ?? true;
        public static bool OnCartel => _cartel?.Value ?? true;
        public static bool OnCustomer => _customer?.Value ?? false;
        public static bool BeforeUpdate => _beforeUpdate?.Value ?? true;
        public static bool BeforeModChange => _beforeModChange?.Value ?? true;
        public static bool SyncToSteamCloud => _syncCloud?.Value ?? true;
    }
}
