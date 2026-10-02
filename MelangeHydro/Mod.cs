using MelonLoader;
using Melange.Core;

[assembly: MelonInfo(typeof(Melange.Hydro.Mod), "Melange Hydro", "0.1.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]
// MelonLoader matches dependencies by assembly name, not by the mod's display name.
[assembly: MelonAdditionalDependencies("MelangeCore")]

namespace Melange.Hydro
{
    /// <summary>
    /// Hydroponics and aeroponics for the late ranks, where the game unlocks nothing: Hydro Tray sections (Underlord III) with a
    /// slow reservoir and a grow medium that lasts, Aeroponic Tower sections (Baron III) that grow fastest, pass the bud cap and
    /// cure in place, the Grow N Juicer nutrient (Kingpin III) that makes Heavenly at harvest, a pump hosed to the tap, and
    /// training for the botanists who tend them. Every hole is a cloned Grow Tent saved under the tent's own ID, so removing
    /// the mod leaves Grow Tents with their plants.
    /// </summary>
    public sealed class Mod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static bool Active;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            if (!Core.Core.Require(new System.Version(0, 2, 0), out string problem))
            {
                Log.Error($"Melange Hydro {problem}; staying off.");
                return;
            }
            Active = true;
            Settings.Create();
            Items.Start();
            Holes.Patch(HarmonyInstance);
            Plants.Patch(HarmonyInstance);
            Clipboard.Patch(HarmonyInstance);
            Events.Subscribe<SaveLoaded>(_ => Holes.AfterLoad());
            Events.Subscribe<MenuLoaded>(_ =>
            {
                MelangeHydroData.Current?.ResetToDefaults();
                Holes.Reset(); Plants.Reset(); Grouping.Reset(); Courses.Reset(); Pumps.Reset(); Looks.Reset(); Clipboard.Reset();
            });
            Log.Msg($"placement: {Settings.Placement}");
        }

        public override void OnLateInitializeMelon()
        {
            if (!Active) return;
            S1API.Lifecycle.GameLifecycle.OnPreLoad += Items.Register;
            S1API.Lifecycle.GameLifecycle.OnLoadComplete += Items.Stock;
            S1API.Lifecycle.GameLifecycle.OnSaveStart += Holes.BeforeSave;
            S1API.Lifecycle.GameLifecycle.OnSaveComplete += Holes.AfterSave;
        }

        public override void OnUpdate()
        {
            if (!Active) return;
            Holes.Tick();
            Courses.Tick();
            Pumps.Tick();
        }
    }
}
