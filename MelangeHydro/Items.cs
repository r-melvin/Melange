using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI.Shop;

namespace Melange.Hydro
{
    /// <summary>
    /// The spoke's items: the sections and frames are clones of the game's Grow Tent definition (Drug Expansion's technique),
    /// so a placed one is a real Pot underneath and botanists, the management clipboard, sprinklers and the game's save loader
    /// take it as one (botanist assignment checks the exact type, so a clone is accepted where a Pot subclass would not be).
    /// The pump is a clone of the Pot Sprinkler, the nutrient a clone of the fertiliser. Each is gated with the game's own
    /// rank lock on the definition, so the shop shows it locked until the rank. Sold by the two hardware stores.
    /// </summary>
    internal static class Items
    {
        /// <summary>The hardware stores, by shop code (Handy Hank's Hardware, Dan's Hardware).</summary>
        private static readonly string[] HardwareShopCodes = { "handy_hanks", "dans_hardware" };

        /// <summary>The rank-up screen lists what each milestone unlocks (the game only lists shop items it knew at scene start).</summary>
        public static void Start()
        {
            foreach (var u in Unlocks.All)
            {
                var unlock = u;
                string iconId = unlock.ItemIds.Count > 0 ? unlock.ItemIds[0] : null;
                RankUpScreen.Register(unlock.Rank, unlock.Tier, unlock.Title, () => iconId == null ? null : Il2CppScheduleOne.Registry.GetItem(iconId)?.Icon);
            }
        }

        /// <summary>Registered before every load: the game drops runtime items on a scene change, and a save's placed items need them.</summary>
        public static void Register()
        {
            var tent = Il2CppScheduleOne.Registry.GetItem(Units.DonorId)?.TryCast<StorableItemDefinition>();
            if (tent == null) Mod.Log.Error($"the Grow Tent ('{Units.DonorId}') is not in the registry; no trays or towers this session");
            else
                foreach (var u in Units.Holes) Clone(u, Units.DonorId, tent);

            var sprinkler = Il2CppScheduleOne.Registry.GetItem(Units.PumpDonorId)?.TryCast<StorableItemDefinition>();
            if (sprinkler == null) Mod.Log.Warning($"the Pot Sprinkler ('{Units.PumpDonorId}') is not in the registry; no pump this session");
            else Clone(Units.Pump, Units.PumpDonorId, sprinkler);

            RegisterJuicer();
        }

        private static void Clone(UnitSpec u, string donorId, StorableItemDefinition donor)
        {
            try
            {
                if (Il2CppScheduleOne.Registry.GetItem(u.Id) != null) return;
                S1API.Items.Buildable.BuildableItemCreator.CloneFrom(donorId)
                    .WithBasicInfo(u.Id, u.Name, u.Description, (S1API.Items.ItemCategory)(int)donor.Category)
                    .WithPricing(u.Price, donor.ResellMultiplier)
                    .WithRequiredRank(new S1API.Leveling.FullRank((S1API.Leveling.Rank)u.Rank, u.RankTier))
                    .Build();
                Mod.Log.Msg($"{u.Name}: registered (${u.Price:N0}, from '{donorId}', unlocks at rank {u.Rank} tier {u.RankTier})");
            }
            catch (Exception e) { Mod.Log.Warning($"{u.Name}: could not register: {e.Message}"); }
        }

        private static void RegisterJuicer()
        {
            var u = Units.Juicer;
            try
            {
                if (Il2CppScheduleOne.Registry.GetItem(u.Id) != null) return;
                var fert = Il2CppScheduleOne.Registry.GetItem(Units.FertiliserId)?.TryCast<StorableItemDefinition>();
                if (fert == null) { Mod.Log.Warning($"the fertiliser ('{Units.FertiliserId}') is not in the registry; no Grow N Juicer this session"); return; }
                S1API.Items.Additive.AdditiveItemCreator.CloneFrom(Units.FertiliserId)
                    .WithBasicInfo(u.Id, u.Name, u.Description, (S1API.Items.ItemCategory)(int)fert.Category)
                    .WithPricing(u.Price, fert.ResellMultiplier)
                    .WithRequiredRank(new S1API.Leveling.FullRank((S1API.Leveling.Rank)u.Rank, u.RankTier))
                    .WithEffects(1f, 0f, Units.JuicerQuality)
                    .Build();
                Mod.Log.Msg($"{u.Name}: registered (+{Units.JuicerQuality} quality, ${u.Price:N0})");
            }
            catch (Exception e) { Mod.Log.Warning($"{u.Name}: could not register: {e.Message}"); }
        }

        /// <summary>The nutrient's game definition, or null when it could not be registered.</summary>
        public static AdditiveDefinition Juicer()
            => Il2CppScheduleOne.Registry.GetItem(Units.Juicer.Id)?.TryCast<AdditiveDefinition>();

        /// <summary>Puts the items in the hardware stores once the save has loaded (shops are scene objects).</summary>
        public static void Stock()
        {
            try
            {
                var shops = HardwareShops();
                if (shops.Length == 0) { Mod.Log.Warning("no hardware store found; the hydro items are not for sale this session"); return; }
                foreach (var u in Layout.ForSale(Settings.Placement))
                {
                    var def = S1API.Items.ItemManager.GetDefinition(u.Id);
                    int n = def == null ? 0 : S1API.Shops.ShopManager.AddToShops(def, (float?)null, shops);
                    Mod.Log.Msg($"{u.Name}: in {n} shop(s)");
                }
                Prices.Refresh();                                // the hub's discounts cover listings added after its own pass
            }
            catch (Exception e) { Mod.Log.Warning("could not stock the hardware stores: " + e.Message); }
        }

        /// <summary>The hardware stores' names, read from the game by shop code, so a renamed shop still matches.</summary>
        private static string[] HardwareShops()
        {
            var names = new List<string>();
            var all = ShopInterface.AllShops;
            for (int i = 0; all != null && i < all.Count; i++)
            {
                var s = all[i];
                if (s != null && Array.IndexOf(HardwareShopCodes, s.ShopCode) >= 0 && !string.IsNullOrEmpty(s.ShopName)) names.Add(s.ShopName);
            }
            return names.ToArray();
        }
    }
}
