using System;
using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Ergot, grown with the game's own shroom kit: Fungal Phil's spores (a copy of the game's spore syringe) plus a grain bag at
    /// the spawn station make ergot spawn (a copy of the shroom spawn), which grows in a mushroom bed like any colony and is
    /// harvested as "Ergot" (a copy of the game's shroom definition, never discovered, so it is never listed or offered in a
    /// deal: an ingredient, not a product to sell).
    /// </summary>
    /// <remarks>
    /// The copies are made with Object.Instantiate so they keep their native types (S1API's builders only make plain storable
    /// items, and the spawn station casts to SporeSyringeDefinition). The spawn keeps the game's colony prefab, already a
    /// registered network prefab, so the bed's FishNet spawn works. That prefab names the vanilla spawn in its own field,
    /// which is what a colony saves and harvests by; the patch below points a new ergot colony at our spawn instead, so it
    /// saves our spawn ID and reloads as ergot (the bed's loader looks the ID up and comes back through the same method).
    /// </remarks>
    internal static class Ergot
    {
        private static ShroomDefinition _vanillaShroom;
        private static SporeSyringeDefinition _vanillaSpores;
        private static string _philShop;
        public const float SporesPrice = 180f, ErgotPrice = 30f;

        /// <summary>The game's base shroom (the template for both products' scaffolds), found through its spore syringe.</summary>
        public static ShroomDefinition VanillaShroom()
        {
            if (_vanillaShroom != null) return _vanillaShroom;
            FindVanilla();
            return _vanillaShroom;
        }

        private static void FindVanilla()
        {
            try
            {
                var all = Il2CppScheduleOne.Registry.Instance?.GetAllItems();
                for (int i = 0; all != null && i < all.Count; i++)
                {
                    var s = all[i]?.TryCast<SporeSyringeDefinition>();
                    if (s == null || s.ID == Ids.ErgotSpores || s.SpawnDefinition?.Shroom == null) continue;
                    _vanillaSpores = s;
                    _vanillaShroom = s.SpawnDefinition.Shroom;
                    Mod.Log.Msg($"vanilla shroom line: spores '{s.ID}', spawn '{s.SpawnDefinition.ID}', shroom '{_vanillaShroom.ID}'");
                    return;
                }
                Mod.Log.Warning("no spore syringe in the registry: no ergot, and the products have no template");
            }
            catch (Exception e) { Mod.Log.Warning("finding the vanilla shroom line: " + e.Message); }
        }

        /// <summary>Before each load: the three copies (or, with growing off, ergot alone as a plain ingredient).</summary>
        public static void Register()
        {
            try
            {
                FindVanilla();
                if (_vanillaSpores == null) return;
                var registry = Il2CppScheduleOne.Registry.Instance;

                // the same objects every load once made (Kept), so the recipe and the spawn keep pointing at live ones
                ShroomDefinition ergot;
                if (Kept.Restore(Ids.Ergot)) ergot = Il2CppScheduleOne.Registry.GetItem(Ids.Ergot).TryCast<ShroomDefinition>();
                else
                {
                    ergot = Object.Instantiate(_vanillaShroom);
                    Name(ergot, Ids.Ergot, "Ergot", "Hard purple-black grains off infected rye. Useless to sell; a chemist knows what to do with it.");
                    ergot.BasePrice = 1f;
                    ergot.MarketValue = 1f;
                    ergot.Properties?.Clear();
                    Tint(ergot);
                    registry.AddToRegistry(ergot);
                    Kept.Keep(ergot);
                }

                if (!Settings.ErgotGrowing) { Mod.Log.Msg("ergot: growing off, sold ready to use"); return; }

                ShroomSpawnDefinition spawn;
                if (Kept.Restore(Ids.ErgotSpawn)) spawn = Il2CppScheduleOne.Registry.GetItem(Ids.ErgotSpawn).TryCast<ShroomSpawnDefinition>();
                else
                {
                    spawn = Object.Instantiate(_vanillaSpores.SpawnDefinition);
                    Name(spawn, Ids.ErgotSpawn, "Ergot Spawn", "Grain shot through with ergot. Apply it to a mushroom bed; it wants the cold, like shrooms.");
                    spawn._Shroom_k__BackingField = ergot;
                    registry.AddToRegistry(spawn);
                    Kept.Keep(spawn);
                }

                if (!Kept.Restore(Ids.ErgotSpores))
                {
                    var spores = Object.Instantiate(_vanillaSpores);
                    Name(spores, Ids.ErgotSpores, "Ergot Spores", "Inoculate a grain bag at a spawn station to make ergot spawn.");
                    spores._SpawnDefinition_k__BackingField = spawn;
                    spores.BasePurchasePrice = SporesPrice;
                    spores.RequiresLevelToPurchase = true;
                    spores.RequiredRank = new Il2CppScheduleOne.Levelling.FullRank((Il2CppScheduleOne.Levelling.ERank)Settings.SporesRank, 1);
                    registry.AddToRegistry(spores);
                    Kept.Keep(spores);
                }
                Mod.Log.Msg("ergot: spores, spawn and harvest registered");
            }
            catch (Exception e) { Mod.Log.Error("ergot could not be registered: " + e); }
        }

        private static void Name(ItemDefinition d, string id, string name, string description)
        {
            d.ID = id;
            d.Name = name;
            d.Description = description;
            d.name = id;
        }

        /// <summary>Ergot's colour: the shroom material, darkened to purple-black (its own copy; the vanilla one is untouched).</summary>
        private static void Tint(ShroomDefinition ergot)
        {
            try
            {
                // not a serialised field, so the copy comes out without it; the game reads it for a shroom's look and save line
                ergot._AppearanceSettings_k__BackingField = new ShroomAppearanceSettings(new Color32(56, 30, 60, 255), new Color32(90, 60, 95, 255), false, new Color32(0, 0, 0, 0));
                var src = ergot.ShroomMaterial;
                if (src == null) return;
                var m = new Material(src);
                var dark = new Color(0.22f, 0.12f, 0.24f);
                foreach (var prop in new[] { "_CapInnerColor", "_CapOuterColor", "_StemLowerColor", "_StemUpperColor", "_UnderColor" })
                    if (m.HasProperty(prop)) m.SetColor(prop, dark);
                Object.DontDestroyOnLoad(m);
                ergot._ShroomMaterial_k__BackingField = m;
            }
            catch (Exception e) { Mod.Log.Warning("ergot colour: " + e.Message); }
        }

        /// <summary>Fungal Phil (the shop that sells the game's spores) sells ergot spores, or ergot itself with growing off.</summary>
        public static void Stock()
        {
            try
            {
                if (_vanillaSpores == null) return;
                var shops = S1API.Shops.ShopManager.FindShopsByItem(_vanillaSpores.ID);
                if (shops == null || shops.Length == 0) { Mod.Log.Warning("Fungal Phil's shop not found (no shop sells spores); no ergot this session"); return; }
                _philShop = shops[0].Name;
                string id = Settings.ErgotGrowing ? Ids.ErgotSpores : Ids.Ergot;
                var def = S1API.Items.ItemManager.GetDefinition(id);
                float price = Settings.ErgotGrowing ? SporesPrice : ErgotPrice;
                int n = def == null ? 0 : S1API.Shops.ShopManager.AddToShops(def, price, _philShop);
                Mod.Log.Msg($"ergot: {id} in {n} shop(s) ({_philShop})");
                Core.Prices.Refresh();
            }
            catch (Exception e) { Mod.Log.Warning("ergot stock: " + e.Message); }
        }

        // ------------------------------------------------------------------ the colony patch

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(MushroomBed), nameof(MushroomBed.CreateAndAssignColony), new[] { typeof(ShroomSpawnDefinition) });
                if (target == null) { Mod.Log.Warning("MushroomBed.CreateAndAssignColony not found: ergot colonies will grow shrooms"); return; }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Ergot), nameof(AfterCreateColony)));
            }
            catch (Exception e) { Mod.Log.Warning("ergot colony patch failed: " + e.Message); }
        }

        /// <summary>
        /// A colony made from our spawn (by the host, from a player's spawn or a save) gets our spawn in its own field, so it
        /// saves our ID and harvests ergot. Not a one-line method, so it is not inlined away. Clients spawn the colony from the
        /// network and keep the prefab's vanilla field (see TESTING.md: a client's harvest).
        /// </summary>
        private static void AfterCreateColony(MushroomBed __instance, ShroomSpawnDefinition shroomSpawn)
        {
            try
            {
                if (shroomSpawn == null || shroomSpawn.ID != Ids.ErgotSpawn) return;
                var colony = __instance.CurrentColony;
                if (colony == null) return;
                colony._spawnDefinition = shroomSpawn;
                Mod.Log.Msg($"ergot colony in bed {__instance.GUID}");
            }
            catch (Exception e) { Mod.Log.Warning("ergot colony: " + e.Message); }
        }
    }
}
