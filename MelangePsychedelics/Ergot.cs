using System;
using System.Collections.Generic;
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

        // ------------------------------------------------------------------ probes (Probe.cs, host only)

        /// <summary>
        /// <c>psy ergot [plant|grow|harvest|status]</c>; no step runs all four a second apart (the game's calls here are
        /// server RPCs, which the host runs on its next network tick, so each step reads the last one's result).
        /// </summary>
        internal static string Probe(string step)
        {
            switch (step)
            {
                case null:
                case "all":
                    MelonLoader.MelonCoroutines.Start(ProbeAll());
                    return "plant, grow, harvest and status follow a second or two apart (PROBE ergot <step> lines)";
                case "plant": return ProbePlant();
                case "grow": return ProbeGrow();
                case "harvest": return ProbeHarvest();
                case "status": return ProbeStatus();
                default: return "ergot [plant|grow|harvest|status]";
            }
        }

        private static System.Collections.IEnumerator ProbeAll()
        {
            foreach (var step in new[] { "plant", "grow", "harvest", "status" })
            {
                string result;
                try { result = Probe(step); }
                catch (Exception e) { result = "threw: " + e; }
                Mod.Log.Msg($"PROBE ergot {step}: {result}");
                float until = Time.realtimeSinceStartup + (step == "plant" && result.StartsWith("placed") ? 3f : 1.5f);
                while (Time.realtimeSinceStartup < until) yield return null;
                if (step == "plant" && result.StartsWith("placed"))
                {
                    try { result = ProbePlant(); } catch (Exception e) { result = "threw: " + e; }
                    Mod.Log.Msg($"PROBE ergot plant: {result}");
                    until = Time.realtimeSinceStartup + 1.5f;
                    while (Time.realtimeSinceStartup < until) yield return null;
                }
            }
        }

        /// <summary>Mushroom beds nearest the local player first.</summary>
        private static List<MushroomBed> Beds()
        {
            var list = new List<MushroomBed>();
            foreach (var b in Object.FindObjectsOfType<MushroomBed>()) if (b != null) list.Add(b);
            var me = Il2CppScheduleOne.PlayerScripts.Player.Local;
            if (me != null)
            {
                var p = me.transform.position;
                list.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
            }
            return list;
        }

        private static bool IsErgot(MushroomBed b)
        {
            var c = b.CurrentColony;
            return c != null && (c._spawnDefinition?.ID == Ids.ErgotSpawn || c.GetSaveData()?.MushroomSpawnID == Ids.ErgotSpawn);
        }

        /// <summary>The nearest bed growing ergot (a player's shroom colony is never touched by the probes), or null.</summary>
        private static MushroomBed Colonised()
        {
            foreach (var b in Beds()) if (IsErgot(b)) return b;
            return null;
        }

        /// <summary>The game's mushroom bed item, found by what it builds (the ID isn't hard-coded).</summary>
        private static string BedItemId()
        {
            var all = Il2CppScheduleOne.Registry.Instance?.GetAllItems();
            for (int i = 0; all != null && i < all.Count; i++)
            {
                var def = all[i]?.TryCast<BuildableItemDefinition>();
                if (def?.BuiltItem != null && def.BuiltItem.GetComponent<MushroomBed>() != null) return def.ID;
            }
            return null;
        }

        /// <summary>
        /// Ergot spawn into the nearest empty bed (placing one in an owned property if there is none) through the call the
        /// player's spawn task ends with (ApplyShroomSpawnTask.Success): one spawn out of the pockets if there is one, the
        /// soil shown full of spores, then <c>MushroomBed.CreateAndAssignColony_Server(spawnId)</c>, whose server side runs
        /// CreateAndAssignColony, the patched method. The pour-and-mix minigame itself is skipped.
        /// </summary>
        private static string ProbePlant()
        {
            if (!Settings.ErgotGrowing) return "ergot growing is off in the settings (ErgotGrowing)";
            var spawn = Il2CppScheduleOne.Registry.GetItem(Ids.ErgotSpawn)?.TryCast<ShroomSpawnDefinition>();
            if (spawn == null) return $"{Ids.ErgotSpawn} is not registered as a shroom spawn (see the 'ergot:' load lines)";
            MushroomBed bed = null;
            foreach (var b in Beds()) if (b.CurrentColony == null) { bed = b; break; }
            if (bed == null)
            {
                string id = BedItemId();
                if (id == null) return "no empty mushroom bed and no mushroom bed item in the registry";
                return Placed.PlaceOnGrid(id) + " (no empty bed was found: run 'psy ergot plant' again once it has spawned)";
            }
            var inv = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
            bool fromPockets = inv != null && inv.GetAmountOfItem(Ids.ErgotSpawn) > 0;
            if (fromPockets) inv.RemoveAmountOfItem(Ids.ErgotSpawn, 1);
            bed.ConfigureSoilAppearance(MushroomBed.EMushroomBedSoilAppearance.FullSpores);
            bed.CreateAndAssignColony_Server(Ids.ErgotSpawn);
            var at = bed.transform.position;
            return $"bed {bed.GUID} at ({at.x:0.0},{at.y:0.0},{at.z:0.0}): CreateAndAssignColony_Server({Ids.ErgotSpawn}) sent, " +
                   $"{(fromPockets ? "one spawn taken from the pockets" : "no ergot spawn in the pockets: planted without one")}; " +
                   "expect 'ergot colony in bed <guid>' from the patch, then 'psy ergot status'";
        }

        /// <summary>The game's own SetFullyGrown (a server RPC) on the nearest ergot colony: growth to 100% now.</summary>
        private static string ProbeGrow()
        {
            var bed = Colonised();
            if (bed == null) return "no bed growing ergot (psy ergot plant; psy ergot status lists every colony)";
            var c = bed.CurrentColony;
            float before = c.GrowthProgress;
            c.SetFullyGrown();
            return $"bed {bed.GUID}: SetFullyGrown sent (growth was {before:P0}, too hot {c.IsTooHotToGrow}); 'psy ergot status' shows it";
        }

        /// <summary>
        /// Every grown mushroom picked with GrowingMushroom.Harvest, the method the player's harvest task calls per mushroom
        /// (it puts the colony's GetHarvestedShroom into the pockets and removes the mushroom), while the pockets have room.
        /// </summary>
        private static string ProbeHarvest()
        {
            var bed = Colonised();
            if (bed == null) return "no bed growing ergot (psy ergot plant; psy ergot status lists every colony)";
            var c = bed.CurrentColony;
            if (!bed.IsReadyForHarvest(out string reason)) return $"bed {bed.GUID} not ready: {reason}";
            var inv = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
            var sample = c.GetHarvestedShroom(1);
            string harvestedId = sample?.ID ?? "null";
            int ergotBefore = Items.Count(Ids.Ergot), vanillaBefore = _vanillaShroom == null ? 0 : Items.Count(_vanillaShroom.ID);
            var shrooms = new List<Il2CppScheduleOne.Growing.GrowingMushroom>();
            var list = c._growingShrooms;
            for (int i = 0; list != null && i < list.Count; i++) if (list[i] != null) shrooms.Add(list[i]);
            int picked = 0;
            foreach (var m in shrooms)
            {
                if (inv == null || !inv.CanItemFitInInventory(c.GetHarvestedShroom(1), 1)) break;
                m.Harvest();
                picked++;
            }
            return $"bed {bed.GUID}: picked {picked}/{shrooms.Count}, harvest item '{harvestedId}' ({(harvestedId == Ids.Ergot ? "ergot" : "NOT ergot")}); " +
                   $"pockets ergot {ergotBefore} -> {Items.Count(Ids.Ergot)}" +
                   (_vanillaShroom == null ? "" : $", {_vanillaShroom.ID} {vanillaBefore} -> {Items.Count(_vanillaShroom.ID)}");
        }

        /// <summary>Every colonised bed: the spawn its colony holds, the spawn ID its save line would carry, growth and mushrooms.</summary>
        private static string ProbeStatus()
        {
            var lines = new List<string>();
            int empty = 0;
            foreach (var b in Beds())
            {
                var c = b.CurrentColony;
                if (c == null) { empty++; continue; }
                var save = c.GetSaveData();
                lines.Add($"bed {b.GUID}: colony spawn '{c._spawnDefinition?.ID}', saved spawn ID '{save?.MushroomSpawnID}' " +
                          $"({(save?.MushroomSpawnID == Ids.ErgotSpawn ? "ergot: reloads as ergot" : "not ergot")}), growth {c.GrowthProgress:P0}, " +
                          $"grown {c.IsFullyGrown}, mushrooms {c._growingShrooms?.Count ?? 0}, too hot {c.IsTooHotToGrow}, harvests '{c.GetHarvestedShroom(1)?.ID}'");
            }
            return $"{lines.Count} colonised bed(s), {empty} empty" + (lines.Count > 0 ? ": " + string.Join("; ", lines) : "");
        }
    }
}
