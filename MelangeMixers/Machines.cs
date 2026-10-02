using System;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;

namespace Melange.Mixers
{
    /// <summary>
    /// The three machines as items: clones of the Mixing Station Mk2's definition (Drug Expansion's technique for its tablet
    /// press), so a placed one is a real Mk2 underneath and chemists, handlers, the management clipboard and the game's save
    /// loader all take it as one. Each is gated with the game's own rank lock on the definition, so the shop shows it locked
    /// until the rank, with or without the levels spoke. Sold by Oscar at the warehouse, like the Brick Press and Cauldron.
    /// </summary>
    internal static class Machines
    {
        /// <summary>The rank-up screen lists each machine at its rank (the game only lists shop items it knew at scene start).</summary>
        public static void Start()
        {
            foreach (var tier in Tiers.All)
            {
                var t = tier;
                RankUpScreen.Register(t.Rank, t.RankTier, t.Name, () => Il2CppScheduleOne.Registry.GetItem(t.Id)?.Icon);
            }
        }

        /// <summary>Registered before every load: the game drops runtime items on a scene change, and a save's placed machines need them.</summary>
        public static void Register()
        {
            var mk2 = Il2CppScheduleOne.Registry.GetItem(Tiers.Mk2Id)?.TryCast<StorableItemDefinition>();
            if (mk2 == null) { Mod.Log.Error($"the Mixing Station Mk2 ('{Tiers.Mk2Id}') is not in the registry; no mixers this session"); return; }
            foreach (var tier in Tiers.All)
            {
                try
                {
                    if (Il2CppScheduleOne.Registry.GetItem(tier.Id) != null) continue;
                    S1API.Items.Buildable.BuildableItemCreator.CloneFrom(Tiers.Mk2Id)
                        .WithBasicInfo(tier.Id, tier.Name, tier.Description, (S1API.Items.ItemCategory)(int)mk2.Category)
                        .WithPricing(Pricing.Price(mk2.BasePurchasePrice, tier), mk2.ResellMultiplier)
                        .WithRequiredRank(new S1API.Leveling.FullRank((S1API.Leveling.Rank)tier.Rank, tier.RankTier))
                        .Build();
                    Mod.Log.Msg($"{tier.Name}: registered at ${Pricing.Price(mk2.BasePurchasePrice, tier):N0} (Mk2 ${mk2.BasePurchasePrice:N0}), unlocks at rank {tier.Rank} tier {tier.RankTier}");
                }
                catch (Exception e) { Mod.Log.Warning($"{tier.Name}: could not register: {e.Message}"); }
            }
        }

        /// <summary>Puts the machines in Oscar's shop once the save has loaded (shops are scene objects).</summary>
        public static void Stock()
        {
            try
            {
                string shop = OscarsShop();
                if (shop == null) { Mod.Log.Warning("Oscar's shop not found; the mixers are not for sale this session"); return; }
                foreach (var tier in Tiers.All)
                {
                    var def = S1API.Items.ItemManager.GetDefinition(tier.Id);
                    int n = def == null ? 0 : S1API.Shops.ShopManager.AddToShops(def, (float?)null, shop);
                    Mod.Log.Msg($"{tier.Name}: in {n} shop(s) ({shop})");
                }
                Prices.Refresh();                                // the hub's discounts cover listings added after its own pass
            }
            catch (Exception e) { Mod.Log.Warning("could not stock Oscar's shop: " + e.Message); }
        }

        /// <summary>
        /// Oscar's shop name, read from the game (the dark market's Oscar), so a renamed shop still matches; failing that, the shop
        /// that sells the Brick Press, which only Oscar does.
        /// </summary>
        private static string OscarsShop()
        {
            try
            {
                var name = NetworkSingleton<Il2CppScheduleOne.Map.DarkMarket>.Instance?.Oscar?.ShopInterface?.ShopName;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            var shops = S1API.Shops.ShopManager.FindShopsByItem("brickpress");
            return shops != null && shops.Length > 0 ? shops[0].Name : null;
        }
    }
}
