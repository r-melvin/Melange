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
        private static MelonPreferences_Entry<bool> _continueOnStartup, _autoSaveEnabled;
        private static MelonPreferences_Entry<int> _everyHours, _kept;

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
        }

        public static bool ContinueOnStartup => _continueOnStartup?.Value ?? false;
        public static bool AutoSaveEnabled => _autoSaveEnabled?.Value ?? true;
        public static int AutoSaveEveryMinutes => (_everyHours?.Value ?? 2) * 60;
        public static int AutoSavesKept => _kept?.Value ?? 10;
    }
}
