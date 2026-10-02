using System;
using S1API.Items;

using S1API.Stations;
using UnityEngine;

namespace Melange.Psychedelics
{
    /// <summary>
    /// The spoke's plain items, clones of game items so they keep working stored-item and held visuals (the donor's look until
    /// the art pass): live toads (quality = origin, see LiveToads), crickets and the toad net (Randy's), Ana's reagent and blank
    /// sheets, the LSD solution the chemistry station makes, and the two placeables, the terrarium and the blotter frame, both
    /// clones of the small storage rack so the game saves, networks and lets handlers reach their slots.
    /// </summary>
    internal static class Items
    {
        // donors: the game's own items (IDs checked in code where the game compares them, or in other Melange spokes)
        private const string ToadDonor = "cocaleaf", SupplyDonor = "banana", ReagentDonor = "acid", SolutionDonor = "liquidmeth", RackDonor = "smallstoragerack";

        public const float TerrariumPrice = 350f, FramePrice = 220f, CricketsPrice = 6f, NetPrice = 80f, ReagentPrice = 120f, SheetPrice = 15f, ToadValue = 60f;

        /// <summary>The rank-up screen lists the late-game ergot spores at their rank.</summary>
        public static void Start()
        {
            Core.RankUpScreen.Register(Settings.SporesRank, 1, "Ergot spores (Fungal Phil)", () => Il2CppScheduleOne.Registry.GetItem(Ids.ErgotSpores)?.Icon);
        }

        /// <summary>Before each load (the game drops runtime items on a scene change, and a save's items need them).</summary>
        public static void Register()
        {
            Quality(Ids.LiveToad, ToadDonor, null, "Toad", "A fat, warty toad. Release it into a terrarium; its grade says where it came from.", ToadValue, 10);
            Plain(Ids.Crickets, SupplyDonor, "Crickets", "A tub of live crickets: a day's food for one toad. Put tubs in a terrarium.", CricketsPrice, 40, ItemCategory.Consumable);
            Plain(Ids.ToadNet, SupplyDonor, "Toad Net", "A soft net. You need one in your pockets to catch toads.", NetPrice, 1, ItemCategory.Tools);
            Plain(Ids.Reagent, ReagentDonor, "Reagent", "Ana Slughin's reagent, for the chemistry station.", ReagentPrice, 10, ItemCategory.Ingredient, keepStationItem: true);
            Plain(Ids.BlankSheet, SupplyDonor, "Blank Blotter Sheet", "Perforated, twenty tabs to a sheet. Load it into a blotter frame.", SheetPrice, 20, ItemCategory.Packaging);
            Quality(Ids.LsdSolution, SolutionDonor, "cocaleaf", "LSD Solution", "One vial doses one blotter sheet at a blotter frame.", 300f, 10);
            Rack(Ids.Terrarium, "Terrarium", "A glass tank for toads. Put toads and crickets in; milk the toads once a day.", TerrariumPrice);
            Rack(Ids.BlotterFrame, "Blotter Frame", "Holds blank sheets and LSD solution, and doses sheets with your chosen design.", FramePrice);
        }

        private static void Quality(string id, string donor, string fallback, string name, string description, float price, int stack)
        {
            try
            {
                if (Kept.Restore(id)) return;
                string from = Il2CppScheduleOne.Registry.GetItem(donor)?.TryCast<Il2CppScheduleOne.ItemFramework.QualityItemDefinition>() != null ? donor : fallback;
                if (from == null) { Mod.Log.Warning($"{name}: donor '{donor}' is not a quality item; not registered"); return; }
                S1API.Items.Quality.QualityItemCreator.CloneFrom(from)
                    .WithBasicInfo(id, name, description, ItemCategory.Ingredient)
                    .WithPricing(price, 0.5f)
                    .WithStackLimit(stack)
                    .WithLegalStatus(S1API.Items.LegalStatus.Illegal)
                    .WithoutStationItem()
                    .Build();
                Kept.Keep(id);
            }
            catch (Exception e) { Mod.Log.Warning($"{name}: could not register: {e.Message}"); }
        }

        private static void Plain(string id, string donor, string name, string description, float price, int stack, ItemCategory category, bool keepStationItem = false)
        {
            try
            {
                if (Kept.Restore(id)) return;
                var b = S1API.Items.Storable.ItemCreator.CloneFrom(donor)
                    .WithBasicInfo(id, name, description, category)
                    .WithPricing(price, 0.5f)
                    .WithStackLimit(stack);
                if (!keepStationItem) b.WithoutStationItem();
                b.Build();
                Kept.Keep(id);
            }
            catch (Exception e) { Mod.Log.Warning($"{name}: could not register: {e.Message}"); }
        }

        private static void Rack(string id, string name, string description, float price)
        {
            try
            {
                if (Kept.Restore(id)) return;
                S1API.Items.Buildable.BuildableItemCreator.CloneFrom(RackDonor)
                    .WithBasicInfo(id, name, description, ItemCategory.Furniture)
                    .WithPricing(price, 0.5f)
                    .Build();
                Kept.Keep(id);
            }
            catch (Exception e) { Mod.Log.Warning($"{name}: could not register: {e.Message}"); }
        }

        /// <summary>
        /// Ergot plus Ana's reagent at a chemistry station make LSD solution. Locked until Ana is unlocked (re-applied on every
        /// peer after each load, Ana.AfterLoad), as S1API asks for progression-gated recipes.
        /// </summary>
        public static ChemistryStationRecipe Recipe { get; private set; }

        public static void RegisterRecipe()
        {
            try
            {
                if (Recipe != null) return;
                if (Il2CppScheduleOne.Registry.GetItem(Ids.Ergot) == null || Il2CppScheduleOne.Registry.GetItem(Ids.Reagent) == null
                    || Il2CppScheduleOne.Registry.GetItem(Ids.LsdSolution) == null)
                { Mod.Log.Warning("LSD solution recipe: an item is missing; not registered"); return; }
                Recipe = ChemistryStationRecipes.CreateAndRegister(b => b
                    .WithRecipeId(Ids.SolutionRecipe)
                    .WithTitle("LSD Solution")
                    .WithInitialAvailability(false, false)
                    .WithCookTimeMinutes(240)
                    .WithFinalLiquidColor(new Color(0.85f, 0.8f, 0.95f, 1f))
                    .WithProduct(Ids.LsdSolution, 1)
                    .WithIngredient(Ids.Ergot, 4)
                    .WithIngredient(Ids.Reagent, 1));
                Mod.Log.Msg("LSD solution recipe registered (locked until Ana Slughin is unlocked)");
            }
            catch (Exception e) { Mod.Log.Warning("LSD solution recipe: " + e.Message); }
        }

        // ------------------------------------------------------------------ helpers for the game layer

        public static Il2CppScheduleOne.ItemFramework.ItemInstance Make(string id, int quantity, int tier = -1)
        {
            var inst = Il2CppScheduleOne.Registry.GetItem(id)?.GetDefaultInstance(quantity);
            if (inst != null && tier >= 0)
                inst.TryCast<Il2CppScheduleOne.ItemFramework.QualityItemInstance>()?.SetQuality((Il2CppScheduleOne.ItemFramework.EQuality)Psychedelics.Tier.Clamp(tier));
            return inst;
        }

        public static int TierOf(Il2CppScheduleOne.ItemFramework.ItemInstance inst)
            => inst?.TryCast<Il2CppScheduleOne.ItemFramework.QualityItemInstance>() is { } q ? (int)q.Quality : Psychedelics.Tier.Standard;

        /// <summary>Gives the local player an item, if it fits. False when the pockets are full.</summary>
        public static bool Give(Il2CppScheduleOne.ItemFramework.ItemInstance inst)
        {
            if (inst == null) return false;
            var inv = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
            if (inv == null || !inv.CanItemFitInInventory(inst, inst.Quantity)) return false;
            inv.AddItemToInventory(inst);
            return true;
        }

        public static bool Has(string id)
        {
            var inv = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
            return inv != null && inv.GetAmountOfItem(id) > 0;
        }

        public static void Notify(string title, string text)
        {
            try { Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.NotificationsManager>.Instance?.SendNotification(title, text, null, 5f, true); }
            catch (Exception e) { Mod.Log.Msg($"{title}: {text} (no notification: {e.Message})"); }
        }
    }

    /// <summary>
    /// The spoke's item definitions, made once per process and put back in the registry before each later load (the game
    /// removes runtime items on a scene change). Re-using the same objects, rather than building new ones each load, keeps
    /// S1API's chemistry recipe (registered once per process, holding the definitions it was built with) pointing at live
    /// items. Marked so Unity's unused-asset sweep leaves them alone between scenes.
    /// </summary>
    internal static class Kept
    {
        private static readonly System.Collections.Generic.Dictionary<string, Il2CppScheduleOne.ItemFramework.ItemDefinition> _defs
            = new System.Collections.Generic.Dictionary<string, Il2CppScheduleOne.ItemFramework.ItemDefinition>();

        /// <summary>True when the item is in the registry now (already there, or put back).</summary>
        public static bool Restore(string id)
        {
            if (Il2CppScheduleOne.Registry.GetItem(id) != null) return true;
            if (!_defs.TryGetValue(id, out var def) || def == null) return false;
            Il2CppScheduleOne.Registry.Instance.AddToRegistry(def);
            return Il2CppScheduleOne.Registry.GetItem(id) != null;
        }

        public static void Keep(string id) => Keep(Il2CppScheduleOne.Registry.GetItem(id));

        public static void Keep(Il2CppScheduleOne.ItemFramework.ItemDefinition def)
        {
            if (def == null) return;
            def.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            _defs[def.ID] = def;
        }
    }
}
