using MelonLoader;
using Melange.Core;

[assembly: MelonInfo(typeof(Melange.Mixers.Mod), "Melange Mixers", "0.1.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]
// MelonLoader matches dependencies by assembly name, not by the mod's display name.
[assembly: MelonAdditionalDependencies("MelangeCore")]

namespace Melange.Mixers
{
    /// <summary>
    /// Mixing stations that take a product and two, three or four mixer ingredients and mix them in series, in slot order: the
    /// result is exactly that many Mixing Station Mk2 passes, but only the final product is made. Unlocked at Underlord I,
    /// Baron I and Kingpin I, sold by Oscar at the warehouse, run by chemists like any mixing station.
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
                Log.Error($"Melange Mixers {problem}; staying off.");
                return;
            }
            Active = true;
            Settings.Create();
            Machines.Start();
            Stations.Patch(HarmonyInstance);
            Events.Subscribe<SaveLoaded>(_ => Stations.AfterLoad());
            Events.Subscribe<MenuLoaded>(_ => { Stations.Reset(); StationUi.Reset(); Looks.Reset(); });
        }

        public override void OnLateInitializeMelon()
        {
            if (!Active) return;
            S1API.Lifecycle.GameLifecycle.OnPreLoad += Machines.Register;
            S1API.Lifecycle.GameLifecycle.OnLoadComplete += Machines.Stock;
            S1API.Lifecycle.GameLifecycle.OnSaveStart += Stations.BeforeSave;
        }

        public override void OnUpdate()
        {
            if (Active) Stations.Tick();
        }

        // after every Update: the station screen's own Update rewrites its labels each frame, and ours must be what shows
        public override void OnLateUpdate()
        {
            if (Active) StationUi.LateTick();
        }
    }
}
