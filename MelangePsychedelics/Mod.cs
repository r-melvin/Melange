using MelonLoader;
using Melange.Core;

[assembly: MelonInfo(typeof(Melange.Psychedelics.Mod), "Melange Psychedelics", "0.1.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]
// MelonLoader matches dependencies by assembly name, not by the mod's display name.
[assembly: MelonAdditionalDependencies("MelangeCore")]

namespace Melange.Psychedelics
{
    /// <summary>
    /// Two products for existing shroom customers, Toad and LSD. Toad: catch toads at the pond (watch for the wildlife officer)
    /// or in the sewer, or buy them from Randy round the back at night; keep them fed in a terrarium so they breed; milk each
    /// once a day; cure the venom on a drying rack. LSD: ergot spores from Fungal Phil grown in mushroom beds, cooked with Ana
    /// Slughin's reagent at a chemistry station, and dosed onto blotter sheets whose design becomes a brand.
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
                Log.Error($"Melange Psychedelics {problem}; staying off.");
                return;
            }
            Active = true;
            Settings.Create();
            Products.Start();
            Items.Start();
            Ergot.Patch(HarmonyInstance);
            Drying.Patch(HarmonyInstance);

            Events.Subscribe<SaveLoaded>(_ => AfterLoad());
            Events.Subscribe<MenuLoaded>(_ => Leave());
            Events.Subscribe<DayPassed>(e => Terraria.OnDayPassed(e.Day));
            // the sewer story (from the sewer spoke, if installed); re-published after every load, so these are idempotent
            Events.Subscribe<SewerKingSpared>(_ => Story(d => d.KingSpared = true, "the Sewer King will teach toad care"));
            Events.Subscribe<SewerKingDefeated>(_ => Story(d => d.KingDefeated = true, "the sewer's toads are there the hard way"));
            Events.Subscribe<GoblinCalmed>(_ => Story(d => d.GoblinCalmed = true, "the goblin showed where the sewer toads hide"));
        }

        public override void OnLateInitializeMelon()
        {
            if (!Active) return;
            S1API.Lifecycle.GameLifecycle.OnPreLoad += PreLoad;
            S1API.Lifecycle.GameLifecycle.OnLoadComplete += LoadComplete;
        }

        /// <summary>Before the save loads: every item and product a save may hold must be registered (the game drops runtime items on a scene change).</summary>
        private static void PreLoad()
        {
            Items.Register();
            Ergot.Register();
            Products.Register();
            Items.RegisterRecipe();
        }

        /// <summary>Shops, discovery and the like need the loaded scene.</summary>
        private static void LoadComplete()
        {
            Products.AfterLoad();
            Ergot.Stock();
        }

        private static void AfterLoad()
        {
            var data = MelangePsychedelicsData.Current;
            if (data == null) { Log.Warning("no psychedelics save data (S1API saveable missing); toads and designs won't be kept"); return; }
            data.Normalise();
            Ana.AfterLoad();
            Log.Msg($"loaded: {data.Terrariums.Count} terrarium(s), {data.Designs.Designs.Count} design(s), {data.Batches.Batches.Count} batch(es), " +
                    $"sewer route {data.Route(Wild.SewerUnlocked())}");
        }

        private static void Leave()
        {
            MelangePsychedelicsData.Current?.ResetToDefaults();
            Terraria.Reset();
            Frames.Reset();
            Wild.Reset();
            WildlifeWarden.Forget();
            Stall.Reset();
            Looks.Reset();
        }

        private static void Story(System.Action<MelangePsychedelicsData> set, string what)
        {
            var data = MelangePsychedelicsData.Current;
            if (data == null) return;
            set(data);
            Log.Msg($"sewer story: {what}");
        }

        public override void OnUpdate()
        {
            if (!Active) return;
            Terraria.Tick();
            Frames.Tick();
            Wild.Tick();
            Stall.Tick();
        }
    }
}
