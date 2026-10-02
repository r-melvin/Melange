using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI.Shop;

namespace Melange.Levels
{
    /// <summary>What happens when the player spends Prestige on an offer they can't refuse. Host only: offers change the world.</summary>
    internal static class OfferActions
    {
        private const string WarehouseModifier = "levels.warehouse";

        public static void Start()
        {
            Prices.Register(new PriceModifier
            {
                Id = WarehouseModifier,
                Order = 20,
                Multiplier = (shopCode, itemId) =>
                    IsOscarShop(shopCode) && Offers.WarehouseDiscountActive(MelangeLevelsData.Current?.WarehouseDiscountDay ?? -1, Today())
                        ? 1f - Offers.WarehouseDiscountPercent / 100f : 1f,
            });
            // the discount ends with the day it was bought for
            Events.Subscribe<DayPassed>(_ => Prices.Refresh());
        }

        public static int Today()
        {
            try { return NetworkSingleton<TimeManager>.Instance.ElapsedDays; } catch { return 0; }
        }

        /// <summary>Suppliers with a dead drop on its way (what a rush order can hurry).</summary>
        public static List<Supplier> PendingDrops()
        {
            var list = new List<Supplier>();
            foreach (var s in UnityEngine.Object.FindObjectsOfType<Supplier>())
                if (s != null && s.MinsUntilDeaddropReady > 1) list.Add(s);
            return list;
        }

        public static bool RushOrder(Supplier supplier)
        {
            if (!Spend(OfferKind.RushOrder) || supplier == null) return false;
            supplier.MinsUntilDeaddropReady = 1;                   // the game completes it on its next minute tick
            Mod.Log.Msg($"offer: rush order from {supplier.FirstName}");
            return true;
        }

        public static bool WarehouseDiscount()
        {
            if (!Spend(OfferKind.WarehouseDiscount)) return false;
            MelangeLevelsData.Current.WarehouseDiscountDay = Today();
            Prices.Refresh();
            Mod.Log.Msg("offer: warehouse discount for today");
            return true;
        }

        private static bool Spend(OfferKind kind)
        {
            var data = MelangeLevelsData.Current;
            if (data == null || !Host.IsHost) return false;
            if (!Offers.Available(kind, data.Prestige, data.LastUsed(kind), Today(), out _)) return false;
            data.Prestige -= Offers.Cost(kind);
            data.OffersLastUsed[kind.ToString()] = Today();
            Events.Publish(new PrestigeChanged(data.Prestige));
            return true;
        }

        private static HashSet<string> _oscarShops;

        /// <summary>
        /// Oscar's shops, by name, once per scene: his store at the warehouse ("Oscar's Store", code "shop") and his
        /// equipment counter ("Oscar's Equipment"). The discount covers both: it's his stock.
        /// </summary>
        public static bool IsOscarShop(string shopCode)
        {
            if (_oscarShops == null)
            {
                _oscarShops = new HashSet<string>();
                var shops = ShopInterface.AllShops;
                for (int i = 0; shops != null && i < shops.Count; i++)
                    if ((shops[i]?.ShopName ?? "").IndexOf("Oscar", StringComparison.OrdinalIgnoreCase) >= 0)
                        _oscarShops.Add(shops[i].ShopCode);
                if (_oscarShops.Count == 0) _oscarShops = null;    // shops not ready yet: look again next time
            }
            return _oscarShops != null && _oscarShops.Contains(shopCode);
        }

        internal static void ForgetScene() => _oscarShops = null;
    }
}
